using GridSpace.Core;
using GridSpace.Editing;
using GridSpace.IO;
using GridSpace.Layout;
using GridSpace.Skia;
using SkiaSharp;
using Xunit;

namespace GridSpace.Tests;

public sealed class LayoutAndRenderingTests
{
    [Fact]
    public void SparseAxisMatchesDenseReference()
    {
        var random = new Random(17);
        var sizes = Enumerable.Range(0, 300).Where(i => i % 7 == 0).ToDictionary(i => i, _ => (double)random.Next(16, 200));
        var hidden = Enumerable.Range(0, 300).Where(i => i % 11 == 0).ToHashSet();
        var axis = new AxisLayout(300, 24, sizes, hidden);
        var position = 0d;
        for (var i = 0; i < 300; i++)
        {
            Assert.Equal(position, axis.Position(i), 8);
            var size = hidden.Contains(i) ? 0 : sizes.GetValueOrDefault(i, 24);
            Assert.Equal(size, axis.Size(i), 8);
            if (size > 0) Assert.Equal(i, axis.IndexAt(position + size / 2));
            position += size;
        }
        Assert.Equal(position, axis.Extent, 8);
    }
    [Fact]
    public void AxisSkipsContiguousHiddenRows()
    {
        var axis = new AxisLayout(CellAddress.MaxRows, 24, new Dictionary<int, double>(), Enumerable.Range(1, 10000).ToHashSet());
        Assert.Equal(10001, axis.IndexAt(24));
        Assert.Equal(new[] { 0, 10001, 10002 }, axis.Visible(0, 72).ToArray());
    }
    [Theory]
    [InlineData(.25)] [InlineData(1)] [InlineData(2)] [InlineData(4)]
    public void FrozenPaneHitTestingMatchesCellBounds(double zoom)
    {
        var sheet = new Worksheet { FrozenRows = 2, FrozenColumns = 1 };
        var view = new GridViewport { Width = 1400, Height = 900, Zoom = zoom };
        view.Refresh(sheet); view.ScrollTo(140, 200);
        foreach (var address in new[] { new CellAddress(0, 0), new CellAddress(0, 8), new CellAddress(30, 0), new CellAddress(30, 8) })
        {
            var rect = view.CellBounds(address);
            if (rect.Right > view.Width || rect.Bottom > view.Height) continue;
            var hit = view.HitTest(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
            Assert.Equal(address.Row, hit.Row); Assert.Equal(address.Column, hit.Column);
        }
    }
    [Theory]
    [InlineData(0, 0)] [InlineData(1000, 40)] [InlineData(1048575, 16383)]
    public void EnsureVisibleReachesFarAddresses(int row, int column)
    {
        var view = new GridViewport { Width = 1200, Height = 800 };
        view.Refresh(new Worksheet()); var address = new CellAddress(row, column); view.EnsureVisible(address);
        var rect = view.CellBounds(address);
        Assert.InRange(rect.Right, GridViewport.RowHeaderWidth, view.Width + .001);
        Assert.InRange(rect.Bottom, GridViewport.ColumnHeaderHeight, view.Height + .001);
    }
    [Theory]
    [InlineData("A1", true)] [InlineData("XFD1048576", true)] [InlineData("B10:A1", true)]
    [InlineData("XFE1", false)] [InlineData("A0", false)] [InlineData("A1:B2:C3", false)] [InlineData("", false)]
    public void RangeParsingIsNonThrowing(string text, bool expected) => Assert.Equal(expected, CellRange.TryParse(text, out _));
    [Fact]
    public void CsvGuardsOriginBasedRectangle()
    {
        var book = new Workbook(); book.ActiveSheet.Set("XFD1048576", "42");
        Assert.Throws<InvalidOperationException>(() => WorkbookFiles.Csv(book));
    }
    [Fact]
    public void SampleRendersWithBoundedVisibleCellCount()
    {
        var session = new SpreadsheetSession(SampleWorkbook.Create());
        var viewport = new GridViewport { Width = 1440, Height = 800 }; viewport.Refresh(session.Sheet);
        using var renderer = new SpreadsheetRenderer();
        using var surface = SKSurface.Create(new SKImageInfo(1440, 800));
        renderer.Render(surface.Canvas, session, viewport);
        Assert.InRange(renderer.LastRenderedCellCount, 1, 2000);
        using var image = surface.Snapshot(); using var bitmap = SKBitmap.FromImage(image);
        Assert.NotEqual(SKColors.White, bitmap.GetPixel(100, 140));
        Directory.CreateDirectory("artifacts/rendering");
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes("artifacts/rendering/revenue.png", data.ToArray());
    }
    [Theory]
    [InlineData(ChartKind.Column)] [InlineData(ChartKind.Line)] [InlineData(ChartKind.Bar)] [InlineData(ChartKind.Pie)]
    public void ChartKindsRenderAndDispose(ChartKind kind)
    {
        var session = new SpreadsheetSession(SampleWorkbook.Create()); session.Sheet.Charts[0].Kind = kind;
        var viewport = new GridViewport { Width = 1600, Height = 900 }; viewport.Refresh(session.Sheet);
        using var renderer = new SpreadsheetRenderer(); using var surface = SKSurface.Create(new SKImageInfo(1600, 900));
        renderer.Render(surface.Canvas, session, viewport);
        Assert.True(renderer.LastRenderedCellCount > 0);
    }
}
