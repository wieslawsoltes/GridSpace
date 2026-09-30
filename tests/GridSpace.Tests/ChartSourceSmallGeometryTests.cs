using GridSpace.Core;
using GridSpace.Layout;
using Xunit;

namespace GridSpace.Tests;

public sealed class ChartSourceSmallGeometryTests
{
    [Theory]
    [InlineData(.25)] [InlineData(.5)] [InlineData(1)]
    public void NearestHandleWinsForNarrowOneCellVectors(double zoom)
    {
        var sheet = new Worksheet(); sheet.ColumnWidths[1] = 24; sheet.RowHeights[1] = 16;
        var viewport = new GridViewport { Width = 800, Height = 600, Zoom = zoom }; viewport.Refresh(sheet);
        var binding = new ChartSourceBinding(ChartSourcePart.SeriesValues, 0, CellRange.Parse("B2"), false);
        foreach (var pane in viewport.Panes()) foreach (var grip in ChartSourceGeometry.Grips(binding, viewport, pane))
        {
            var hit = ChartSourceGeometry.HitTest([binding], viewport, grip.X, grip.Y);
            Assert.NotNull(hit); Assert.Equal(grip.Handle, hit.Handle);
        }
    }
}
