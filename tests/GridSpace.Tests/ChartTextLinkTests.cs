using GridSpace.Core;
using GridSpace.Editing;
using GridSpace.Formulas;
using GridSpace.Layout;
using GridSpace.Skia;
using SkiaSharp;
using Xunit;

namespace GridSpace.Tests;

public sealed class ChartTextLinkTests
{
    internal static (SpreadsheetSession Session, string Id) Create()
    {
        var book = new Workbook(); book.ActiveSheet.Name = "Data";
        book.ActiveSheet.Set("A1", "Category"); book.ActiveSheet.Set("B1", "Revenue");
        book.ActiveSheet.Set("A2", "First"); book.ActiveSheet.Set("B2", "20");
        book.ActiveSheet.Set("A3", "Second"); book.ActiveSheet.Set("B3", "40");
        var s = new SpreadsheetSession(book); s.AddSheet("O'Brien! labels");
        s.SetInput("Annual Revenue", CellAddress.Parse("A1"));
        s.SetInput("Period", CellAddress.Parse("A2"));
        s.SetInput("=SUM(Data!B2:B3)", CellAddress.Parse("A3"));
        s.Select("A3"); s.ApplyStyle(style => style with { NumberFormat = "$0.00" });
        s.AddSheet("Report");
        var id = s.AddChart(new ChartSpec { SourceSheet = "Data", Range = "A1:B3",
            TitleReference = new("O'Brien! labels", "A1"), CategoryAxisTitleReference = new("O'Brien! labels", "A2"),
            ValueAxisTitleReference = new("O'Brien! labels", "A3") });
        return (s, id);
    }
    private static ChartData Data(SpreadsheetSession s, string id) => ChartDataResolver.Resolve(s.Book, s.Sheet, s.FindChart(id)!, s.Calculation);

    [Theory]
    [InlineData("A1", "Data", "A1")]
    [InlineData("$XFD$1048576", "Data", "XFD1048576")]
    [InlineData("=Other!B2", "Other", "B2")]
    [InlineData("='O''Brien! labels'!$A$3", "O'Brien! labels", "A3")]
    [InlineData("'Zażółć gęślą'!D10", "Zażółć gęślą", "D10")]
    public void ParsesOnlyNamedSingleCellReferences(string input, string sheet, string cell)
    {
        var reference = ChartTextReference.Parse(input, "Data");
        Assert.Equal(sheet, reference.Sheet); Assert.Equal(cell, reference.Cell);
        Assert.Equal(reference, ChartTextReference.Parse(reference.ToFormula(), "ignored"));
    }

    [Theory]
    [InlineData("A1:A2")][InlineData("SUM(A1)")][InlineData("A:A")][InlineData("A1+1")]
    [InlineData("'[external.xlsx]Data'!A1")][InlineData("'O'Brien'!A1")]
    [InlineData("'unclosed!A1")][InlineData("A$1$")][InlineData("XFE1")][InlineData("A1048577")]
    [InlineData("Data!A1,B2")][InlineData("A0")][InlineData("Data!")]
    public void RejectsExpressionsExternalBooksAndMalformedReferences(string input)
    {
        var error = Record.Exception(() => ChartTextReference.Parse(input, "Data"));
        Assert.True(error is ArgumentException or FormatException, error?.ToString() ?? "Reference was accepted unexpectedly.");
    }

    [Fact]
    public void TextTracksFormattedCalculatedValuesAndSourceChangesWithoutMutatingTheChart()
    {
        var (s, id) = Create(); var original = s.FindChart(id); var before = s.Book.DrawingRevision;
        var data = Data(s, id);
        Assert.Equal("Annual Revenue", data.Text.Title); Assert.Equal("Period", data.Text.CategoryAxisTitle);
        Assert.Equal("$60.00", data.Text.ValueAxisTitle);
        s.Book.FindSheet("Data")!.Set("B3", "80");
        Assert.Equal("$100.00", Data(s, id).Text.ValueAxisTitle);
        Assert.Same(original, s.FindChart(id)); Assert.Equal(before, s.Book.DrawingRevision);
    }

