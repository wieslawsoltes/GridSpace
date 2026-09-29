using GridSpace.Core;
using Windows.ApplicationModel.DataTransfer;

namespace GridSpace.Controls;

/// <summary>Reusable PivotTable field list with live row/column/value/filter areas, drag/drop and keyboard-accessible commands.</summary>
public sealed partial class PivotFieldListControl : UserControl
{
    private readonly StackPanel _root = new() { Spacing = 10, Padding = new Thickness(12) };
    private PivotTableSpec _document = new();
    private string[] _fields = [];
    private bool _loading;
    private bool _requiresRefresh;
    public Func<PivotTableSpec, bool>? CommitChanges { get; set; }
    public Func<int, IReadOnlyList<string>>? GetFieldValues { get; set; }
    public event Action? RefreshRequested;
    public event Action? ChartRequested;
    public event Action? CloseRequested;
    public event Action? RemoveRequested;

    public PivotFieldListControl()
    {
        _scroll = new ScrollViewer { Content = _root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Content = _scroll;
        _dragScrollTimer.Tick += (_, _) => ScrollFieldDrag();
        Unloaded += (_, _) => ClearFieldDrag();
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        AutomationProperties.SetAutomationId(this, "PivotFieldList");
    }

    public void Bind(PivotTableSpec document)
    {
        // The host owns source snapshots. Keeping them out of the edit draft prevents a
        // complete cache validation on every caption, format or field-area interaction.
        ArgumentNullException.ThrowIfNull(document);
        _requiresRefresh = document.NeedsLayoutRefresh || document.Cache is null && document.OutputRange is not null;
        _document = (document with { Cache = null }).CloneDocument();
        _fields = document.FieldNames.ToArray();
        Build();
    }

    private void Build()
    {
        _loading = true;
        try
        {
            ClearFieldDrag(); _dropAreas.Clear(); _root.Children.Clear();
            var header = new Grid(); header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(OfficeTheme.Label("PivotTable Fields", 18));
            var close = new OfficeButton("×", () => CloseRequested?.Invoke()) { Width = 28, FontSize = 18 };
            Grid.SetColumn(close, 1); header.Children.Add(close); _root.Children.Add(header);
            var help = OfficeTheme.Label("Drag fields into areas, or use Add. Changes update the report immediately.", 11, "#666666");
            help.TextWrapping = TextWrapping.Wrap; _root.Children.Add(help);
            var info = OfficeTheme.Label(_document.SourceSheet + "!" + _document.SourceRange + "  ·  " + _document.LastSourceRowCount.ToString("N0") + " records", 11);
            info.TextWrapping = TextWrapping.Wrap; _root.Children.Add(info);
            var commands = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            commands.Children.Add(Button("Pivot Refresh", "Refresh", () => RefreshRequested?.Invoke()));
            commands.Children.Add(Button("Pivot Chart", "PivotChart", () => ChartRequested?.Invoke()));
            _root.Children.Add(commands);
            if (_requiresRefresh)
            {
                var message = OfficeTheme.Label("Refresh this report before changing fields or filters. Its imported layout or source cache needs conversion.", 12, "#666666");
                message.TextWrapping = TextWrapping.Wrap;
                _root.Children.Add(message);
                return;
            }

            var addArea = OfficeForm.Choice("Pivot add area", new[] { "Rows", "Columns", "Values", "Filters" }.Select(s => new OfficeChoice<string>(s, s)), "Rows");
            _root.Children.Add(OfficeForm.Field("Add selected field to", addArea));
            var catalog = new StackPanel { Spacing = 2 };
            var search = OfficeTheme.Field("Pivot field search"); search.PlaceholderText = "Search fields";
            _root.Children.Add(search);
            var fieldButtons = new List<(string Name, FrameworkElement Element)>();
            for (var i = 0; i < _fields.Length; i++)
            {
                var field = i;
                var button = new OfficeDragButton
                {
                    Content = "+  " + _fields[i], HorizontalContentAlignment = HorizontalAlignment.Left,
                    DragCoordinateRoot = this
                };
                AutomationProperties.SetAutomationId(button, "Pivotfield" + i);
                AutomationProperties.SetName(button, _fields[i]);
                button.Click += (_, _) => Assign(field, OfficeForm.Value<string>(addArea));
                button.DragPreview += point => PreviewFieldDrag(point);
                button.DragCommitted += point => CommitFieldDrag(field, point);
                button.DragCancelled += ClearFieldDrag;
                catalog.Children.Add(button); fieldButtons.Add((_fields[i], button));
            }
            search.TextChanged += (_, _) =>
            {
                foreach (var item in fieldButtons) item.Element.Visibility = item.Name.Contains(search.Text, StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
            };
            _root.Children.Add(new ScrollViewer { Content = catalog, MaxHeight = 160, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            Area("Rows", _document.Rows);
            Area("Columns", _document.Columns);
            ValueArea();
            FilterArea();
            Check("Pivot grand total column", _document.RowGrandTotals, value => Change(p => p with { RowGrandTotals = value }));
            Check("Pivot grand total row", _document.ColumnGrandTotals, value => Change(p => p with { ColumnGrandTotals = value }));
            Check("Pivot sort ascending", _document.SortAscending, value => Change(p => p with { SortAscending = value }));
            Check("Pivot include hidden source rows", _document.IncludeHiddenRows, value => Change(p => p with { IncludeHiddenRows = value }));
            _root.Children.Add(Button("Pivot Remove", "Remove PivotTable", () => RemoveRequested?.Invoke()));
        }
        finally { _loading = false; }
    }

    private StackPanel DropArea(string name)
    {
        var panel = new StackPanel { Spacing = 5 };
        var border = new Border
        {
            BorderBrush = OfficeTheme.Brush("#CCCCCC"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3),
            Background = OfficeTheme.Brush("#FAFAFA"), Padding = new Thickness(8), MinHeight = 55, Child = panel, AllowDrop = true
        };
        AutomationProperties.SetAutomationId(border, "PivotArea" + name);
        _dropAreas.Add(name, border);
        panel.Children.Add(OfficeTheme.Label(name, 13, "#107C41"));
        border.DragOver += (_, e) => { if (e.DataView.Contains(StandardDataFormats.Text)) { e.AcceptedOperation = DataPackageOperation.Copy; e.Handled = true; } };
        border.Drop += async (_, e) =>
        {
            try
            {
                if (!e.DataView.Contains(StandardDataFormats.Text)) return;
                var value = await e.DataView.GetTextAsync();
                const string prefix = "GridSpace.PivotField:";
                if (value.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(value[prefix.Length..], out var field)
                    && field >= 0 && field < _fields.Length) { Assign(field, name); e.Handled = true; }
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException)
            {
                ToolTipService.SetToolTip(border, error.Message);
            }
        };
        _root.Children.Add(border); return panel;
    }

    private void Area(string name, IReadOnlyList<int> fields)
    {
        var panel = DropArea(name);
        for (var i = 0; i < fields.Count; i++)
        {
            var index = i;
            var row = new Grid { ColumnSpacing = 3 };
            row.ColumnDefinitions.Add(new ColumnDefinition()); for (var c = 0; c < 3; c++) row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(25) });
            var label = OfficeTheme.Label(_fields[fields[i]], 12); label.TextTrimming = TextTrimming.CharacterEllipsis; row.Children.Add(label);
            Add(Button("Pivot " + name + " up " + index, "↑", () => Move(name, index, -1)), 1);
            Add(Button("Pivot " + name + " down " + index, "↓", () => Move(name, index, 1)), 2);
            Add(Button("Pivot " + name + " remove " + index, "×", () => Change(p => name == "Rows" ? p with { Rows = p.Rows.Where((_, n) => n != index).ToList() } : p with { Columns = p.Columns.Where((_, n) => n != index).ToList() })), 3);
            panel.Children.Add(row);
            void Add(UIElement element, int column) { Grid.SetColumn(element, column); row.Children.Add(element); }
        }
        if (fields.Count == 0) panel.Children.Add(OfficeTheme.Label("Drop a field here", 11, "#777777"));
    }

    private void ValueArea()
    {
        var panel = DropArea("Values");
        for (var i = 0; i < _document.Values.Count; i++)
        {
            var index = i; var value = _document.Values[i];
            panel.Children.Add(OfficeTheme.Label(_fields[value.Field], 12));
            var aggregate = OfficeForm.EnumChoice("Pivot value " + i + " aggregate", value.Aggregate);
            aggregate.SelectionChanged += (_, _) => UpdateValue(index, v => v with { Aggregate = OfficeForm.Value<PivotAggregate>(aggregate) });
            panel.Children.Add(aggregate);
            var show = OfficeForm.EnumChoice("Pivot value " + i + " show as", value.ShowAs);
            show.SelectionChanged += (_, _) => UpdateValue(index, v => v with { ShowAs = OfficeForm.Value<PivotShowAs>(show) }); panel.Children.Add(show);
            var caption = OfficeTheme.Field("Pivot value " + i + " caption"); caption.Text = value.Caption; caption.PlaceholderText = "Automatic caption";
            caption.LostFocus += (_, _) => { if (!_loading && caption.Text != value.Caption) UpdateValue(index, v => v with { Caption = caption.Text }); };
            panel.Children.Add(caption);
            var format = OfficeTheme.Field("Pivot value " + i + " format"); format.Text = value.NumberFormat;
            format.LostFocus += (_, _) => { if (!_loading && format.Text != value.NumberFormat) UpdateValue(index, v => v with { NumberFormat = format.Text }); };
            panel.Children.Add(format);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            buttons.Children.Add(Button("Pivot Values up " + i, "↑", () => Move("Values", index, -1)));
            buttons.Children.Add(Button("Pivot Values down " + i, "↓", () => Move("Values", index, 1)));
            buttons.Children.Add(Button("Pivot Values remove " + i, "Remove", () => { if (_document.Values.Count > 1) Change(p => p with { Values = p.Values.Where((_, n) => n != index).ToList() }); }));
            panel.Children.Add(buttons);
        }
    }

    private void FilterArea()
    {
        var panel = DropArea("Filters");
        foreach (var filter in _document.Filters)
        {
            panel.Children.Add(OfficeTheme.Label(_fields[filter.Field], 12));
            var available = (GetFieldValues?.Invoke(filter.Field) ?? filter.Values).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
            var accepted = filter.Values.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var search = OfficeTheme.Field("Pivot filter " + filter.Field + " search"); search.PlaceholderText = "Search values"; panel.Children.Add(search);
            var choices = new ListView { SelectionMode = ListViewSelectionMode.Multiple, IsMultiSelectCheckBoxEnabled = true, Height = 110 };
            void Populate()
            {
                var visible = available.Where(v => v.Contains(search.Text, StringComparison.OrdinalIgnoreCase)).ToArray();
                choices.ItemsSource = visible;
                foreach (var value in visible) if (accepted.Contains(value)) choices.SelectedItems.Add(value);
            }
            var changing = false;
            choices.SelectionChanged += (_, e) => { if (_loading || changing) return; foreach (string item in e.AddedItems) accepted.Add(item); foreach (string item in e.RemovedItems) accepted.Remove(item); };
            search.TextChanged += (_, _) => { changing = true; try { Populate(); } finally { changing = false; } };
            Populate(); panel.Children.Add(choices);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            buttons.Children.Add(Button("Pivot filter " + filter.Field + " apply", "Apply", () => Change(p => p with { Filters = p.Filters.Select(f => f.Field == filter.Field ? f with { Values = accepted.ToArray() } : f).ToList() })));
            buttons.Children.Add(Button("Pivot filter " + filter.Field + " remove", "Remove", () => Change(p => p with { Filters = p.Filters.Where(f => f.Field != filter.Field).ToList() })));
            panel.Children.Add(buttons);
        }
    }

    private void Assign(int field, string area)
    {
        if (_loading) return;
        Change(p =>
        {
            if (area != "Values") { p.Rows.Remove(field); p.Columns.Remove(field); p.Filters.RemoveAll(f => f.Field == field); }
            switch (area)
            {
                case "Rows": p.Rows.Add(field); break;
                case "Columns": p.Columns.Add(field); break;
                case "Values":
                    p.Values.Add(new PivotValueField { Field = field, Aggregate = PivotAggregate.Sum,
                        Caption = p.Values.Any(v => v.Field == field && v.Aggregate == PivotAggregate.Sum) ? "Sum of " + _fields[field] + " " + (p.Values.Count + 1) : "" });
                    break;
                case "Filters": p.Filters.Add(new PivotFilter { Field = field, Values = (GetFieldValues?.Invoke(field) ?? []).ToArray() }); break;
            }
            return p;
        });
    }
    private void Move(string area, int index, int delta)
    {
        var count = area == "Rows" ? _document.Rows.Count : area == "Columns" ? _document.Columns.Count : _document.Values.Count;
        var target = index + delta; if (target < 0 || target >= count) return;
        Change(p =>
        {
            if (area == "Values") (p.Values[index], p.Values[target]) = (p.Values[target], p.Values[index]);
            else { var items = area == "Rows" ? p.Rows : p.Columns; (items[index], items[target]) = (items[target], items[index]); }
            return p;
        });
    }
    private void UpdateValue(int index, Func<PivotValueField, PivotValueField> edit) => Change(p =>
    { if (index < p.Values.Count) p.Values[index] = edit(p.Values[index]); return p; });
    private void Change(Func<PivotTableSpec, PivotTableSpec> edit)
    {
        if (_loading) return;
        try
        {
            var next = edit(_document.CloneDocument()); next.Validate();
            if (CommitChanges?.Invoke(next) != true) { Build(); return; }
            _document = next; Build();
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or FormatException)
        {
            ToolTipService.SetToolTip(this, error.Message);
        }
    }
    private void Check(string name, bool initial, Action<bool> commit)
    {
        var box = new CheckBox { Content = name, IsChecked = initial, FontSize = 12, MinHeight = 24 };
        AutomationProperties.SetAutomationId(box, name.Replace(" ", ""));
        box.Checked += (_, _) => { if (!_loading) commit(true); }; box.Unchecked += (_, _) => { if (!_loading) commit(false); };
        _root.Children.Add(box);
    }
    private static OfficeButton Button(string id, string label, Action action)
    {
        var button = new OfficeButton(label, action); AutomationProperties.SetAutomationId(button, id.Replace(" ", "")); return button;
    }
}
