using GridSpace.Formulas;
using GridSpace.Layout;

namespace GridSpace.Workbench;

public sealed partial class SpreadsheetWorkbench
{
    private readonly Border _analytics = new() { Width = 320, Background = OfficeTheme.Brush("#FFFFFF"), BorderBrush = OfficeTheme.Brush("#D9D9D9"), BorderThickness = new Thickness(1, 0, 0, 0), Visibility = Visibility.Collapsed };
    private ChartEditorControl? _chartEditor;
    private PivotFieldListControl? _pivotEditor;
    private string? _inspectedPivotId;
    private bool _applyingInspector;
    private ChartSpec? _inspectedChart;
    private PivotTableSpec? _inspectedPivot;
    private string? _contextTab;
    private string? _lastSelectedPivot;

    private UIElement AnalyticsSurface()
    {
        var root = new Grid(); root.ColumnDefinitions.Add(new ColumnDefinition()); root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.Children.Add(Surface); Grid.SetColumn(_analytics, 1); root.Children.Add(_analytics);
        Surface.ChartSelectionChanged += ChartSelected;
        Surface.PivotDrillDownRequested += address => Try(() => { Session.DrillDownPivot(address); HideAnalytics(); Surface.RevealSelection(); });
        return root;
    }

    private void ChartSelected(string? id)
    {
        if (id is null)
        {
            if (_chartEditor is not null) HideAnalytics();
            return;
        }
        ShowChartInspector();
    }

    private void ShowChartInspector()
    {
        if (Surface.SelectedChart is not { } chart) return;
        _pivotEditor = null; _inspectedPivotId = null;
        _chartEditor ??= new ChartEditorControl();
        _chartEditor.CloseRequested -= HideAnalytics; _chartEditor.CloseRequested += HideAnalytics;
        _chartEditor.CommitChanges = next =>
        {
            _applyingInspector = true;
            try { return Try(() => Session.UpdateChart(next.Id, _ => next)); }
            finally { _applyingInspector = false; Surface.Invalidate(); }
        };
        _analytics.Child = _chartEditor; _analytics.Visibility = Visibility.Visible;
        BindChartInspector(chart);
        ContextTab("Chart Design");
    }

    private void BindChartInspector(ChartSpec chart)
    {
        _inspectedChart = chart;
        ChartData? data = null;
        try { data = Surface.Renderer.Charts.Data.Get(Session.Book, Session.Sheet, chart, Session.Calculation); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or FormatException) { ShowStatus(error.Message, true); }
        _chartEditor?.Bind(chart, Session.Sheet.Name, Session.Book.Sheets.Select(s => s.Name), data);
    }

    private void ShowPivotInspector(PivotTableSpec? pivot = null)
    {
        pivot ??= Session.ActivePivot;
        if (pivot is null) throw new InvalidOperationException("Select a PivotTable cell first.");
        Surface.SelectChart(null);
        _chartEditor = null; _inspectedChart = null; _inspectedPivotId = pivot.Id;
        _pivotEditor = new PivotFieldListControl();
        _pivotEditor.CloseRequested += HideAnalytics;
        _pivotEditor.RefreshRequested += () => RunCommand("pivot-refresh");
        _pivotEditor.ChartRequested += () => RunCommand("pivot-chart");
        _pivotEditor.RemoveRequested += () => RunCommand("pivot-remove");
        _pivotEditor.ExpandGroupsRequested += expanded => RunCommand(expanded ? "pivot-expand-all" : "pivot-collapse-all");
        _pivotEditor.GetFieldValues = field =>
        {
            var spec = Session.Sheet.PivotTables.First(p => p.Id == _inspectedPivotId);
            if (spec.Cache is null || spec.NeedsLayoutRefresh)
                throw new InvalidOperationException("Refresh this PivotTable before editing cached field filters.");
            return PivotFieldValues.Get(spec.Cache, field);
        };
        _pivotEditor.CommitChanges = next =>
        {
            _applyingInspector = true;
            try { return Try(() => Session.ReconfigurePivotTable(next)); }
            finally
            {
                _applyingInspector = false;
                DispatcherQueue.TryEnqueue(() => { UpdateAnalytics(); Surface.Invalidate(); });
            }
        };
        _inspectedPivot = pivot; _pivotEditor.Bind(pivot); _lastSelectedPivot = pivot.Id; _analytics.Child = _pivotEditor; _analytics.Visibility = Visibility.Visible; ContextTab("PivotTable Analyze");
    }

