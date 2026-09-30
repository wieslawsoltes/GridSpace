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

public sealed class ChartTextInterchangeTests
{
    private static readonly XNamespace C = "http://schemas.openxmlformats.org/drawingml/2006/chart";

    [Theory]
    [InlineData(ChartKind.Column)][InlineData(ChartKind.Line)][InlineData(ChartKind.Bar)]
    [InlineData(ChartKind.Pie)][InlineData(ChartKind.Area)][InlineData(ChartKind.Scatter)]
    [InlineData(ChartKind.Doughnut)][InlineData(ChartKind.Radar)][InlineData(ChartKind.Combo)]
    public void TextLinksEmitStandardReferencesAndSurviveWithoutNativeMetadata(ChartKind kind)
    {
        var (s, id) = ChartTextLinkTests.Create(); s.UpdateChart(id, c => c with { Kind = kind }); s.CustomizeChartSource(id);
        var bytes = XlsxWorkbook.Write(s.Book); Validate(bytes);
        var xml = Part(bytes); var title = xml.Root!.Element(C + "chart")!.Element(C + "title")!;
        Assert.Equal("'O''Brien! labels'!$A$1", title.Descendants(C + "f").Single().Value);
        Assert.Equal("'Data'!$B$1", xml.Descendants(C + "ser").First().Element(C + "tx")!.Descendants(C + "f").Single().Value);
        var imported = XlsxWorkbook.Read(Rewrite(bytes, doc => doc.Root!.Element(C + "extLst")!.Remove()));
        var chart = Assert.Single(imported.Workbook.ActiveSheet.Charts);
        Assert.Equal(s.FindChart(id)!.TitleReference, chart.TitleReference);
        Assert.Equal(new ChartTextReference("Data", "B1"), chart.Series[0].NameReference);
        if (kind is not ChartKind.Pie and not ChartKind.Doughnut)
        {
            Assert.Equal(s.FindChart(id)!.CategoryAxisTitleReference, chart.CategoryAxisTitleReference);
            Assert.Equal(s.FindChart(id)!.ValueAxisTitleReference, chart.ValueAxisTitleReference);
        }
        imported.Workbook.FindSheet("O'Brien! labels")!.Set("A1", "New title");
        imported.Workbook.FindSheet("Data")!.Set("B1", "New series");
        var data = ChartDataResolver.Resolve(imported.Workbook, imported.Workbook.ActiveSheet, chart, new(imported.Workbook));
        Assert.Equal("New title", data.Text.Title); Assert.Equal("New series", data.Series[0].Name);
        Assert.DoesNotContain(imported.Warnings, w => w.Contains("Chart text") || w.Contains("chart was skipped"));
    }

    [Fact]
    public void UnsupportedTextExpressionKeepsCachedCaptionWithoutDroppingTheChart()
    {
        var (s, _) = ChartTextLinkTests.Create();
        var bytes = Rewrite(XlsxWorkbook.Write(s.Book), doc =>
        {
            doc.Root!.Element(C + "extLst")!.Remove();
            doc.Root.Element(C + "chart")!.Element(C + "title")!.Descendants(C + "f").Single().Value = "'O''Brien! labels'!A1:A3";
        });
        var imported = XlsxWorkbook.Read(bytes); var chart = Assert.Single(imported.Workbook.ActiveSheet.Charts);
        Assert.Null(chart.TitleReference); Assert.Equal("Annual Revenue", chart.Title);
        Assert.Contains(imported.Warnings, w => w.Contains("cached caption was retained"));
    }

    [Fact]
    public void BrokenLinksRoundTripWithoutReconnecting()
    {
        var (s, id) = ChartTextLinkTests.Create(); s.SwitchSheet(1); s.DeleteRows(0); s.SwitchSheet(2);
        var bytes = XlsxWorkbook.Write(s.Book); Validate(bytes);
        var imported = XlsxWorkbook.Read(Rewrite(bytes, doc => doc.Root!.Element(C + "extLst")!.Remove()));
        var chart = Assert.Single(imported.Workbook.ActiveSheet.Charts);
        Assert.True(chart.TitleReference!.IsBroken);
        Assert.Equal("#REF!", ChartTextResolver.Resolve(imported.Workbook, chart.TitleReference, new(imported.Workbook)));
    }

    [Fact]
    public void LongLinkedCaptionsAreNotMistakenForOversizedLiteralMetadata()
    {
        var (s, _) = ChartTextLinkTests.Create(); var longTitle = new string('W', 4000);
        s.Book.FindSheet("O'Brien! labels")!.Set("A1", longTitle);
        var imported = XlsxWorkbook.Read(Rewrite(XlsxWorkbook.Write(s.Book), doc => doc.Root!.Element(C + "extLst")!.Remove()));
        var chart = Assert.Single(imported.Workbook.ActiveSheet.Charts); Assert.Equal(1024, chart.Title.Length);
        Assert.Equal(longTitle, ChartTextResolver.Resolve(imported.Workbook, chart.TitleReference!, new(imported.Workbook)));
    }

    [Fact]
    public void AutomaticNamesExportLiveHeaderReferencesBeforeCustomization()
    {
        var (s, _) = ChartTextLinkTests.Create(); var xml = Part(XlsxWorkbook.Write(s.Book));
        Assert.Equal("'Data'!$B$1", xml.Descendants(C + "ser").First().Element(C + "tx")!.Descendants(C + "f").Single().Value);
    }

    private static XDocument Part(byte[] bytes)
    {
        using var zip = new ZipArchive(new MemoryStream(bytes));
        using var input = zip.GetEntry("xl/charts/chart3_1.xml")!.Open(); return XDocument.Load(input);
    }
    private static void Validate(byte[] bytes)
    {
        using var file = SpreadsheetDocument.Open(new MemoryStream(bytes), false);
        var errors = new OpenXmlValidator(FileFormatVersions.Office2019).Validate(file).Select(e => e.Part?.Uri + ": " + e.Description).ToArray();
        Assert.True(errors.Length == 0, string.Join("\n", errors));
    }
    private static byte[] Rewrite(byte[] bytes, Action<XDocument> change)
    {
        using var source = new ZipArchive(new MemoryStream(bytes)); using var output = new MemoryStream();
        using (var target = new ZipArchive(output, ZipArchiveMode.Create, true))
        foreach (var entry in source.Entries)
        {
            using var input = entry.Open(); using var destination = target.CreateEntry(entry.FullName).Open();
            if (entry.FullName == "xl/charts/chart3_1.xml") { var xml = XDocument.Load(input); change(xml); xml.Save(destination); }
            else input.CopyTo(destination);
        }
        return output.ToArray();
    }
}
