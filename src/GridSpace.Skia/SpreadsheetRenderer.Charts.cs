using GridSpace.Core;
using GridSpace.Editing;
using GridSpace.Layout;
using SkiaSharp;

namespace GridSpace.Skia;

public sealed partial class SpreadsheetRenderer
{
    private ChartRenderer? _charts;
    public ChartRenderer Charts => _charts ??= new(Fonts);
    public string? SelectedChartId { get; set; }
    public ChartSpec? ChartPreview { get; set; }

    private void DrawCharts(SKCanvas canvas, SpreadsheetSession session, GridViewport viewport, GridPane pane)
    {
        foreach (var document in session.Sheet.Charts)
        {
            var chart = ChartPreview?.Id == document.Id ? ChartPreview : document;
            var bounds = ChartGeometry.Bounds(chart, viewport, pane);
            if (!bounds.Intersects(pane.Clip)) continue;
            Charts.Render(canvas, session.Book, session.Sheet, chart, session.Calculation, bounds, viewport.Zoom);
            if (chart.Id != SelectedChartId) continue;
            Stroke(canvas, Rect(bounds), Color("#107C41"), 1.5f);
            foreach (var handle in ChartGeometry.Handles(bounds))
            {
                var box = new SKRect((float)handle.X - 3.5f, (float)handle.Y - 3.5f, (float)handle.X + 3.5f, (float)handle.Y + 3.5f);
                Fill(canvas, box, SKColors.White); Stroke(canvas, box, Color("#107C41"));
            }
        }
    }
}
