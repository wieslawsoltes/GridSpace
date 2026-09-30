using GridSpace.Core;
using GridSpace.Editing;
using GridSpace.Formulas;
using GridSpace.Skia;
using Xunit;

namespace GridSpace.Tests;

public sealed class ChartSourceCacheTests
{
    [Fact]
    public void WarmOutlineBindingLookupHasNoAllocationOrCalculationWork()
    {
        var session = new SpreadsheetSession(new Workbook());
        var id = session.AddChart(new ChartSpec { Range = "A1:C6" });
        using var renderer = new SpreadsheetRenderer { SelectedChartId = id };
        for (var i = 0; i < 100; i++) renderer.SourceBindings(session);
        var evaluations = session.Calculation.EvaluatedCellCount;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) renderer.SourceBindings(session);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(evaluations, session.Calculation.EvaluatedCellCount);
    }

    [Fact]
    public void CustomizeKeepsCurrentDataAndIsOneReversibleCommand()
    {
        var book = new Workbook(); book.ActiveSheet.Set("A1", "Item"); book.ActiveSheet.Set("B1", "Revenue");
        book.ActiveSheet.Set("A2", "A"); book.ActiveSheet.Set("B2", "10");
        book.ActiveSheet.Set("A3", "B"); book.ActiveSheet.Set("B3", "20");
        var session = new SpreadsheetSession(book); var id = session.AddChart(new ChartSpec { Range = "A1:B3" });
        var before = ChartDataResolver.Resolve(book, session.Sheet, session.FindChart(id)!, session.Calculation);
        session.CustomizeChartSource(id);
        var chart = session.FindChart(id)!;
        Assert.Equal("A2:A3", chart.Categories); Assert.Equal("Revenue", Assert.Single(chart.Series).Name);
        var after = ChartDataResolver.Resolve(book, session.Sheet, chart, session.Calculation);
        Assert.Equal(before.Categories, after.Categories); Assert.Equal(before.Series[0].Values, after.Series[0].Values);
        var history = session.RetainedHistoryBytes; session.CustomizeChartSource(id); Assert.Equal(history, session.RetainedHistoryBytes);
        session.Undo(); Assert.Empty(session.FindChart(id)!.Series); Assert.Null(session.FindChart(id)!.Categories);
        session.Redo(); Assert.Single(session.FindChart(id)!.Series);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void AutomaticHeaderlessAndTransposedTablesStayBounded(bool rows)
    {
        var chart = new ChartSpec { Range = rows ? "A1:D2" : "A1:B4", SeriesInRows = rows, HasHeaders = false };
        var binding = Assert.Single(ChartSourceEditing.Bindings(chart, "Sheet1"));
        var next = ChartSourceEditing.Replace(chart, "Sheet1", binding, CellRange.Parse(rows ? "A1:G2" : "A1:B7"));
        Assert.False(next.HasHeaders); Assert.Equal(rows, next.SeriesInRows); Assert.Empty(next.Series);
    }
}
