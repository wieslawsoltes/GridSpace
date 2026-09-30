using System.Collections.Immutable;
using System.IO.Compression;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using GridSpace.Core;
using GridSpace.Editing;
using GridSpace.Formulas;
using GridSpace.IO;
using GridSpace.Layout;
using GridSpace.Skia;
using SkiaSharp;
using Xunit;

namespace GridSpace.Tests;

public sealed class PivotHierarchyTests
{
    internal static SpreadsheetSession Create(PivotLayout layout = PivotLayout.Compact, PivotSubtotals subtotals = PivotSubtotals.None, PivotAggregate aggregate = PivotAggregate.Sum)
    {
        var book = new Workbook(); book.ActiveSheet.Name = "Source";
        string[][] data = [["Region", "Product", "Quarter", "Amount"],
            ["North", "A", "Q1", "10"], ["North", "A", "Q2", "20"], ["North", "B", "Q1", "90"],
            ["South", "A", "Q1", "40"], ["South", "B", "Q1", "50"], ["South", "B", "Q2", "60"]];
        for (var row = 0; row < data.Length; row++) for (var col = 0; col < data[row].Length; col++)
            book.ActiveSheet.Set(new CellAddress(row, col), new Cell { Input = data[row][col] });
        var s = new SpreadsheetSession(book); s.AddSheet("Report");
        s.SetPivotTable(new PivotTableSpec { Name = "ReportPivot", SourceSheet = "Source", SourceRange = "A1:D7", Destination = "B3",
            Rows = [0, 1], Columns = [2], Values = [new() { Field = 3, Aggregate = aggregate }], Layout = layout, Subtotals = subtotals });
        return s;
    }
    private static PivotTableSpec Pivot(SpreadsheetSession s) => s.Sheet.PivotTables[0];
    private static PivotReport Report(SpreadsheetSession s) => PivotReportCache.Get(Pivot(s));
    private static PivotGroupPath North(SpreadsheetSession s) => PivotHierarchy.Path(Pivot(s), new PivotKey([CalcValue.Str("North")]));

    [Theory]
    [InlineData(PivotLayout.Compact, PivotSubtotals.None, 8)]
    [InlineData(PivotLayout.Compact, PivotSubtotals.Top, 8)]
    [InlineData(PivotLayout.Compact, PivotSubtotals.Bottom, 10)]
    [InlineData(PivotLayout.Outline, PivotSubtotals.None, 8)]
    [InlineData(PivotLayout.Outline, PivotSubtotals.Top, 8)]
    [InlineData(PivotLayout.Outline, PivotSubtotals.Bottom, 10)]
    [InlineData(PivotLayout.Tabular, PivotSubtotals.None, 6)]
    [InlineData(PivotLayout.Tabular, PivotSubtotals.Top, 8)]
    [InlineData(PivotLayout.Tabular, PivotSubtotals.Bottom, 8)]
    public void LayoutsHaveProvenanceAndNonDuplicatedGrandTotals(PivotLayout layout, PivotSubtotals totals, int rows)
    {
        var s = Create(layout, totals); var report = Report(s);
        Assert.Equal(rows, report.RowCount);
        Assert.Equal(layout == PivotLayout.Compact ? 1 : 2, report.LabelColumns);
        Assert.Equal(270, report[report.RowCount - 1, report.ColumnCount - 1].Number);
        Assert.Equal(4, report.RowBands.Count(b => b.IsChartCategory));
        Assert.Equal(6, report.DrillDown(report.RowCount - 1, report.ColumnCount - 1).Count);
    }

