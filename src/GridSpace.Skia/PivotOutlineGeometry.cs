using System.Runtime.CompilerServices;
using GridSpace.Core;
using GridSpace.Formulas;
using GridSpace.Layout;

namespace GridSpace.Skia;

public sealed record PivotToggleHit(string PivotId, PivotGroupPath Group, bool Expanded, GridRect Bounds);

/// <summary>Shared label indentation and toggle geometry for Skia drawing, input and read-only diagnostics.</summary>
public static class PivotOutlineGeometry
{
    private sealed class Location(PivotTableSpec pivot)
    {
        public string? Output = pivot.OutputRange;
        public string Destination = pivot.Destination;
        public CellRange Range = CellRange.Parse(pivot.OutputRange!);
        public CellAddress Anchor = pivot.Anchor;
    }
    private static readonly ConditionalWeakTable<PivotTableSpec, Location> Locations = new();
    public static bool TryGetCell(Worksheet sheet, CellAddress address, out PivotTableSpec? pivot, out PivotOutlineCell cell)
    {
        pivot = null; cell = default;
        foreach (var candidate in sheet.PivotTables)
        {
            if (candidate.Cache is null || candidate.NeedsLayoutRefresh || candidate.OutputRange is null) continue;
            var location = Locations.GetValue(candidate, static p => new(p));
            if (location.Output != candidate.OutputRange || location.Destination != candidate.Destination)
            {
                location.Output = candidate.OutputRange; location.Destination = candidate.Destination;
                location.Range = CellRange.Parse(candidate.OutputRange); location.Anchor = candidate.Anchor;
            }
            if (!location.Range.Contains(address)) continue;
            var row = address.Row - location.Anchor.Row;
            var column = address.Column - location.Anchor.Column;
            var labels = candidate.Layout == PivotLayout.Compact ? 1 : Math.Max(1, candidate.Rows.Count);
            if (row <= 0 || column < 0 || column >= labels) return false;
            pivot = candidate;
            return PivotReportCache.Get(candidate).OutlineCells.TryGetValue((row, column), out cell);
        }
        return false;
    }

    public static GridRect ToggleBounds(GridRect cell, PivotOutlineCell outline, double zoom)
    {
        var size = Math.Max(0, Math.Min(12 * zoom, cell.Height - 4 * zoom));
        return new(cell.X + (3 + 14 * outline.Indent) * zoom, cell.Y + (cell.Height - size) / 2, size, size);
    }

    public static PivotToggleHit? HitTest(Worksheet sheet, GridViewport view, double x, double y)
    {
        var hit = view.HitTest(x, y);
        if (hit.Kind != GridHitKind.Cell) return null;
        var address = new CellAddress(hit.Row, hit.Column);
        if (!TryGetCell(sheet, address, out var pivot, out var outline) || outline.Group is null) return null;
        var bounds = ToggleBounds(view.CellBounds(address), outline, view.Zoom);
        if (!bounds.Contains(x, y)) return null;
        // HitTest resolves the frontmost frozen pane; bounds may extend behind its edge.
        if (!view.Panes().Any(p => p.Clip.Contains(x, y))) return null;
        return new(pivot!.Id, outline.Group, outline.Expanded, bounds);
    }
}
