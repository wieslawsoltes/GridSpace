namespace GridSpace.Core;

public sealed partial class Worksheet
{
    public void ValidateMetadata()
    {
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
