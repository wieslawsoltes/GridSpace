using GridSpace.Core;
using GridSpace.Editing;
using GridSpace.Formulas;
using GridSpace.Layout;
using SkiaSharp;

namespace GridSpace.Skia;

/// <summary>Draws only visible cells. Owns and deterministically disposes its bounded font cache.</summary>
public sealed partial class SpreadsheetRenderer : IDisposable
{
    private readonly Dictionary<(string Family, bool Bold, bool Italic), SKTypeface> _typefaces = [];
    private readonly SKPaint _paint = new() { IsAntialias = true };
    private readonly SKPaint _line = new() { IsAntialias = false, Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
    private bool _disposed;
    public int LastRenderedCellCount { get; private set; }
    public static SKColor Color(string value, string fallback = "#242424") => SKColor.TryParse(value, out var color) ? color : SKColor.Parse(fallback);
    private static SKRect Rect(GridRect r) => new((float)r.X, (float)r.Y, (float)r.Right, (float)r.Bottom);
    private SKTypeface Typeface(CellStyle style)
    {
        var key = (style.FontFamily, style.Bold, style.Italic);
        if (_typefaces.TryGetValue(key, out var typeface)) return typeface;
        if (_typefaces.Count >= 128) { foreach (var face in _typefaces.Values) face.Dispose(); _typefaces.Clear(); }
        typeface = SKTypeface.FromFamilyName(style.FontFamily, style.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, style.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);
        _typefaces.Add(key, typeface);
        return typeface;
    }
    private void Fill(SKCanvas canvas, SKRect rect, SKColor color) { _paint.Color = color; canvas.DrawRect(rect, _paint); }
    private void Stroke(SKCanvas canvas, SKRect rect, SKColor color, float width = 1) { _line.Color = color; _line.StrokeWidth = width; canvas.DrawRect(rect, _line); }
    private void Line(SKCanvas canvas, float x1, float y1, float x2, float y2, SKColor color, float width = 1) { _line.Color = color; _line.StrokeWidth = width; canvas.DrawLine(x1, y1, x2, y2, _line); }
    private void Text(SKCanvas canvas, string text, float x, float baseline, SKFont font, SKColor color) { _paint.Color = color; canvas.DrawText(text, x, baseline, font, _paint); }

    public void Render(SKCanvas canvas, SpreadsheetSession session, GridViewport viewport)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        canvas.Clear(SKColors.White);
        LastRenderedCellCount = 0;
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
            DrawSelection(canvas, session, viewport, pane);
            DrawCharts(canvas, session, viewport, pane);
            canvas.Restore();
        }
        DrawHeaders(canvas, session, viewport);
        if (viewport.FrozenColumns > 0) Line(canvas, (float)(GridViewport.RowHeaderWidth + viewport.FrozenWidth), (float)GridViewport.ColumnHeaderHeight, (float)(GridViewport.RowHeaderWidth + viewport.FrozenWidth), (float)viewport.Height, Color("#969696"));
        if (viewport.FrozenRows > 0) Line(canvas, (float)GridViewport.RowHeaderWidth, (float)(GridViewport.ColumnHeaderHeight + viewport.FrozenHeight), (float)viewport.Width, (float)(GridViewport.ColumnHeaderHeight + viewport.FrozenHeight), Color("#969696"));
    }
    private void DrawCell(SKCanvas canvas, SpreadsheetSession session, GridViewport viewport, CellAddress address, GridRect bounds)
    {
        LastRenderedCellCount++;
        var cell = session.Sheet.Get(address);
        var rect = Rect(bounds);
        Fill(canvas, rect, Color(cell.Style.Background, "#FFFFFF"));
        if (session.Sheet.ShowGridLines || cell.Style.Border) Stroke(canvas, rect, cell.Style.Border ? Color("#808080") : Color("#E1E1E1"));
        var value = session.Calculation.Evaluate(session.Sheet, address);
        var text = session.Sheet.ShowFormulas && cell.IsFormula ? cell.Input : NumberFormatter.Format(value, cell.Style.NumberFormat);
        if (text.Length > 0)
        {
            canvas.Save(); canvas.ClipRect(new SKRect(rect.Left + 3, rect.Top + 1, rect.Right - 3, rect.Bottom - 1));
            using var font = new SKFont(Typeface(cell.Style), (float)(cell.Style.FontSize * 96 / 72 * viewport.Zoom));
            var width = font.MeasureText(text);
            var alignment = cell.Style.Alignment == CellAlignment.General ? value.Kind == ValueKind.Number ? CellAlignment.Right : CellAlignment.Left : cell.Style.Alignment;
            var x = alignment == CellAlignment.Right ? rect.Right - width - 5 : alignment == CellAlignment.Center ? rect.MidX - width / 2 : rect.Left + 5;
            var baseline = rect.MidY - (font.Metrics.Ascent + font.Metrics.Descent) / 2;
            if (cell.Style.Wrap)
            {
                var lineHeight = font.Spacing;
                var wrapped = Wrap(text, font, Math.Max(1, rect.Width - 10)).Take(128).ToArray();
                baseline = rect.Top + 3 - font.Metrics.Ascent;
                foreach (var part in wrapped)
                {
                    if (baseline + font.Metrics.Ascent > rect.Bottom) break;
                    Text(canvas, part, rect.Left + 5, baseline, font, Color(cell.Style.Foreground)); baseline += lineHeight;
                }
            }
            else
            {
                if (value.Kind == ValueKind.Number && width > rect.Width - 10) { text = new string('#', Math.Clamp((int)((rect.Width - 10) / Math.Max(1, font.MeasureText("#"))), 1, 100)); x = rect.Left + 5; }
                Text(canvas, text, x, baseline, font, Color(cell.Style.Foreground));
                if (cell.Style.Underline) Line(canvas, x, baseline + 2, Math.Min(rect.Right - 3, x + width), baseline + 2, Color(cell.Style.Foreground));
            }
            canvas.Restore();
        }
        if (!string.IsNullOrEmpty(cell.Note) || value.IsError)
        {
            using var triangle = new SKPath();
            var x = value.IsError ? rect.Left : rect.Right;
            triangle.MoveTo(x, rect.Top); triangle.LineTo(x + (value.IsError ? 6 : -6), rect.Top); triangle.LineTo(x, rect.Top + 6); triangle.Close();
            _paint.Color = value.IsError ? Color("#107C41") : Color("#AD2E24"); canvas.DrawPath(triangle, _paint);
        }
        if (session.Sheet.FilterRange is { } filter && CellRange.TryParse(filter, out var filterRange) && address.Row == filterRange.Top && address.Column >= filterRange.Left && address.Column <= filterRange.Right)
        {
            var box = new SKRect(rect.Right - 16, rect.Bottom - 17, rect.Right - 2, rect.Bottom - 3);
            Fill(canvas, box, Color("#F7F7F7")); Stroke(canvas, box, Color("#B8B8B8"));
            using var arrow = new SKPath(); arrow.MoveTo(box.Left + 4, box.Top + 5); arrow.LineTo(box.Right - 4, box.Top + 5); arrow.LineTo(box.MidX, box.Top + 9); arrow.Close(); _paint.Color = Color("#444444"); canvas.DrawPath(arrow, _paint);
        }
    }
    private static IEnumerable<string> Wrap(string text, SKFont font, float width)
    {
        foreach (var paragraph in text.Replace("\r", "").Split('\n'))
        {
            var line = "";
            foreach (var word in paragraph.Split(' '))
            {
                var candidate = line.Length == 0 ? word : line + " " + word;
                if (line.Length > 0 && font.MeasureText(candidate) > width) { yield return line; line = word; }
                else line = candidate;
            }
            yield return line;
        }
    }
    private void DrawSelection(SKCanvas canvas, SpreadsheetSession session, GridViewport viewport, GridPane pane)
    {
        var rect = Rect(viewport.RangeBounds(session.Selection, pane));
        if (session.Selection.Count > 1) Fill(canvas, rect, new SKColor(16, 124, 65, 24));
        Stroke(canvas, rect, Color("#107C41"), 2);
        Fill(canvas, new SKRect(rect.Right - 3, rect.Bottom - 3, rect.Right + 3, rect.Bottom + 3), Color("#107C41"));
    }
    private void DrawHeaders(SKCanvas canvas, SpreadsheetSession session, GridViewport viewport)
    {
        var header = Color("#F4F4F4");
        Fill(canvas, new SKRect(0, 0, (float)viewport.Width, (float)GridViewport.ColumnHeaderHeight), header);
        Fill(canvas, new SKRect(0, 0, (float)GridViewport.RowHeaderWidth, (float)viewport.Height), header);
        using var font = new SKFont(Typeface(CellStyle.Default), 12);
        foreach (var pane in viewport.Panes())
        {
            canvas.Save(); canvas.ClipRect(new SKRect((float)pane.Clip.X, 0, (float)pane.Clip.Right, (float)GridViewport.ColumnHeaderHeight));
            foreach (var c in viewport.Columns.Visible((pane.Clip.X - pane.OriginX) / viewport.Zoom, (pane.Clip.Right - pane.OriginX) / viewport.Zoom, pane.FirstColumn, pane.LastColumn))
            {
                var rect = new SKRect((float)(pane.OriginX + viewport.Columns.Position(c) * viewport.Zoom), 0, (float)(pane.OriginX + viewport.Columns.Position(c + 1) * viewport.Zoom), (float)GridViewport.ColumnHeaderHeight);
                var selected = c >= session.Selection.Left && c <= session.Selection.Right;
                Fill(canvas, rect, selected ? Color("#DCEDE2") : header); Stroke(canvas, rect, Color("#D6D6D6"));
                var text = CellAddress.ColumnName(c); Text(canvas, text, rect.MidX - font.MeasureText(text) / 2, 18, font, selected ? Color("#107C41") : Color("#555555"));
            }
            canvas.Restore();
            canvas.Save(); canvas.ClipRect(new SKRect(0, (float)pane.Clip.Y, (float)GridViewport.RowHeaderWidth, (float)pane.Clip.Bottom));
            foreach (var r in viewport.Rows.Visible((pane.Clip.Y - pane.OriginY) / viewport.Zoom, (pane.Clip.Bottom - pane.OriginY) / viewport.Zoom, pane.FirstRow, pane.LastRow))
            {
                var rect = new SKRect(0, (float)(pane.OriginY + viewport.Rows.Position(r) * viewport.Zoom), (float)GridViewport.RowHeaderWidth, (float)(pane.OriginY + viewport.Rows.Position(r + 1) * viewport.Zoom));
                var selected = r >= session.Selection.Top && r <= session.Selection.Bottom;
                Fill(canvas, rect, selected ? Color("#DCEDE2") : header); Stroke(canvas, rect, Color("#D6D6D6"));
                var text = (r + 1).ToString(System.Globalization.CultureInfo.InvariantCulture); Text(canvas, text, rect.Right - font.MeasureText(text) - 8, rect.MidY - (font.Metrics.Ascent + font.Metrics.Descent) / 2, font, selected ? Color("#107C41") : Color("#555555"));
            }
            canvas.Restore();
        }
        Fill(canvas, new SKRect(0, 0, (float)GridViewport.RowHeaderWidth, (float)GridViewport.ColumnHeaderHeight), header);
        using var corner = new SKPath(); corner.MoveTo(32, 6); corner.LineTo(32, 20); corner.LineTo(18, 20); corner.Close(); _paint.Color = Color("#B8B8B8"); canvas.DrawPath(corner, _paint);
    }
    public double MeasureColumn(SpreadsheetSession session, int column)
    {
        var width = 24d;
        foreach (var entry in session.Sheet.Cells)
        {
            var address = CellAddress.Parse(entry.Key); if (address.Column != column) continue;
            using var font = new SKFont(Typeface(entry.Value.Style), (float)(entry.Value.Style.FontSize * 96 / 72));
            var text = NumberFormatter.Format(session.Calculation.Evaluate(session.Sheet, address), entry.Value.Style.NumberFormat);
            width = Math.Max(width, font.MeasureText(text) + 14);
        }
        return Math.Min(1000, width);
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        foreach (var font in _typefaces.Values) font.Dispose(); _typefaces.Clear(); _paint.Dispose(); _line.Dispose();
    }
}
