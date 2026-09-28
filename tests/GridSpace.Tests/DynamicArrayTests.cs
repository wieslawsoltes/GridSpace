using GridSpace.Core;
using GridSpace.Editing;
using GridSpace.Formulas;
using GridSpace.IO;
using Xunit;

namespace GridSpace.Tests;

public sealed class DynamicArrayTests
{
    private static CalcValue Formula(string formula)
    {
        var book = new Workbook(); return new CalculationEngine(book).EvaluateFormula(book.ActiveSheet, formula);
    }
    [Theory]
    [InlineData("=SEQUENCE(2,3,10,2)", "10,12,14,16,18,20", 3)]
    [InlineData("={1,2;3,4}", "1,2,3,4", 2)]
    [InlineData("={1;2;3}*10+{1,2}", "11,12,21,22,31,32", 2)]
    [InlineData("=TRANSPOSE({1,2;3,4})", "1,3,2,4", 2)]
    [InlineData("=FILTER({1,2;3,4;5,6},{TRUE;FALSE;TRUE})", "1,2,5,6", 2)]
    [InlineData("=FILTER({1,2;3,4},{TRUE,FALSE})", "1,3", 1)]
    [InlineData("=SORT({2,8;1,7;1,9})", "1,7,1,9,2,8", 2)]
    [InlineData("=SORT({3,1,2;4,5,6},1,1,TRUE)", "1,2,3,5,6,4", 3)]
    [InlineData("=SORTBY({10;20;30},{2;1;1},1,{1;2;3},-1)", "30,20,10", 1)]
    [InlineData("=UNIQUE({1,2;1,2;3,4})", "1,2,3,4", 2)]
    [InlineData("=UNIQUE({1,2;1,2;3,4},FALSE,TRUE)", "3,4", 2)]
    [InlineData("=UNIQUE({1,1,2;3,3,4},TRUE)", "1,2,3,4", 2)]
    [InlineData("=TAKE({1,2;3,4;5,6},-2,-1)", "4,6", 1)]
    [InlineData("=DROP({1,2;3,4;5,6},1)", "3,4,5,6", 2)]
    [InlineData("=CHOOSECOLS({1,2,3;4,5,6},3,1,3)", "3,1,3,6,4,6", 3)]
    [InlineData("=CHOOSEROWS({1,2;3,4;5,6},-1,1)", "5,6,1,2", 2)]
    [InlineData("=HSTACK({1;2},{3})", "1,3,2,#N/A", 2)]
    [InlineData("=VSTACK({1,2},{3})", "1,2,3,#N/A", 2)]
    [InlineData("=IF({TRUE;FALSE},{1;2},{3;4})", "1,4", 1)]
    [InlineData("=IFERROR({1,#N/A},0)", "1,0", 2)]
    [InlineData("=LET(values,SEQUENCE(3),values*values)", "1,4,9", 1)]
    [InlineData("=_xlfn._xlws.FILTER({1;2;3},{0;1;1})", "2,3", 1)]
    public void RectangularFunctions(string formula, string expected, int width)
    {
        var value = Formula(formula);
        Assert.Equal(ValueKind.Array, value.Kind);
        Assert.Equal(width, value.Columns);
        Assert.Equal(expected, string.Join(',', value.Items!));
    }
    [Theory]
    [InlineData("=SEQUENCE(100001)", "#LIMIT!")]
    [InlineData("=SEQUENCE(0)", "#VALUE!")]
    [InlineData("=SEQUENCE(1,1,1,0)", "1")]
    [InlineData("=FILTER({1;2},{FALSE;FALSE})", "#CALC!")]
    [InlineData("=FILTER({1;2},{FALSE;FALSE},\"empty\")", "empty")]
    [InlineData("=FILTER({1;2},{TRUE;#N/A})", "#N/A")]
    [InlineData("=SORT({1;2},2)", "#VALUE!")]
    [InlineData("=CHOOSECOLS({1,2},0)", "#VALUE!")]
    [InlineData("=TAKE({1;2},0)", "#CALC!")]
    [InlineData("=DROP({1;2},2)", "#CALC!")]
    [InlineData("={1,2;3}", "#VALUE!")]
    [InlineData("=LET(x,2,LET(x,3,x)+x)", "5")]
    [InlineData("=LET(x,2,y,x+3,y)", "5")]
    [InlineData("=IF(TRUE,42,SEQUENCE(-1))", "42")]
    public void BoundariesAndScalarLaziness(string formula, string expected) => Assert.Equal(expected, Formula(formula).ToString());

