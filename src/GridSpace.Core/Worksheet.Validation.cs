namespace GridSpace.Core;

public sealed partial class Worksheet
{
    public void ValidateMetadata()
    {
        if (Charts is null || PivotTables is null || Charts.Count > 128 || PivotTables.Count > 32 || Charts.Any(c => c is null) || PivotTables.Any(p => p is null))
            throw new InvalidDataException("A sheet supports at most 128 charts and 32 PivotTables.");
        if (Charts.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() != Charts.Count
            || PivotTables.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() != PivotTables.Count)
            throw new InvalidDataException("Chart and PivotTable identifiers must be unique within a sheet.");
        foreach (var chart in Charts) chart.Validate();
        foreach (var pivot in PivotTables) pivot.Validate();
        for (var i = 0; i < PivotTables.Count; i++)
        {
            if (PivotTables[i].OutputRange is not { } output) continue;
            var rectangle = CellRange.Parse(output);
            if (rectangle.Count > 100000 || rectangle.Start != PivotTables[i].Anchor)
                throw new InvalidDataException("A PivotTable output must start at its destination.");
            if (PivotTables.Skip(i + 1).Any(p => p.OutputRange is { } other && CellRange.Parse(other).Intersects(rectangle)))
                throw new InvalidDataException("PivotTable reports cannot overlap.");
        }
        if (ConditionalFormats is null || Filters is null || FilteredRows is null || SortLevels is null)
            throw new InvalidDataException("Worksheet data-tool collections cannot be null.");
        if (ConditionalFormats.Count > 256 || Filters.Count > 256 || SortLevels.Count > 64)
            throw new InvalidDataException("A sheet supports 256 conditional rules, 256 filter columns and 64 sort levels.");
        if (FilteredRows.Any(r => r < 0 || r >= CellAddress.MaxRows)) throw new InvalidDataException("Invalid filtered row index.");
        if (ConditionalFormats.Select(r => r.Id).Distinct(StringComparer.Ordinal).Count() != ConditionalFormats.Count)
            throw new InvalidDataException("Conditional-format rule identifiers must be unique within a sheet.");
        foreach (var rule in ConditionalFormats) rule.Validate();
        if (Filters.Select(f => f.Column).Distinct().Count() != Filters.Count) throw new InvalidDataException("Filter columns must be unique.");
        var filterRange = FilterRange is null ? (CellRange?)null : CellRange.Parse(FilterRange);
        foreach (var filter in Filters)
        {
            filter.Validate();
            if (filterRange is null || filter.Column < filterRange.Value.Left || filter.Column > filterRange.Value.Right)
                throw new InvalidDataException("A filter column must belong to its AutoFilter range.");
        }
        if (SortRange is not null) _ = CellRange.Parse(SortRange);
        if (SortLevels.Any(s => s.Column < 0 || s.Column >= CellAddress.MaxColumns)) throw new InvalidDataException("Invalid sort column.");
        if (FrozenRows < 0 || FrozenRows >= CellAddress.MaxRows || FrozenColumns < 0 || FrozenColumns >= CellAddress.MaxColumns)
            throw new InvalidDataException("Frozen-pane boundaries must be inside the worksheet.");
    }
}
