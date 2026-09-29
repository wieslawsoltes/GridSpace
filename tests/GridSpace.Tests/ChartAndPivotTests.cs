using GridSpace.Core;
using GridSpace.Editing;
using GridSpace.Formulas;
using GridSpace.Layout;
using GridSpace.Skia;
using SkiaSharp;
using Xunit;

namespace GridSpace.Tests;

public sealed class ChartAndPivotTests
{
    public static SpreadsheetSession Sales()
    {
        var book = new Workbook();
        book.ActiveSheet.Name = "Sales";
        string[][] rows = [
            ["Region", "Product", "Sales", "Units"],
            ["East", "A", "10", "1"], ["East", "B", "20", "2"],
            ["East", "A", "30", "3"], ["West", "A", "100", "4"],
            ["West", "B", "40", "5"]];
        for (var r = 0; r < rows.Length; r++) for (var c = 0; c < rows[r].Length; c++)
            book.ActiveSheet.Set(new CellAddress(r, c), new Cell { Input = rows[r][c] });
        return new(book);
    }
    public static PivotTableSpec Definition() => new()
    {
        SourceSheet = "Sales", SourceRange = "A1:D6", Destination = "A1",
        Rows = [0], Columns = [1], Values = [new() { Field = 2 }]
    };

    [Fact]
    public void PivotProducesRowColumnAndGrandTotals()
    {
        var s = Sales(); var p = PivotEngine.Calculate(s.Book, Definition(), s.Calculation);
        Assert.Equal(4, p.RowCount); Assert.Equal(4, p.ColumnCount);
        Assert.Equal("East", p[1, 0].Text);
        Assert.Equal(40, p[1, 1].Number); Assert.Equal(20, p[1, 2].Number);
        Assert.Equal(60, p[1, 3].Number);
        Assert.Equal(100, p[2, 1].Number); Assert.Equal(40, p[2, 2].Number);
        Assert.Equal(200, p[3, 3].Number);
    }

    [Theory]
    [InlineData(PivotAggregate.Sum, 200)]
    [InlineData(PivotAggregate.Count, 5)]
    [InlineData(PivotAggregate.CountNumbers, 5)]
    [InlineData(PivotAggregate.Average, 40)]
    [InlineData(PivotAggregate.Min, 10)]
    [InlineData(PivotAggregate.Max, 100)]
    [InlineData(PivotAggregate.Product, 24000000)]
    [InlineData(PivotAggregate.PopulationVariance, 1000)]
    [InlineData(PivotAggregate.Variance, 1250)]
    public void AggregatesCalculateFromSourceRatherThanAggregatedValues(PivotAggregate kind, double expected)
    {
        var s = Sales(); var spec = Definition() with { Values = [new() { Field = 2, Aggregate = kind }] };
        var p = PivotEngine.Calculate(s.Book, spec, s.Calculation);
        Assert.Equal(expected, p[p.RowCount - 1, p.ColumnCount - 1].Number, 8);
    }

    [Fact]
    public void ReportFiltersAndPercentagesUseFilteredWeightedTotals()
    {
        var s = Sales(); var spec = Definition() with
        {
            Columns = [], Filters = [new() { Field = 1, Values = ["A"] }],
            Values = [new() { Field = 2, ShowAs = PivotShowAs.PercentOfGrandTotal }]
        };
        var p = PivotEngine.Calculate(s.Book, spec, s.Calculation);
        Assert.Equal(40d / 140, p[1, 1].Number, 10);
        Assert.Equal(100d / 140, p[2, 1].Number, 10);
        Assert.Equal(1, p[3, 1].Number);
        Assert.Equal(3, p.Source.Rows.Count);
    }

    [Fact]
    public void NumericAndTextKeysRemainDistinctWhileTextKeysIgnoreCase()
    {
        var s = Sales();
        s.Sheet.Set("A2", "1"); s.Sheet.Set("A3", "'1");
        s.Sheet.Set("A4", "east"); s.Sheet.Set("A5", "EAST");
        var p = PivotEngine.Calculate(s.Book, Definition() with { Columns = [] }, s.Calculation);
        Assert.Equal(4, p.RowKeys.Count);
        Assert.Contains(p.RowKeys, k => k.Items[0].Kind == ValueKind.Number);
        Assert.Contains(p.RowKeys, k => k.Items[0].Kind == ValueKind.Text && k.Items[0].Text == "1");
    }

    [Fact]
    public void PivotCreationRefreshUndoAndOwnershipAreAtomic()
    {
        var s = Sales(); s.AddSheet("Report");
        var book = s.Book; var calc = s.Calculation;
        var spec = Definition(); s.SetPivotTable(spec);
        Assert.Single(s.Sheet.PivotTables); Assert.Equal(200, s.Calculation.Evaluate(s.Sheet, "D4").Number);
        Assert.Throws<InvalidOperationException>(() => s.SetInput("999", CellAddress.Parse("B2")));
        s.Select("B2"); s.ApplyStyle(style => style with { Bold = true }); // formatting is editable
        s.Book.FindSheet("Sales")!.Set("C2", "50");
        s.RefreshPivotTable(spec.Id);
        Assert.Equal(240, s.Calculation.Evaluate(s.Sheet, "D4").Number);
        s.Undo(); Assert.Equal(200, s.Calculation.Evaluate(s.Sheet, "D4").Number);
        Assert.Same(book, s.Book); Assert.Same(calc, s.Calculation);
        s.Redo(); Assert.Equal(240, s.Calculation.Evaluate(s.Sheet, "D4").Number);
    }

