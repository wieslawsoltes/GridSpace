using GridSpace.Core;
using GridSpace.Editing;
using GridSpace.Layout;

namespace GridSpace.Controls;

public sealed partial class SpreadsheetGrid
{
    private sealed class SourceDrag(SpreadsheetSession owner, Worksheet sheet, ChartSpec original,
        ChartSourceHit hit, uint pointerId, Point point, CellAddress pointerCell, double zoom)
    {
        public SpreadsheetSession Owner { get; } = owner;
        public Worksheet Sheet { get; } = sheet;
        public ChartSpec Original { get; } = original;
        public ChartSourceHit Hit { get; } = hit;
        public uint PointerId { get; } = pointerId;
        public Point Start { get; } = point;
        public CellAddress PointerCell { get; } = pointerCell;
        public double Zoom { get; } = zoom;
        public Point Latest { get; set; } = point;
        public bool Moved { get; set; }
        public CellRange? Range { get; set; }
        public string? Error { get; set; }
    }

    private readonly DispatcherTimer _sourceScrollTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private SourceDrag? _sourceDrag;
    private bool _sourceEventsAttached;
    public bool IsChartSourceEditing => _sourceDrag is not null;
    public string? ChartSourcePreviewRange => _sourceDrag?.Range?.ToString();
    public string? ChartSourcePreviewError => _sourceDrag?.Error;

    private void EnsureChartSourceEvents()
    {
        if (_sourceEventsAttached) return;
        _sourceEventsAttached = true;
        _canvas.PointerCaptureLost += (_, _) => CancelChartSourceGesture();
        _sourceScrollTimer.Tick += (_, _) => ScrollChartSource();
        Unloaded += (_, _) => CancelChartSourceGesture();
    }

    private bool TryChartSourcePressed(PointerRoutedEventArgs e)
    {
        if (Session is null || SelectedChart is not { } chart) return false;
        var point = e.GetCurrentPoint(_canvas);
        if (!point.Properties.IsLeftButtonPressed) return false;
        var hit = ChartSourceGeometry.HitTest(Renderer.SourceBindings(Session), Viewport, point.Position.X, point.Position.Y);
        if (hit is null) return false;
        var cell = Viewport.HitTest(point.Position.X, point.Position.Y);
        if (cell.Kind != GridHitKind.Cell) return false;
        EnsureChartSourceEvents(); FocusGrid();
        _sourceDrag = new(Session, Session.Sheet, chart, hit, e.Pointer.PointerId, point.Position, new(cell.Row, cell.Column), Viewport.Zoom);
        Session.Changed += SourceOwnerChanged;
        if (!_canvas.CapturePointer(e.Pointer)) CancelChartSourceGesture();
        e.Handled = true;
        return true;
    }

    private void SourceOwnerChanged(object? sender, SessionChangedEventArgs args)
    {
        if (_sourceDrag is { } drag && (args.DocumentChanged || !ReferenceEquals(drag.Sheet, drag.Owner.Sheet)))
            CancelChartSourceGesture();
    }

    private bool TryChartSourceMoved(PointerRoutedEventArgs e)
    {
        if (_sourceDrag is not { } drag) return false;
        e.Handled = true;
        if (e.Pointer.PointerId != drag.PointerId) return true;
        drag.Latest = e.GetCurrentPoint(_canvas).Position;
        if (!drag.Moved && Math.Abs(drag.Latest.X - drag.Start.X) + Math.Abs(drag.Latest.Y - drag.Start.Y) < 6) return true;
        drag.Moved = true;
        _sourceScrollTimer.Start();
        UpdateChartSourcePreview();
        return true;
    }

