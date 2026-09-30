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

public sealed class PivotHierarchyInterchangeTests
{
    private static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace C = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    [Theory]
    [InlineData(PivotLayout.Compact, PivotSubtotals.None)]
    [InlineData(PivotLayout.Compact, PivotSubtotals.Top)]
    [InlineData(PivotLayout.Compact, PivotSubtotals.Bottom)]
    [InlineData(PivotLayout.Outline, PivotSubtotals.None)]
    [InlineData(PivotLayout.Outline, PivotSubtotals.Top)]
    [InlineData(PivotLayout.Outline, PivotSubtotals.Bottom)]
    [InlineData(PivotLayout.Tabular, PivotSubtotals.None)]
    [InlineData(PivotLayout.Tabular, PivotSubtotals.Top)]
    [InlineData(PivotLayout.Tabular, PivotSubtotals.Bottom)]
    public void LayoutAndSubtotalsHaveValidStandardPartsAndNativeRoundtrips(PivotLayout layout, PivotSubtotals totals)
    {
        var s = PivotHierarchyTests.Create(layout, totals); var pivot = s.Sheet.PivotTables[0];
        s.AddPivotChart(pivot.Id);
        var bytes = XlsxWorkbook.Write(s.Book); Validate(bytes);
        var table = Part(bytes, "xl/pivotTables/pivotTable2_1.xml");
        Assert.Equal(layout == PivotLayout.Compact ? "1" : "0", (string?)table.Root!.Attribute("compact"));
        Assert.Equal(PivotReportCache.Get(pivot).RowBands.Count, table.Descendants(S + "rowItems").Elements().Count());
        Assert.Equal(totals == PivotSubtotals.None ? 0 : 2, table.Descendants(S + "rowItems").Elements().Count(e => (string?)e.Attribute("t") == "default"));
        var read = XlsxWorkbook.Read(bytes);
        Assert.Equal(new[] { "XLSX interoperability is a subset. Use a native .gridspace copy to preserve GridSpace-specific state; keep the original Excel file." }, read.Warnings);
        var loaded = Assert.Single(read.Workbook.ActiveSheet.PivotTables);
        Assert.Equal(layout, loaded.Layout); Assert.Equal(totals, loaded.Subtotals); Assert.False(loaded.NeedsLayoutRefresh);
        Assert.Equal(PivotReportCache.Get(pivot).Cells, PivotReportCache.Get(loaded).Cells);
    }

    [Fact]
    public void CollapseUsesExactWorksheetValueAreasAndCompositeCategoryLiterals()
    {
        var s = PivotHierarchyTests.Create(PivotLayout.Compact, PivotSubtotals.Bottom); var pivot = s.Sheet.PivotTables[0];
        s.TogglePivotGroup(pivot.Id, PivotHierarchy.Path(pivot, new PivotKey([CalcValue.Str("North")])));
        s.AddPivotChart(pivot.Id); var bytes = XlsxWorkbook.Write(s.Book); Validate(bytes);
        var xml = Part(bytes, "xl/charts/chart2_1.xml");
        var categories = xml.Descendants(C + "cat").First().Element(C + "strLit")!;
        Assert.Equal(new[] { "North", "South / A", "South / B" }, categories.Elements(C + "pt").Select(p => p.Element(C + "v")!.Value));
        var formula = xml.Descendants(C + "val").First().Descendants(C + "f").Single().Value;
        // B3 report: North at row 4, South header at row 5, details at 6:7, subtotal at 8.
        Assert.Equal("('Report'!$C$4:$C$4,'Report'!$C$6:$C$7)", formula);
        Assert.DoesNotContain("$C$8", formula);
        var native = XlsxWorkbook.Read(bytes).Workbook;
        Assert.Single(native.ActiveSheet.PivotTables[0].CollapsedRows);
        Assert.Equal(new[] { "North", "South / A", "South / B" }, ChartDataResolver.Resolve(native, native.ActiveSheet, native.ActiveSheet.Charts[0], new(native)).Categories);
    }

    [Fact]
    public void StandardLayoutAndTopLevelCollapseSurviveWithoutNativePivotMetadata()
    {
        var s = PivotHierarchyTests.Create(PivotLayout.Outline, PivotSubtotals.Top); var pivot = s.Sheet.PivotTables[0];
        s.TogglePivotGroup(pivot.Id, PivotHierarchy.Path(pivot, new PivotKey([CalcValue.Str("North")])));
        var bytes = Rewrite(XlsxWorkbook.Write(s.Book), "xl/pivotTables/pivotTable2_1.xml", xml => xml.Root!.Element(S + "extLst")!.Remove());
        Validate(bytes);
        var loaded = XlsxWorkbook.Read(bytes).Workbook; var imported = loaded.ActiveSheet.PivotTables[0];
        Assert.True(imported.NeedsLayoutRefresh); Assert.Equal(PivotLayout.Outline, imported.Layout); Assert.Equal(PivotSubtotals.Top, imported.Subtotals);
        Assert.Single(imported.CollapsedRows);
        Assert.Throws<InvalidOperationException>(() => XlsxWorkbook.Write(loaded));
        var session = new SpreadsheetSession(loaded); session.RefreshPivotTable(imported.Id);
        Assert.Equal(PivotRowKind.Collapsed, PivotReportCache.Get(session.Sheet.PivotTables[0]).RowBands[0].Kind);
        Assert.Equal(120, PivotReportCache.Get(session.Sheet.PivotTables[0])[1, 4].Number);
    }

    private static XDocument Part(byte[] bytes, string name)
    { using var zip = new ZipArchive(new MemoryStream(bytes)); using var part = zip.GetEntry(name)!.Open(); return XDocument.Load(part); }
    private static void Validate(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes); using var file = SpreadsheetDocument.Open(stream, false);
        var errors = new OpenXmlValidator(FileFormatVersions.Office2019).Validate(file).Select(e => e.Part?.Uri + ": " + e.Description).ToArray();
        Assert.True(errors.Length == 0, string.Join("\n", errors));
    }
    private static byte[] Rewrite(byte[] bytes, string path, Action<XDocument> edit)
    {
        using var source = new ZipArchive(new MemoryStream(bytes)); using var output = new MemoryStream();
        using (var target = new ZipArchive(output, ZipArchiveMode.Create, true)) foreach (var entry in source.Entries)
        {
            using var input = entry.Open(); using var destination = target.CreateEntry(entry.FullName).Open();
            if (entry.FullName == path) { var xml = XDocument.Load(input); edit(xml); xml.Save(destination); } else input.CopyTo(destination);
        }
        return output.ToArray();
    }
}
