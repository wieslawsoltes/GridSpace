using GridSpace.Core;
using GridSpace.Editing;
using GridSpace.Layout;
using GridSpace.Skia;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using Windows.ApplicationModel.DataTransfer;
using Windows.Devices.Input;

namespace GridSpace.Controls;

/// <summary>Embeddable Uno/Skia spreadsheet. Every committed document edit uses SpreadsheetSession transactions.</summary>
public sealed partial class SpreadsheetGrid : UserControl, IDisposable
{
    private sealed class DrawingCanvas : SKCanvasElement
    {
        public Action<SKCanvas, Size>? Paint { get; set; }
        protected override void RenderOverride(SKCanvas canvas, Size area) => Paint?.Invoke(canvas, area);
    }
    private enum Gesture { None, Select, Fill, ColumnSize, RowSize, TouchPan }
    private readonly DrawingCanvas _canvas = new();
    private readonly Canvas _overlay = new();
    private readonly SheetScrollBar _horizontal = new() { Orientation = Orientation.Horizontal, Height = 14 };
    private readonly SheetScrollBar _vertical = new() { Orientation = Orientation.Vertical, Width = 14 };
    private SpreadsheetSession? _session;
    private TextBox? _editor;
    private CellAddress _editAddress;
    private Gesture _gesture;
    private CellRange _source;
    private Point _pointerStart;
    private double _resizeOriginal, _resizeValue, _panX, _panY;
    private int _resizeIndex;
    private bool _endingEdit, _disposed;
    private Worksheet? _geometrySheet;
    private long _geometryRevision = -1;
    public int LayoutRefreshCount { get; private set; }
    private void RefreshGeometry()
    {
        if (Session is null || ReferenceEquals(_geometrySheet, Session.Sheet) && _geometryRevision == Session.Book.StructureRevision) return;
        _geometrySheet = Session.Sheet; _geometryRevision = Session.Book.StructureRevision;
        Viewport.Refresh(Session.Sheet); LayoutRefreshCount++;
    }
    public GridViewport Viewport { get; } = new();
    public SpreadsheetRenderer Renderer { get; } = new();
    public event Action<string>? CommandRequested;
    public event Action<string>? Error;
    public event Action<Point>? ContextRequested;
    public event Action? ViewChanged;
    public bool IsEditing => _editor is not null;
    public SpreadsheetSession? Session
    {
        get => _session;
        set
        {
            if (_session == value) return;
            if (_session is not null) _session.Changed -= SessionChanged;
            CancelEdit(); CancelGesture(); _session = value;
            if (_session is not null) { _session.Changed += SessionChanged; RefreshGeometry(); }
            Invalidate();
        }
    }
    public SpreadsheetGrid()
    {
        IsTabStop = true; HorizontalContentAlignment = HorizontalAlignment.Stretch; VerticalContentAlignment = VerticalAlignment.Stretch;
        AutomationProperties.SetName(this, "Worksheet grid"); AutomationProperties.SetAutomationId(this, "WorksheetGrid");
        var root = new Grid(); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(14) });
        root.ColumnDefinitions.Add(new ColumnDefinition()); root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        var area = new Grid(); area.Children.Add(_canvas); area.Children.Add(_overlay); root.Children.Add(area);
        Grid.SetRow(_horizontal, 1); Grid.SetColumn(_vertical, 1); root.Children.Add(_horizontal); root.Children.Add(_vertical); Content = root;
        _canvas.Paint = (canvas, size) =>
        {
            Viewport.Width = size.Width; Viewport.Height = size.Height;
            if (Session is not null) Renderer.Render(canvas, Session, Viewport);
            if (_gesture is Gesture.ColumnSize or Gesture.RowSize)
            {
                using var pen = new SKPaint { Color = SKColor.Parse("#107C41"), StrokeWidth = 1, Style = SKPaintStyle.Stroke };
                var position = (_gesture == Gesture.ColumnSize ? _pointerStart.X : _pointerStart.Y) + (_resizeValue - _resizeOriginal) * Viewport.Zoom;
                if (_gesture == Gesture.ColumnSize) canvas.DrawLine((float)position, 0, (float)position, (float)size.Height, pen);
                else canvas.DrawLine(0, (float)position, (float)size.Width, (float)position, pen);
            }
        };
        _canvas.SizeChanged += (_, _) => { Viewport.Width = _canvas.ActualWidth; Viewport.Height = _canvas.ActualHeight; Viewport.ScrollTo(Viewport.ScrollX, Viewport.ScrollY); Invalidate(); };
        _horizontal.ValueChanged += value => ScrollTo(value, Viewport.ScrollY);
        _vertical.ValueChanged += value => ScrollTo(Viewport.ScrollX, value);
        _canvas.PointerPressed += Pressed; _canvas.PointerMoved += Moved; _canvas.PointerReleased += Released;
        _canvas.PointerCanceled += (_, _) => CancelGesture();
        _canvas.PointerCaptureLost += (_, _) => { if (_gesture != Gesture.None) CancelGesture(); };
        _canvas.PointerWheelChanged += Wheel;
        _canvas.DoubleTapped += (_, e) =>
        {
            if (Session is null) return;
            var p = e.GetPosition(_canvas); var hit = Viewport.HitTest(p.X, p.Y);
            if (hit.Kind == GridHitKind.ColumnHeader) Run(() => Session.SetColumnWidth(hit.Column, Renderer.MeasureColumn(Session, hit.Column)));
            else if (hit.Kind == GridHitKind.Cell) BeginEdit();
            e.Handled = true;
        };
        _canvas.RightTapped += (_, e) => { ContextRequested?.Invoke(e.GetPosition(this)); e.Handled = true; };
        KeyDown += HandleKey;
    }
    private void SessionChanged(object? sender, SessionChangedEventArgs args)
    {
        if (Session is null) return;
        RefreshGeometry();
        if (args.Reason is "Switch worksheet" or "Open workbook" or "Insert worksheet") { CancelEdit(); Viewport.ScrollTo(0, 0); }
        Invalidate();
        AutomationProperties.SetHelpText(this, Session.Sheet.Name + "!" + Session.Selection + ": " + Session.Calculation.Evaluate(Session.Sheet, Session.ActiveCell));
    }
    public void Invalidate()
    {
        _canvas.Invalidate();
        _horizontal.SetRange(Viewport.MaximumScrollX, Math.Max(1, Viewport.Width - GridViewport.RowHeaderWidth) / Viewport.Zoom, Viewport.ScrollX);
        _vertical.SetRange(Viewport.MaximumScrollY, Math.Max(1, Viewport.Height - GridViewport.ColumnHeaderHeight) / Viewport.Zoom, Viewport.ScrollY);
        PositionEditor(); ViewChanged?.Invoke();
    }
    public void FocusGrid() => Focus(FocusState.Programmatic);
    public void ScrollTo(double x, double y) { Viewport.ScrollTo(x, y); Invalidate(); }
    public void SetZoom(double zoom) { Viewport.Zoom = zoom; if (Session is not null) Viewport.EnsureVisible(Session.ActiveCell); Invalidate(); }
    public void RevealSelection() { if (Session is not null) Viewport.EnsureVisible(Session.ActiveCell); Invalidate(); }
    private void Run(Action action) { try { action(); } catch (Exception e) when (e is InvalidOperationException or ArgumentException or FormatException) { Error?.Invoke(e.Message); } }
    private void Pressed(object sender, PointerRoutedEventArgs e)
    {
        if (Session is null || !CommitEdit()) return;
        FocusGrid(); var point = e.GetCurrentPoint(_canvas); var p = point.Position; var hit = Viewport.HitTest(p.X, p.Y);
        if (hit.Kind == GridHitKind.None) return;
        _pointerStart = p; _source = Session.Selection; _gesture = Gesture.Select;
        var bounds = Viewport.CellBounds(new CellAddress(Math.Max(0, hit.Row), Math.Max(0, hit.Column)));
        if (hit.Kind == GridHitKind.ColumnHeader && Math.Abs(p.X - bounds.Right) < 5)
        {
            _gesture = Gesture.ColumnSize; _resizeIndex = hit.Column; _resizeOriginal = Viewport.Columns.Size(hit.Column); _resizeValue = _resizeOriginal;
        }
        else if (hit.Kind == GridHitKind.RowHeader && Math.Abs(p.Y - bounds.Bottom) < 5)
        {
            _gesture = Gesture.RowSize; _resizeIndex = hit.Row; _resizeOriginal = Viewport.Rows.Size(hit.Row); _resizeValue = _resizeOriginal;
        }
        else
        {
            var end = Viewport.CellBounds(new CellAddress(Session.Selection.Bottom, Session.Selection.Right));
            if (hit.Kind == GridHitKind.Cell && Math.Abs(p.X - end.Right) <= 6 && Math.Abs(p.Y - end.Bottom) <= 6) _gesture = Gesture.Fill;
            else
            {
                var address = new CellAddress(hit.Row, hit.Column);
                if (point.Properties.IsRightButtonPressed && Session.Selection.Contains(address)) { _gesture = Gesture.None; return; }
                if (hit.Kind == GridHitKind.Corner) Session.Select(new CellRange(new CellAddress(0, 0), new CellAddress(CellAddress.MaxRows - 1, CellAddress.MaxColumns - 1)));
                else if (hit.Kind == GridHitKind.ColumnHeader) Session.Select(new CellRange(new CellAddress(0, hit.Column), new CellAddress(CellAddress.MaxRows - 1, hit.Column)));
                else if (hit.Kind == GridHitKind.RowHeader) Session.Select(new CellRange(new CellAddress(hit.Row, 0), new CellAddress(hit.Row, CellAddress.MaxColumns - 1)));
                else { address = Session.Sheet.MergeAnchor(address); Session.Select(new CellRange(e.KeyModifiers.HasFlag(VirtualKeyModifiers.Shift) ? Session.Selection.Start : address, address)); }
                _source = Session.Selection;
                if (e.Pointer.PointerDeviceType == PointerDeviceType.Touch) { _gesture = Gesture.TouchPan; _panX = Viewport.ScrollX; _panY = Viewport.ScrollY; }
            }
        }
        _canvas.CapturePointer(e.Pointer); e.Handled = true;
    }
    private void Moved(object sender, PointerRoutedEventArgs e)
    {
        if (Session is null || _gesture == Gesture.None) return;
        var p = e.GetCurrentPoint(_canvas).Position;
        if (_gesture is Gesture.ColumnSize or Gesture.RowSize)
        {
            var delta = _gesture == Gesture.ColumnSize ? p.X - _pointerStart.X : p.Y - _pointerStart.Y;
            _resizeValue = Math.Clamp(_resizeOriginal + delta / Viewport.Zoom, _gesture == Gesture.ColumnSize ? 24 : 16, _gesture == Gesture.ColumnSize ? 1000 : 600); _canvas.Invalidate();
        }
        else if (_gesture == Gesture.TouchPan)
        {
            if (Math.Abs(p.X - _pointerStart.X) + Math.Abs(p.Y - _pointerStart.Y) > 8) ScrollTo(_panX - (p.X - _pointerStart.X) / Viewport.Zoom, _panY - (p.Y - _pointerStart.Y) / Viewport.Zoom);
        }
        else
        {
            if (p.X > Viewport.Width - 10) Viewport.ScrollTo(Viewport.ScrollX + 24 / Viewport.Zoom, Viewport.ScrollY);
            if (p.Y > Viewport.Height - 10) Viewport.ScrollTo(Viewport.ScrollX, Viewport.ScrollY + 24 / Viewport.Zoom);
            if (p.X < GridViewport.RowHeaderWidth + 5) Viewport.ScrollTo(Viewport.ScrollX - 24 / Viewport.Zoom, Viewport.ScrollY);
            if (p.Y < GridViewport.ColumnHeaderHeight + 5) Viewport.ScrollTo(Viewport.ScrollX, Viewport.ScrollY - 24 / Viewport.Zoom);
            var hit = Viewport.HitTest(Math.Clamp(p.X, GridViewport.RowHeaderWidth, Math.Max(GridViewport.RowHeaderWidth, Viewport.Width - 1)), Math.Clamp(p.Y, GridViewport.ColumnHeaderHeight, Math.Max(GridViewport.ColumnHeaderHeight, Viewport.Height - 1)));
            if (hit.Kind == GridHitKind.Cell)
            {
                var end = new CellAddress(hit.Row, hit.Column);
                if (_gesture == Gesture.Fill) Session.Select(new CellRange(new CellAddress(Math.Min(_source.Top, end.Row), Math.Min(_source.Left, end.Column)), new CellAddress(Math.Max(_source.Bottom, end.Row), Math.Max(_source.Right, end.Column))));
                else if (_source.Count <= 100000) Session.Select(new CellRange(_source.Start, end));
            }
            Invalidate();
        }
        e.Handled = true;
    }
    private void Released(object sender, PointerRoutedEventArgs e)
    {
        if (Session is null) return;
        var gesture = _gesture; _gesture = Gesture.None;
        if (gesture == Gesture.ColumnSize) Run(() => Session.SetColumnWidth(_resizeIndex, _resizeValue));
        else if (gesture == Gesture.RowSize) Run(() => Session.SetRowHeight(_resizeIndex, _resizeValue));
        else if (gesture == Gesture.Fill) { var destination = Session.Selection; Run(() => Session.Fill(_source, destination)); }
        _canvas.ReleasePointerCaptures(); Invalidate(); e.Handled = true;
    }
    private void CancelGesture()
    {
        var restore = _gesture == Gesture.Fill; _gesture = Gesture.None; _canvas.ReleasePointerCaptures();
        if (restore && Session is not null) Session.Select(_source); _canvas.Invalidate();
    }
    private void Wheel(object sender, PointerRoutedEventArgs e)
    {
        if (!CommitEdit()) return;
        var properties = e.GetCurrentPoint(_canvas).Properties; var delta = properties.MouseWheelDelta;
        if (e.KeyModifiers.HasFlag(VirtualKeyModifiers.Control)) SetZoom(Viewport.Zoom * Math.Pow(1.1, delta / 120d));
        else if (e.KeyModifiers.HasFlag(VirtualKeyModifiers.Shift) || properties.IsHorizontalMouseWheel) ScrollTo(Viewport.ScrollX - delta / Viewport.Zoom, Viewport.ScrollY);
        else ScrollTo(Viewport.ScrollX, Viewport.ScrollY - delta / Viewport.Zoom);
        e.Handled = true;
    }
    public void BeginEdit(string? initial = null)
    {
        if (Session is null || !CommitEdit()) return;
        if (Session.IsSpillFollower)
        {
            Error?.Invoke("You cannot change part of a spilled array. Edit " + Session.Calculation.GetSpill(Session.Sheet, Session.ActiveCell)!.Anchor + " instead.");
            return;
        }
        _editAddress = Session.ActiveCell; Viewport.EnsureVisible(_editAddress);
        _editor = OfficeTheme.Field("Cell editor"); _editor.Text = initial ?? Session.Sheet.Get(_editAddress).Input;
        _editor.FontSize = Session.SelectedStyle.FontSize * 96 / 72 * Viewport.Zoom;
        _editor.BorderBrush = OfficeTheme.Brush("#107C41"); _editor.BorderThickness = new Thickness(2);
        _editor.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Escape) { CancelEdit(); FocusGrid(); e.Handled = true; }
            else if (e.Key is VirtualKey.Enter or VirtualKey.Tab)
            {
                if (CommitEdit()) { Navigate(e.Key == VirtualKey.Enter ? 1 : 0, e.Key == VirtualKey.Tab ? 1 : 0, false); FocusGrid(); }
                e.Handled = true;
            }
        };
        _editor.LostFocus += (_, _) => { if (!_endingEdit) CommitEdit(); };
        _overlay.Children.Add(_editor); PositionEditor(); _editor.Focus(FocusState.Programmatic); _editor.SelectionStart = _editor.Text.Length;
        Invalidate();
    }
    private void PositionEditor()
    {
        if (_editor is null || Session is null) return;
        var rect = Viewport.CellBounds(_editAddress);
        var merge = Session.Sheet.Merges.FirstOrDefault(m => m.Contains(_editAddress));
        if (merge.Count > 1) rect = rect with { Width = (Viewport.Columns.Position(merge.Right + 1) - Viewport.Columns.Position(merge.Left)) * Viewport.Zoom, Height = (Viewport.Rows.Position(merge.Bottom + 1) - Viewport.Rows.Position(merge.Top)) * Viewport.Zoom };
        Canvas.SetLeft(_editor, Math.Max(GridViewport.RowHeaderWidth, rect.X)); Canvas.SetTop(_editor, Math.Max(GridViewport.ColumnHeaderHeight, rect.Y));
        _editor.Width = Math.Max(40, Math.Min(Viewport.Width - Math.Max(GridViewport.RowHeaderWidth, rect.X), Math.Max(rect.Width + 2, 120)));
        _editor.Height = Math.Max(26, rect.Height + 2);
    }
    public bool CommitEdit()
    {
        if (_editor is null || _endingEdit) return true;
        _endingEdit = true;
        try { Session?.SetInput(_editor.Text, _editAddress); _overlay.Children.Remove(_editor); _editor = null; return true; }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or FormatException) { Error?.Invoke(e.Message); return false; }
        finally { _endingEdit = false; }
    }
    public void CancelEdit() { _endingEdit = true; if (_editor is not null) _overlay.Children.Remove(_editor); _editor = null; _endingEdit = false; }
    public async Task CopyAsync(bool cut = false)
    {
        if (Session is null || !CommitEdit()) return;
        var copied = Session.Copy(); var data = new DataPackage(); data.SetText(copied.Text); Clipboard.SetContent(data);
        if (cut) Session.Clear();
    }
    public async Task PasteAsync(bool valuesOnly = false)
    {
        if (Session is null || !CommitEdit()) return;
        var clipboard = Clipboard.GetContent();
        var text = clipboard.Contains(StandardDataFormats.Text) ? await clipboard.GetTextAsync() : Session.Clipboard?.Text;
        if (text is not null) Session.Paste(text, valuesOnly);
    }
    private static bool Down(VirtualKey key) => (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
    private void HandleKey(object sender, KeyRoutedEventArgs e)
    {
        if (Session is null || IsEditing || e.OriginalSource is TextBox) return;
        var control = Down(VirtualKey.Control) || Down(VirtualKey.LeftWindows) || Down(VirtualKey.RightWindows);
        var shift = Down(VirtualKey.Shift);
        if (control)
        {
            var command = e.Key switch { VirtualKey.C => "copy", VirtualKey.X => "cut", VirtualKey.V => shift ? "paste-values" : "paste", VirtualKey.Z => shift ? "redo" : "undo", VirtualKey.Y => "redo", VirtualKey.B => "bold", VirtualKey.I => "italic", VirtualKey.U => "underline", VirtualKey.D => "fill-down", VirtualKey.R => "fill-right", VirtualKey.S => "save", VirtualKey.O => "open", VirtualKey.N => "new", VirtualKey.F => "find", VirtualKey.H => "replace", VirtualKey.Number1 => "format-cells", _ => null };
            if (command is not null) { CommandRequested?.Invoke(command); e.Handled = true; return; }
            if (e.Key == VirtualKey.Home) { Session.Select("A1"); RevealSelection(); e.Handled = true; return; }
            if (e.Key == VirtualKey.End) { var a = new CellAddress(Session.Sheet.UsedRange.Bottom, Session.Sheet.UsedRange.Right); Session.Select(new CellRange(a, a)); RevealSelection(); e.Handled = true; return; }
            if (e.Key is VirtualKey.PageUp or VirtualKey.PageDown) { Session.SwitchSheet(Session.Book.ActiveSheetIndex + (e.Key == VirtualKey.PageUp ? -1 : 1)); e.Handled = true; return; }
        }
        switch (e.Key)
        {
            case VirtualKey.Left: Navigate(0, -1, shift); break;
            case VirtualKey.Right: Navigate(0, 1, shift); break;
            case VirtualKey.Up: Navigate(-1, 0, shift); break;
            case VirtualKey.Down: Navigate(1, 0, shift); break;
            case VirtualKey.Enter: Navigate(shift ? -1 : 1, 0, false); break;
            case VirtualKey.Tab: Navigate(0, shift ? -1 : 1, false); break;
            case VirtualKey.PageDown: Navigate(Math.Max(1, (int)(Viewport.Height / (24 * Viewport.Zoom)) - 2), 0, shift); break;
            case VirtualKey.PageUp: Navigate(-Math.Max(1, (int)(Viewport.Height / (24 * Viewport.Zoom)) - 2), 0, shift); break;
            case VirtualKey.Home: Session.Select(new CellRange(new CellAddress(Session.ActiveCell.Row, 0), new CellAddress(Session.ActiveCell.Row, 0))); RevealSelection(); break;
            case VirtualKey.F2: BeginEdit(); break;
            case VirtualKey.Delete: Run(() => Session.Clear()); break;
            case VirtualKey.Escape: CancelGesture(); break;
            default:
                if (control || Down(VirtualKey.Menu)) return;
                var key = (int)e.Key;
                if (key >= 65 && key <= 90) BeginEdit(((char)(shift ? key : key + 32)).ToString());
                else if (key >= 48 && key <= 57) BeginEdit(shift ? ")!@#$%^&*("[key - 48].ToString() : ((char)key).ToString());
                else if (key is 187 or 189 or 190 or 188) BeginEdit(key == 187 ? shift ? "+" : "=" : key == 189 ? shift ? "_" : "-" : key == 190 ? "." : ",");
                else if (e.Key == VirtualKey.Space) BeginEdit(" ");
                else return;
                break;
        }
        e.Handled = true;
    }
    private void Navigate(int rows, int columns, bool extend)
    {
        if (Session is null) return;
        var from = extend ? Session.Selection.End : Session.ActiveCell;
        var row = Math.Clamp(from.Row + rows, 0, CellAddress.MaxRows - 1); var column = Math.Clamp(from.Column + columns, 0, CellAddress.MaxColumns - 1);
        if (rows > 0) row = Viewport.Rows.IndexAt(Viewport.Rows.Position(row));
        if (rows < 0 && Viewport.Rows.Size(row) == 0) row = Viewport.Rows.IndexAt(Math.Max(0, Viewport.Rows.Position(row) - .001));
        if (columns > 0) column = Viewport.Columns.IndexAt(Viewport.Columns.Position(column));
        if (columns < 0 && Viewport.Columns.Size(column) == 0) column = Viewport.Columns.IndexAt(Math.Max(0, Viewport.Columns.Position(column) - .001));
        var address = new CellAddress(row, column); Session.Select(new CellRange(extend ? Session.Selection.Start : address, address));
        Viewport.EnsureVisible(address); Invalidate();
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; CancelEdit(); CancelGesture();
        if (_session is not null) _session.Changed -= SessionChanged;
        Renderer.Dispose(); _canvas.Paint = null;
    }
}