    [Theory]
    [InlineData(PivotAggregate.Average, 40)]
    [InlineData(PivotAggregate.Sum, 120)]
    [InlineData(PivotAggregate.Count, 3)]
    [InlineData(PivotAggregate.CountNumbers, 3)]
    [InlineData(PivotAggregate.Min, 10)]
    [InlineData(PivotAggregate.Max, 90)]
    [InlineData(PivotAggregate.Product, 18000)]
    [InlineData(PivotAggregate.Variance, 1900)]
    [InlineData(PivotAggregate.PopulationVariance, 1266.6666666666667)]
    public void SubtotalsUseSourceAccumulatorsNotChildResults(PivotAggregate aggregate, double expected)
    {
        var s = Create(PivotLayout.Compact, PivotSubtotals.Top, aggregate); var r = Report(s);
        Assert.Equal(PivotRowKind.Subtotal, r.RowBands[0].Kind);
        Assert.Equal(expected, r[1, r.ColumnCount - 1].Number, 8);
        Assert.Equal(3, r.DrillDown(1, r.ColumnCount - 1).Count);
    }

    [Theory]
    [InlineData(PivotLayout.Compact)] [InlineData(PivotLayout.Outline)] [InlineData(PivotLayout.Tabular)]
    public void CollapseIsUndoableAndUsesTheLastRefreshEpoch(PivotLayout layout)
    {
        var s = Create(layout, PivotSubtotals.Bottom); var before = Pivot(s); var initialCount = Report(s).RowCount;
        s.Book.FindSheet("Source")!.Set("D2", "9999");
        s.TogglePivotGroup(before.Id, North(s));
        Assert.Same(before.Cache, Pivot(s).Cache);
        Assert.Equal(PivotRowKind.Collapsed, Report(s).RowBands[0].Kind);
        Assert.Equal(120, Report(s)[1, Report(s).ColumnCount - 1].Number);
        Assert.True(Report(s).RowCount < initialCount);
        Assert.Equal(3, Report(s).DrillDown(1, Report(s).ColumnCount - 1).Count);
        Assert.Equal("10", Pivot(s).Cache!.Rows[0][3]);
        s.Undo(); Assert.Empty(Pivot(s).CollapsedRows); Assert.Equal(initialCount, Report(s).RowCount);
        s.Redo(); Assert.Single(Pivot(s).CollapsedRows); Assert.Equal(120, Report(s)[1, Report(s).ColumnCount - 1].Number);
        s.RefreshPivotTable(before.Id); Assert.Equal(10109, Report(s)[1, Report(s).ColumnCount - 1].Number);
    }

    [Fact]
    public void ExpandAllAndCollapseAllUseTypedGroupKeys()
    {
        var s = Create(); s.SetPivotGroupsExpanded(Pivot(s).Id, false);
        Assert.Equal(2, Pivot(s).CollapsedRows.Count);
        Assert.Equal(4, Report(s).RowCount); // header, North, South, grand
        s.SetPivotGroupsExpanded(Pivot(s).Id, true);
        Assert.Empty(Pivot(s).CollapsedRows); Assert.Equal(8, Report(s).RowCount);
    }

    [Fact]
    public void FailedExpansionCannotOverwriteUnrelatedCells()
    {
        var s = Create(); s.SetPivotGroupsExpanded(Pivot(s).Id, false);
        s.SetInput("keep", CellAddress.Parse("B9"));
        var before = s.Book.ToJson();
        Assert.Throws<InvalidOperationException>(() => s.SetPivotGroupsExpanded(Pivot(s).Id, true));
        Assert.Equal(before, s.Book.ToJson());
    }

    [Fact]
    public void ChangingRowFieldsDiscardsOnlyIncompatibleCollapsePrefixes()
    {
        var s = Create(); s.TogglePivotGroup(Pivot(s).Id, North(s));
        s.ReconfigurePivotTable(Pivot(s) with { Rows = [1, 0] });
        Assert.Empty(Pivot(s).CollapsedRows);
        Assert.Equal("A", Report(s).RowBands[0].Key.Label);
    }

