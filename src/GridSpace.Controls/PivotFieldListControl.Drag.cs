namespace GridSpace.Controls;

public sealed partial class PivotFieldListControl
{
    private readonly ScrollViewer _scroll;
    private readonly Dictionary<string, Border> _dropAreas = new();
    private readonly DispatcherTimer _dragScrollTimer = new() { Interval = TimeSpan.FromMilliseconds(32) };
    private Point? _dragPoint;
    private string? _highlightedArea;

    private void PreviewFieldDrag(Point point)
    {
        _dragPoint = point;
        HighlightArea(FindDropArea(point));
        _dragScrollTimer.Start();
    }

    private void CommitFieldDrag(int field, Point point)
    {
        var area = FindDropArea(point);
        ClearFieldDrag();
        if (area is not null) Assign(field, area);
    }

    private string? FindDropArea(Point point)
    {
        // A scrolled-off area must not be a drop target outside the inspector's clip.
        if (point.X < 0 || point.Y < 0 || point.X >= ActualWidth || point.Y >= ActualHeight) return null;
        foreach (var (name, border) in _dropAreas)
        {
            var origin = border.TransformToVisual(this).TransformPoint(new Point());
            if (point.X >= origin.X && point.X < origin.X + border.ActualWidth &&
                point.Y >= origin.Y && point.Y < origin.Y + border.ActualHeight) return name;
        }
        return null;
    }

    private void HighlightArea(string? name)
    {
        if (_highlightedArea == name) return;
        foreach (var (area, border) in _dropAreas)
        {
            border.BorderBrush = OfficeTheme.Brush(area == name ? "#107C41" : "#CCCCCC");
            border.Background = OfficeTheme.Brush(area == name ? "#DCEDE2" : "#FAFAFA");
        }
        _highlightedArea = name;
    }

    private void ScrollFieldDrag()
    {
        if (_dragPoint is not { } point || point.X < 0 || point.X >= ActualWidth) return;
        var delta = point.Y >= 0 && point.Y < 30 ? -16 : point.Y > ActualHeight - 30 && point.Y < ActualHeight ? 16 : 0;
        if (delta == 0) return;
        _scroll.ChangeView(null, Math.Clamp(_scroll.VerticalOffset + delta, 0, _scroll.ScrollableHeight), null, true);
        HighlightArea(FindDropArea(point));
    }

    private void ClearFieldDrag()
    {
        _dragScrollTimer.Stop();
        _dragPoint = null;
        HighlightArea(null);
    }
}
