using GridSpace.Core;
using GridSpace.Editing;
using GridSpace.Layout;
using SkiaSharp;

namespace GridSpace.Skia;

public sealed partial class SpreadsheetRenderer
{
    private ChartRenderer? _charts;
    private ChartSpec? _sourceBindingDocument;
    private string? _sourceBindingSheet;
    private long _sourceBindingRevision = -1;
    private IReadOnlyList<ChartSourceBinding> _sourceBindings = Array.Empty<ChartSourceBinding>();
    public ChartRenderer Charts => _charts ??= new(Fonts);
    public string? SelectedChartId { get; set; }
    public ChartSpec? ChartPreview { get; set; }
    public ChartSpec? ChartSourcePreview { get; set; }

    /// <summary>Bounded metadata-only cache. No formula evaluation or worksheet scan is needed to show source grips.</summary>
    public IReadOnlyList<ChartSourceBinding> SourceBindings(SpreadsheetSession session)
    {
        var chart = session.FindChart(SelectedChartId);
        if (chart is not null && ChartSourcePreview?.Id == chart.Id) chart = ChartSourcePreview;
        if (ReferenceEquals(chart, _sourceBindingDocument) && _sourceBindingSheet == session.Sheet.Name &&
            _sourceBindingRevision == session.Book.StructureRevision) return _sourceBindings;
        _sourceBindingDocument = chart;
        _sourceBindingSheet = session.Sheet.Name;
        _sourceBindingRevision = session.Book.StructureRevision;
        return _sourceBindings = chart is null ? Array.Empty<ChartSourceBinding>() : ChartSourceEditing.Bindings(chart, session.Sheet.Name);
    }

    private void DrawChartSources(SKCanvas canvas, SpreadsheetSession session, GridViewport view, GridPane pane)
    {
        foreach (var binding in SourceBindings(session))
        {
            if (!ChartSourceGeometry.Intersects(binding, pane)) continue;
            var bounds = view.RangeBounds(binding.Range, pane);
            if (bounds.Width <= 0 || bounds.Height <= 0 || !bounds.Intersects(pane.Clip)) continue;
            var color = Color(binding.Part switch
            {
                ChartSourcePart.DataRange => "#7030A0",
                ChartSourcePart.Categories => "#D66A00",
                _ => GridSpace.Formulas.ChartDataResolver.Palette[binding.SeriesIndex % GridSpace.Formulas.ChartDataResolver.Palette.Length]
            });
            Fill(canvas, Rect(bounds), color.WithAlpha(13));
            Stroke(canvas, Rect(bounds), color, 2);
            foreach (var grip in ChartSourceGeometry.Grips(binding, view, pane))
            {
                var radius = (float)ChartSourceGeometry.GripRadius;
                var box = new SKRect((float)grip.X - radius, (float)grip.Y - radius, (float)grip.X + radius, (float)grip.Y + radius);
                Fill(canvas, box, SKColors.White);
                Stroke(canvas, box, color, 1.5f);
            }
        }
    }

    private void DrawCharts(SKCanvas canvas, SpreadsheetSession session, GridViewport viewport, GridPane pane)
    {
        // Drawings remain above source outlines, both visually and in pointer hit testing.
        DrawChartSources(canvas, session, viewport, pane);
        foreach (var document in session.Sheet.Charts)
        {
            var chart = ChartPreview?.Id == document.Id ? ChartPreview
                : ChartSourcePreview?.Id == document.Id ? ChartSourcePreview : document;
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