    private void HideAnalytics()
    {
        _analytics.Visibility = Visibility.Collapsed; _analytics.Child = null;
        _chartEditor = null; _pivotEditor = null; _inspectedChart = null; _inspectedPivotId = null; _inspectedPivot = null;
        ContextTab(null);
    }

    private void UpdateAnalytics()
    {
        if (_applyingInspector) return;
        var activePivot = Surface.SelectedChartId is null ? Session.ActivePivot : null;
        if (activePivot?.Id != _lastSelectedPivot)
        {
            _lastSelectedPivot = activePivot?.Id;
            if (activePivot is not null && _pivotEditor is null) ShowPivotInspector(activePivot);
        }
        if (_chartEditor is not null && Surface.SelectedChart is { } chart && !ReferenceEquals(chart, _inspectedChart))
            BindChartInspector(chart);
        if (_pivotEditor is not null && _inspectedPivotId is { } id)
        {
            var pivot = Session.Sheet.PivotTables.FirstOrDefault(p => p.Id == id);
            if (pivot is null) HideAnalytics();
            else if (!ReferenceEquals(pivot, _inspectedPivot)) { _inspectedPivot = pivot; _pivotEditor.Bind(pivot); }
        }
    }

    private void ContextTab(string? name)
    {
        if (_contextTab == name) return;
        _contextTab = name;
        var tabs = WorkbookRibbon.Create().ToList();
        if (name == "Chart Design") tabs.Add(new(name, [
            new("Chart", [new("chart-format", "Format Chart", OfficeIconKind.Chart, true), new("chart-title", "Edit Title", OfficeIconKind.Font, true),
                new("chart-duplicate", "Duplicate", OfficeIconKind.Copy, true), new("chart-delete", "Delete", OfficeIconKind.Clear, true)]),
            new("Layout", [new("chart-switch", "Switch Row/Column", OfficeIconKind.Grid, true), new("chart-labels", "Data Labels", OfficeIconKind.Font, true),
                new("chart-legend", "Legend", OfficeIconKind.Grid, true), new("chart-front", "Bring to Front", OfficeIconKind.Plus, true), new("chart-back", "Send to Back", OfficeIconKind.Grid, true)])]));
        if (name == "PivotTable Analyze") tabs.Add(new(name, [
            new("PivotTable", [new("pivot-fields", "Field List", OfficeIconKind.Grid, true), new("pivot-refresh", "Refresh", OfficeIconKind.Redo, true),
                new("pivot-chart", "PivotChart", OfficeIconKind.Chart, true), new("pivot-details", "Show Details", OfficeIconKind.Find, true), new("pivot-remove", "Remove", OfficeIconKind.Clear, true)]),
            new("Hierarchy", [new("pivot-toggle", "Expand/Collapse", OfficeIconKind.Plus, true),
                new("pivot-expand-all", "Expand All", OfficeIconKind.Plus, true), new("pivot-collapse-all", "Collapse All", OfficeIconKind.Grid, true)])]));
        _ribbon.SetTabs(tabs); _ribbon.SelectTab(name ?? "Home");
    }

