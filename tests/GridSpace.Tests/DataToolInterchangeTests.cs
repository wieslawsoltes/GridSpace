using System.IO.Compression;
using System.Xml.Linq;
using GridSpace.Core;
using GridSpace.Editing;
using GridSpace.Formulas;
using GridSpace.IO;
using GridSpace.Layout;
using GridSpace.Skia;
using SkiaSharp;
using Xunit;

namespace GridSpace.Tests;

public sealed class DataToolInterchangeTests
{
    [Theory]
    [InlineData(ConditionalFormatKind.CellValue)] [InlineData(ConditionalFormatKind.Expression)]
    [InlineData(ConditionalFormatKind.ContainsText)] [InlineData(ConditionalFormatKind.DuplicateValues)]
    [InlineData(ConditionalFormatKind.UniqueValues)] [InlineData(ConditionalFormatKind.Top)]
    [InlineData(ConditionalFormatKind.Bottom)] [InlineData(ConditionalFormatKind.AboveAverage)]
    [InlineData(ConditionalFormatKind.BelowAverage)] [InlineData(ConditionalFormatKind.ColorScale)] [InlineData(ConditionalFormatKind.DataBar)]
    public void SupportedConditionalRulesRoundTrip(ConditionalFormatKind kind)
    {
        var session = new SpreadsheetSession(new Workbook()); session.Paste("1\n2\n3");
        var rule = new ConditionalFormatRule { Range = "A1:A3", Kind = kind, Operand = kind == ConditionalFormatKind.Expression ? "A1>1" : "2", StopIfTrue = true, LowColor = "#F01020", Style = new DifferentialStyle { Background = "#112233", Bold = false, Foreground = "#445566", NumberFormat = "0.00" } };
        session.SetConditionalFormat(rule);
        var read = XlsxWorkbook.Read(XlsxWorkbook.Write(session.Book));
        var restored = Assert.Single(read.Workbook.ActiveSheet.ConditionalFormats);
        Assert.Equal(kind, restored.Kind); Assert.Equal(rule.Range, restored.Range);
        if (kind is not ConditionalFormatKind.ColorScale and not ConditionalFormatKind.DataBar)
        {
            Assert.Equal(rule.Style, restored.Style); Assert.True(restored.StopIfTrue);
        }
        else Assert.Equal(rule.LowColor, restored.LowColor);
        Assert.DoesNotContain(read.Warnings, w => w.StartsWith("Conditional formatting:"));
    }

    [Fact]
    public void FilterCriteriaSortLevelsAndManualHidingRoundTrip()
    {
        var session = new SpreadsheetSession(new Workbook()); session.Paste("Region\tAmount\nEast\t3\nWest\t2\nEast\t1");
        session.Select("A1:B4");
        session.SetFilter(new ColumnFilter { Column = 0, Values = ["East"] });
        session.SetFilter(new ColumnFilter { Column = 1, First = new FilterCondition(FilterOperator.GreaterThanOrEqual, "2"), Second = new FilterCondition(FilterOperator.LessThan, "5"), And = true });
        session.Sheet.HiddenRows.Add(2);
        session.Sort([new SortLevel(0), new SortLevel(1, true)]);
        var restored = new SpreadsheetSession(XlsxWorkbook.Read(XlsxWorkbook.Write(session.Book)).Workbook);
        Assert.Equal(2, restored.Sheet.Filters.Count); Assert.Equal(2, restored.Sheet.SortLevels.Count);
        Assert.Equal("A1:B4", restored.Sheet.SortRange); Assert.True(restored.Sheet.SortHasHeader);
        Assert.Contains(2, restored.Sheet.HiddenRows);
        Assert.Equal(session.Sheet.FilteredRows.Order(), restored.Sheet.FilteredRows.Order());
        restored.ClearFilters(); Assert.True(restored.Sheet.IsRowHidden(2));
    }

    [Fact]
    public void ConditionalElementsAreWrittenInWorksheetSchemaOrder()
    {
        var session = new SpreadsheetSession(new Workbook()); session.SetInput("5"); session.SetConditionalFormat(new ConditionalFormatRule()); session.SetValidation(["5", "10"]);
        using var memory = new MemoryStream(XlsxWorkbook.Write(session.Book)); using var zip = new ZipArchive(memory);
        using var stream = zip.GetEntry("xl/worksheets/sheet1.xml")!.Open(); var xml = XDocument.Load(stream);
        var names = xml.Root!.Elements().Select(e => e.Name.LocalName).ToList();
        Assert.True(names.IndexOf("conditionalFormatting") > names.IndexOf("sheetData"));
        Assert.True(names.IndexOf("conditionalFormatting") < names.IndexOf("dataValidations"));
        Assert.Equal("extLst", names[^1]);
    }

    [Fact]
    public void FilteringGeometryAndConditionalPixelsUseTheSameModel()
    {
        var session = new SpreadsheetSession(new Workbook()); session.Paste("Header\n5\n10\n15"); session.Select("A1:A4");
        session.SetFilter(new ColumnFilter { Column = 0, First = new FilterCondition(FilterOperator.GreaterThan, "5") });
        session.SetConditionalFormat(new ConditionalFormatRule { Range = "A2:A4", Operand = "8", Style = new DifferentialStyle { Background = "#FF0000" } });
        var view = new GridViewport { Width = 500, Height = 300 }; view.Refresh(session.Sheet);
        Assert.Equal(0, view.Rows.Size(1)); Assert.Equal(2, view.HitTest(55, 51).Row);
        using var renderer = new SpreadsheetRenderer(); using var surface = SKSurface.Create(new SKImageInfo(500, 300));
        session.Select("C1"); renderer.Render(surface.Canvas, session, view);
        using var image = surface.Snapshot(); using var bitmap = SKBitmap.FromImage(image);
        Assert.Equal(SKColors.Red, bitmap.GetPixel(55, 54));
    }
}
