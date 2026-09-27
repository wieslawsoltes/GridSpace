namespace GridSpace.Workbench;

public sealed partial class SpreadsheetWorkbench
{
    private int _requestedFilterColumn = -1;
    public FrameworkElement? DataToolOverlay { get; private set; }

    private void InitializeDataTools()
    {
        Surface.EnableDataToolInteractions();
        Surface.FilterRequested += FilterRequested;
    }
    private void FilterRequested(int column)
    {
        if (_executing) return;
        _requestedFilterColumn = column;
        RunCommand("filter");
    }

    private async Task<bool> ExecuteDataToolAsync(string id)
    {
        switch (id)
        {
            case "filter":
                var column = _requestedFilterColumn >= 0 ? _requestedFilterColumn : Session.ActiveCell.Column;
                _requestedFilterColumn = -1;
                await ShowFilterAsync(column);
                break;
            case "clear-filter": Session.ClearFilters(); ShowStatus("All filter criteria cleared; manually hidden rows retained"); break;
            case "reapply-filter": Session.ReapplyFilters(); ShowStatus("Filters reapplied"); break;
            case "toggle-filter": Session.ToggleFilter(); break;
            case "custom-sort": await ShowSortAsync(); break;
            case "delete-row": Session.Delete(true); ShowStatus("Rows deleted; references updated"); break;
            case "delete-column": Session.Delete(false); ShowStatus("Columns deleted; references updated"); break;
            case "conditional-format": await ShowConditionalEditorAsync(); break;
            case "conditional-manage": await ManageConditionalFormatsAsync(); break;
            case "conditional-clear": Session.ClearConditionalFormats(); ShowStatus("Conditional rules intersecting the selection cleared"); break;
            case "color-scale": AddVisualRule(ConditionalFormatKind.ColorScale); break;
            case "data-bars": AddVisualRule(ConditionalFormatKind.DataBar); break;
            case "duplicate-values": AddVisualRule(ConditionalFormatKind.DuplicateValues); break;
            case "hide-rows": SetHidden(true, true); break;
            case "unhide-rows": SetHidden(true, false); break;
            case "hide-columns": SetHidden(false, true); break;
            case "unhide-columns": SetHidden(false, false); break;
            default: return false;
        }
        Surface.RevealSelection();
        return true;
    }

    private void SetHidden(bool rows, bool hidden)
    {
        var first = rows ? Session.Selection.Top : Session.Selection.Left;
        var last = rows ? Session.Selection.Bottom : Session.Selection.Right;
        if (last - first >= 100_000) throw new InvalidOperationException("Limit a hide/unhide operation to 100,000 indexes.");
        Session.Perform((hidden ? "Hide " : "Unhide ") + (rows ? "rows" : "columns"), () =>
        {
            var set = rows ? Session.Sheet.HiddenRows : Session.Sheet.HiddenColumns;
            for (var i = first; i <= last; i++) if (hidden) set.Add(i); else set.Remove(i);
        });
    }

    private void AddVisualRule(ConditionalFormatKind kind)
    {
        Session.SetConditionalFormat(new ConditionalFormatRule
        {
            Range = Session.Selection.ToString(), Kind = kind,
            Priority = Session.Sheet.ConditionalFormats.Select(r => r.Priority).DefaultIfEmpty(0).Max() + 1
        });
        ShowStatus("Applied " + kind + " to " + Session.Selection);
    }