    private bool ExecuteAnalytics(string id)
    {
        string ChartId() => Surface.SelectedChartId ?? throw new InvalidOperationException("Select a chart first.");
        string PivotId() => _inspectedPivotId ?? Session.ActivePivot?.Id ?? throw new InvalidOperationException("Select a PivotTable first.");
        switch (id)
        {
            case "chart-format": ShowChartInspector(); break;
            case "chart-title": Surface.BeginChartTitleEdit(); break;
            case "chart-delete": Session.DeleteChart(ChartId()); Surface.SelectChart(null); break;
            case "chart-duplicate": Surface.SelectChart(Session.DuplicateChart(ChartId())); DispatcherQueue.TryEnqueue(Surface.RevealChart); break;
            case "chart-switch": Session.UpdateChart(ChartId(), c => c with { SeriesInRows = !c.SeriesInRows, Series = [], Categories = null }); break;
            case "chart-labels": Session.UpdateChart(ChartId(), c => c with { ShowDataLabels = !c.ShowDataLabels }); break;
            case "chart-legend": Session.UpdateChart(ChartId(), c => c with { Legend = c.Legend == ChartLegendPosition.None ? ChartLegendPosition.Bottom : ChartLegendPosition.None }); break;
            case "chart-front": Session.OrderChart(ChartId(), true); break;
            case "chart-back": Session.OrderChart(ChartId(), false); break;
            case "pivot": CreatePivot(); break;
            case "pivot-fields": ShowPivotInspector(); break;
            case "pivot-refresh": Session.RefreshPivotTable(PivotId()); ShowPivotInspector(Session.Sheet.PivotTables.First(p => p.Id == PivotId())); break;
            case "pivot-refresh-all": Session.RefreshAllPivotTables(); break;
            case "pivot-chart": Surface.SelectChart(Session.AddPivotChart(PivotId())); DispatcherQueue.TryEnqueue(Surface.RevealChart); break;
            case "pivot-details": Session.DrillDownPivot(Session.ActiveCell); HideAnalytics(); break;
            case "pivot-remove": Session.RemovePivotTable(PivotId()); HideAnalytics(); break;
            case "pivot-toggle": Surface.ToggleSelectedPivotGroup(); Surface.FocusGrid(); break;
            case "pivot-expand-all": Session.SetPivotGroupsExpanded(PivotId(), true); Surface.FocusGrid(); break;
            case "pivot-collapse-all": Session.SetPivotGroupsExpanded(PivotId(), false); Surface.FocusGrid(); break;
            default:
                if (!id.StartsWith("chart-", StringComparison.Ordinal) || !Enum.TryParse<ChartKind>(id[6..], true, out var kind)) return false;
                var range = Session.DataRange();
                var chart = new ChartSpec { Kind = kind, Range = range.ToString(), Row = range.Top + 1,
                    Column = Math.Min(range.Right + 1, CellAddress.MaxColumns - 1), Width = 560, Height = 330 };
                Session.AddChart(chart); Surface.SelectChart(chart.Id); DispatcherQueue.TryEnqueue(Surface.RevealChart);
                break;
        }
        Surface.Invalidate(); return true;
    }

    private void CreatePivot()
    {
        var source = Session.Sheet; var range = Session.DataRange();
        var width = range.Right - range.Left + 1;
        if (width < 2 || range.Bottom == range.Top) throw new InvalidOperationException("Select a table with named columns and at least one data row.");
        var numeric = Enumerable.Range(0, width).Where(c => Session.Calculation.Evaluate(source, new CellAddress(range.Top + 1, range.Left + c)).Kind == ValueKind.Number).ToArray();
        var value = numeric.LastOrDefault(width - 1);
        var row = Enumerable.Range(0, width).FirstOrDefault(c => c != value, 0);
        var number = 1;
        while (Session.Book.Sheets.SelectMany(s => s.PivotTables).Any(p => p.Name == "PivotTable" + number)) number++;
        var spec = new PivotTableSpec { Name = "PivotTable" + number, SourceSheet = source.Name, SourceRange = range.ToString(), Rows = [row],
            Values = [new PivotValueField { Field = value, Aggregate = numeric.Length > 0 ? PivotAggregate.Sum : PivotAggregate.Count }], Destination = "B3" };
        _ = PivotEngine.Calculate(Session.Book, spec, Session.Calculation);
        Session.Perform("Insert PivotTable", () =>
        {
            var sheetName = "Pivot " + number;
            while (Session.Book.FindSheet(sheetName) is not null) sheetName = "Pivot " + ++number;
            Session.AddSheet(sheetName);
            Session.SetPivotTable(spec); Session.Select(spec.Destination);
        });
        ShowPivotInspector(Session.Sheet.PivotTables.First(p => p.Id == spec.Id)); Surface.RevealSelection();
    }
}
