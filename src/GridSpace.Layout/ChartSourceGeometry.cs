using GridSpace.Core;

namespace GridSpace.Layout;

public sealed record ChartSourceHit(ChartSourceBinding Binding, ChartSourceHandle Handle);
public readonly record struct ChartSourceGrip(ChartSourceHandle Handle, double X, double Y);

/// <summary>Shared pane-clipped source-outline geometry; clipped edges never become false resize handles.</summary>
public static class ChartSourceGeometry
{
    public const double GripRadius = 4;
    public const double HitTolerance = 6;

    public static bool Intersects(ChartSourceBinding binding, GridPane pane) =>
        binding.Range.Left < pane.LastColumn && binding.Range.Right >= pane.FirstColumn &&
        binding.Range.Top < pane.LastRow && binding.Range.Bottom >= pane.FirstRow;

    public static IEnumerable<ChartSourceGrip> Grips(ChartSourceBinding binding, GridViewport view, GridPane pane)
    {
        var range = binding.Range.Normalized;
        var bounds = view.RangeBounds(range, pane);
        if (Owns(range.Start) && pane.Clip.Contains(bounds.X, bounds.Y))
            yield return new(ChartSourceHandle.Start, bounds.X, bounds.Y);
        // Keep an exclusive bottom/right endpoint inside its owning pane.
        var x = bounds.Right - .5; var y = bounds.Bottom - .5;
        if (Owns(range.End) && pane.Clip.Contains(x, y))
            yield return new(ChartSourceHandle.End, x, y);
        bool Owns(CellAddress cell) => cell.Column >= pane.FirstColumn && cell.Column < pane.LastColumn &&
            cell.Row >= pane.FirstRow && cell.Row < pane.LastRow && view.Columns.Size(cell.Column) > 0 && view.Rows.Size(cell.Row) > 0;
    }

    public static ChartSourceHit? HitTest(IReadOnlyList<ChartSourceBinding> bindings, GridViewport view, double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y)) return null;
        foreach (var pane in view.Panes())
        {
            if (!pane.Clip.Contains(x, y)) continue;
            // Grips outrank borders. Choose the nearest actual endpoint when low zoom
            // or narrow cells make their hit regions overlap; vector order breaks ties.
            ChartSourceHit? nearest = null;
            var distance = double.PositiveInfinity;
            for (var i = bindings.Count - 1; i >= 0; i--)
            {
                var binding = bindings[i];
                if (!Intersects(binding, pane)) continue;
                foreach (var grip in Grips(binding, view, pane))
                {
                    var dx = Math.Abs(x - grip.X); var dy = Math.Abs(y - grip.Y);
                    var squared = dx * dx + dy * dy;
                    if (dx <= HitTolerance && dy <= HitTolerance && squared < distance)
                    { distance = squared; nearest = new(binding, grip.Handle); }
                }
            }
            if (nearest is not null) return nearest;
            for (var i = bindings.Count - 1; i >= 0; i--)
            {
                var binding = bindings[i];
                if (!Intersects(binding, pane)) continue;
                var b = view.RangeBounds(binding.Range, pane);
                if (b.Width <= 0 || b.Height <= 0) continue;
                var vertical = y >= b.Y - HitTolerance && y <= b.Bottom + HitTolerance &&
                    (Math.Abs(x - b.X) <= HitTolerance || Math.Abs(x - b.Right) <= HitTolerance);
                var horizontal = x >= b.X - HitTolerance && x <= b.Right + HitTolerance &&
                    (Math.Abs(y - b.Y) <= HitTolerance || Math.Abs(y - b.Bottom) <= HitTolerance);
                if (vertical || horizontal) return new(binding, ChartSourceHandle.Move);
            }
        }
        return null;
    }
}
