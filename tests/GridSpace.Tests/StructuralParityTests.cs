using GridSpace.Core;
using GridSpace.Editing;
using GridSpace.Formulas;
using Xunit;

namespace GridSpace.Tests;

public sealed class StructuralParityTests
{
    [Theory]
    [InlineData("=SUM(A1:A10)", 2, 3, "=SUM(A1:A7)")]
    [InlineData("=SUM(A3:A10)", 2, 3, "=SUM(A3:A7)")]
    [InlineData("=SUM(A1:A5)", 2, 3, "=SUM(A1:A2)")]
    [InlineData("=SUM(A3:A5)", 2, 3, "=SUM(#REF!)")]
    [InlineData("=$B$4", 2, 3, "=#REF!")]
    [InlineData("=$B$8", 2, 3, "=$B$5")]
    [InlineData("=SUM(A10:A1)", 2, 3, "=SUM(A7:A1)")]
    [InlineData("=\"A3:A8\"&A8", 2, 3, "=\"A3:A8\"&A5")]
    [InlineData("=SUM('Other'!A3:A8)+A8", 2, 3, "=SUM('Other'!A3:A8)+A5")]
    public void DeletionContractsWholeReferenceIntervals(string formula, int position, int count, string expected) =>
        Assert.Equal(expected, FormulaReferences.Delete(formula, "Sheet1", "Sheet1", true, position, count));

    [Theory]
    [InlineData("=SUM('Input data'!$A$1:$A$10)", "=SUM('Input data'!$A$1:$A$7)")]
    [InlineData("='Input data'!A4+Sheet2!A4", "=#REF!+Sheet2!A4")]
    [InlineData("=LOG10(100)+A4", "=LOG10(100)+A4")]
    public void QualifiedRangesInheritTheirFirstSheet(string formula, string expected) =>
        Assert.Equal(expected, FormulaReferences.Delete(formula, "Sheet2", "Input data", true, 2, 3));

    [Fact]
    public void DeleteColumnsContractsAndPreservesAbsoluteMarkers() =>
        Assert.Equal("=SUM($A1:$C9)+#REF!", FormulaReferences.Delete("=SUM($A1:$F9)+D2", "Sheet1", "Sheet1", false, 1, 3));

    [Fact]
    public void DeletionUpdatesCellsFormulasNamesAndUndo()
    {
        var session = new SpreadsheetSession(new Workbook());
        for (var r = 1; r <= 8; r++) session.Sheet.Set("A" + r, r.ToString());
        session.Sheet.Set("B1", "=SUM(A1:A8)");
        session.Book.Names["Numbers"] = "Sheet1!$A$1:$A$8";
        session.AddSheet("Report"); session.SetInput("=SUM(Sheet1!A1:A8)"); session.SwitchSheet(0);
        session.DeleteRows(2, 3);
        Assert.Equal("6", session.Sheet.Get("A3").Input);
        Assert.Equal("=SUM(A1:A5)", session.Sheet.Get("B1").Input);
        Assert.Equal("Sheet1!$A$1:$A$5", session.Book.Names["Numbers"]);
        Assert.Equal("24", session.Calculation.Evaluate(session.Book.Sheets[1], "A1").ToString());
        session.Undo(); Assert.Equal("3", session.Sheet.Get("A3").Input);
        session.Redo(); Assert.Equal("6", session.Sheet.Get("A3").Input);
    }

    [Fact]
    public void MetadataRebasesInOneTransaction()
    {
        var session = new SpreadsheetSession(new Workbook());
        session.Sheet.Merges.Add(CellRange.Parse("B2:C6"));
        session.Sheet.RowHeights[6] = 44; session.Sheet.HiddenRows.Add(7); session.Sheet.FrozenRows = 5;
        session.Sheet.ValidationLists["D2:D8"] = ["Yes", "No"];
        session.Sheet.Charts.Add(new ChartSpec { Range = "E1:F8", Row = 7, Column = 6 });
        session.SetConditionalFormat(new ConditionalFormatRule { Range = "D2:D8", Kind = ConditionalFormatKind.Expression, Operand = "=$A2>0" });
        session.DeleteRows(1, 3);
        Assert.Equal("B2:C3", session.Sheet.Merges.Single().ToString());
        Assert.Equal(44, session.Sheet.RowHeights[3]); Assert.Contains(4, session.Sheet.HiddenRows);
        Assert.Equal(2, session.Sheet.FrozenRows); Assert.Contains("D2:D5", session.Sheet.ValidationLists.Keys);
        Assert.Equal("E1:F5", session.Sheet.Charts.Single().Range);
        Assert.Equal("D2:D5", session.Sheet.ConditionalFormats.Single().Range);
        Assert.Equal("=$A2>0", session.Sheet.ConditionalFormats.Single().Operand);
    }

    [Fact]
    public void DeletedSheetReferencesDoNotResurrectWhenNameIsReused()
    {
        var session = new SpreadsheetSession(new Workbook()); session.AddSheet("Data"); session.SetInput("5");
        session.SwitchSheet(0); session.SetInput("=Data!A1"); session.SwitchSheet(1); session.DeleteSheet(); session.AddSheet("Data"); session.SetInput("99");
        Assert.Equal("=#REF!", session.Book.Sheets[0].Get("A1").Input);
        Assert.Equal("#REF!", session.Calculation.Evaluate(session.Book.Sheets[0], "A1").ToString());
    }

    [Fact]
    public void InsertionRejectsLossAndRollsBack()
    {
        var session = new SpreadsheetSession(new Workbook()); session.Sheet.Set("XFD1", "Keep");
        var before = session.Book.ToJson();
        Assert.Throws<InvalidOperationException>(() => session.InsertColumns(0, 2));
        Assert.Equal(before, session.Book.ToJson()); Assert.False(session.CanUndo);
    }

    [Fact]
    public void InsertAndDeleteTransformIsIdentityForUnaffectedCells()
    {
        var random = new Random(404);
        for (var i = 0; i < 500; i++)
        {
            var position = random.Next(1, 500); var count = random.Next(1, 20); var address = new CellAddress(random.Next(0, 1000), random.Next(0, 100));
            var insert = new AxisEdit(true, position, count, false); var delete = new AxisEdit(true, position, count, true);
            Assert.Equal(address, delete.Map(insert.Map(address)!.Value));
        }
    }
}