    [Fact]
    public void CustomizationKeepsLiveHeaderLinksAndTextOnlyRenamesReuseAllVectors()
    {
        var (s, id) = Create(); s.CustomizeChartSource(id);
        Assert.Equal(new ChartTextReference("Data", "B1"), s.FindChart(id)!.Series[0].NameReference);
        var cache = new ChartDataCache(); var a = cache.Get(s.Book, s.Sheet, s.FindChart(id)!, s.Calculation);
        var evaluations = s.Calculation.EvaluatedCellCount;
        s.UpdateChart(id, c => c with { Series = [c.Series[0] with { Name = "Override", NameReference = null }] });
        var b = cache.Get(s.Book, s.Sheet, s.FindChart(id)!, s.Calculation);
        Assert.Equal("Override", b.Series[0].Name); Assert.Same(a.Series[0].Values, b.Series[0].Values);
        Assert.Same(a.Categories, b.Categories); Assert.Same(a.XValues, b.XValues);
        Assert.Equal(1, cache.ResolveCount); Assert.Equal(1, cache.TextRefreshCount);
        Assert.Equal(evaluations, s.Calculation.EvaluatedCellCount);
        s.Undo(); s.Book.FindSheet("Data")!.Set("B1", "Updated header");
        Assert.Equal("Updated header", Data(s, id).Series[0].Name);
    }

    [Fact]
    public void TransposedAutomaticHeadersRemainCellLinked()
    {
        var (s, id) = Create();
        s.UpdateChart(id, c => c with { SeriesInRows = true }); s.CustomizeChartSource(id);
        Assert.Equal(new[] { "A2", "A3" }, s.FindChart(id)!.Series.Select(v => v.NameReference!.Cell));
        s.Book.FindSheet("Data")!.Set("A2", "Renamed"); Assert.Equal("Renamed", Data(s, id).Series[0].Name);
    }

    [Fact]
    public void UnlinkFreezesCurrentCaptionAndUndoRestoresTheLink()
    {
        var (s, id) = Create(); s.SetChartTextLink(id, ChartSourcePart.Title, null);
        Assert.Null(s.FindChart(id)!.TitleReference); Assert.Equal("Annual Revenue", s.FindChart(id)!.Title);
        s.Undo(); Assert.NotNull(s.FindChart(id)!.TitleReference);
        var original = s.FindChart(id)!; var source = s.Book.FindSheet("O'Brien! labels")!.Get("A1");
        s.CommitChartTitleEdit(original, "Manual title");
        Assert.Null(s.FindChart(id)!.TitleReference); Assert.Equal(source, s.Book.FindSheet("O'Brien! labels")!.Get("A1"));
        Assert.Throws<InvalidOperationException>(() => s.CommitChartTitleEdit(original, "stale"));
        s.Undo(); Assert.NotNull(s.FindChart(id)!.TitleReference);
    }

    [Fact]
    public void DeletedTextReferencesNeverReconnectAndUndoRestoresOriginalIdentity()
    {
        var (s, id) = Create(); s.SwitchSheet(1); s.DeleteRows(0);
        s.SwitchSheet(2); Assert.True(s.FindChart(id)!.TitleReference!.IsBroken);
        Assert.Equal("#REF!", Data(s, id).Text.Title);
        s.Book.FindSheet("O'Brien! labels")!.Set("A1", "Replacement"); Assert.Equal("#REF!", Data(s, id).Text.Title);
        s.Undo(); s.SwitchSheet(2); Assert.False(s.FindChart(id)!.TitleReference!.IsBroken);
        s.SwitchSheet(1); s.DeleteSheet(); s.AddSheet("O'Brien! labels"); s.SetInput("Unrelated", CellAddress.Parse("A1"));
        s.SwitchSheet(1); Assert.Equal("Report", s.Sheet.Name); Assert.True(s.FindChart(id)!.TitleReference!.IsBroken);
        Assert.Equal("#REF!", Data(s, id).Text.Title);
    }

    [Fact]
    public void StructuralEditsAndRenameRebaseLinksOnTheirOwnSheetsOnly()
    {
        var (s, id) = Create(); s.CustomizeChartSource(id); s.SwitchSheet(1);
        s.InsertRows(0, 2); s.InsertColumns(0); s.RenameSheet("Renamed labels");
        s.SwitchSheet(2); var chart = s.FindChart(id)!;
        Assert.Equal(new ChartTextReference("Renamed labels", "B3"), chart.TitleReference);
        Assert.Equal(new ChartTextReference("Renamed labels", "B5"), chart.ValueAxisTitleReference);
        Assert.Equal(new ChartTextReference("Data", "B1"), chart.Series[0].NameReference);
        Assert.Equal("Annual Revenue", Data(s, id).Text.Title);
    }

