using System.IO.Compression;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using GridSpace.Core;
using GridSpace.Editing;
using GridSpace.IO;
using Xunit;

namespace GridSpace.Tests;

public sealed class SharedFormulaTests
{
    private static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void FollowersBeforeMasterRecalculateAndRoundtripAsIndependentFormulas()
    {
        var imported = Import("""
            <row r="2"><c r="A2"><v>20</v></c><c r="B2"><f t="shared" si="7"/><v>-99</v></c></row>
            <row r="1"><c r="A1"><v>10</v></c><c r="B1"><f t="shared" si="7" ref="B1:B3">A1*2</f><v>-99</v></c></row>
            <row r="3"><c r="A3"><v>30</v></c><c r="B3"><f t="shared" si="7"/><v>-99</v></c></row>
            """);
        Assert.DoesNotContain(imported.Warnings, w => w.Contains("shared formula"));
        var session = new SpreadsheetSession(imported.Workbook);
        Assert.Equal("=A2*2", session.Sheet.Get("B2").Input);
        Assert.Equal(40, session.Calculation.Evaluate(session.Sheet, "B2").Number);
        session.SetInput("50", CellAddress.Parse("A2"));
        Assert.Equal(100, session.Calculation.Evaluate(session.Sheet, "B2").Number);
        var bytes = XlsxWorkbook.Write(session.Book);
        var loaded = XlsxWorkbook.Read(bytes).Workbook;
        Assert.Equal("=A2*2", loaded.ActiveSheet.Get("B2").Input);
        using var document = SpreadsheetDocument.Open(new MemoryStream(bytes), false);
        Assert.Empty(new OpenXmlValidator().Validate(document));
    }

    [Fact]
    public void TwoDimensionalGroupsTranslateMixedAbsoluteAndQuotedReferences()
    {
        var result = Import("""
            <row r="2"><c r="C2"><f t="shared" si="4294967295" ref="C2:D3">A2+$B2+A$1+$B$1+'Data Set'!A2+IF(A2="A2",1,0)+LOG10(100)</f><v>0</v></c></row>
            <row r="3"><c r="D3"><f t="shared" si="4294967295"/><v>0</v></c></row>
            """);
        Assert.Equal("=B3+$B3+B$1+$B$1+'Data Set'!B3+IF(B3=\"A2\",1,0)+LOG10(100)", result.Workbook.ActiveSheet.Get("D3").Input);
    }

    [Fact]
    public void NormalFormulaOverridesMembershipAndSharedFollowerExpressionDoesNot()
    {
        var book = Import("""
            <row r="1"><c r="B1"><f t="shared" si="0" ref="B1:B3">A1+1</f><v>0</v></c></row>
            <row r="2"><c r="B2"><f>777</f><v>777</v></c></row>
            <row r="3"><c r="B3"><f t="shared" si="0">999</f><v>999</v></c></row>
            """).Workbook;
        Assert.Equal("=777", book.ActiveSheet.Get("B2").Input);
        Assert.Equal("=A3+1", book.ActiveSheet.Get("B3").Input);
    }

    [Fact]
    public void SharedGroupIndexesAreWorksheetLocal()
    {
        var book = Import(
            "<row r=\"1\"><c r=\"A1\"><f t=\"shared\" si=\"1\" ref=\"A1:A2\">3</f><v>3</v></c></row><row r=\"2\"><c r=\"A2\"><f t=\"shared\" si=\"1\"/><v>0</v></c></row>",
            "<row r=\"1\"><c r=\"A1\"><f t=\"shared\" si=\"1\" ref=\"A1:A2\">5</f><v>5</v></c></row><row r=\"2\"><c r=\"A2\"><f t=\"shared\" si=\"1\"/><v>0</v></c></row>").Workbook;
        Assert.Equal("=3", book.Sheets[0].Get("A2").Input);
        Assert.Equal("=5", book.Sheets[1].Get("A2").Input);
    }

    [Theory]
    [InlineData("str", "0012", "'0012")]
    [InlineData("str", "=1+1", "'=1+1")]
    [InlineData("b", "1", "TRUE")]
    [InlineData("b", "0", "FALSE")]
    [InlineData("e", "#DIV/0!", "=#DIV/0!")]
    [InlineData("n", "12.5", "12.5")]
    public void UnresolvedFollowersKeepCachedType(string type, string value, string input)
    {
        var result = Import($"<row r=\"1\"><c r=\"A1\" t=\"{type}\"><f t=\"shared\" si=\"1\"/><v>{value}</v></c></row>");
        Assert.Equal(input, result.Workbook.ActiveSheet.Get("A1").Input);
        Assert.Contains(result.Warnings, w => w.Contains("unresolved"));
    }

    [Theory]
    [InlineData("-1")] [InlineData("4294967296")] [InlineData("not-an-index")] [InlineData("")]
    public void InvalidSharedIndexesUseCachedValues(string index)
    {
        var result = Import($"<row r=\"1\"><c r=\"A1\"><f t=\"shared\" si=\"{index}\" ref=\"A1:A2\">99</f><v>7</v></c></row>");
        Assert.Equal("7", result.Workbook.ActiveSheet.Get("A1").Input);
        Assert.Contains(result.Warnings, w => w.Contains("shared formula"));
    }

