using GridSpace.Core;
using GridSpace.Editing;
using GridSpace.Formulas;
using GridSpace.Layout;
using SkiaSharp;

namespace GridSpace.Skia;

public sealed partial class SpreadsheetRenderer
{
    private ConditionalFormattingEngine? _conditional;
    private ConditionalFormattingEngine Conditions(SpreadsheetSession session)
    {
        if (_conditional?.Calculation != session.Calculation) _conditional = new ConditionalFormattingEngine(session.Calculation);
        return _conditional;
    }

    private void DrawCell(SKCanvas canvas, SpreadsheetSession session, GridViewport viewport, CellAddress address, GridRect bounds)
    {
        LastRenderedCellCount++;
        var cell = session.Sheet.Get(address);
        var value = session.Calculation.Evaluate(session.Sheet, address);
        var conditional = Conditions(session).Evaluate(session.Sheet, address, value);
        var style = conditional.Style;
        var rect = Rect(bounds);
        Fill(canvas, rect, Color(style.Background, "#FFFFFF"));
        if (session.Sheet.ShowGridLines || style.Border) Stroke(canvas, rect, style.Border ? Color("#808080") : Color("#E1E1E1"));
        if (conditional.DataBar is { } bar)
        {
            var width = Math.Max(0, rect.Width - 6);
            var top = rect.Top + 3; var bottom = rect.Bottom - 3;
            if (bottom > top && bar.End > bar.Start)
                Fill(canvas, new SKRect(rect.Left + 3 + (float)bar.Start * width, top, rect.Left + 3 + (float)bar.End * width, bottom), Color(bar.Color).WithAlpha(145));
            if (bar.Axis > 0 && bar.Axis < 1)
            {
                var x = rect.Left + 3 + (float)bar.Axis * width;
                Line(canvas, x, top, x, bottom, Color("#888888"));
            }
        }
        var text = session.Sheet.ShowFormulas && cell.IsFormula ? cell.Input : conditional.HideValue ? "" : NumberFormatter.Format(value, style.NumberFormat);
        var padding = 0f;
        if (PivotOutlineGeometry.TryGetCell(session.Sheet, address, out _, out var outline))
        {
            padding = (float)((16 + 14 * outline.Indent) * viewport.Zoom);
            if (outline.Group is not null)
            {
                var toggle = Rect(PivotOutlineGeometry.ToggleBounds(bounds, outline, viewport.Zoom));
                Fill(canvas, toggle, SKColors.White);
                Stroke(canvas, toggle, Color("#708078"));
                var inset = (float)(3 * viewport.Zoom);
                Line(canvas, toggle.Left + inset, toggle.MidY, toggle.Right - inset, toggle.MidY, Color("#31513F"));
                if (!outline.Expanded) Line(canvas, toggle.MidX, toggle.Top + inset, toggle.MidX, toggle.Bottom - inset, Color("#31513F"));
            }
        }
        if (text.Length > 0)
        {
            canvas.Save(); canvas.ClipRect(new SKRect(Math.Min(rect.Right - 3, rect.Left + 3 + padding), rect.Top + 1, rect.Right - 3, rect.Bottom - 1));
            using var font = new SKFont(Typeface(style), (float)(style.FontSize * 96 / 72 * viewport.Zoom));
            var width = font.MeasureText(text);
            var alignment = style.Alignment == CellAlignment.General ? value.Kind == ValueKind.Number ? CellAlignment.Right : CellAlignment.Left : style.Alignment;
            var x = alignment == CellAlignment.Right ? rect.Right - width - 5 : alignment == CellAlignment.Center ? rect.MidX - width / 2 : rect.Left + 5 + padding;
            var baseline = rect.MidY - (font.Metrics.Ascent + font.Metrics.Descent) / 2;
            if (style.Wrap)
            {
                var lineHeight = font.Spacing;
                baseline = rect.Top + 3 - font.Metrics.Ascent;
                foreach (var part in Wrap(text, font, Math.Max(1, rect.Width - 10 - padding)).Take(128))
                {
                    if (baseline + font.Metrics.Ascent > rect.Bottom) break;
                    Text(canvas, part, rect.Left + 5 + padding, baseline, font, Color(style.Foreground)); baseline += lineHeight;
                }
            }
            else
            {
                if (value.Kind == ValueKind.Number && width > rect.Width - 10 - padding) { text = new string('#', Math.Clamp((int)((rect.Width - 10 - padding) / Math.Max(1, font.MeasureText("#"))), 1, 100)); x = rect.Left + 5 + padding; }
                Text(canvas, text, x, baseline, font, Color(style.Foreground));
                if (style.Underline) Line(canvas, x, baseline + 2, Math.Min(rect.Right - 3, x + width), baseline + 2, Color(style.Foreground));
            }
            canvas.Restore();
        }
        if (!string.IsNullOrEmpty(cell.Note) || value.IsError)
        {
            using var triangle = new SKPath(); var x = value.IsError ? rect.Left : rect.Right;
            triangle.MoveTo(x, rect.Top); triangle.LineTo(x + (value.IsError ? 6 : -6), rect.Top); triangle.LineTo(x, rect.Top + 6); triangle.Close();
            _paint.Color = value.IsError ? Color("#107C41") : Color("#AD2E24"); canvas.DrawPath(triangle, _paint);
        }
        if (CellRange.TryParse(session.Sheet.FilterRange, out var filterRange) && address.Row == filterRange.Top && address.Column >= filterRange.Left && address.Column <= filterRange.Right)
        {
            var active = session.Sheet.Filters.Any(f => f.Column == address.Column);
            var box = new SKRect(rect.Right - 16, rect.Bottom - 17, rect.Right - 2, rect.Bottom - 3);
            Fill(canvas, box, Color(active ? "#DCEDE2" : "#F7F7F7")); Stroke(canvas, box, Color(active ? "#107C41" : "#B8B8B8"));
            using var icon = new SKPath();
            icon.MoveTo(box.Left + 3, box.Top + 4); icon.LineTo(box.Right - 3, box.Top + 4);
            if (active) { icon.LineTo(box.MidX + 1, box.Top + 8); icon.LineTo(box.MidX + 1, box.Bottom - 2); icon.LineTo(box.MidX - 1, box.Bottom - 1); icon.LineTo(box.MidX - 1, box.Top + 8); }
            else icon.LineTo(box.MidX, box.Top + 9);
            icon.Close(); _paint.Color = Color(active ? "#107C41" : "#444444"); canvas.DrawPath(icon, _paint);
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
                if (line.Length > 0 && font.MeasureText(candidate) > width) { yield return line; line = word; } else line = candidate;
            }
            yield return line;
        }
    }
    public double MeasureColumn(SpreadsheetSession session, int column)
    {
        var width = 24d;
        foreach (var entry in session.Sheet.Cells)
        {
            var address = CellAddress.Parse(entry.Key); if (address.Column != column) continue;
            var value = session.Calculation.Evaluate(session.Sheet, address);
            var style = Conditions(session).Evaluate(session.Sheet, address, value).Style;
            using var font = new SKFont(Typeface(style), (float)(style.FontSize * 96 / 72));
            var text = NumberFormatter.Format(value, style.NumberFormat);
            var indentation = PivotOutlineGeometry.TryGetCell(session.Sheet, address, out _, out var outline) ? 16 + 14 * outline.Indent : 0;
            width = Math.Max(width, font.MeasureText(text) + 14 + indentation);
        }
        return Math.Min(1000, width);
    }
}
