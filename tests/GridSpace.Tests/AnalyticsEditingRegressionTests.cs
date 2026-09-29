using GridSpace.Core;
using GridSpace.Editing;
using GridSpace.Formulas;
using GridSpace.IO;
using GridSpace.Layout;
using Xunit;

namespace GridSpace.Tests;

public sealed class AnalyticsEditingRegressionTests
{
    [Fact]
    public void ChartClipboardRetainsSourceAcrossSheetsWithoutCopyingWorkbookData()
    {
        var session = ChartAndPivotTests.Sales();
        var chart = new ChartSpec { Range = "A1:D6", SourceSheet = null, Series = [new() { Name = "Revenue", Values = "C2:C6" }], Categories = "B2:B6" };
        session.AddChart(chart);
        var payload = session.CopyChart(chart.Id);
        Assert.StartsWith("GridSpace.Chart/1\n", payload);
        Assert.DoesNotContain("100", payload); // no series caches or source rows in the clipboard
        session.AddSheet("Destination"); session.Select("F10");
        var pasted = session.PasteChart(payload);
        var clone = session.FindChart(pasted)!;
        Assert.NotEqual(chart.Id, clone.Id);
        Assert.Equal("Sales", clone.SourceSheet);
        Assert.Equal(9, clone.Row); Assert.Equal(5, clone.Column);
        var values = ChartDataResolver.Resolve(session.Book, session.Sheet, clone, session.Calculation);
        Assert.Equal(new double?[] { 10, 20, 30, 100, 40 }, values.Series[0].Values);
        session.Undo(); Assert.Empty(session.Sheet.Charts);
        session.Redo(); Assert.Single(session.Sheet.Charts);
    }

    [Theory]
    [InlineData("not a chart")]
    [InlineData("GridSpace.Chart/1\nnull")]
    [InlineData("GridSpace.Chart/1\n{")]
    [InlineData("GridSpace.Chart/1\n{\"Series\":null}")]
    [InlineData("GridSpace.Chart/1\n{\"Width\":-100}")]
    public void InvalidDrawingClipboardNeverChangesWorkbook(string text)
    {
        var session = ChartAndPivotTests.Sales(); var before = session.Book.ToJson();
        Assert.ThrowsAny<ArgumentException>(() => session.PasteChart(text));
        Assert.Equal(before, session.Book.ToJson()); Assert.False(session.CanUndo);
    }

    [Fact]
    public void DrawingOnlyEditsKeepCalculationAndLayoutRevisionsAndCachedData()
    {
        var session = ChartAndPivotTests.Sales();
        session.Sheet.Set("F1", "=SUM(C2:C6)");
        var chart = new ChartSpec { Range = "B1:D6" }; session.AddChart(chart);
        var cache = new ChartDataCache(); var originalData = cache.Get(session.Book, session.Sheet, chart, session.Calculation);
        Assert.Equal(200, session.Calculation.Evaluate(session.Sheet, "F1").Number);
        var revision = session.Book.Revision; var structure = session.Book.StructureRevision;
        var evaluations = session.Calculation.EvaluatedCellCount;
        var book = session.Book; var calculator = session.Calculation;
        for (var i = 0; i < 100; i++)
        {
            session.UpdateChart(chart.Id, c => c with { OffsetX = i + 1, Title = "Revision " + i });
            Assert.Same(originalData, cache.Get(session.Book, session.Sheet, session.FindChart(chart.Id)!, calculator));
        }
        Assert.Equal(revision, book.Revision); Assert.Equal(structure, book.StructureRevision);
        Assert.Equal(1, cache.ResolveCount);
        session.Undo(); session.Redo();
        Assert.Same(book, session.Book); Assert.Same(calculator, session.Calculation);
        Assert.Equal(200, calculator.Evaluate(session.Sheet, "F1").Number);
        Assert.Equal(evaluations, calculator.EvaluatedCellCount);
        Assert.InRange(session.RetainedHistoryBytes, 1, 100_000);
    }