    private bool TryChartSourceReleased(PointerRoutedEventArgs e)
    {
        if (_sourceDrag is not { } drag) return false;
        e.Handled = true;
        if (e.Pointer.PointerId != drag.PointerId) return true;
        drag.Latest = e.GetCurrentPoint(_canvas).Position;
        if (drag.Moved) UpdateChartSourcePreview();
        var inside = drag.Latest.X >= GridViewport.RowHeaderWidth && drag.Latest.X < Viewport.Width &&
            drag.Latest.Y >= GridViewport.ColumnHeaderHeight && drag.Latest.Y < Viewport.Height;
        var range = drag.Range; var error = drag.Error;
        var ownerMatches = ReferenceEquals(Session, drag.Owner) && ReferenceEquals(Session?.Sheet, drag.Sheet);
        CancelChartSourceGesture();
        if (inside && drag.Moved && ownerMatches)
        {
            if (error is not null) Error?.Invoke(error);
            else if (range is { } changed) Run(() => drag.Owner.CommitChartSourceEdit(drag.Original, drag.Hit.Binding, changed));
        }
        FocusGrid(); Invalidate();
        return true;
    }

    private void UpdateChartSourcePreview()
    {
        if (_sourceDrag is not { } drag || !drag.Moved) return;
        if (!ReferenceEquals(Session, drag.Owner) || !ReferenceEquals(drag.Sheet, drag.Owner.Sheet) ||
            !ReferenceEquals(drag.Owner.FindChart(drag.Original.Id), drag.Original) || drag.Zoom != Viewport.Zoom)
        { CancelChartSourceGesture(); return; }
        var x = Math.Clamp(drag.Latest.X, GridViewport.RowHeaderWidth, Math.Max(GridViewport.RowHeaderWidth, Viewport.Width - 1));
        var y = Math.Clamp(drag.Latest.Y, GridViewport.ColumnHeaderHeight, Math.Max(GridViewport.ColumnHeaderHeight, Viewport.Height - 1));
        var cell = Viewport.HitTest(x, y);
        if (cell.Kind != GridHitKind.Cell) return;
        var range = ChartSourceEditing.Transform(drag.Hit.Binding, drag.Hit.Handle,
            cell.Row - drag.PointerCell.Row, cell.Column - drag.PointerCell.Column);
        if (drag.Range == range) return; // No new chart/value snapshot for movement within the same snapped cell.
        drag.Range = range;
        try
        {
            Renderer.ChartSourcePreview = ChartSourceEditing.Replace(drag.Original, drag.Sheet.Name, drag.Hit.Binding, range);
            drag.Error = null;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or FormatException)
        {
            drag.Error = error.Message;
            Renderer.ChartSourcePreview = null; // Do not commit an earlier valid preview after an invalid endpoint.
        }
        Invalidate();
    }

    private void ScrollChartSource()
    {
        if (_sourceDrag is not { Moved: true } drag) { _sourceScrollTimer.Stop(); return; }
        if (Viewport.Width <= GridViewport.RowHeaderWidth || Viewport.Height <= GridViewport.ColumnHeaderHeight) return;
        var dx = drag.Latest.X > Viewport.Width - 20 ? 12 :
            drag.Latest.X < GridViewport.RowHeaderWidth + Viewport.FrozenWidth + 20 && drag.Latest.X >= GridViewport.RowHeaderWidth + Viewport.FrozenWidth ? -12 : 0;
        var dy = drag.Latest.Y > Viewport.Height - 20 ? 12 :
            drag.Latest.Y < GridViewport.ColumnHeaderHeight + Viewport.FrozenHeight + 20 && drag.Latest.Y >= GridViewport.ColumnHeaderHeight + Viewport.FrozenHeight ? -12 : 0;
        if (dx == 0 && dy == 0) return;
        var oldX = Viewport.ScrollX; var oldY = Viewport.ScrollY;
        Viewport.ScrollTo(oldX + dx / Viewport.Zoom, oldY + dy / Viewport.Zoom);
        if (oldX == Viewport.ScrollX && oldY == Viewport.ScrollY) return;
        UpdateChartSourcePreview(); Invalidate();
    }

    private void CancelChartSourceGesture()
    {
        _sourceScrollTimer.Stop();
        if (_sourceDrag is not { } drag) return;
        _sourceDrag = null;
        drag.Owner.Changed -= SourceOwnerChanged;
        Renderer.ChartSourcePreview = null;
        _canvas.ReleasePointerCaptures();
        _canvas.Invalidate(); ViewChanged?.Invoke();
    }
}