    [Fact]
    public void CopyPreservesExternalLinksAndSheetDuplicationRebindsOnlyLocalLinks()
    {
        var (s, id) = Create(); s.SetInput("Local caption", CellAddress.Parse("A1"));
        s.SetChartTextLink(id, ChartSourcePart.Title, new("Report", "A1"));
        var payload = s.CopyChart(id); s.AddSheet("Pasted"); var pasted = s.PasteChart(payload);
        Assert.Equal("Report", s.FindChart(pasted)!.TitleReference!.Sheet);
        s.SwitchSheet(2); s.DuplicateSheet(); var chart = Assert.Single(s.Sheet.Charts);
        Assert.Equal(s.Sheet.Name, chart.TitleReference!.Sheet); Assert.Equal("O'Brien! labels", chart.ValueAxisTitleReference!.Sheet);
    }

    [Fact]
    public void TextSourceHandlesMoveOneCellAndPreserveNumericBindingsAndHistory()
    {
        var (s, id) = Create(); s.SetInput("Local", CellAddress.Parse("A1"));
        s.SetChartTextLink(id, ChartSourcePart.Title, new("Report", "A1"));
        var chart = s.FindChart(id)!; var binding = Assert.Single(ChartSourceEditing.Bindings(chart, "Report"));
        Assert.True(binding.IsSingleCell); Assert.False(binding.IsVector);
        var moved = ChartSourceEditing.Transform(binding, ChartSourceHandle.End, 3, 2); Assert.Equal("C4", moved.ToString());
        var revision = s.Book.Revision; var structure = s.Book.StructureRevision;
        s.CommitChartSourceEdit(chart, binding, moved);
        Assert.Equal("C4", s.FindChart(id)!.TitleReference!.Cell);
        Assert.Equal("A1:B3", s.FindChart(id)!.Range); Assert.Equal(revision, s.Book.Revision); Assert.Equal(structure, s.Book.StructureRevision);
        s.Undo(); Assert.Equal("A1", s.FindChart(id)!.TitleReference!.Cell);
        Assert.Throws<ArgumentException>(() => ChartSourceEditing.Replace(s.FindChart(id)!, "Report", binding, CellRange.Parse("A1:B2")));
    }

    [Fact]
    public void WarmTextBindingAndSourceMetadataLookupsAllocateNothing()
    {
        var (s, id) = Create(); s.CustomizeChartSource(id); var chart = s.FindChart(id)!;
        var cache = new ChartDataCache(); var data = cache.Get(s.Book, s.Sheet, chart, s.Calculation);
        using var renderer = new SpreadsheetRenderer { SelectedChartId = id }; _ = renderer.SourceBindings(s);
        for (var i = 0; i < 100; i++) { cache.Get(s.Book, s.Sheet, chart, s.Calculation); renderer.SourceBindings(s); }
        var before = GC.GetAllocatedBytesForCurrentThread(); var evaluated = s.Calculation.EvaluatedCellCount;
        for (var i = 0; i < 1000; i++) { cache.Get(s.Book, s.Sheet, chart, s.Calculation); renderer.SourceBindings(s); }
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, bytes); Assert.Equal(evaluated, s.Calculation.EvaluatedCellCount); Assert.Same(data, cache.Get(s.Book, s.Sheet, chart, s.Calculation));
    }

    [Fact]
    public void CaptionLinkChangesDoNotResolveTheNumericalDataAgain()
    {
        var (s, id) = Create(); var cache = new ChartDataCache();
        var a = cache.Get(s.Book, s.Sheet, s.FindChart(id)!, s.Calculation);
        s.SetChartTextLink(id, ChartSourcePart.Title, new("O'Brien! labels", "A2"));
        var b = cache.Get(s.Book, s.Sheet, s.FindChart(id)!, s.Calculation);
        Assert.Equal("Period", b.Text.Title); Assert.Equal(1, cache.ResolveCount); Assert.Equal(1, cache.TextRefreshCount);
        Assert.Same(a.Series[0].Values, b.Series[0].Values);
    }

    [Fact]
    public void NativePersistenceRetainsLinkTypeAndRendersLiveText()
    {
        var (s, id) = Create(); s.CustomizeChartSource(id);
        var restored = new SpreadsheetSession(Workbook.FromJson(s.Book.ToJson())); var chart = restored.FindChart(id)!;
        Assert.Equal(s.FindChart(id)!.TitleReference, chart.TitleReference);
        using var fonts = new TypefaceCatalog(); using var renderer = new ChartRenderer(fonts);
        using var surface = SKSurface.Create(new SKImageInfo(800, 500));
        renderer.Render(surface.Canvas, restored.Book, restored.Sheet, chart, restored.Calculation, new(0, 0, chart.Width, chart.Height));
        Assert.True(renderer.LastRenderedPoints > 0);
        Directory.CreateDirectory("artifacts/rendering"); using var image = surface.Snapshot(); using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes("artifacts/rendering/cell-linked-chart-text.png", png.ToArray());
    }
}