    [Fact]
    public void ChartBindingsFollowRowEditsAndSheetRenameIncludingExplicitSeries()
    {
        var session = ChartAndPivotTests.Sales(); session.AddSheet("Charts");
        var chart = new ChartSpec { SourceSheet = "Sales", Range = "A1:D6", Categories = "B2:B6", Series = [new() { Values = "C2:C6" }] };
        session.AddChart(chart);
        session.SwitchSheet(0); session.InsertRows(2, 2);
        var updated = session.Book.FindSheet("Charts")!.Charts.Single();
        Assert.Equal("A1:D8", updated.Range); Assert.Equal("B2:B8", updated.Categories); Assert.Equal("C2:C8", updated.Series[0].Values);
        session.RenameSheet("Orders");
        Assert.Equal("Orders", session.Book.FindSheet("Charts")!.Charts.Single().SourceSheet);
        session.Undo(); session.Undo();
        Assert.Equal("C2:C6", session.Book.FindSheet("Charts")!.Charts.Single().Series[0].Values);
    }

    [Fact]
    public void RemovingAnEntireChartSourceCannotReconnectItToNewData()
    {
        var session = ChartAndPivotTests.Sales(); session.AddSheet("Charts");
        var chart = new ChartSpec { SourceSheet = "Sales", Range = "A1:D6" }; session.AddChart(chart);
        session.SwitchSheet(0); session.DeleteSheet(); session.AddSheet("Sales");
        var owner = session.Book.FindSheet("Charts")!; var lost = owner.Charts.Single();
        Assert.True(lost.SourceUnavailable);
        Assert.Throws<InvalidOperationException>(() => ChartDataResolver.Resolve(session.Book, owner, lost, session.Calculation));
    }

