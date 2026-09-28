using System.IO.Compression;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using GridSpace.Core;
using GridSpace.Editing;
using GridSpace.Formulas;
using GridSpace.IO;
using Xunit;

namespace GridSpace.Tests;

public sealed class DynamicArrayInterchangeTests
{
    private static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    [Fact]
    public void ExportHasDynamicMetadataCachedFollowersAndIndependentSchemaValidation()
    {
        var book = new Workbook(); var sheet = book.ActiveSheet;
        sheet.Set("A1", "=SEQUENCE(3,2)"); sheet.Set("D1", "=SUM(A1#)");
        var bytes = XlsxWorkbook.Write(book);
        using (var doc = SpreadsheetDocument.Open(new MemoryStream(bytes), false))
        {
            var errors = new OpenXmlValidator(FileFormatVersions.Office2019).Validate(doc).Select(e => e.Description + " " + e.Path?.XPath).ToArray();
            Assert.True(errors.Length == 0, string.Join('\n', errors));
        }
        using var zip = new ZipArchive(new MemoryStream(bytes));
        Assert.NotNull(zip.GetEntry("xl/metadata.xml"));
        var xml = XDocument.Load(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        var cells = xml.Descendants(S + "c").ToDictionary(c => (string)c.Attribute("r")!);
        Assert.Equal("1", (string?)cells["A1"].Attribute("cm"));
        Assert.Equal("A1:B3", (string?)cells["A1"].Element(S + "f")!.Attribute("ref"));
        Assert.Equal("6", cells["B3"].Element(S + "v")!.Value);
        Assert.Contains("_xlfn.ANCHORARRAY(A1)", cells["D1"].Element(S + "f")!.Value);
        var imported = XlsxWorkbook.Read(bytes);
        Assert.DoesNotContain(imported.Warnings, w => w.Contains("unsupported", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, imported.Workbook.ActiveSheet.Cells.Count);
        var session = new SpreadsheetSession(imported.Workbook);
        Assert.Equal("6", session.Calculation.Evaluate(session.Sheet, "B3").ToString());
        session.SetInput("=SEQUENCE(2,2)");
        Assert.Equal("10", session.Calculation.Evaluate(session.Sheet, "D1").ToString());
        Assert.Equal("", session.Calculation.Evaluate(session.Sheet, "B3").ToString());
    }
    [Fact]
    public void ExportRetainsFollowerFormattingWithoutTurningCachesIntoBlockers()
    {
        var book = new Workbook(); book.ActiveSheet.Set("B2", "=SEQUENCE(2)");
        book.ActiveSheet.Set(new(2, 1), new Cell { Style = new CellStyle { Background = "#FFC000", Bold = true } });
        var restored = XlsxWorkbook.Read(XlsxWorkbook.Write(book)).Workbook;
        Assert.Equal("", restored.ActiveSheet.Get("B3").Input);
        Assert.True(restored.ActiveSheet.Get("B3").Style.Bold);
        Assert.Equal("2", new CalculationEngine(restored).Evaluate(restored.ActiveSheet, "B3").ToString());
    }
    [Fact]
    public void UnsupportedDynamicFunctionRetainsTypedCachedValues()
    {
        var book = new Workbook(); book.ActiveSheet.Set("A1", "=SEQUENCE(2)");
        var bytes = ChangeSheet(XlsxWorkbook.Write(book), root => root.Descendants(S + "f").First().Value = "_xlfn.UNSUPPORTED_FUNCTION(2)");
        var result = XlsxWorkbook.Read(bytes);
        Assert.Contains(result.Warnings, w => w.Contains("typed cached"));
        Assert.Equal("1", result.Workbook.ActiveSheet.Get("A1").Input);
        Assert.Equal("2", result.Workbook.ActiveSheet.Get("A2").Input);
    }
    [Fact]
    public void ConflictingFormulaInsideDynamicRangeIsNeverDiscarded()
    {
        var book = new Workbook(); book.ActiveSheet.Set("A1", "=SEQUENCE(2)");
        var bytes = ChangeSheet(XlsxWorkbook.Write(book), root =>
            root.Descendants(S + "c").Single(c => (string?)c.Attribute("r") == "A2").AddFirst(new XElement(S + "f", "99")));
        var result = XlsxWorkbook.Read(bytes);
        Assert.Equal("=99", result.Workbook.ActiveSheet.Get("A2").Input);
        Assert.Equal("#SPILL!", new CalculationEngine(result.Workbook).Evaluate(result.Workbook.ActiveSheet, "A1").ToString());
        Assert.Contains(result.Warnings, w => w.Contains("ownership"));
    }
    [Fact]
    public void LegacyCseWithoutDynamicMetadataKeepsCachesNotLiveSpills()
    {
        var book = new Workbook(); book.ActiveSheet.Set("A1", "=SEQUENCE(3)");
        var bytes = XlsxWorkbook.Write(book);
        using var memory = new MemoryStream(); memory.Write(bytes);
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Update, true))
        {
            var entry = zip.GetEntry("xl/worksheets/sheet1.xml")!;
            XDocument xml;
            using (var input = entry.Open()) xml = XDocument.Load(input);
            foreach (var cell in xml.Descendants(S + "c")) cell.Attribute("cm")?.Remove();
            entry.Delete();
            using var output = zip.CreateEntry("xl/worksheets/sheet1.xml").Open(); xml.Save(output);
        }
        var imported = XlsxWorkbook.Read(memory.ToArray());
        Assert.Contains(imported.Warnings, w => w.Contains("CSE"));
        Assert.False(imported.Workbook.ActiveSheet.Get("A1").IsFormula);
        Assert.Equal("3", new CalculationEngine(imported.Workbook).Evaluate(imported.Workbook.ActiveSheet, "A3").ToString());
    }
    [Theory]
    [InlineData("=SUM(A1#)", "SUM(_xlfn.ANCHORARRAY(A1))")]
    [InlineData("=\"A1# SEQUENCE(2)\"", "\"A1# SEQUENCE(2)\"")]
    [InlineData("='A1#'!B1", "'A1#'!B1")]
    [InlineData("=A1*2", "A1*2")]
    public void FormulaDialectPreservesLiteralText(string input, string expected) => Assert.Equal(expected, FormulaNotation.ToOpenXml(input));
    private static byte[] ChangeSheet(byte[] bytes, Action<XElement> change)
    {
        using var memory = new MemoryStream(); memory.Write(bytes);
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Update, true))
        {
            var entry = zip.GetEntry("xl/worksheets/sheet1.xml")!;
            XDocument doc; using (var stream = entry.Open()) doc = XDocument.Load(stream);
            change(doc.Root!); entry.Delete();
            using var output = zip.CreateEntry("xl/worksheets/sheet1.xml").Open(); doc.Save(output);
        }
        return memory.ToArray();
    }
}
