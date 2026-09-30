using GridSpace.Core;

namespace GridSpace.Editing;

internal sealed record ChartPatch(int BeforeIndex, int AfterIndex, ChartSpec? Before, ChartSpec? After)
{
    public string Id => (After ?? Before)!.Id;
    public long Size => 64 + Estimate(Before) + Estimate(After);

    private static long Link(ChartTextReference? reference) => reference is null ? 0 : 96L + 2L * (reference.Sheet.Length + reference.Cell.Length);
    private static long Estimate(ChartSpec? chart)
    {
        if (chart is null) return 0;
        long size = 512 + 2L * (chart.Id.Length + chart.Title.Length + chart.Range.Length + (chart.SourceSheet?.Length ?? 0)
            + (chart.Categories?.Length ?? 0) + chart.CategoryAxisTitle.Length + chart.ValueAxisTitle.Length + chart.ValueFormat.Length);
        size += Link(chart.TitleReference) + Link(chart.CategoryAxisTitleReference) + Link(chart.ValueAxisTitleReference);
        foreach (var series in chart.Series) size += 128 + 2L * (series.Name.Length + series.Values.Length + series.Color.Length) + Link(series.NameReference);
        return size;
    }
}
