using GridSpace.Core;
using GridSpace.Editing;
using GridSpace.Formulas;
using GridSpace.IO;
using Xunit;

namespace GridSpace.Tests;

public sealed class PivotHierarchyEdgeTests
{
    [Fact]
    public void NestedCollapseIsScopedToItsParentAndSurvivesParentRoundtrip()
    {
        var session = PivotHierarchyTests.Create(PivotLayout.Compact, PivotSubtotals.Bottom);
        var pivot = session.Sheet.PivotTables[0];
        session.ReconfigurePivotTable(pivot with { Rows = [0, 1, 2], Columns = [] });
        pivot = session.Sheet.PivotTables[0];
        var north = PivotHierarchy.Path(pivot, new PivotKey([CalcValue.Str("North")]));
        var northA = PivotHierarchy.Path(pivot, new PivotKey([CalcValue.Str("North"), CalcValue.Str("A")]));
        session.TogglePivotGroup(pivot.Id, northA);
        var report = PivotReportCache.Get(session.Sheet.PivotTables[0]);
        Assert.Contains(report.RowBands, b => b.Kind == PivotRowKind.Collapsed && b.Key.Label == "North / A");
        Assert.Contains(report.RowBands, b => b.Kind == PivotRowKind.Detail && b.Key.Label == "South / A / Q1");
        var collapsed = report.RowBands.ToList().FindIndex(b => b.Kind == PivotRowKind.Collapsed);
        Assert.Equal(30, report[collapsed + 1, 1].Number);
        Assert.Equal(2, report.DrillDown(collapsed + 1, 1).Count);
        session.TogglePivotGroup(pivot.Id, north);
        session.TogglePivotGroup(pivot.Id, north);
        var restored = PivotReportCache.Get(session.Sheet.PivotTables[0]);
        Assert.Contains(restored.RowBands, b => b.Kind == PivotRowKind.Collapsed && b.Key.Label == "North / A");
        var loaded = Workbook.FromJson(session.Book.ToJson());
        Assert.Single(loaded.ActiveSheet.PivotTables[0].CollapsedRows);
        Assert.Equal(restored.Cells, PivotReportCache.Get(loaded.ActiveSheet.PivotTables[0]).Cells);
    }

    [Fact]
    public void TypedPathDoesNotCollapseEqualDisplayTextFromADifferentType()
    {
        var session = PivotHierarchyTests.Create();
        var source = session.Book.FindSheet("Source")!;
        source.Set("A2", "1"); source.Set("A3", "'1"); source.Set("A4", "");
        var id = session.Sheet.PivotTables[0].Id;
        session.RefreshPivotTable(id);
        var pivot = session.Sheet.PivotTables[0];
        var number = PivotHierarchy.Path(pivot, new PivotKey([CalcValue.Num(1)]));
        var text = PivotHierarchy.Path(pivot, new PivotKey([CalcValue.Str("1")]));
        Assert.False(PivotHierarchy.SamePath(number, text));
        session.TogglePivotGroup(id, number);
        var report = PivotReportCache.Get(session.Sheet.PivotTables[0]);
        Assert.Single(report.RowBands, b => b.Kind == PivotRowKind.Collapsed);
        Assert.Contains(report.RowBands, b => b.Kind == PivotRowKind.GroupHeader && b.Key.Items[0].Kind == ValueKind.Text && b.Key.Label == "1");
        var workbook = XlsxWorkbook.Read(XlsxWorkbook.Write(session.Book)).Workbook;
        var path = Assert.Single(workbook.ActiveSheet.PivotTables[0].CollapsedRows);
        Assert.Equal(ValueKind.Number, PivotHierarchy.Key(path).Items[0].Kind);
    }

    [Fact]
    public void ReversedOrderingKeepsChildrenUnderTheirOwnParent()
    {
        var session = PivotHierarchyTests.Create(PivotLayout.Outline, PivotSubtotals.Bottom);
        session.ReconfigurePivotTable(session.Sheet.PivotTables[0] with { SortAscending = false });
        var report = PivotReportCache.Get(session.Sheet.PivotTables[0]);
        Assert.Equal(new[] { "South", "South / B", "South / A", "South", "North", "North / B", "North / A", "North", "" }, report.RowBands.Select(b => b.Key.Label));
        Assert.Equal(270, report[report.RowCount - 1, report.ColumnCount - 1].Number);
    }

    [Fact]
    public void SourceColumnInsertionTransformsCollapseFieldIdentityAndUndoRestoresCache()
    {
        var session = PivotHierarchyTests.Create(); var id = session.Sheet.PivotTables[0].Id;
        session.ReconfigurePivotTable(session.Sheet.PivotTables[0] with { Rows = [0, 1, 2], Columns = [] });
        session.TogglePivotGroup(id, PivotHierarchy.Path(session.Sheet.PivotTables[0], new PivotKey([CalcValue.Str("North"), CalcValue.Str("A")])));
        session.SwitchSheet(0); session.Select("B1"); session.Insert(false);
        var report = session.Book.FindSheet("Report")!.PivotTables[0];
        Assert.Equal(new[] { 0, 2 }, report.CollapsedRows[0].Fields);
        Assert.Null(report.Cache);
        session.Undo();
        report = session.Book.FindSheet("Report")!.PivotTables[0];
        Assert.Equal(new[] { 0, 1 }, report.CollapsedRows[0].Fields);
        Assert.NotNull(report.Cache);
        Assert.Contains(PivotReportCache.Get(report).RowBands, b => b.Kind == PivotRowKind.Collapsed && b.Key.Label == "North / A");
    }
}
