using GridSpace.Core;
using GridSpace.Editing;
using GridSpace.Formulas;
using Xunit;

namespace GridSpace.Tests;

public sealed class SpillReferenceEditTests
{
    [Fact]
    public void OutOfBoundsCopyRemovesTheSpillSuffixFromRefErrors()
    {
        Assert.Equal("=SUM(#REF!)", FormulaReferences.Translate("=SUM(A1#)", -1, 0));
        Assert.Equal("=SUM(B2#)", FormulaReferences.Translate("=SUM(A1#)", 1, 1));
        Assert.Equal("=SUM($A$1#)", FormulaReferences.Translate("=SUM($A$1#)", 10, 10));
    }

    [Fact]
    public void RenameAndDeletePreserveQuotedLiteralSpillText()
    {
        const string formula = "=SUM('Old sheet'!A1#)&\"A1#\"";
        Assert.Equal("=SUM('New sheet'!A1#)&\"A1#\"", FormulaReferences.RenameSheet(formula, "Old sheet", "New sheet"));
        Assert.Equal("=SUM(#REF!)&\"A1#\"", FormulaReferences.DeleteSheet(formula, "Old sheet"));
    }

    [Fact]
    public void DeletingAnAnchorRowProducesRefNotMalformedFormula()
    {
        var session = new SpreadsheetSession(new Workbook());
        session.SetInput("=SEQUENCE(3)");
        session.SetInput("=SUM(A1#)", new CellAddress(4, 2));
        session.DeleteRows(0, 1);
        Assert.Equal("=SUM(#REF!)", session.Sheet.Get("C4").Input);
        Assert.Equal("#REF!", session.Calculation.Evaluate(session.Sheet, "C4").ToString());
        session.Undo();
        Assert.Equal("6", session.Calculation.Evaluate(session.Sheet, "C5").ToString());
    }

    [Fact]
    public void InsertionRetainsSpillOperatorsAndAbsoluteMarkers()
    {
        var session = new SpreadsheetSession(new Workbook());
        session.SetInput("=SEQUENCE(3)");
        session.SetInput("=SUM($A$1#)", new CellAddress(0, 2));
        session.InsertRows(0, 1);
        Assert.Equal("=SUM($A$2#)", session.Sheet.Get("C2").Input);
        Assert.Equal("6", session.Calculation.Evaluate(session.Sheet, "C2").ToString());
    }

    [Fact]
    public void DeletingAnArraySheetInvalidatesCrossSheetSpillReferences()
    {
        var session = new SpreadsheetSession(new Workbook());
        session.SetInput("=SEQUENCE(3)");
        session.AddSheet("Consumer");
        session.SetInput("=SUM(Sheet1!A1#)");
        Assert.Equal("6", session.Calculation.Evaluate(session.Sheet, "A1").ToString());
        session.SwitchSheet(0); session.DeleteSheet();
        Assert.Equal("=SUM(#REF!)", session.Sheet.Get("A1").Input);
        Assert.Equal("#REF!", session.Calculation.Evaluate(session.Sheet, "A1").ToString());
    }
}
