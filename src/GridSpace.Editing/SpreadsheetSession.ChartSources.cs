using GridSpace.Core;
using GridSpace.Formulas;

namespace GridSpace.Editing;

public sealed partial class SpreadsheetSession
{
    /// <summary>
    /// Commits a source preview only against the same live chart definition. Intervening edits,
    /// undo, loads, sheet changes and report-owned bindings cannot be overwritten by a stale drag.
    /// Uses drawing-delta history; source cells and formula/geometry revisions remain untouched.
    /// </summary>
    public void CommitChartSourceEdit(ChartSpec expected, ChartSourceBinding binding, CellRange range)
    {
        ArgumentNullException.ThrowIfNull(expected);
        if (!ReferenceEquals(FindChart(expected.Id), expected))
            throw new InvalidOperationException("The chart changed while its source was being edited. Start the edit again.");
        UpdateChart(expected.Id, chart => ChartSourceEditing.Replace(chart, Sheet.Name, binding, range), "Change chart source");
    }

    /// <summary>Converts automatic bindings to explicit vectors once, keeping current names as captions.</summary>
    public void CustomizeChartSource(string id)
    {
        var chart = FindChart(id) ?? throw new InvalidOperationException("Select a chart first.");
        if (chart.PivotTableId is not null) throw new InvalidOperationException("Change PivotChart fields in the PivotTable field list.");
        if (chart.Series.Count > 0) return;
        ChartSourceEditing.ValidateSourceBudget(chart);
        var data = ChartDataResolver.Resolve(Book, Sheet, chart with { PlotHiddenCells = true }, Calculation);
        UpdateChart(id, current => current with
        {
            Categories = data.CategoriesRange,
            CategoriesHorizontal = CellRange.Parse(data.CategoriesRange).Count == 1
                ? current.Categories is null ? current.SeriesInRows : current.CategoriesHorizontal ?? current.SeriesInRows
                : null,
            Series = data.Series.Select((series, i) => new ChartSeries
            {
                Name = series.Name, Values = series.ValuesRange,
                ValuesHorizontal = CellRange.Parse(series.ValuesRange).Count == 1 ? current.SeriesInRows : null,
                Color = ChartDataResolver.Palette[i % ChartDataResolver.Palette.Length]
            }).ToList()
        }, "Customize chart series");
    }
}
