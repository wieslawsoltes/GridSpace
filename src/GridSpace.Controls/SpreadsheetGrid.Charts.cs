using GridSpace.Core;
using GridSpace.Layout;

namespace GridSpace.Controls;

public sealed partial class SpreadsheetGrid
{
    private ChartSpec? _chartDragStart;
    private GridRect _chartDragBounds;
    private ChartHitPart _chartDragPart;
    private Point _chartDragPointer;
    private TextBox? _chartTitleEditor;
    private bool _endingChartTitle;
    public string? SelectedChartId { get; private set; }
    public ChartSpec? SelectedChart => Session?.FindChart(SelectedChartId);
    public event Action<string?>? ChartSelectionChanged;
    public event Action<CellAddress>? PivotDrillDownRequested;

    public void SelectChart(string? id)
    {
        id = Session?.FindChart(id)?.Id;
        if (id == SelectedChartId) return;
        CancelChartTitle(); CancelChartGesture();
        SelectedChartId = id; Renderer.SelectedChartId = id;
        ChartSelectionChanged?.Invoke(id); Invalidate();
    }

    /// <summary>Reveals a selected drawing without changing the cell selection. Call after host layout changes.</summary>
    public void RevealChart()
    {
        if (SelectedChart is not { } chart) return;
        var bounds = ChartGeometry.SheetBounds(chart, Viewport);
        var width = Math.Max(1, Viewport.Width - GridViewport.RowHeaderWidth) / Viewport.Zoom;
        var height = Math.Max(1, Viewport.Height - GridViewport.ColumnHeaderHeight) / Viewport.Zoom;
        var x = Viewport.ScrollX; var y = Viewport.ScrollY;
        var frozenX = Viewport.Columns.Position(Viewport.FrozenColumns);
        var frozenY = Viewport.Rows.Position(Viewport.FrozenRows);
        if (chart.Column >= Viewport.FrozenColumns)
        {
            if (bounds.Right > x + width) x = bounds.Right - width + 8 / Viewport.Zoom;
            if (bounds.X < x + frozenX) x = bounds.X - frozenX - 8 / Viewport.Zoom;
        }
        if (chart.Row >= Viewport.FrozenRows)
        {
            if (bounds.Bottom > y + height) y = bounds.Bottom - height + 8 / Viewport.Zoom;
            if (bounds.Y < y + frozenY) y = bounds.Y - frozenY - 8 / Viewport.Zoom;
        }
        Viewport.ScrollTo(x, y); Invalidate();
    }

    private bool TryChartPressed(PointerRoutedEventArgs e)
    {
        if (Session is null) return false;
        var point = e.GetCurrentPoint(_canvas);
        var hit = ChartGeometry.HitTest(Session.Sheet.Charts, Viewport, point.Position.X, point.Position.Y, SelectedChartId);
        if (hit is null) return false;
        SelectChart(hit.Value.Id);
        if (point.Properties.IsRightButtonPressed) { CommandRequested?.Invoke("chart-format"); e.Handled = true; return true; }
        FocusGrid();
        _chartDragStart = SelectedChart!.CloneDocument();
        _chartDragBounds = ChartGeometry.SheetBounds(_chartDragStart, Viewport);
        _chartDragPart = hit.Value.Part;
        _chartDragPointer = point.Position;
        _canvas.CapturePointer(e.Pointer); e.Handled = true;
        return true;
    }

    private bool TryChartMoved(PointerRoutedEventArgs e)
    {
        if (_chartDragStart is null) return false;
        var point = e.GetCurrentPoint(_canvas).Position;
        var bounds = ChartGeometry.Transform(_chartDragBounds, _chartDragPart,
            (point.X - _chartDragPointer.X) / Viewport.Zoom, (point.Y - _chartDragPointer.Y) / Viewport.Zoom,
            e.KeyModifiers.HasFlag(VirtualKeyModifiers.Shift));
        Renderer.ChartPreview = ChartGeometry.Place(_chartDragStart, bounds, Viewport);
        _canvas.Invalidate(); ViewChanged?.Invoke(); e.Handled = true;
        return true;
    }

    private bool TryChartReleased(PointerRoutedEventArgs e)
    {
        if (_chartDragStart is null) return false;
        var preview = Renderer.ChartPreview; var kind = _chartDragPart;
        _chartDragStart = null; Renderer.ChartPreview = null;
        _canvas.ReleasePointerCaptures();
        if (preview is not null && Session is not null)
            Run(() => Session.UpdateChart(preview.Id, _ => preview, kind is ChartHitPart.Body or ChartHitPart.Title ? "Move chart" : "Resize chart"));
        Invalidate(); e.Handled = true;
        return true;
    }

    private void CancelChartGesture()
    {
        if (_chartDragStart is null) return;
        _chartDragStart = null; Renderer.ChartPreview = null; _canvas.ReleasePointerCaptures(); _canvas.Invalidate();
    }

