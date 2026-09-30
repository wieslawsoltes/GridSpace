using GridSpace.Core;
using GridSpace.Editing;
using GridSpace.IO;
using Xunit;

namespace GridSpace.Tests;

public sealed class ChartSourceScalarOrientationTests
{
    [Theory]
    [InlineData(ChartSourcePart.Categories, true)]
    [InlineData(ChartSourcePart.Categories, false)]
    [InlineData(ChartSourcePart.SeriesValues, true)]
    [InlineData(ChartSourcePart.SeriesValues, false)]
    public void ShrinkAndReexpandRetainsOrientationAcrossUndoNativeAndXlsx(ChartSourcePart part, bool horizontal)
    {
        var session = new SpreadsheetSession(new Workbook());
        var chart = new ChartSpec
        {
            Range = "A1:D4", SeriesInRows = !horizontal,
            Categories = horizontal ? "A1:D1" : "A1:A4",
            Series = [new() { Values = horizontal ? "A2:D2" : "B1:B4" }]
        };
        session.AddChart(chart); chart = session.FindChart(chart.Id)!;
        var binding = ChartSourceEditing.Bindings(chart, session.Sheet.Name).Single(b => b.Part == part);
        var scalar = new CellRange(binding.Range.Start, binding.Range.Start);
        session.CommitChartSourceEdit(chart, binding, scalar);
        var current = session.FindChart(chart.Id)!;
        Assert.Equal(horizontal, ChartSourceEditing.Bindings(current, session.Sheet.Name).Single(b => b.Part == part).Horizontal);
        session.Undo(); session.Redo();
        foreach (var book in new[] { session.Book, Workbook.FromJson(session.Book.ToJson()), XlsxWorkbook.Read(XlsxWorkbook.Write(session.Book)).Workbook })
        {
            var saved = book.ActiveSheet.Charts[0];
            var single = ChartSourceEditing.Bindings(saved, book.ActiveSheet.Name).Single(b => b.Part == part);
            Assert.Equal(1, single.Range.Count);
            Assert.Equal(horizontal, single.Horizontal);
            var restored = ChartSourceEditing.Transform(single, ChartSourceHandle.End, 3, 3);
            Assert.Equal(binding.Range, restored);
        }
    }

    [Fact]
    public void NonScalarRangeOverridesOldOrientationHint()
    {
        var chart = new ChartSpec
        {
            Range = "A1:D4", Categories = "A1:D1", CategoriesHorizontal = false,
            Series = [new() { Values = "B1:B4", ValuesHorizontal = true }]
        };
        var bindings = ChartSourceEditing.Bindings(chart, "Sheet1");
        Assert.True(bindings[0].Horizontal);
        Assert.False(bindings[1].Horizontal);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void CustomizingAutomaticScalarSourcesDoesNotReuseAnObsoleteCategoryHint(bool rows)
    {
        var session = new SpreadsheetSession(new Workbook());
        var id = session.AddChart(new ChartSpec { Range = "A1:B2", SeriesInRows = rows, CategoriesHorizontal = !rows });
        session.CustomizeChartSource(id);
        var bindings = ChartSourceEditing.Bindings(session.FindChart(id)!, session.Sheet.Name);
        Assert.Equal(3, bindings.Count);
        Assert.All(bindings.Where(b => b.IsVector), b => { Assert.Equal(1, b.Range.Count); Assert.Equal(rows, b.Horizontal); });
        var name = Assert.Single(bindings, b => b.Part == ChartSourcePart.SeriesName);
        Assert.True(name.IsSingleCell);
        Assert.Equal(rows ? "A2" : "B1", name.Range.ToString());
    }
}
