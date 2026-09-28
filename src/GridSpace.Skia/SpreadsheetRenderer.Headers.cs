using GridSpace.Core;
using GridSpace.Editing;
using GridSpace.Layout;
using SkiaSharp;

namespace GridSpace.Skia;

public sealed partial class SpreadsheetRenderer
{
    private void DrawSelection(SKCanvas canvas, SpreadsheetSession session, GridViewport viewport, GridPane pane)
    {
        if (session.Calculation.GetSpill(session.Sheet, session.ActiveCell) is { } spill)
            Stroke(canvas, Rect(viewport.RangeBounds(spill.Range, pane)), Color("#4472C4"), 1);
        var rect = Rect(viewport.RangeBounds(session.Selection, pane));
        if (session.Selection.Count > 1) Fill(canvas, rect, new SKColor(16, 124, 65, 24));
        Stroke(canvas, rect, Color("#107C41"), 2); Fill(canvas, new SKRect(rect.Right - 3, rect.Bottom - 3, rect.Right + 3, rect.Bottom + 3), Color("#107C41"));
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
            foreach (var column in viewport.Columns.Visible((pane.Clip.X - pane.OriginX) / viewport.Zoom, (pane.Clip.Right - pane.OriginX) / viewport.Zoom, pane.FirstColumn, pane.LastColumn))
            {
                var rect = new SKRect((float)(pane.OriginX + viewport.Columns.Position(column) * viewport.Zoom), 0, (float)(pane.OriginX + viewport.Columns.Position(column + 1) * viewport.Zoom), (float)GridViewport.ColumnHeaderHeight);
                var selected = column >= session.Selection.Left && column <= session.Selection.Right;
                Fill(canvas, rect, selected ? Color("#DCEDE2") : header); Stroke(canvas, rect, Color("#D6D6D6"));
                var text = CellAddress.ColumnName(column); Text(canvas, text, rect.MidX - font.MeasureText(text) / 2, 18, font, selected ? Color("#107C41") : Color("#555555"));
            }
            canvas.Restore();
            canvas.Save(); canvas.ClipRect(new SKRect(0, (float)pane.Clip.Y, (float)GridViewport.RowHeaderWidth, (float)pane.Clip.Bottom));
            foreach (var row in viewport.Rows.Visible((pane.Clip.Y - pane.OriginY) / viewport.Zoom, (pane.Clip.Bottom - pane.OriginY) / viewport.Zoom, pane.FirstRow, pane.LastRow))
            {
                var rect = new SKRect(0, (float)(pane.OriginY + viewport.Rows.Position(row) * viewport.Zoom), (float)GridViewport.RowHeaderWidth, (float)(pane.OriginY + viewport.Rows.Position(row + 1) * viewport.Zoom));
                var selected = row >= session.Selection.Top && row <= session.Selection.Bottom;
                Fill(canvas, rect, selected ? Color("#DCEDE2") : header); Stroke(canvas, rect, Color("#D6D6D6"));
                var text = (row + 1).ToString(System.Globalization.CultureInfo.InvariantCulture); Text(canvas, text, rect.Right - font.MeasureText(text) - 8, rect.MidY - (font.Metrics.Ascent + font.Metrics.Descent) / 2, font, selected ? Color("#107C41") : Color("#555555"));
            }
            canvas.Restore();
        }
        Fill(canvas, new SKRect(0, 0, (float)GridViewport.RowHeaderWidth, (float)GridViewport.ColumnHeaderHeight), header);
        using var corner = new SKPath(); corner.MoveTo(32, 6); corner.LineTo(32, 20); corner.LineTo(18, 20); corner.Close(); _paint.Color = Color("#B8B8B8"); canvas.DrawPath(corner, _paint);
    }
}
