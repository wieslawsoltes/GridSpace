namespace GridSpace.Controls;

/// <summary>
/// A button that keeps ordinary click, keyboard and automation behavior, but owns a
/// thresholded in-process drag. It does not depend on an OS/browser data-transfer service.
/// Drag positions are in <see cref="DragCoordinateRoot"/> coordinates.
/// </summary>
public sealed class OfficeDragButton : OfficeButton
{
    private Pointer? _pointer;
    private Point _start;
    private bool _dragging;
    private bool _releasing;
    private bool _suppressClick;

    public FrameworkElement? DragCoordinateRoot { get; set; }
    public bool IsDragging => _dragging;
    public event Action<Point>? DragPreview;
    public event Action<Point>? DragCommitted;
    public event Action? DragCancelled;

    public OfficeDragButton()
    {
        Unloaded += (_, _) => CancelDrag();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != VirtualKey.Escape || _pointer is null) return;
            CancelDrag(); e.Handled = true;
        };
    }

    protected override void OnPointerPressed(PointerRoutedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_pointer is not null || !IsEnabled || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _start = Position(e);
        if (CapturePointer(e.Pointer)) _pointer = e.Pointer;
    }

    protected override void OnPointerMoved(PointerRoutedEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_pointer?.PointerId != e.Pointer.PointerId) return;
        var point = Position(e);
        var dx = point.X - _start.X;
        var dy = point.Y - _start.Y;
        if (!_dragging && dx * dx + dy * dy < 36) return;
        _dragging = true;
        DragPreview?.Invoke(point);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerRoutedEventArgs e)
    {
        if (_pointer?.PointerId != e.Pointer.PointerId) { base.OnPointerReleased(e); return; }
        var point = Position(e);
        var commit = _dragging;
        _releasing = true;
        _suppressClick = commit;
        try
        {
            // Release native button state/capture without letting a drag also raise Click.
            base.OnPointerReleased(e);
            ReleasePointerCaptures();
        }
        finally
        {
            _pointer = null; _dragging = false; _releasing = false; _suppressClick = false;
        }
        if (commit) { DragCommitted?.Invoke(point); e.Handled = true; }
    }

    protected override void OnPointerCaptureLost(PointerRoutedEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (!_releasing) CancelDrag();
    }

    protected override void OnPointerCanceled(PointerRoutedEventArgs e)
    {
        base.OnPointerCanceled(e);
        CancelDrag();
    }

    protected override void OnClick()
    {
        if (!_dragging && !_suppressClick) base.OnClick();
    }

    public void CancelDrag()
    {
        if (_pointer is null) return;
        _pointer = null;
        _dragging = false;
        _suppressClick = true;
        try { ReleasePointerCaptures(); DragCancelled?.Invoke(); }
        finally { _suppressClick = false; }
    }

    private Point Position(PointerRoutedEventArgs e) => e.GetCurrentPoint(DragCoordinateRoot ?? this).Position;
}
