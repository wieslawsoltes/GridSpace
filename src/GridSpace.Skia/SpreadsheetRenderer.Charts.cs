using GridSpace.Core;
using GridSpace.Editing;
using GridSpace.Formulas;
using GridSpace.Layout;
using SkiaSharp;

namespace GridSpace.Skia;

public sealed partial class SpreadsheetRenderer
{
    private void DrawCharts(SKCanvas canvas, SpreadsheetSession session, GridViewport viewport, GridPane pane)
    {
        foreach (var chart in session.Sheet.Charts)
        {
            var bounds = new GridRect(pane.OriginX + viewport.Columns.Position(chart.Column) * viewport.Zoom, pane.OriginY + viewport.Rows.Position(chart.Row) * viewport.Zoom, Math.Clamp(chart.Width, 100, 4000) * viewport.Zoom, Math.Clamp(chart.Height, 80, 4000) * viewport.Zoom);
            if (!bounds.Intersects(pane.Clip)) continue;
            var rect = Rect(bounds);
            Fill(canvas, new SKRect(rect.Left + 3, rect.Top + 3, rect.Right + 3, rect.Bottom + 3), new SKColor(0, 0, 0, 16));
            Fill(canvas, rect, SKColors.White); Stroke(canvas, rect, Color("#D6D6D6"));
            canvas.Save(); canvas.ClipRect(rect);
            using var titleFont = new SKFont(Typeface(new CellStyle { Bold = true }), (float)(15 * viewport.Zoom));
            using var labelFont = new SKFont(Typeface(CellStyle.Default), (float)(10 * viewport.Zoom));
            Text(canvas, chart.Title, rect.Left + 20, rect.Top + 28, titleFont, Color("#333333"));
            var range = CellRange.Parse(chart.Range);
            var points = Enumerable.Range(range.Top + 1, Math.Min(64, Math.Max(0, range.Bottom - range.Top))).Select(r => (Label: session.Calculation.Evaluate(session.Sheet, new CellAddress(r, range.Left)).ToString(), Value: session.Calculation.Evaluate(session.Sheet, new CellAddress(r, range.Right)))).Where(p => p.Value.Kind == ValueKind.Number && double.IsFinite(p.Value.Number)).ToArray();
            if (points.Length > 0)
            {
                var plot = new SKRect(rect.Left + 58, rect.Top + 52, rect.Right - 20, rect.Bottom - 40);
                if (chart.Kind == ChartKind.Pie)
                {
                    var total = points.Sum(p => Math.Max(0, p.Value.Number));
                    var diameter = Math.Min(plot.Width, plot.Height);
                    var pie = new SKRect(plot.MidX - diameter / 2, plot.MidY - diameter / 2, plot.MidX + diameter / 2, plot.MidY + diameter / 2);
                    float angle = -90;
                    for (var i = 0; total > 0 && i < points.Length; i++)
                    {
                        var sweep = (float)(Math.Max(0, points[i].Value.Number) / total * 360);
                        _paint.Color = SKColor.FromHsv((145 + i * 39) % 360, 55, 75);
                        canvas.DrawArc(pie, angle, sweep, true, _paint); angle += sweep;
                    }
                }
                else
                {
                    var min = Math.Min(0, points.Min(p => p.Value.Number));
                    var max = Math.Max(0, points.Max(p => p.Value.Number));
                    if (max <= min) max = min + 1;
                    float Y(double n) => plot.Bottom - (float)((n - min) / (max - min) * plot.Height);
                    float X(double n) => plot.Left + (float)((n - min) / (max - min) * plot.Width);
                    for (var tick = 0; tick <= 4; tick++)
                    {
                        var value = min + (max - min) * tick / 4;
                        var y = Y(value); Line(canvas, plot.Left, y, plot.Right, y, Color("#E8E8E8"));
                        var label = value.ToString("0,0", System.Globalization.CultureInfo.InvariantCulture); Text(canvas, label, plot.Left - labelFont.MeasureText(label) - 7, y + 4, labelFont, Color("#777777"));
                    }
                    using var line = new SKPath();
                    for (var i = 0; i < points.Length; i++)
                    {
                        var slot = plot.Width / points.Length; var x = plot.Left + slot * (i + .5f); var y = Y(points[i].Value.Number);
                        if (chart.Kind == ChartKind.Column) Fill(canvas, new SKRect(x - slot * .32f, Math.Min(y, Y(0)), x + slot * .32f, Math.Max(y, Y(0))), Color("#21A366"));
                        else if (chart.Kind == ChartKind.Bar)
                        {
                            var h = plot.Height / points.Length; var top = plot.Top + h * i;
                            Fill(canvas, new SKRect(Math.Min(X(0), X(points[i].Value.Number)), top + h * .15f, Math.Max(X(0), X(points[i].Value.Number)), top + h * .8f), Color("#21A366"));
                        }
                        else { if (i == 0) line.MoveTo(x, y); else line.LineTo(x, y); }
                        if (i % Math.Max(1, points.Length / 8) == 0 && chart.Kind != ChartKind.Bar) Text(canvas, points[i].Label, x - labelFont.MeasureText(points[i].Label) / 2, plot.Bottom + 18, labelFont, Color("#666666"));
                    }
                    if (chart.Kind == ChartKind.Line) { _line.Color = Color("#107C41"); _line.StrokeWidth = 2; canvas.DrawPath(line, _line); }
                }
            }
            canvas.Restore();
        }
    }
}
