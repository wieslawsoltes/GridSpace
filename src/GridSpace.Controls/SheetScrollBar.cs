using SkiaSharp;
using Uno.WinUI.Graphics2DSK;

namespace GridSpace.Controls;

/// <summary>Compact, custom-rendered scrollbar with bounded thumb size and full worksheet-range precision.</summary>
public sealed class SheetScrollBar : UserControl
{
    private sealed class Track : SKCanvasElement
    {
        public Action<SKCanvas, Size>? Paint { get; set; }
        protected override void RenderOverride(SKCanvas canvas, Size area) => Paint?.Invoke(canvas, area);
    }
    private readonly Track _track = new();
    private bool _dragging;
    private double _start, _initial;
    public Orientation Orientation { get; set; }
    public double Maximum { get; private set; }
    public double ViewportSize { get; private set; }
    public double Value { get; private set; }
    public event Action<double>? ValueChanged;
    private double Length => Orientation == Orientation.Horizontal ? ActualWidth : ActualHeight;
    private double ThumbSize => Math.Min(Length, Math.Max(28, Length * ViewportSize / Math.Max(1, Maximum + ViewportSize)));
    private double ThumbPosition => Maximum <= 0 ? 0 : Value / Maximum * Math.Max(0, Length - ThumbSize);
    public SheetScrollBar()
    {
        Content = _track; IsTabStop = true;
        _track.Paint = Paint;
        _track.SizeChanged += (_, _) => _track.Invalidate();
        _track.PointerPressed += (_, e) =>
        {
            var p = e.GetCurrentPoint(_track).Position; var position = Orientation == Orientation.Horizontal ? p.X : p.Y;
            if (position < ThumbPosition || position > ThumbPosition + ThumbSize) Change(Value + (position < ThumbPosition ? -1 : 1) * ViewportSize * .9);
            else { _dragging = true; _start = position; _initial = Value; _track.CapturePointer(e.Pointer); }
            e.Handled = true;
        };
        _track.PointerMoved += (_, e) =>
        {
            if (!_dragging) return;
            var p = e.GetCurrentPoint(_track).Position; var position = Orientation == Orientation.Horizontal ? p.X : p.Y;
            Change(_initial + (position - _start) * Maximum / Math.Max(1, Length - ThumbSize)); e.Handled = true;
        };
        _track.PointerReleased += (_, _) => { _dragging = false; _track.ReleasePointerCaptures(); };
        _track.PointerCanceled += (_, _) => _dragging = false;
        _track.PointerCaptureLost += (_, _) => _dragging = false;
        KeyDown += (_, e) => { if (e.Key is VirtualKey.Left or VirtualKey.Up) { Change(Value - 24); e.Handled = true; } else if (e.Key is VirtualKey.Right or VirtualKey.Down) { Change(Value + 24); e.Handled = true; } };
    }
    public void SetRange(double maximum, double viewport, double value) { Maximum = Math.Max(0, maximum); ViewportSize = Math.Max(0, viewport); Value = Math.Clamp(value, 0, Maximum); _track.Invalidate(); }
    private void Change(double value) { Value = Math.Clamp(value, 0, Maximum); _track.Invalidate(); ValueChanged?.Invoke(Value); }
    private void Paint(SKCanvas canvas, Size size)
    {
        canvas.Clear(SKColor.Parse("#F5F5F5"));
        if (Maximum <= 0) return;
        using var paint = new SKPaint { Color = SKColor.Parse(_dragging ? "#8B8B8B" : "#C2C2C2"), IsAntialias = true };
        var rect = Orientation == Orientation.Horizontal ? new SKRect((float)ThumbPosition + 2, 3, (float)(ThumbPosition + ThumbSize) - 2, (float)size.Height - 3) : new SKRect(3, (float)ThumbPosition + 2, (float)size.Width - 3, (float)(ThumbPosition + ThumbSize) - 2);
        canvas.DrawRoundRect(rect, 3, 3, paint);
    }
}