    [Fact]
    public void FollowerFirstReadSpillsWithoutStoredCells()
    {
        var book = new Workbook(); book.ActiveSheet.Set("B2", "=SEQUENCE(3,2)");
        var engine = new CalculationEngine(book);
        Assert.Equal("6", engine.Evaluate(book.ActiveSheet, "C4").ToString());
        Assert.Equal("1", engine.Evaluate(book.ActiveSheet, "B2").ToString());
        Assert.Single(book.ActiveSheet.Cells);
        Assert.Equal("B2:C4", engine.GetSpill(book.ActiveSheet, CellAddress.Parse("C4"))!.Range.ToString());
        Assert.Equal("21", engine.EvaluateFormula(book.ActiveSheet, "=SUM(B2#)").ToString());
        Assert.Equal("1", engine.EvaluateFormula(book.ActiveSheet, "=SUM(B2)").ToString());
    }
    [Fact]
    public void ObstaclesRetainDataAndRemovingThemReleasesSpill()
    {
        var book = new Workbook(); var sheet = book.ActiveSheet;
        sheet.Set("A1", "=SEQUENCE(3)"); sheet.Set("A2", "obstacle");
        var engine = new CalculationEngine(book);
        Assert.Equal("#SPILL!", engine.Evaluate(sheet, "A1").ToString());
        Assert.Equal("obstacle", engine.Evaluate(sheet, "A2").ToString());
        sheet.Set("A2", "");
        Assert.Equal("3", engine.Evaluate(sheet, "A3").ToString());
        sheet.Set("A1", "=SEQUENCE(2)");
        Assert.Equal("", engine.Evaluate(sheet, "A3").ToString());
        Assert.Equal("2", engine.Evaluate(sheet, "A2").ToString());
    }
    [Fact]
    public void SpillReferencesChainAcrossSheetsAndReadOrder()
    {
        var book = new Workbook(); book.Sheets.Add(new Worksheet { Name = "Other data" }); book.Attach();
        book.Sheets[1].Set("A1", "=SEQUENCE(3)");
        book.ActiveSheet.Set("C1", "=TRANSPOSE('Other data'!A1#)");
        book.ActiveSheet.Set("A1", "=SUM(C1#)");
        var engine = new CalculationEngine(book);
        Assert.Equal("6", engine.Evaluate(book.ActiveSheet, "A1").ToString());
        Assert.Equal("3", engine.Evaluate(book.ActiveSheet, "E1").ToString());
        book.Sheets[1].Set("A1", "=SEQUENCE(2)");
        Assert.Equal("3", engine.Evaluate(book.ActiveSheet, "A1").ToString());
        Assert.Equal("", engine.Evaluate(book.ActiveSheet, "E1").ToString());
    }
    [Theory]
    [InlineData("A1", "=SEQUENCE(3)+A2", "#CYCLE!")]
    [InlineData("A1", "=SEQUENCE(3)+A1", "#CYCLE!")]
    [InlineData("XFD1048576", "=SEQUENCE(2)", "#SPILL!")]
    public void CyclesAndBoundsAreExplicit(string address, string formula, string error)
    {
        var book = new Workbook(); book.ActiveSheet.Set(address, formula);
        Assert.Equal(error, new CalculationEngine(book).Evaluate(book.ActiveSheet, address).ToString());
    }
    [Fact]
    public void MergesBlockSpillsButFormattingDoesNot()
    {
        var book = new Workbook(); var sheet = book.ActiveSheet;
        sheet.Set("A1", "=SEQUENCE(2)"); sheet.Set(new(1, 0), new Cell { Style = new CellStyle { Bold = true } });
        var engine = new CalculationEngine(book); Assert.Equal("2", engine.Evaluate(sheet, "A2").ToString());
        sheet.Merges.Add(CellRange.Parse("A2:B2")); book.Touch();
        Assert.Equal("#SPILL!", engine.Evaluate(sheet, "A1").ToString());
    }
    [Fact]
    public void SessionProtectsFollowersAndSupportsAnchorUndo()
    {
        var session = new SpreadsheetSession(new Workbook()); session.SetInput("=SEQUENCE(3)");
        session.Select("A2"); Assert.True(session.IsSpillFollower); Assert.Equal("=SEQUENCE(3)", session.FormulaInput);
        Assert.Throws<InvalidOperationException>(() => session.SetInput("5"));
        Assert.Throws<InvalidOperationException>(() => session.Clear());
        Assert.Throws<InvalidOperationException>(() => session.Paste("7"));
        session.Select("A1"); session.SetInput("=SEQUENCE(2)");
        Assert.Equal("", session.Calculation.Evaluate(session.Sheet, "A3").ToString());
        session.Undo(); Assert.Equal("3", session.Calculation.Evaluate(session.Sheet, "A3").ToString());
        session.Redo(); Assert.Equal("", session.Calculation.Evaluate(session.Sheet, "A3").ToString());
        session.Select("A1"); session.Clear(); Assert.Equal("", session.Calculation.Evaluate(session.Sheet, "A2").ToString());
    }
    [Fact]
    public void PartialSpillCopiesValuesAndNativeRoundtripRecalculates()
    {
        var session = new SpreadsheetSession(new Workbook()); session.SetInput("=SEQUENCE(3)");
        session.Select("A2:A3"); var clipboard = session.Copy(); session.Select("D1"); session.Paste(clipboard.Text);
        Assert.Equal("2", session.Calculation.Evaluate(session.Sheet, "D1").ToString());
        var restored = Workbook.FromJson(session.Book.ToJson()); var calculation = new CalculationEngine(restored);
        Assert.Equal("3", calculation.Evaluate(restored.ActiveSheet, "A3").ToString());
        Assert.Equal(3, restored.ActiveSheet.Cells.Count);
        Assert.Contains("3", System.Text.Encoding.UTF8.GetString(WorkbookFiles.Csv(restored)));
    }
}
