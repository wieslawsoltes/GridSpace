using GridSpace.Core;
using GridSpace.Formulas;
using Xunit;

namespace GridSpace.Tests;

public class FormulaTests
{
    [Theory]
    [InlineData("=1+2*3", "7")]
    [InlineData("=(1+2)*3", "9")]
    [InlineData("=2^3^2", "512")]
    [InlineData("=50%*200", "100")]
    [InlineData("=1/0", "#DIV/0!")]
    [InlineData("=IF(FALSE,1/0,42)", "42")]
    [InlineData("=IFERROR(1/0,7)", "7")]
    [InlineData("=SUM(1,2,3)", "6")]
    [InlineData("=AVERAGE(2,4,6)", "4")]
    [InlineData("=MIN(4,2,8)", "2")]
    [InlineData("=MAX(4,2,8)", "8")]
    [InlineData("=COUNT(1,\"text\",3)", "2")]
    [InlineData("=COUNTA(1,\"text\",3)", "3")]
    [InlineData("=ROUND(1.235,2)", "1.24")]
    [InlineData("=ROUND(1234,-2)", "1200")]
    [InlineData("=ROUNDUP(-1.231,2)", "-1.24")]
    [InlineData("=ROUNDDOWN(-1.239,2)", "-1.23")]
    [InlineData("=MOD(-3,2)", "1")]
    [InlineData("=SQRT(-1)", "#NUM!")]
    [InlineData("=ABS(-12)", "12")]
    [InlineData("=INT(-1.2)", "-2")]
    [InlineData("=PRODUCT(2,3,4)", "24")]
    [InlineData("=AND(TRUE,1=1)", "TRUE")]
    [InlineData("=OR(FALSE,2>1)", "TRUE")]
    [InlineData("=NOT(TRUE)", "FALSE")]
    [InlineData("=\"Hello \"&\"world\"", "Hello world")]
    [InlineData("=CONCAT(\"a\",\"b\",2)", "ab2")]
    [InlineData("=LEFT(\"abcdef\",3)", "abc")]
    [InlineData("=RIGHT(\"abcdef\",3)", "def")]
    [InlineData("=MID(\"abcdef\",2,3)", "bcd")]
    [InlineData("=LEN(\"hello\")", "5")]
    [InlineData("=TRIM(\"  a   b  \")", "a b")]
    [InlineData("=UPPER(\"abc\")", "ABC")]
    [InlineData("=LOWER(\"ABC\")", "abc")]
    [InlineData("=SUBSTITUTE(\"a a a\",\"a\",\"b\",2)", "a b a")]
    [InlineData("=REPLACE(\"abcdef\",2,3,\"X\")", "aXef")]
    [InlineData("=FIND(\"cd\",\"abcdef\")", "3")]
    [InlineData("=SEARCH(\"CD\",\"abcdef\")", "3")]
    [InlineData("=EXACT(\"a\",\"A\")", "FALSE")]
    [InlineData("=VALUE(\"12.5\")", "12.5")]
    [InlineData("=YEAR(DATE(2026,9,27))", "2026")]
    [InlineData("=MONTH(DATE(2026,9,27))", "9")]
    [InlineData("=DAY(DATE(2026,9,27))", "27")]
    [InlineData("=ISERROR(1/0)", "TRUE")]
    [InlineData("=ISNUMBER(42)", "TRUE")]
    [InlineData("=ISTEXT(\"42\")", "TRUE")]
    [InlineData("=UNKNOWN(1)", "#NAME?")]
    [InlineData("=1+", "#VALUE!")]
    [InlineData("=\"a\"=\"A\"", "TRUE")]
    public void Calculates(string formula, string expected)
    {
        var book = new Workbook(); var engine = new CalculationEngine(book);
        Assert.Equal(expected, engine.EvaluateFormula(book.ActiveSheet, formula).ToString());
    }
    [Fact] public void ReferencesRangesAndInvalidation()
    {
        var book = new Workbook(); var sheet = book.ActiveSheet; var engine = new CalculationEngine(book);
        sheet.Set("A1", "2"); sheet.Set("A2", "3"); sheet.Set("B1", "=SUM(A1:A2)");
        Assert.Equal("5", engine.Evaluate(sheet, "B1").ToString());
        sheet.Set("A2", "7"); Assert.Equal("9", engine.Evaluate(sheet, "B1").ToString());
        sheet.Set("B2", "=B1"); sheet.Set("B1", "=B2"); Assert.Equal("#CYCLE!", engine.Evaluate(sheet, "B1").ToString());
    }
    [Fact] public void CrossSheetAndNames()
    {
        var book = new Workbook(); book.Sheets.Add(new() { Name = "Input Data" }); book.Attach();
        book.Sheets[1].Set("A1", "8"); book.Names["Rate"] = "'Input Data'!A1"; book.Touch();
        var engine = new CalculationEngine(book);
        Assert.Equal("16", engine.EvaluateFormula(book.ActiveSheet, "=Rate*2").ToString());
    }
    [Fact] public void CriteriaAndLookup()
    {
        var book = new Workbook(); var s = book.ActiveSheet;
        s.Set("A1", "Apple"); s.Set("A2", "Pear"); s.Set("A3", "Apple");
        s.Set("B1", "10"); s.Set("B2", "20"); s.Set("B3", "30");
        var e = new CalculationEngine(book);
        Assert.Equal("40", e.EvaluateFormula(s, "=SUMIF(A1:A3,\"Apple\",B1:B3)").ToString());
        Assert.Equal("2", e.EvaluateFormula(s, "=COUNTIF(B1:B3,\">=20\")").ToString());
        Assert.Equal("20", e.EvaluateFormula(s, "=VLOOKUP(\"Pear\",A1:B3,2,FALSE)").ToString());
        Assert.Equal("20", e.EvaluateFormula(s, "=XLOOKUP(\"Pear\",A1:A3,B1:B3)").ToString());
        Assert.Equal("30", e.EvaluateFormula(s, "=INDEX(A1:B3,3,2)").ToString());
        Assert.Equal("2", e.EvaluateFormula(s, "=MATCH(\"Pear\",A1:A3,0)").ToString());
    }
    [Theory]
    [InlineData("A1",0,0)] [InlineData("XFD1048576",1048575,16383)] [InlineData("$AA$12",11,26)]
    public void Addresses(string text, int row, int col) => Assert.Equal(new CellAddress(row,col),CellAddress.Parse(text));
    [Theory] [InlineData("A0")] [InlineData("XFE1")] [InlineData("A1048577")] [InlineData("A1evil")]
    public void RejectsInvalidAddresses(string address) => Assert.False(CellAddress.TryParse(address,out _));
    [Fact] public void TranslationHonorsStringsAndAbsoluteAxes()
    {
        Assert.Equal("=B2+$A2+B$1+$A$1+\"A1\"", FormulaReferences.Translate("=A1+$A1+A$1+$A$1+\"A1\"",1,1));
        Assert.Equal("=#REF!",FormulaReferences.Translate("=A1",-1,0));
    }
    [Fact] public void RoundTripPreservesStylesAndFormulas()
    {
        var book = new Workbook(); book.ActiveSheet.Set("A1","=1+2");
        book.ActiveSheet.Set(new(0,0),book.ActiveSheet.Get("A1") with {Style=new(){Bold=true,NumberFormat="0.00"}});
        var loaded = Workbook.FromJson(book.ToJson());
        Assert.True(loaded.ActiveSheet.Get("A1").Style.Bold);
        Assert.Equal("3",new CalculationEngine(loaded).Evaluate(loaded.ActiveSheet,"A1").ToString());
    }
}