    private bool TryChartDoubleTap(Point point)
    {
        if (Session is null) return false;
        var hit = ChartGeometry.HitTest(Session.Sheet.Charts, Viewport, point.X, point.Y, SelectedChartId);
        if (hit is null) return false;
        SelectChart(hit.Value.Id);
        if (hit.Value.Part == ChartHitPart.Title) BeginChartTitleEdit();
        else CommandRequested?.Invoke("chart-format");
        return true;
    }

    public void BeginChartTitleEdit()
    {
        var chart = SelectedChart;
        if (chart is null || !CommitEdit()) return;
        CancelChartGesture();
        _chartTitleEditor = OfficeTheme.Field("Chart title editor");
        _chartTitleEditor.Text = chart.Title;
        _chartTitleEditor.FontSize = 16 * Viewport.Zoom;
        _chartTitleEditor.BorderBrush = OfficeTheme.Brush("#107C41");
        _chartTitleEditor.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Escape) { CancelChartTitle(); FocusGrid(); e.Handled = true; }
            else if (e.Key == VirtualKey.Enter) { if (CommitChartTitle()) FocusGrid(); e.Handled = true; }
        };
        _chartTitleEditor.LostFocus += (_, _) => { if (!_endingChartTitle) CommitChartTitle(); };
        _overlay.Children.Add(_chartTitleEditor);
        PositionChartTitle(); _chartTitleEditor.Focus(FocusState.Programmatic); _chartTitleEditor.SelectAll();
    }

    private void PositionChartTitle()
    {
        if (_chartTitleEditor is null || SelectedChart is not { } chart) return;
        foreach (var pane in Viewport.Panes())
        {
            var bounds = ChartGeometry.Bounds(chart, Viewport, pane);
            if (!bounds.Intersects(pane.Clip)) continue;
            Canvas.SetLeft(_chartTitleEditor, Math.Max(pane.Clip.X, bounds.X + 8 * Viewport.Zoom));
            Canvas.SetTop(_chartTitleEditor, Math.Max(pane.Clip.Y, bounds.Y + 4 * Viewport.Zoom));
            _chartTitleEditor.Width = Math.Max(40, Math.Min(bounds.Width - 16 * Viewport.Zoom, pane.Clip.Right - Math.Max(pane.Clip.X, bounds.X + 8 * Viewport.Zoom)));
            _chartTitleEditor.Height = Math.Max(28, 34 * Viewport.Zoom);
            break;
        }
    }

    private bool CommitChartTitle()
    {
        if (_chartTitleEditor is null || _endingChartTitle) return true;
        _endingChartTitle = true;
        try
        {
            var text = _chartTitleEditor.Text;
            if (SelectedChartId is { } id) Session?.UpdateChart(id, chart => chart with { Title = text }, "Edit chart title");
            _overlay.Children.Remove(_chartTitleEditor); _chartTitleEditor = null;
            return true;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException) { Error?.Invoke(error.Message); return false; }
        finally { _endingChartTitle = false; }
    }

    private void CancelChartTitle()
    {
        _endingChartTitle = true;
        if (_chartTitleEditor is not null) _overlay.Children.Remove(_chartTitleEditor);
        _chartTitleEditor = null; _endingChartTitle = false;
    }

    private bool HandleChartKey(KeyRoutedEventArgs e)
    {
        if (SelectedChart is not { } chart || Session is null) return false;
        var control = Down(VirtualKey.Control) || Down(VirtualKey.LeftWindows) || Down(VirtualKey.RightWindows);
        if (e.Key == VirtualKey.Escape) { if (_chartDragStart is not null) CancelChartGesture(); else SelectChart(null); }
        else if (e.Key == VirtualKey.Delete || e.Key == VirtualKey.Back) { Session.DeleteChart(chart.Id); SelectChart(null); }
        else if (e.Key == VirtualKey.F2) BeginChartTitleEdit();
        else if (control && e.Key == VirtualKey.D) SelectChart(Session.DuplicateChart(chart.Id));
        else if (e.Key is VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down)
        {
            var amount = Down(VirtualKey.Shift) ? 10 : 1;
            var dx = e.Key == VirtualKey.Left ? -amount : e.Key == VirtualKey.Right ? amount : 0;
            var dy = e.Key == VirtualKey.Up ? -amount : e.Key == VirtualKey.Down ? amount : 0;
            var bounds = ChartGeometry.Transform(ChartGeometry.SheetBounds(chart, Viewport), ChartHitPart.Body, dx, dy);
            Session.UpdateChart(chart.Id, c => ChartGeometry.Place(c, bounds, Viewport), "Nudge chart");
        }
        else return false;
        e.Handled = true; return true;
    }
}