    [Fact]
    public void SourceSchemaEditsRebaseFieldsAndRequireFreshCache()
    {
        var session = ChartAndPivotTests.Sales(); session.AddSheet("Report");
        var definition = ChartAndPivotTests.Definition(); session.SetPivotTable(definition);
        session.SwitchSheet(0); session.InsertColumns(1);
        var pivot = session.Book.FindSheet("Report")!.PivotTables.Single();
        Assert.Equal("A1:E6", pivot.SourceRange);
        Assert.Equal(2, pivot.Columns.Single()); Assert.Equal(3, pivot.Values.Single().Field);
        Assert.Null(pivot.Cache);
        session.SetInput("Inserted field", CellAddress.Parse("B1"));
        session.SwitchSheet(1); session.RefreshPivotTable(pivot.Id);
        Assert.NotNull(session.Sheet.PivotTables.Single().Cache);
        Assert.Equal(200, session.Calculation.Evaluate(session.Sheet, "D4").Number);
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 2)]
    public void ReferencedPivotSourceHeaderAndFieldDeletionRollBack(bool rows, int position)
    {
        var session = ChartAndPivotTests.Sales(); session.AddSheet("Report");
        session.SetPivotTable(ChartAndPivotTests.Definition()); session.SwitchSheet(0);
        var before = session.Book.ToJson();
        Assert.Throws<InvalidOperationException>(() => { if (rows) session.DeleteRows(position); else session.DeleteColumns(position); });
        Assert.Equal(before, session.Book.ToJson());
    }

    [Fact]
    public void ReportAxisEditsCannotPartiallyShiftOwnedOutputButCanMoveWholeReport()
    {
        var session = ChartAndPivotTests.Sales(); session.AddSheet("Report");
        session.SetPivotTable(ChartAndPivotTests.Definition() with { Destination = "D5" });
        var before = session.Book.ToJson();
        Assert.Throws<InvalidOperationException>(() => session.InsertRows(5));
        Assert.Throws<InvalidOperationException>(() => session.DeleteColumns(4));
        Assert.Equal(before, session.Book.ToJson());
        session.InsertRows(0, 3); session.InsertColumns(0, 2);
        var report = session.Sheet.PivotTables.Single();
        Assert.Equal("F8", report.Destination); Assert.Equal("F8:I11", report.OutputRange);
        Assert.Equal(200, session.Calculation.Evaluate(session.Sheet, "I11").Number);
    }

    [Fact]
    public void DuplicateReportHasIndependentIdentitiesAndLinkedCharts()
    {
        var session = ChartAndPivotTests.Sales(); session.AddSheet("Report");
        var definition = ChartAndPivotTests.Definition(); session.SetPivotTable(definition);
        var chartId = session.AddPivotChart(definition.Id);
        session.DuplicateSheet();
        var pivot = session.Sheet.PivotTables.Single(); var chart = session.Sheet.Charts.Single();
        Assert.NotEqual(definition.Id, pivot.Id); Assert.NotEqual(chartId, chart.Id);
        Assert.NotEqual(definition.Name, pivot.Name); Assert.Equal(pivot.Id, chart.PivotTableId);
        Assert.Equal(session.Sheet.Name, chart.SourceSheet);
        var data = ChartDataResolver.Resolve(session.Book, session.Sheet, chart, session.Calculation);
        Assert.Equal(new[] { "East", "West" }, data.Categories);
    }

    [Fact]
    public void FrozenPaneChartHitTestingUsesTheRenderedClipAndTopmostDrawing()
    {
        var sheet = new Worksheet { FrozenRows = 2, FrozenColumns = 1 };
        var viewport = new GridViewport { Width = 1200, Height = 800 }; viewport.Refresh(sheet); viewport.ScrollTo(150, 200);
        var a = new ChartSpec { Row = 12, Column = 5, Width = 250, Height = 150 };
        var b = a with { Id = "top" };
        var pane = viewport.Panes().First(p => p.FirstRow == 2 && p.FirstColumn == 1);
        var rect = ChartGeometry.Bounds(a, viewport, pane);
        var hit = ChartGeometry.HitTest(new[] { a, b }, viewport, rect.X + 100, rect.Y + 75, null);
        Assert.NotNull(hit); Assert.Equal("top", hit.Value.Id);
        Assert.Null(ChartGeometry.HitTest(new[] { a, b }, viewport, 10, 10, null));
    }

    [Fact]
    public void FilterThatRemovesEveryRowProducesAValidCacheAndExport()
    {
        var session = ChartAndPivotTests.Sales(); session.AddSheet("Report");
        var spec = ChartAndPivotTests.Definition() with { Columns = [], Filters = [new() { Field = 1, Values = [] }] };
        session.SetPivotTable(spec);
        var bytes = XlsxWorkbook.Write(session.Book);
        var imported = XlsxWorkbook.Read(bytes);
        Assert.Single(imported.Workbook.ActiveSheet.PivotTables);
        Assert.Equal(0, imported.Workbook.ActiveSheet.PivotTables[0].LastSourceRowCount);
        Assert.Equal(5, imported.Workbook.ActiveSheet.PivotTables[0].Cache!.Rows.Length);
    }

    [Fact]
    public void AnalyticsSampleRendersOwnedReportAndLinkedCharts()
    {
        var session = new SpreadsheetSession(AnalyticsSampleWorkbook.Create());
        var viewport = new GridViewport { Width = 1440, Height = 850 };
        viewport.Refresh(session.Sheet);
        using var renderer = new GridSpace.Skia.SpreadsheetRenderer();
        using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(1440, 850));
        renderer.SelectedChartId = session.Sheet.Charts[0].Id;
        renderer.Render(surface.Canvas, session, viewport);
        Assert.InRange(renderer.LastRenderedCellCount, 1, 2500);
        Assert.Equal(2, renderer.Charts.Data.ResolveCount);
        Directory.CreateDirectory("artifacts/rendering");
        using var image = surface.Snapshot();
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes("artifacts/rendering/analytics-pivot-and-charts.png", data.ToArray());
    }

    [Theory]
    [InlineData("1Report")] [InlineData("A1")]
    public void PivotNamesCannotBeCellReferencesOrStartWithDigits(string name)
    {
        var session = ChartAndPivotTests.Sales();
        var definition = ChartAndPivotTests.Definition() with { Name = name };
        Assert.Throws<ArgumentException>(() => session.SetPivotTable(definition));
    }

    [Fact]
    public void CachedPivotChartLookupAllocatesNothingAndMetadataRefreshInvalidates()
    {
        var session = ChartAndPivotTests.Sales(); session.AddSheet("Report");
        var spec = ChartAndPivotTests.Definition(); session.SetPivotTable(spec);
        var id = session.AddPivotChart(spec.Id); var chart = session.FindChart(id)!;
        var cache = new ChartDataCache();
        var data = cache.Get(session.Book, session.Sheet, chart, session.Calculation);
        for (var i = 0; i < 100; i++) _ = cache.Get(session.Book, session.Sheet, chart, session.Calculation);
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) _ = cache.Get(session.Book, session.Sheet, chart, session.Calculation);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - allocated);
        Assert.Equal(1, cache.ResolveCount);
        session.RefreshPivotTable(spec.Id);
        Assert.NotSame(data, cache.Get(session.Book, session.Sheet, chart, session.Calculation));
        Assert.Equal(2, cache.ResolveCount);
    }
}
