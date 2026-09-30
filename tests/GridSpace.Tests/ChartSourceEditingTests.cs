using GridSpace.Core;
using GridSpace.Editing;
using GridSpace.Formulas;
using GridSpace.IO;
using GridSpace.Layout;
using GridSpace.Skia;
using SkiaSharp;
using Xunit;

namespace GridSpace.Tests;

public sealed class ChartSourceEditingTests
{
    private static SpreadsheetSession Session()
    {
        var book = new Workbook(); book.ActiveSheet.Name = "Data";
        book.ActiveSheet.Set("A1", "Category"); book.ActiveSheet.Set("B1", "Revenue"); book.ActiveSheet.Set("C1", "Cost");
        for (var i = 2; i <= 12; i++)
        {
            book.ActiveSheet.Set("A" + i, "Item " + i);
            book.ActiveSheet.Set("B" + i, (10 * i).ToString());
            book.ActiveSheet.Set("C" + i, (5 * i).ToString());
        }
        return new(book);
    }
    private static ChartSpec Add(SpreadsheetSession session, bool explicitSeries = false)
    {
        var chart = new ChartSpec { Range = "A1:C6", Column = 8, Width = 300, Height = 200 };
        if (explicitSeries) chart.Series = [new() { Name = "Revenue", Values = "B2:B6" }, new() { Name = "Cost", Values = "C2:C6", Color = "#ED7D31" }];
        return session.FindChart(session.AddChart(chart))!;
    }

    [Fact]
    public void AutomaticBindingIsOneTableWithoutMaterializingCellsOrSeries()
    {
        var s = Session(); var chart = Add(s); var before = s.Book.ToJson();
        var binding = Assert.Single(ChartSourceEditing.Bindings(chart, "Data"));
        Assert.Equal(ChartSourcePart.DataRange, binding.Part);
        var next = ChartSourceEditing.Replace(chart, "Data", binding, CellRange.Parse("A1:C10"));
        Assert.Equal("A1:C10", next.Range); Assert.Empty(next.Series);
        Assert.Equal(before, s.Book.ToJson());
    }

    [Fact]
    public void ExplicitSeriesAndImplicitCategoriesRemainIndependentlyEditable()
    {
        var s = Session(); var chart = Add(s, true);
        var bindings = ChartSourceEditing.Bindings(chart, "Data");
        Assert.Equal(3, bindings.Count);
        Assert.Equal("A2:A6", bindings[0].Range.ToString());
        var next = ChartSourceEditing.Replace(chart, "Data", bindings[1], CellRange.Parse("D3:D9"));
        Assert.Equal("D3:D9", next.Series[0].Values);
        Assert.Equal(chart.Series[0].Name, next.Series[0].Name);
        Assert.Equal(chart.Series[1], next.Series[1]); Assert.Null(next.Categories);
        Assert.Equal("B2:B6", chart.Series[0].Values);
        next = ChartSourceEditing.Replace(chart, "Data", bindings[0], CellRange.Parse("E2:E6"));
        Assert.Equal("E2:E6", next.Categories); Assert.Equal(chart.Series, next.Series);
    }

    [Theory]
    [InlineData("Data", false, false, 1)]
    [InlineData("data", false, false, 1)]
    [InlineData("Other", false, false, 0)]
    [InlineData("Data", true, false, 0)]
    [InlineData("Data", false, true, 0)]
    public void ReportOwnedExternalAndUnavailableBindingsHaveNoGrips(string source, bool pivot, bool unavailable, int expected)
    {
        var chart = new ChartSpec { Range = "A1:B4", SourceSheet = source, PivotTableId = pivot ? "pivot" : null, SourceUnavailable = unavailable };
        Assert.Equal(expected, ChartSourceEditing.Bindings(chart, "Data").Count);
    }

    [Theory]
    [InlineData(ChartSourceHandle.Move, 2, 3, "E4:F7")]
    [InlineData(ChartSourceHandle.Move, -100, -100, "A1:B4")]
    [InlineData(ChartSourceHandle.Start, 1, 1, "C3:C5")]
    [InlineData(ChartSourceHandle.End, 2, 1, "B2:D7")]
    [InlineData(ChartSourceHandle.Start, 6, 5, "C5:G8")]
    public void RectangleTransformsAreBoundedAndCrossingEndpointsNormalize(ChartSourceHandle handle, int rows, int columns, string expected)
    {
        var binding = new ChartSourceBinding(ChartSourcePart.DataRange, -1, CellRange.Parse("B2:C5"), false);
        Assert.Equal(expected, ChartSourceEditing.Transform(binding, handle, rows, columns).ToString());
    }

