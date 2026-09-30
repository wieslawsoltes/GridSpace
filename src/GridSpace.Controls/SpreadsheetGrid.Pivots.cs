using GridSpace.Core;
using GridSpace.Formulas;
using GridSpace.Skia;

namespace GridSpace.Controls;

public sealed partial class SpreadsheetGrid
{
    private sealed record PivotPress(uint PointerId, PivotToggleHit Hit, Point Start);
    private PivotPress? _pivotPress;

    private bool TryPivotPressed(PointerRoutedEventArgs e)
    {
        if (Session is null) return false;
        var point = e.GetCurrentPoint(_canvas);
        if (!point.Properties.IsLeftButtonPressed) return false;
        var hit = PivotOutlineGeometry.HitTest(Session.Sheet, Viewport, point.Position.X, point.Position.Y);
        if (hit is null) return false;
        SelectChart(null);
        FocusGrid();
        // Do not mutate or select on press: opening the inspector can reflow the
        // viewport and move the target before the matching release arrives.
        _pivotPress = new(e.Pointer.PointerId, hit, point.Position);
        if (!_canvas.CapturePointer(e.Pointer)) _pivotPress = null;
        e.Handled = true;
        return true;
    }

    private bool TryPivotMoved(PointerRoutedEventArgs e)
    {
        if (_pivotPress is not { } press) return false;
        if (press.PointerId == e.Pointer.PointerId)
        {
            var point = e.GetCurrentPoint(_canvas).Position;
            if (Math.Abs(point.X - press.Start.X) + Math.Abs(point.Y - press.Start.Y) > 8)
                CancelPivotGesture();
        }
        e.Handled = true;
        return true;
    }

    private bool TryPivotReleased(PointerRoutedEventArgs e)
    {
        if (_pivotPress is not { } press || press.PointerId != e.Pointer.PointerId) return false;
        var point = e.GetCurrentPoint(_canvas).Position;
        _pivotPress = null; // CaptureLost must not cancel an already committed gesture.
        _canvas.ReleasePointerCaptures();
        if (Session is not null && press.Hit.Bounds.Contains(point.X, point.Y))
            Run(() => Session.TogglePivotGroup(press.Hit.PivotId, press.Hit.Group));
        FocusGrid(); RevealSelection(); e.Handled = true;
        return true;
    }

    private void CancelPivotGesture()
    {
        if (_pivotPress is null) return;
        _pivotPress = null;
        _canvas.ReleasePointerCaptures();
    }

    private bool TryPivotDoubleTap(Point point)
    {
        if (Session is null) return false;
        // Glyph clicks are handled by the press/release pair. A synthesized
        // double-tap must not apply a third toggle after those two clicks.
        if (PivotOutlineGeometry.HitTest(Session.Sheet, Viewport, point.X, point.Y) is not null) return true;
        var hit = Viewport.HitTest(point.X, point.Y);
        if (hit.Kind != GridSpace.Layout.GridHitKind.Cell) return false;
        var address = new CellAddress(hit.Row, hit.Column);
        if (!PivotOutlineGeometry.TryGetCell(Session.Sheet, address, out var pivot, out var outline) || outline.Group is null) return false;
        Run(() => Session.TogglePivotGroup(pivot!.Id, outline.Group));
        FocusGrid(); RevealSelection(); return true;
    }

    /// <summary>Toggle the selected parent label. The group is identified by typed source values, not display text.</summary>
    public void ToggleSelectedPivotGroup(bool? expanded = null)
    {
        if (Session is null) return;
        if (!PivotOutlineGeometry.TryGetCell(Session.Sheet, Session.ActiveCell, out var pivot, out var outline) || outline.Group is null)
            throw new InvalidOperationException("Select a parent row label with an expand/collapse button.");
        if (expanded is null || expanded.Value != outline.Expanded) Session.TogglePivotGroup(pivot!.Id, outline.Group);
        RevealSelection();
    }

    private bool HandlePivotKey(KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape && _pivotPress is not null)
        { CancelPivotGesture(); e.Handled = true; return true; }
        if (Session?.ActivePivot is null || !Down(VirtualKey.Control) || !Down(VirtualKey.Menu)) return false;
        if (e.Key is not VirtualKey.Left and not VirtualKey.Right) return false;
        Run(() => ToggleSelectedPivotGroup(e.Key == VirtualKey.Right));
        e.Handled = true;
        return true;
    }
}