    [Fact]
    public void FailedPivotExpansionDoesNotClearOldOutput()
    {
        var s = Sales(); s.AddSheet("Report"); var spec = Definition();
        s.SetPivotTable(spec); s.SetInput("keep me", CellAddress.Parse("E2"));
        var before = s.Book.ToJson();
        Assert.Throws<InvalidOperationException>(() => s.SetPivotTable(spec with
        { Values = [new() { Field = 2 }, new() { Field = 3 }] }));
        Assert.Equal(before, s.Book.ToJson());
    }

    [Fact]
    public void PivotDetailsUseSnapshotUntilAnExplicitRefresh()
    {
        var s = Sales(); s.AddSheet("Report"); var spec = Definition(); s.SetPivotTable(spec);
        s.Book.FindSheet("Sales")!.Set("C2", "999");
        s.DrillDownPivot(CellAddress.Parse("B2"));
        Assert.StartsWith("Details", s.Sheet.Name);
        Assert.Equal(10, s.Calculation.Evaluate(s.Sheet, "C2").Number);
        Assert.Equal(30, s.Calculation.Evaluate(s.Sheet, "C3").Number);
    }

    [Fact]
    public void PivotCacheSurvivesNativeSaveAndPreservesLiteralTypes()
    {
        var s = Sales(); s.Sheet.Set("B2", "'=literal");
        s.AddSheet("Report"); var spec = Definition(); s.SetPivotTable(spec);
        var loaded = Workbook.FromJson(s.Book.ToJson());
        var cache = loaded.ActiveSheet.PivotTables.Single().Cache!;
        Assert.Equal("'=literal", cache.Rows[0][1]);
        Assert.Equal(5, PivotEngine.FromCache(loaded.ActiveSheet.PivotTables.Single()).Source.Rows.Count);
    }

    [Fact]
    public void PivotCannotOverwriteSourceOrAnotherReport()
    {
        var s = Sales(); Assert.Throws<InvalidOperationException>(() => s.SetPivotTable(Definition()));
        s.AddSheet("Report"); s.SetPivotTable(Definition());
        Assert.Throws<InvalidOperationException>(() => s.SetPivotTable(Definition() with { Name = "Other" }));
    }