    [Theory]
    [InlineData(false, "B2:B8", 4, 99, "B2:B12")]
    [InlineData(true, "B2:H2", 99, 4, "B2:L2")]
    [InlineData(false, "B2", 3, 10, "B2:B5")]
    [InlineData(true, "B2", 10, 3, "B2:E2")]
    public void VectorResizePreservesItsAxisEvenFromOneCell(bool horizontal, string input, int rows, int columns, string expected)
    {
        var binding = new ChartSourceBinding(ChartSourcePart.SeriesValues, 0, CellRange.Parse(input), horizontal);
        Assert.Equal(expected, ChartSourceEditing.Transform(binding, ChartSourceHandle.End, rows, columns).ToString());
    }

    [Fact]
    public void MoveAtWorksheetLimitKeepsFullShape()
    {
        var binding = new ChartSourceBinding(ChartSourcePart.DataRange, -1, CellRange.Parse("B2:C5"), false);
        Assert.Equal("XFC1048573:XFD1048576", ChartSourceEditing.Transform(binding, ChartSourceHandle.Move, int.MaxValue, int.MaxValue).ToString());
    }

    [Fact]
    public void OversizedInvalidAutomaticAndTwoDimensionalVectorsFailBeforeMutation()
    {
        var s = Session(); var chart = Add(s); var binding = ChartSourceEditing.Bindings(chart, "Data")[0]; var before = s.Book.ToJson();
        Assert.Throws<ArgumentException>(() => s.CommitChartSourceEdit(chart, binding, CellRange.Parse("A1:A10")));
        Assert.Throws<ArgumentException>(() => s.CommitChartSourceEdit(chart, binding, CellRange.Parse("A1:AH10")));
        Assert.Throws<ArgumentException>(() => s.CommitChartSourceEdit(chart, binding, CellRange.Parse("A1:C40000")));
        Assert.Equal(before, s.Book.ToJson());
        var explicitChart = Add(s, true); var vector = ChartSourceEditing.Bindings(explicitChart, "Data")[1];
        Assert.Throws<ArgumentException>(() => s.CommitChartSourceEdit(explicitChart, vector, CellRange.Parse("A1:B4")));
        Assert.Throws<ArgumentException>(() => s.CommitChartSourceEdit(explicitChart, vector, CellRange.Parse("A1:D1")));
    }

    [Fact]
    public void CombinedVectorBudgetIsCheckedBeforeAnyHistoryChange()
    {
        var s = Session(); var chart = Add(s, true); var binding = ChartSourceEditing.Bindings(chart, "Data")[1];
        var history = s.RetainedHistoryBytes; var before = s.Book.ToJson();
        Assert.Throws<ArgumentException>(() => s.CommitChartSourceEdit(chart, binding, CellRange.Parse("B1:B100000")));
        Assert.Equal(history, s.RetainedHistoryBytes); Assert.Equal(before, s.Book.ToJson());
    }

    [Fact]
    public void CommitAndUndoKeepSourceCellsCalculationAndGeometryRevisions()
    {
        var s = Session(); var chart = Add(s); var binding = ChartSourceEditing.Bindings(chart, "Data")[0];
        var revision = s.Book.Revision; var layout = s.Book.StructureRevision; var calculator = s.Calculation;
        var cells = s.Sheet.Cells.ToDictionary(p => p.Key, p => p.Value);
        var cache = new ChartDataCache(); var old = cache.Get(s.Book, s.Sheet, chart, s.Calculation);
        s.CommitChartSourceEdit(chart, binding, CellRange.Parse("A1:C10"));
        Assert.Equal(revision, s.Book.Revision); Assert.Equal(layout, s.Book.StructureRevision); Assert.Same(calculator, s.Calculation);
        Assert.Equal(cells.Count, s.Sheet.Cells.Count); foreach (var (address, cell) in cells) Assert.Equal(cell, s.Sheet.Cells[address]);
        var data = cache.Get(s.Book, s.Sheet, s.FindChart(chart.Id)!, s.Calculation);
        Assert.NotSame(old, data); Assert.Equal(9, data.Categories.Length);
        Assert.Same(data, cache.Get(s.Book, s.Sheet, s.FindChart(chart.Id)!, s.Calculation));
        s.Undo(); Assert.Equal("A1:C6", s.FindChart(chart.Id)!.Range);
        s.Redo(); Assert.Equal("A1:C10", s.FindChart(chart.Id)!.Range);
        Assert.Same(calculator, s.Calculation);
    }

