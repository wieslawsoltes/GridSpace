using GridSpace.Core;

namespace GridSpace.Editing;

/// <summary>
/// Pure, cell-independent chart binding edits. Automatic charts resize one source table;
/// explicit charts move/resize category and value vectors without guessing new series.
/// </summary>
public static class ChartSourceEditing
{
    public const int MaximumSourceCells = 100_000;

    public static IReadOnlyList<ChartSourceBinding> Bindings(ChartSpec chart, string hostSheet)
    {
        ArgumentNullException.ThrowIfNull(chart);
        if (chart.PivotTableId is not null || chart.SourceUnavailable ||
            chart.SourceSheet is { } source && !source.Equals(hostSheet, StringComparison.OrdinalIgnoreCase))
            return Array.Empty<ChartSourceBinding>();
        chart.Validate();
        var result = new List<ChartSourceBinding>(chart.Series.Count + 2);
        var data = CellRange.Parse(chart.Range).Normalized;
        if (chart.Series.Count == 0)
            result.Add(new(ChartSourcePart.DataRange, -1, data, chart.SeriesInRows));
        if (chart.Categories is not null || chart.Series.Count > 0)
        {
            var categories = chart.Categories is not null ? CellRange.Parse(chart.Categories).Normalized : CategoryRange(chart, data);
            result.Add(Binding(ChartSourcePart.Categories, -1, categories));
        }
        for (var i = 0; i < chart.Series.Count; i++)
            result.Add(Binding(ChartSourcePart.SeriesValues, i, CellRange.Parse(chart.Series[i].Values).Normalized));
        return result.AsReadOnly();

        ChartSourceBinding Binding(ChartSourcePart part, int index, CellRange range)
        {
            var hint = part == ChartSourcePart.Categories
                ? chart.Categories is null ? null : chart.CategoriesHorizontal
                : chart.Series[index].ValuesHorizontal;
            var horizontal = range.Count == 1 ? hint ?? chart.SeriesInRows : range.Top == range.Bottom;
            return new(part, index, range, horizontal);
        }
    }

    public static ChartSpec Replace(ChartSpec chart, string hostSheet, ChartSourceBinding binding, CellRange replacement)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (!Bindings(chart, hostSheet).Contains(binding))
            throw new InvalidOperationException("This chart reference changed or is not editable on the current worksheet.");
        replacement = replacement.Normalized;
        if (!replacement.Start.IsValid || !replacement.End.IsValid || replacement.Count > MaximumSourceCells)
            throw new ArgumentException("Chart references must stay inside the worksheet and contain at most 100,000 cells.");
        if (binding.IsVector && (binding.Horizontal ? replacement.Top != replacement.Bottom : replacement.Left != replacement.Right))
            throw new ArgumentException("A chart vector must keep its row or column orientation.");
        var next = chart.CloneDocument();
        switch (binding.Part)
        {
            case ChartSourcePart.DataRange: next.Range = replacement.ToString(); break;
            case ChartSourcePart.Categories:
                next.Categories = replacement.ToString();
                next.CategoriesHorizontal = replacement.Count == 1 ? binding.Horizontal : null;
                break;
            case ChartSourcePart.SeriesValues:
                next.Series[binding.SeriesIndex] = next.Series[binding.SeriesIndex] with
                {
                    Values = replacement.ToString(),
                    ValuesHorizontal = replacement.Count == 1 ? binding.Horizontal : null
                };
                break;
            default: throw new ArgumentOutOfRangeException(nameof(binding));
        }
        ValidateSourceBudget(next);
        return next;
    }

    /// <summary>Move by cell-index delta, or resize one endpoint. Clamping never truncates a moved range.</summary>
    public static CellRange Transform(ChartSourceBinding binding, ChartSourceHandle handle, int rowDelta, int columnDelta)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (!Enum.IsDefined(handle)) throw new ArgumentOutOfRangeException(nameof(handle));
        var range = binding.Range.Normalized;
        if (handle == ChartSourceHandle.Move)
        {
            var rows = Math.Clamp(rowDelta, -range.Top, CellAddress.MaxRows - 1 - range.Bottom);
            var columns = Math.Clamp(columnDelta, -range.Left, CellAddress.MaxColumns - 1 - range.Right);
            return new(new(range.Top + rows, range.Left + columns), new(range.Bottom + rows, range.Right + columns));
        }
        if (binding.IsVector)
        {
            if (binding.Horizontal) rowDelta = 0;
            else columnDelta = 0;
        }
        var moving = handle == ChartSourceHandle.Start ? range.Start : range.End;
        var stationary = handle == ChartSourceHandle.Start ? range.End : range.Start;
        var moved = new CellAddress(
            (int)Math.Clamp((long)moving.Row + rowDelta, 0, CellAddress.MaxRows - 1),
            (int)Math.Clamp((long)moving.Column + columnDelta, 0, CellAddress.MaxColumns - 1));
        return new CellRange(stationary, moved).Normalized;
    }

    public static void ValidateSourceBudget(ChartSpec chart)
    {
        chart.Validate();
        var range = CellRange.Parse(chart.Range);
        var categories = chart.Categories is { } explicitCategories ? CellRange.Parse(explicitCategories) : CategoryRange(chart, range);
        long cells = categories.Count;
        if (chart.Series.Count == 0)
        {
            var seriesCount = chart.SeriesInRows ? range.Bottom - range.Top : range.Right - range.Left;
            var points = (chart.SeriesInRows ? range.Right - range.Left : range.Bottom - range.Top) + 1 - (chart.HasHeaders ? 1 : 0);
            if (seriesCount is < 1 or > 32 || points < 1)
                throw new ArgumentException("Select categories, at least one data point, and between 1 and 32 automatic series.");
            cells += (long)seriesCount * points;
        }
        else foreach (var series in chart.Series) cells += CellRange.Parse(series.Values).Count;
        if (cells > MaximumSourceCells)
            throw new ArgumentException("Combined chart category and value vectors exceed 100,000 source cells.");
    }

    private static CellRange CategoryRange(ChartSpec chart, CellRange range) => chart.SeriesInRows
        ? new(new(range.Top, Math.Min(range.Right, range.Left + (chart.HasHeaders ? 1 : 0))), new(range.Top, range.Right))
        : new(new(Math.Min(range.Bottom, range.Top + (chart.HasHeaders ? 1 : 0)), range.Left), new(range.Bottom, range.Left));
}
