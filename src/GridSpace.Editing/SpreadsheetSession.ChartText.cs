using GridSpace.Core;
using GridSpace.Formulas;

namespace GridSpace.Editing;

public sealed partial class SpreadsheetSession
{
    /// <summary>Link text to one cell, or freeze its current displayed value when clearing the link.</summary>
    public void SetChartTextLink(string id, ChartSourcePart part, ChartTextReference? reference, int seriesIndex = -1)
    {
        reference?.Validate();
        if (reference is not null && !reference.IsBroken && Book.FindSheet(reference.Sheet) is null)
            throw new ArgumentException("The referenced worksheet does not exist.");
        var current = FindChart(id) ?? throw new InvalidOperationException("Select a chart first.");
        string Caption(string literal, ChartTextReference? previous) => reference is null && previous is not null
            ? ChartTextResolver.Resolve(Book, previous, Calculation) : literal;
        UpdateChart(id, chart =>
        {
            switch (part)
            {
                case ChartSourcePart.Title: return chart with { Title = Caption(chart.Title, current.TitleReference), TitleReference = reference };
                case ChartSourcePart.CategoryAxisTitle: return chart with { CategoryAxisTitle = Caption(chart.CategoryAxisTitle, current.CategoryAxisTitleReference), CategoryAxisTitleReference = reference };
                case ChartSourcePart.ValueAxisTitle: return chart with { ValueAxisTitle = Caption(chart.ValueAxisTitle, current.ValueAxisTitleReference), ValueAxisTitleReference = reference };
                case ChartSourcePart.SeriesName:
                    if (chart.PivotTableId is not null) throw new InvalidOperationException("Change PivotChart measure captions in the PivotTable field list.");
                    if ((uint)seriesIndex >= (uint)chart.Series.Count) throw new ArgumentOutOfRangeException(nameof(seriesIndex));
                    var series = chart.Series[seriesIndex];
                    chart.Series[seriesIndex] = series with { Name = Caption(series.Name, series.NameReference), NameReference = reference };
                    return chart;
                default: throw new ArgumentException("Choose a chart title, axis title or explicit series name.", nameof(part));
            }
        }, reference is null ? "Unlink chart text" : "Link chart text");
    }

    /// <summary>Inline editing changes only the chart, with optimistic ownership like source-range dragging.</summary>
    public void CommitChartTitleEdit(ChartSpec expected, string text)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(text);
        if (!ReferenceEquals(FindChart(expected.Id), expected))
            throw new InvalidOperationException("The chart changed while its title was being edited. Start the edit again.");
        UpdateChart(expected.Id, chart => chart with { Title = text, TitleReference = null }, "Edit chart title");
    }
}