    [Fact]
    public void StaleChartOrBindingCannotOverwriteInterveningEdits()
    {
        var s = Session(); var chart = Add(s); var binding = ChartSourceEditing.Bindings(chart, "Data")[0];
        s.UpdateChart(chart.Id, c => c with { Title = "Changed during drag" });
        Assert.Throws<InvalidOperationException>(() => s.CommitChartSourceEdit(chart, binding, CellRange.Parse("A1:C8")));
        Assert.Equal("Changed during drag", s.FindChart(chart.Id)!.Title);
        var current = s.FindChart(chart.Id)!;
        Assert.Throws<InvalidOperationException>(() => s.CommitChartSourceEdit(current, binding with { Range = CellRange.Parse("A2:C6") }, CellRange.Parse("A1:C8")));
    }

    [Fact]
    public void NativeAndStandardXlsxRetainEditedReferences()
    {
        var s = Session(); var chart = Add(s, true); var binding = ChartSourceEditing.Bindings(chart, "Data")[1];
        s.CommitChartSourceEdit(chart, binding, CellRange.Parse("D2:D6"));
        Assert.Equal("D2:D6", Workbook.FromJson(s.Book.ToJson()).ActiveSheet.Charts[0].Series[0].Values);
        var imported = XlsxWorkbook.Read(XlsxWorkbook.Write(s.Book));
        Assert.Equal("D2:D6", imported.Workbook.ActiveSheet.Charts[0].Series[0].Values);
    }

    [Theory]
    [InlineData(.5)] [InlineData(1)] [InlineData(2)]
    public void FrozenPanesUseSharedPaintAndHitCoordinates(double zoom)
    {
        var s = Session(); var chart = Add(s); s.Sheet.FrozenColumns = 1; s.Sheet.FrozenRows = 1;
        var view = new GridViewport { Width = 1200, Height = 800, Zoom = zoom }; view.Refresh(s.Sheet);
        var binding = ChartSourceEditing.Bindings(chart, "Data");
        var seen = 0;
        foreach (var pane in view.Panes()) foreach (var grip in ChartSourceGeometry.Grips(binding[0], view, pane))
        {
            var hit = ChartSourceGeometry.HitTest(binding, view, grip.X, grip.Y);
            Assert.NotNull(hit); Assert.Equal(grip.Handle, hit.Handle); seen++;
        }
        Assert.Equal(2, seen);
    }

    [Fact]
    public void ClippedEndpointsDoNotGrowFakeHandlesAndInteriorIsNotHit()
    {
        var s = Session(); var chart = Add(s); var view = new GridViewport { Width = 600, Height = 180 }; view.Refresh(s.Sheet);
        var bindings = ChartSourceEditing.Bindings(chart, "Data");
        Assert.Null(ChartSourceGeometry.HitTest(bindings, view, 180, 95));
        view.ScrollTo(20, 70);
        var grips = view.Panes().SelectMany(p => ChartSourceGeometry.Grips(bindings[0], view, p)).ToArray();
        Assert.DoesNotContain(grips, g => g.Handle == ChartSourceHandle.Start);
        Assert.Null(ChartSourceGeometry.HitTest(bindings, view, double.NaN, 50));
    }

    [Fact]
    public void SourcePreviewAndMetadataCacheDoNotMutateDocument()
    {
        var s = Session(); var chart = Add(s); using var renderer = new SpreadsheetRenderer { SelectedChartId = chart.Id };
        var bindings = renderer.SourceBindings(s); var before = s.Book.ToJson(); var revision = s.Book.DrawingRevision;
        Assert.Same(bindings, renderer.SourceBindings(s));
        renderer.ChartSourcePreview = ChartSourceEditing.Replace(chart, "Data", bindings[0], CellRange.Parse("A1:C9"));
        var view = new GridViewport { Width = 1100, Height = 700 }; view.Refresh(s.Sheet);
        using var surface = SKSurface.Create(new SKImageInfo(1100, 700)); renderer.Render(surface.Canvas, s, view);
        Assert.Equal("A1:C9", renderer.SourceBindings(s)[0].Range.ToString());
        Assert.Equal(before, s.Book.ToJson()); Assert.Equal(revision, s.Book.DrawingRevision);
        renderer.ChartSourcePreview = null;
        Assert.Equal("A1:C6", renderer.SourceBindings(s)[0].Range.ToString());
    }
}
