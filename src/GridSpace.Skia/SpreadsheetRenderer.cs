using GridSpace.Core;
using GridSpace.Editing;
using GridSpace.Layout;
using SkiaSharp;

namespace GridSpace.Skia;

/// <summary>Viewport-culled spreadsheet rendering. Owns paint and font resources; geometry is supplied by the host.</summary>
public sealed partial class SpreadsheetRenderer : IDisposable
{
    private readonly SKPaint _paint = new() { IsAntialias = true };
    private readonly SKPaint _line = new() { IsAntialias = false, Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
    private bool _disposed;
    public TypefaceCatalog Fonts { get; } = new();
    public int LastRenderedCellCount { get; private set; }
    public static SKColor Color(string value, string fallback = "#242424") => SKColor.TryParse(value, out var color) ? color : SKColor.Parse(fallback);
    private static SKRect Rect(GridRect r) => new((float)r.X, (float)r.Y, (float)r.Right, (float)r.Bottom);
    private SKTypeface Typeface(CellStyle style) => Fonts.Resolve(style);
    private void Fill(SKCanvas canvas, SKRect rect, SKColor color) { _paint.Color = color; canvas.DrawRect(rect, _paint); }
    private void Stroke(SKCanvas canvas, SKRect rect, SKColor color, float width = 1) { _line.Color = color; _line.StrokeWidth = width; canvas.DrawRect(rect, _line); }
    private void Line(SKCanvas canvas, float x1, float y1, float x2, float y2, SKColor color, float width = 1) { _line.Color = color; _line.StrokeWidth = width; canvas.DrawLine(x1, y1, x2, y2, _line); }
    private void Text(SKCanvas canvas, string text, float x, float baseline, SKFont font, SKColor color) { _paint.Color = color; canvas.DrawText(text, x, baseline, font, _paint); }

    public void Render(SKCanvas canvas, SpreadsheetSession session, GridViewport viewport)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); canvas.Clear(SKColors.White); LastRenderedCellCount = 0;
        foreach (var pane in viewport.Panes())
        {
            canvas.Save(); canvas.ClipRect(Rect(pane.Clip));
            var columns = viewport.Columns.Visible((pane.Clip.X - pane.OriginX) / viewport.Zoom, (pane.Clip.Right - pane.OriginX) / viewport.Zoom, pane.FirstColumn, pane.LastColumn).ToArray();
            var rows = viewport.Rows.Visible((pane.Clip.Y - pane.OriginY) / viewport.Zoom, (pane.Clip.Bottom - pane.OriginY) / viewport.Zoom, pane.FirstRow, pane.LastRow).ToArray();
            foreach (var row in rows) foreach (var column in columns)
            {
                var address = new CellAddress(row, column);
                if (session.Sheet.Merges.Any(m => m.Contains(address))) continue;
                DrawCell(canvas, session, viewport, address, new(pane.OriginX + viewport.Columns.Position(column) * viewport.Zoom, pane.OriginY + viewport.Rows.Position(row) * viewport.Zoom, viewport.Columns.Size(column) * viewport.Zoom, viewport.Rows.Size(row) * viewport.Zoom));
            }
            foreach (var merge in session.Sheet.Merges)
            {
                var rect = viewport.RangeBounds(merge, pane);
                if (rect.Intersects(pane.Clip)) DrawCell(canvas, session, viewport, new CellAddress(merge.Top, merge.Left), rect);
            }
            DrawSelection(canvas, session, viewport, pane); DrawCharts(canvas, session, viewport, pane); canvas.Restore();
        }
        DrawHeaders(canvas, session, viewport);
        if (viewport.FrozenColumns > 0) Line(canvas, (float)(GridViewport.RowHeaderWidth + viewport.FrozenWidth), (float)GridViewport.ColumnHeaderHeight, (float)(GridViewport.RowHeaderWidth + viewport.FrozenWidth), (float)viewport.Height, Color("#969696"));
        if (viewport.FrozenRows > 0) Line(canvas, (float)GridViewport.RowHeaderWidth, (float)(GridViewport.ColumnHeaderHeight + viewport.FrozenHeight), (float)viewport.Width, (float)(GridViewport.ColumnHeaderHeight + viewport.FrozenHeight), Color("#969696"));
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; Fonts.Dispose(); _paint.Dispose(); _line.Dispose();
    }
}