    [Theory]
    [InlineData("A1:A2", "A3")]
    [InlineData("A2:A3", "A2")]
    [InlineData("invalid", "A2")]
    public void InvalidRangesAndOutOfRangeMembersDoNotReuseWrongFormula(string range, string follower)
    {
        var result = Import($"<row r=\"1\"><c r=\"A1\"><f t=\"shared\" si=\"1\" ref=\"{range}\">99</f><v>7</v></c></row><row r=\"3\"><c r=\"{follower}\"><f t=\"shared\" si=\"1\"/><v>8</v></c></row>");
        Assert.Equal("8", result.Workbook.ActiveSheet.Get(follower).Input);
        Assert.Contains(result.Warnings, w => w.Contains("shared formula"));
    }

    [Fact]
    public void ConflictingMastersAreNotResolvedByXmlOrder()
    {
        var result = Import("""
            <row r="1"><c r="A1"><f t="shared" si="1" ref="A1:A3">99</f><v>7</v></c></row>
            <row r="2"><c r="A2"><f t="shared" si="1" ref="A2:A3">999</f><v>8</v></c></row>
            <row r="3"><c r="A3"><f t="shared" si="1"/><v>9</v></c></row>
            """);
        Assert.Equal("9", result.Workbook.ActiveSheet.Get("A3").Input);
        Assert.Contains(result.Warnings, w => w.Contains("ambiguous"));
    }

    [Fact]
    public void MissingCachedResultsAreExplicitErrors()
    {
        var result = Import("<row r=\"1\"><c r=\"A1\"><f t=\"shared\" si=\"1\"/></c></row>");
        Assert.Equal("=#REF!", result.Workbook.ActiveSheet.Get("A1").Input);
        Assert.Contains(result.Warnings, w => w.Contains("no cached result"));
    }

    [Fact]
    public void GroupRectangleDoesNotMaterializeAbsentCells()
    {
        var book = Import("<row r=\"1\"><c r=\"A1\"><f t=\"shared\" si=\"1\" ref=\"A1:XFD1048576\">42</f><v>42</v></c></row>").Workbook;
        Assert.Single(book.ActiveSheet.Cells);
        Assert.Equal("=42", book.ActiveSheet.Get("A1").Input);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SharedFormulaExpansionHasAWorkbookWideCharacterBudget(bool multipleSheets)
    {
        var expression = "\"" + new string('x', 16000) + "\"";
        string Rows(int count) => string.Concat(Enumerable.Range(1, count).Select(i =>
            $"<row r=\"{i}\"><c r=\"A{i}\"><f t=\"shared\" si=\"1\"{(i == 1 ? $" ref=\"A1:A{count}\"" : "")}>{(i == 1 ? expression : "")}</f><v>0</v></c></row>"));
        var sheets = multipleSheets ? new[] { Rows(1100), Rows(1100) } : new[] { Rows(2200) };
        var error = Assert.Throws<InvalidDataException>(() => Import(sheets));
        Assert.Contains("Expanded shared formulas", error.Message);
    }

    [Theory]
    [InlineData("<v>24</v>", "24")]
    [InlineData("", "=#N/A")]
    public void DataTablesImportCachedResultsWithoutPretendingToRecalculate(string value, string input)
    {
        var result = Import($"<row r=\"1\"><c r=\"A1\"><f t=\"dataTable\" ref=\"A1:A5\" r1=\"B1\"/>{value}</c></row>");
        Assert.Equal(input, result.Workbook.ActiveSheet.Get("A1").Input);
        Assert.Contains(result.Warnings, w => w.Contains("data-table"));
    }

    private static ImportResult Import(params string[] sheets)
    {
        var book = new Workbook { Sheets = sheets.Select((_, i) => new Worksheet { Name = "Sheet" + (i + 1) }).ToList() };
        var bytes = XlsxWorkbook.Write(book);
        using var source = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        using var output = new MemoryStream();
        using (var target = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            foreach (var entry in source.Entries)
            {
                using var input = entry.Open(); using var destination = target.CreateEntry(entry.FullName).Open();
                if (entry.FullName.StartsWith("xl/worksheets/sheet", StringComparison.Ordinal) && entry.FullName.EndsWith(".xml", StringComparison.Ordinal))
                {
                    var sheet = int.Parse(Path.GetFileNameWithoutExtension(entry.FullName)[5..]) - 1;
                    var xml = XDocument.Load(input);
                    xml.Root!.Element(S + "sheetData")!.ReplaceWith(XElement.Parse("<sheetData xmlns=\"" + S + "\">" + sheets[sheet] + "</sheetData>"));
                    xml.Save(destination);
                }
                else input.CopyTo(destination);
            }
        }
        return XlsxWorkbook.Read(output.ToArray());
    }
}