    private async Task ShowFilterAsync(int column)
    {
        var range = CellRange.TryParse(Session.Sheet.FilterRange, out var existing) ? existing : Session.DataRange();
        if (column < range.Left || column > range.Right) throw new InvalidOperationException("Select a column inside the filter range.");
        var editor = new FilterEditorControl(column, Session.FilterValues(column), Session.Sheet.Filters.FirstOrDefault(f => f.Column == column));
        var root = new StackPanel { Spacing = 8, MaxWidth = Math.Max(260, Math.Min(340, XamlRoot.Size.Width - 40)) };
        var title = Session.Calculation.Evaluate(Session.Sheet, new CellAddress(range.Top, column)).ToString();
        root.Children.Add(OfficeTheme.Label("Sort & Filter · " + (title.Length > 0 ? title : CellAddress.ColumnName(column)), 14, "#107C41"));
        var popup = new Flyout { Content = root };
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var action = ""; ColumnFilter? result = null;
        void Finish(string chosen) { action = chosen; popup.Hide(); }
        var ascending = new OfficeButton("Sort A to Z / Smallest to Largest", () => Finish("ascending")) { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
        var descending = new OfficeButton("Sort Z to A / Largest to Smallest", () => Finish("descending")) { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetAutomationId(ascending, "FilterSortAscending"); AutomationProperties.SetAutomationId(descending, "FilterSortDescending");
        root.Children.Add(ascending); root.Children.Add(descending);
        root.Children.Add(new Border { Height = 1, Background = OfficeTheme.Brush("#DDDDDD") }); root.Children.Add(editor);
        var error = OfficeTheme.Label("", 12, "#A4262C"); error.TextWrapping = TextWrapping.Wrap; root.Children.Add(error);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var apply = new OfficeButton("Apply", () =>
        {
            try { result = editor.BuildFilter(); Finish("apply"); }
            catch (Exception failure) { error.Text = failure.Message; }
        }) { Background = OfficeTheme.Brush("#107C41"), Foreground = OfficeTheme.Brush("#FFFFFF"), MinWidth = 70 };
        AutomationProperties.SetAutomationId(apply, "FilterApply");
        var clear = new OfficeButton("Clear column", () => Finish("clear")); AutomationProperties.SetAutomationId(clear, "FilterClearColumn");
        footer.Children.Add(apply); footer.Children.Add(clear); footer.Children.Add(new OfficeButton("Cancel", () => Finish(""))); root.Children.Add(footer);
        popup.Closed += (_, _) => completion.TrySetResult(action);
        DataToolOverlay = root;
        try
        {
            var bounds = Surface.Viewport.CellBounds(new CellAddress(range.Top, column));
            popup.ShowAt(Surface, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions
            {
                Position = new Windows.Foundation.Point(Math.Clamp(bounds.X, 0, Math.Max(0, Surface.ActualWidth - 350)), Math.Clamp(bounds.Bottom, 0, Math.Max(0, Surface.ActualHeight - 40)))
            });
            switch (await completion.Task)
            {
                case "apply": Session.SetFilter(result!, range); ShowStatus("Filter applied to " + CellAddress.ColumnName(column)); break;
                case "clear": Session.ClearFilters(column); ShowStatus("Column filter cleared"); break;
                case "ascending": Session.Sort([new SortLevel(column)], range: range); break;
                case "descending": Session.Sort([new SortLevel(column, true)], range: range); break;
            }
        }
        finally { DataToolOverlay = null; }
    }

    private async Task ShowSortAsync()
    {
        var editor = new SortEditorControl(Session, Session.DataRange());
        var dialog = Dialog("Sort", editor, "Sort");
        IReadOnlyList<SortLevel>? levels = null;
        dialog.PrimaryButtonClick += (_, args) =>
        {
            try { levels = editor.BuildLevels(); }
            catch (Exception error) { args.Cancel = true; ShowStatus(error.Message, true); }
        };
        DataToolOverlay = dialog;
        try
        {
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                Session.Sort(levels!, editor.HasHeader, editor.CaseSensitive, editor.Range);
        }
        finally { DataToolOverlay = null; }
    }

    private async Task ShowConditionalEditorAsync(ConditionalFormatRule? rule = null)
    {
        var editor = new ConditionalFormatEditorControl(rule ?? new ConditionalFormatRule
        {
            Range = Session.Selection.ToString(),
            Priority = Session.Sheet.ConditionalFormats.Select(r => r.Priority).DefaultIfEmpty(0).Max() + 1
        });
        var dialog = Dialog(rule is null ? "New Formatting Rule" : "Edit Formatting Rule", editor, "Apply");
        ConditionalFormatRule? result = null;
        dialog.PrimaryButtonClick += (_, args) =>
        {
            try { result = editor.BuildRule(); }
            catch (Exception error) { args.Cancel = true; ShowStatus(error.Message, true); }
        };
        DataToolOverlay = dialog;
        try
        {
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                Session.SetConditionalFormat(result!);
                ShowStatus("Conditional rule saved for " + result!.Range);
            }
        }
        finally { DataToolOverlay = null; }
    }

    private async Task ManageConditionalFormatsAsync()
    {
        while (true)
        {
            ConditionalFormatRule? selected = null; var create = false;
            var rows = new StackPanel { Spacing = 8, MinWidth = 320, MaxWidth = 560 };
            var panel = new StackPanel { Spacing = 10 };
            var dialog = Dialog("Conditional Formatting Rules Manager", panel);
            dialog.PrimaryButtonText = ""; dialog.CloseButtonText = "Done";
            panel.Children.Add(OfficeTheme.Label("Rules are evaluated from top to bottom. Highest priority wins each property.", 12));
            var add = new OfficeButton("New Rule…", () => { create = true; dialog.Hide(); }); AutomationProperties.SetAutomationId(add, "ConditionalNewRule"); panel.Children.Add(add);
            panel.Children.Add(new ScrollViewer { Content = rows, MaxHeight = 380 });
            void Rebuild()
            {
                rows.Children.Clear();
                var rules = Session.Sheet.ConditionalFormats.OrderBy(r => r.Priority).ToArray();
                if (rules.Length == 0) rows.Children.Add(OfficeTheme.Label("No conditional rules on this worksheet.", 13, "#666666"));
                for (var i = 0; i < rules.Length; i++)
                {
                    var rule = rules[i];
                    var row = new StackPanel { Spacing = 4 };
                    row.Children.Add(OfficeTheme.Label($"{rule.Priority}. {rule.Kind} · {rule.Range}" + (rule.StopIfTrue ? " · Stop If True" : ""), 13));
                    var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                    var edit = new OfficeButton("Edit…", () => { selected = rule; dialog.Hide(); }); AutomationProperties.SetAutomationId(edit, "ConditionalEdit" + i);
                    actions.Children.Add(edit);
                    actions.Children.Add(new OfficeButton("Move Up", () => { Session.MoveConditionalFormat(rule.Id, -1); Rebuild(); }));
                    actions.Children.Add(new OfficeButton("Move Down", () => { Session.MoveConditionalFormat(rule.Id, 1); Rebuild(); }));
                    actions.Children.Add(new OfficeButton("Delete", () => { Session.RemoveConditionalFormat(rule.Id); Rebuild(); }));
                    row.Children.Add(actions); rows.Children.Add(row);
                }
            }
            Rebuild(); DataToolOverlay = dialog;
            try { await dialog.ShowAsync(); }
            finally { DataToolOverlay = null; }
            if (selected is not null || create) await ShowConditionalEditorAsync(selected);
            else break;
        }
    }
}
