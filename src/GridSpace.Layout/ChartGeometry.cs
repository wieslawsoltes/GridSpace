using GridSpace.Core;

namespace GridSpace.Layout;

public enum ChartHitPart { None, Body, Title, NorthWest, North, NorthEast, East, SouthEast, South, SouthWest, West }
public readonly record struct ChartHit(string Id, ChartHitPart Part, GridRect Bounds, GridPane Pane);

/// <summary>Shared unscaled document geometry and clipped DIP hit testing for chart rendering and manipulation.</summary>
public static class ChartGeometry
{
    public static GridRect SheetBounds(ChartSpec chart, GridViewport view) =>
        new(view.Columns.Position(chart.Column) + chart.OffsetX, view.Rows.Position(chart.Row) + chart.OffsetY, chart.Width, chart.Height);

    public static GridRect Bounds(ChartSpec chart, GridViewport view, GridPane pane)
    {
        var world = SheetBounds(chart, view);
        return new(pane.OriginX + world.X * view.Zoom, pane.OriginY + world.Y * view.Zoom, world.Width * view.Zoom, world.Height * view.Zoom);
    }

    public static IEnumerable<(ChartHitPart Part, double X, double Y)> Handles(GridRect bounds)
    {
        var x = bounds.X; var y = bounds.Y; var right = bounds.Right; var bottom = bounds.Bottom;
        yield return (ChartHitPart.NorthWest, x, y);
        yield return (ChartHitPart.North, x + bounds.Width / 2, y);
        yield return (ChartHitPart.NorthEast, right, y);
        yield return (ChartHitPart.East, right, y + bounds.Height / 2);
        yield return (ChartHitPart.SouthEast, right, bottom);
        yield return (ChartHitPart.South, x + bounds.Width / 2, bottom);
        yield return (ChartHitPart.SouthWest, x, bottom);
        yield return (ChartHitPart.West, x, y + bounds.Height / 2);
    }

    public static ChartHit? HitTest(IReadOnlyList<ChartSpec> charts, GridViewport view, double x, double y, string? selected = null)
    {
        foreach (var pane in view.Panes())
        {
            if (!pane.Clip.Contains(x, y)) continue;
            // Selected handles have precedence even at boundaries that touch another chart.
            if (charts.FirstOrDefault(c => c.Id == selected) is { } chosen)
            {
                var bounds = Bounds(chosen, view, pane);
                foreach (var handle in Handles(bounds))
                    if (Math.Abs(x - handle.X) <= 6 && Math.Abs(y - handle.Y) <= 6) return new(chosen.Id, handle.Part, bounds, pane);
            }
            for (var i = charts.Count - 1; i >= 0; i--)
            {
                var chart = charts[i]; var bounds = Bounds(chart, view, pane);
                if (bounds.Contains(x, y)) return new(chart.Id, y < bounds.Y + 40 * view.Zoom ? ChartHitPart.Title : ChartHitPart.Body, bounds, pane);
            }
        }
        return null;
    }

    public static GridRect Transform(GridRect original, ChartHitPart part, double dx, double dy, bool preserveAspect = false)
    {
        if (!double.IsFinite(dx) || !double.IsFinite(dy)) throw new ArgumentException("Chart deltas must be finite.");
        if (part is ChartHitPart.Body or ChartHitPart.Title)
            return original with { X = Math.Max(0, original.X + dx), Y = Math.Max(0, original.Y + dy) };
        var west = part is ChartHitPart.West or ChartHitPart.NorthWest or ChartHitPart.SouthWest;
        var east = part is ChartHitPart.East or ChartHitPart.NorthEast or ChartHitPart.SouthEast;
        var north = part is ChartHitPart.North or ChartHitPart.NorthEast or ChartHitPart.NorthWest;
        var south = part is ChartHitPart.South or ChartHitPart.SouthEast or ChartHitPart.SouthWest;
        var width = Math.Clamp(original.Width + (east ? dx : west ? -dx : 0), 120, 4000);
        var height = Math.Clamp(original.Height + (south ? dy : north ? -dy : 0), 100, 4000);
        if (preserveAspect && (west || east) && (north || south))
        {
            var scale = Math.Max(width / original.Width, height / original.Height);
            scale = Math.Clamp(scale, Math.Max(120 / original.Width, 100 / original.Height), Math.Min(4000 / original.Width, 4000 / original.Height));
            width = original.Width * scale; height = original.Height * scale;
        }
        var left = west ? Math.Max(0, original.Right - width) : original.X;
        var top = north ? Math.Max(0, original.Bottom - height) : original.Y;
        if (west) width = original.Right - left;
        if (north) height = original.Bottom - top;
        return new(left, top, width, height);
    }

    public static ChartSpec Place(ChartSpec original, GridRect bounds, GridViewport view)
    {
        var x = Math.Clamp(bounds.X, 0, Math.Max(0, view.Columns.Extent - bounds.Width));
        var y = Math.Clamp(bounds.Y, 0, Math.Max(0, view.Rows.Extent - bounds.Height));
        var column = view.Columns.IndexAt(x); var row = view.Rows.IndexAt(y);
        return original with
        {
            Column = column, Row = row, OffsetX = x - view.Columns.Position(column), OffsetY = y - view.Rows.Position(row),
            Width = bounds.Width, Height = bounds.Height
        };
    }
}