    [Fact]
    public void EmptyFilteredReportDoesNotFailAndHasDeterministicShape()
    {
        var s = Sales(); var spec = Definition() with { Columns = [], Filters = [new() { Field = 1, Values = [] }] };
        var report = PivotEngine.Calculate(s.Book, spec, s.Calculation);
        Assert.Empty(report.Source.Rows);
        Assert.True(report.RowCount >= 2); Assert.Equal(2, report.ColumnCount);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public void GrandTotalSwitchesControlOutputShape(bool row, bool col)
    {
        var s = Sales(); var p = PivotEngine.Calculate(s.Book, Definition() with { RowGrandTotals = row, ColumnGrandTotals = col }, s.Calculation);
        Assert.Equal(col ? 4 : 3, p.RowCount); Assert.Equal(row ? 4 : 3, p.ColumnCount);
    }

    [Fact]
    public void ChartDataUsesAllSeriesAndRetainsMissingValues()
    {
        var s = Sales(); var chart = new ChartSpec { Range = "B1:D6" };
        var data = ChartDataResolver.Resolve(s.Book, s.Sheet, chart, s.Calculation);
        Assert.Equal(2, data.Series.Count); Assert.Equal(5, data.Categories.Length);
        Assert.Equal(new double?[] { 10, 20, 30, 100, 40 }, data.Series[0].Values);
        s.Sheet.Set("C3", "'not a number");
        data = ChartDataResolver.Resolve(s.Book, s.Sheet, chart, s.Calculation);
        Assert.Null(data.Series[0].Values[1]);
    }

    [Fact]
    public void DrawingEditsDoNotInvalidateFormulaOrChartDataCaches()
    {
        var s = Sales(); s.Sheet.Set("F1", "=SUM(C2:C6)");
        Assert.Equal(200, s.Calculation.Evaluate(s.Sheet, "F1").Number);
        var cache = new ChartDataCache(); var id = s.AddChart(new ChartSpec { Range = "B1:D6" });
        var data = cache.Get(s.Book, s.Sheet, s.FindChart(id)!, s.Calculation);
        var revision = s.Book.Revision; var structure = s.Book.StructureRevision; var calc = s.Calculation;
        s.UpdateChart(id, c => c with { OffsetX = 30, Width = 750 });
        Assert.Same(data, cache.Get(s.Book, s.Sheet, s.FindChart(id)!, s.Calculation));
        Assert.Equal(revision, s.Book.Revision); Assert.Equal(structure, s.Book.StructureRevision);
        s.Undo(); Assert.Equal(480, s.FindChart(id)!.Width);
        s.Redo(); Assert.Equal(750, s.FindChart(id)!.Width);
        Assert.Same(calc, s.Calculation); Assert.Equal(1, cache.ResolveCount);
    }

    [Fact]
    public void ChartCloneAndZOrderAreUndoableWithoutSharingMutableSeriesLists()
    {
        var s = Sales(); var id = s.AddChart(new ChartSpec { Range = "B1:D6", Series = [new() { Values = "C2:C6" }] });
        var other = s.DuplicateChart(id);
        Assert.NotEqual(id, other); Assert.NotSame(s.FindChart(id)!.Series, s.FindChart(other)!.Series);
        s.OrderChart(id, true); Assert.Equal(id, s.Sheet.Charts[^1].Id);
        s.Undo(); Assert.Equal(other, s.Sheet.Charts[^1].Id);
        s.DeleteChart(other); Assert.Single(s.Sheet.Charts); s.Undo(); Assert.Equal(2, s.Sheet.Charts.Count);
    }

    [Theory]
    [InlineData(.25)] [InlineData(.5)] [InlineData(1)] [InlineData(2)] [InlineData(4)]
    public void ChartHandlesAndHitTestingUseTheSameZoomedGeometry(double zoom)
    {
        var s = Sales(); var spec = new ChartSpec { Row = 2, Column = 2, Width = 200, Height = 140, OffsetX = 7, OffsetY = 5 };
        s.AddChart(spec); var view = new GridViewport { Width = 3000, Height = 1500, Zoom = zoom }; view.Refresh(s.Sheet);
        var pane = view.Panes()[0]; var bounds = ChartGeometry.Bounds(spec, view, pane);
        var hit = ChartGeometry.HitTest(s.Sheet.Charts, view, bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2, spec.Id);
        Assert.Equal(spec.Id, hit?.Id);
        foreach (var handle in ChartGeometry.Handles(bounds))
        {
            var actual = ChartGeometry.HitTest(s.Sheet.Charts, view, handle.X, handle.Y, spec.Id);
            Assert.Equal(handle.Part, actual?.Part);
        }
    }

    [Theory]
    [InlineData(ChartHitPart.NorthWest)] [InlineData(ChartHitPart.North)] [InlineData(ChartHitPart.NorthEast)]
    [InlineData(ChartHitPart.East)] [InlineData(ChartHitPart.SouthEast)] [InlineData(ChartHitPart.South)]
    [InlineData(ChartHitPart.SouthWest)] [InlineData(ChartHitPart.West)]
    public void EveryResizeHandleProducesFiniteBoundedGeometry(ChartHitPart part)
    {
        var original = new GridRect(400, 300, 600, 350);
        var box = ChartGeometry.Transform(original, part, 10000, -10000, true);
        Assert.True(double.IsFinite(box.X) && double.IsFinite(box.Y));
        Assert.InRange(box.Width, 120, 4000); Assert.InRange(box.Height, 100, 4000);
        Assert.True(box.X >= 0 && box.Y >= 0);
    }

    [Theory]
    [InlineData(ChartKind.Column)] [InlineData(ChartKind.Bar)] [InlineData(ChartKind.Line)]
    [InlineData(ChartKind.Area)] [InlineData(ChartKind.Pie)] [InlineData(ChartKind.Doughnut)]
    [InlineData(ChartKind.Scatter)] [InlineData(ChartKind.Radar)] [InlineData(ChartKind.Combo)]
    public void ChartKindsRenderRealMultiseriesPixels(ChartKind kind)
    {
        var s = Sales(); var chart = new ChartSpec
        {
            Range = kind == ChartKind.Scatter ? "C1:D6" : "B1:D6", Kind = kind, Title = kind + " · sales analysis", ShowDataLabels = true,
            CategoryAxisTitle = "Product", ValueAxisTitle = "Revenue", Legend = ChartLegendPosition.Bottom
        };
        using var fonts = new TypefaceCatalog(); using var renderer = new ChartRenderer(fonts);
        using var surface = SKSurface.Create(new SKImageInfo(800, 460));
        surface.Canvas.Clear(SKColors.White);
        renderer.Render(surface.Canvas, s.Book, s.Sheet, chart, s.Calculation, new GridRect(0, 0, 800, 460));
        using var bitmap = SKBitmap.FromImage(surface.Snapshot());
        Assert.True(renderer.LastRenderedPoints > 0);
        Assert.True(bitmap.Pixels.Count(c => c.Blue > c.Red + 30 || c.Red > c.Blue + 50) > 100);
        var path = Path.Combine("artifacts", "rendering", "charts-" + kind + ".png");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var image = surface.Snapshot(); using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, encoded.ToArray());
    }
}