    [Fact]
    public void SubtotalsAndCollapsedTotalsAreExcludedFromDuplicateChartAggregation()
    {
        var s = Create(PivotLayout.Compact, PivotSubtotals.Bottom); var id = s.AddPivotChart(Pivot(s).Id);
        var cache = new ChartDataCache(); var data = cache.Get(s.Book, s.Sheet, s.FindChart(id)!, s.Calculation);
        Assert.Equal(new[] { "North / A", "North / B", "South / A", "South / B" }, data.Categories);
        Assert.Equal(270, data.Series.Sum(v => v.Values.Sum(x => x ?? 0)));
        s.TogglePivotGroup(Pivot(s).Id, North(s));
        var collapsed = cache.Get(s.Book, s.Sheet, s.FindChart(id)!, s.Calculation);
        Assert.Equal(new[] { "North", "South / A", "South / B" }, collapsed.Categories);
        Assert.Equal(270, collapsed.Series.Sum(v => v.Values.Sum(x => x ?? 0)));
        Assert.All(collapsed.Series, v => Assert.NotNull(v.ReferenceAreas));
        Assert.Same(collapsed, cache.Get(s.Book, s.Sheet, s.FindChart(id)!, s.Calculation));
    }

    [Fact]
    public void ReportHeadersRejectDrillThroughAndTotalsRetainAllContributingRecords()
    {
        var s = Create(); var r = Report(s);
        Assert.Throws<ArgumentException>(() => r.DrillDown(1, r.LabelColumns));
        Assert.Single(r.DrillDown(2, r.LabelColumns));
        Assert.Equal(4, r.DrillDown(r.RowCount - 1, r.LabelColumns).Count);
        Assert.Equal(2, r.DrillDown(r.RowCount - 1, r.LabelColumns + 1).Count);
    }

    [Theory]
    [InlineData(.5)] [InlineData(1)] [InlineData(2)]
    public void PaintedToggleBoundsAndHitTestingAgreeWithFrozenPanes(double zoom)
    {
        var s = Create(); s.Sheet.FrozenRows = 2; s.Sheet.FrozenColumns = 1;
        var view = new GridViewport { Width = 1200, Height = 800, Zoom = zoom }; view.Refresh(s.Sheet);
        var a = new CellAddress(3, 1); var cell = view.CellBounds(a);
        Assert.True(PivotOutlineGeometry.TryGetCell(s.Sheet, a, out var pivot, out var outline));
        var bounds = PivotOutlineGeometry.ToggleBounds(cell, outline, zoom);
        var hit = PivotOutlineGeometry.HitTest(s.Sheet, view, bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
        Assert.NotNull(hit); Assert.True(hit.Expanded); Assert.Equal(pivot!.Id, hit.PivotId);
        Assert.Null(PivotOutlineGeometry.HitTest(s.Sheet, view, bounds.Right + 5, bounds.Y + 2));
    }

    [Fact]
    public void WarmOutlineReadsDoNotAllocateAndShareTheCachedReport()
    {
        var s = Create(); var pivot = Pivot(s); var report = Report(s); _ = report.OutlineCells;
        var address = new CellAddress(3, 1);
        for (var i = 0; i < 100; i++) PivotOutlineGeometry.TryGetCell(s.Sheet, address, out _, out _);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) PivotOutlineGeometry.TryGetCell(s.Sheet, address, out _, out _);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Same(report, Report(s));
    }

    [Fact]
    public void HierarchicalReportRendersWithVisibleExpansionButtons()
    {
        var s = Create(PivotLayout.Compact, PivotSubtotals.Bottom); s.AddPivotChart(Pivot(s).Id);
        var view = new GridViewport { Width = 1200, Height = 700 }; view.Refresh(s.Sheet);
        using var renderer = new SpreadsheetRenderer(); using var surface = SKSurface.Create(new SKImageInfo(1200, 700));
        renderer.Render(surface.Canvas, s, view);
        Assert.InRange(renderer.LastRenderedCellCount, 1, 2000);
        Directory.CreateDirectory("artifacts/rendering");
        using var image = surface.Snapshot(); using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes("artifacts/rendering/pivot-hierarchy.png", data.ToArray());
    }
}
