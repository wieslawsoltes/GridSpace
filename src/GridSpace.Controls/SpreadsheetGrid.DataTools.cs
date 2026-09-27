using GridSpace.Core;
using GridSpace.Layout;

namespace GridSpace.Controls;

public sealed partial class SpreadsheetGrid
{
    private bool _dataToolsEnabled;
    public event Action<int>? FilterRequested;

    /// <summary>Enables data-tool gestures for hosts supplying a filter editor. No global event handlers are installed.</summary>
    public void EnableDataToolInteractions()
    {
        if (_dataToolsEnabled) return;
        _dataToolsEnabled = true;
        AddHandler(PointerPressedEvent, new PointerEventHandler(DataToolPointerPressed), true);
        var filter = new KeyboardAccelerator { Key = VirtualKey.Down, Modifiers = VirtualKeyModifiers.Menu };
        filter.Invoked += (_, e) =>
        {
            if (Session is null || IsEditing) return;
            FilterRequested?.Invoke(Session.ActiveCell.Column); e.Handled = true;
        };
        KeyboardAccelerators.Add(filter);
        var toggle = new KeyboardAccelerator { Key = VirtualKey.L, Modifiers = VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift };
        toggle.Invoked += (_, e) => { if (!IsEditing) { CommandRequested?.Invoke("toggle-filter"); e.Handled = true; } };
        KeyboardAccelerators.Add(toggle);
    }

    private void DataToolPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (Session is null || !CellRange.TryParse(Session.Sheet.FilterRange, out var range)) return;
        var point = e.GetCurrentPoint(_canvas);
        if (point.Properties.IsRightButtonPressed) return;
        var hit = Viewport.HitTest(point.Position.X, point.Position.Y);
        if (hit.Kind != GridHitKind.Cell || hit.Row != range.Top || hit.Column < range.Left || hit.Column > range.Right) return;
        var rect = Viewport.CellBounds(new CellAddress(hit.Row, hit.Column));
        if (point.Position.X < rect.Right - 19 || point.Position.Y < rect.Bottom - 20) return;
        CancelGesture();
        FilterRequested?.Invoke(hit.Column);
        e.Handled = true;
    }
}
