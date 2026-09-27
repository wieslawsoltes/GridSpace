using GridSpace.Core;
using GridSpace.Editing;
using GridSpace.Formulas;
using GridSpace.Layout;
using SkiaSharp;

namespace GridSpace.Skia;

public sealed partial class SpreadsheetRenderer
{
    private void DrawCell(SKCanvas canvas, SpreadsheetSession session, GridViewport viewport, CellAddress address, GridRect bounds)
    {
        LastRenderedCellCount++; var cell = session.Sheet.Get(address); var rect = Rect(bounds);
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
                var lineHeight = font.Spacing; var wrapped = Wrap(text, font, Math.Max(1, rect.Width - 10)).Take(128).ToArray(); baseline = rect.Top + 3 - font.Metrics.Ascent;
                foreach (var part in wrapped) { if (baseline + font.Metrics.Ascent > rect.Bottom) break; Text(canvas, part, rect.Left + 5, baseline, font, Color(cell.Style.Foreground)); baseline += lineHeight; }
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
            using var triangle = new SKPath(); var x = value.IsError ? rect.Left : rect.Right;
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
            using var font = new SKFont(Typeface(entry.Value.Style), (float)(entry.Value.Style.FontSize * 96 / 72));
            var text = NumberFormatter.Format(session.Calculation.Evaluate(session.Sheet, address), entry.Value.Style.NumberFormat); width = Math.Max(width, font.MeasureText(text) + 14);
        }
        return Math.Min(1000, width);
    }
}
