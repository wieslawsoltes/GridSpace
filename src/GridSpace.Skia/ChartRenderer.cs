using GridSpace.Core;
using GridSpace.Formulas;
using GridSpace.Layout;
using SkiaSharp;

namespace GridSpace.Skia;

/// <summary>Reusable multi-series chart renderer. Data snapshots survive selection, pan, zoom and chart movement.</summary>
public sealed class ChartRenderer : IDisposable
{
    private readonly TypefaceCatalog _fonts;
    private readonly SKPaint _fill = new() { IsAntialias = true };
    private readonly SKPaint _stroke = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
    private bool _disposed;
    public ChartDataCache Data { get; } = new();
    public int LastRenderedPoints { get; private set; }
    public ChartRenderer(TypefaceCatalog fonts) => _fonts = fonts;

    public void Render(SKCanvas canvas, Workbook book, Worksheet host, ChartSpec spec,
        CalculationEngine calculation, GridRect bounds, double zoom = 1)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        LastRenderedPoints = 0;
        canvas.Save();
        try
        {
            canvas.Translate((float)bounds.X, (float)bounds.Y);
            canvas.Scale((float)zoom);
            var outer = new SKRect(0, 0, (float)spec.Width, (float)spec.Height);
            Fill(canvas, new(3, 3, outer.Right + 3, outer.Bottom + 3), new SKColor(0, 0, 0, 20));
            Fill(canvas, outer, Ink(spec.Background)); Stroke(canvas, outer, Ink("#CACACA"));
            canvas.ClipRect(outer);
            using var title = Font(16, true);
            using var text = Font(11);
            using var small = Font(10);
            var heading = Ellipsis(spec.Title, title, Math.Max(20, outer.Width - 24));
            Text(canvas, heading, outer.MidX - title.MeasureText(heading) / 2, 29, title, Ink(spec.Foreground));
            ChartData data;
            try { data = Data.Get(book, host, spec, calculation); }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or FormatException)
            {
                Text(canvas, error.Message, 15, 60, text, Ink("#A4262C")); return;
            }
            var active = Enumerable.Range(0, data.Series.Count).Where(i => i >= spec.Series.Count || spec.Series[i].Visible).ToArray();
            if (active.Length == 0 || data.Categories.Length == 0)
            {
                Text(canvas, "Select a data range with categories and numeric series.", 15, 60, text, Ink("#666666")); return;
            }
            var cartesian = spec.Kind is not ChartKind.Pie and not ChartKind.Doughnut and not ChartKind.Radar;
            var plot = new SKRect(cartesian ? 65 : 24, 49, outer.Right - 22, outer.Bottom - 43);
            if (cartesian && spec.ValueAxisTitle.Length > 0) plot.Left += 14;
            if (cartesian && spec.CategoryAxisTitle.Length > 0) plot.Bottom -= 16;
            switch (spec.Legend)
            {
                case ChartLegendPosition.Bottom: plot.Bottom -= 32; break;
                case ChartLegendPosition.Top: plot.Top += 30; break;
                case ChartLegendPosition.Right: plot.Right -= Math.Min(125, outer.Width * .27f); break;
                case ChartLegendPosition.Left: plot.Left += Math.Min(110, outer.Width * .25f); break;
            }
            if (plot.Width < 20 || plot.Height < 20) return;
            if (spec.Kind is ChartKind.Pie or ChartKind.Doughnut) DrawPie(canvas, data, spec, active, plot, text);
            else if (spec.Kind == ChartKind.Radar) DrawRadar(canvas, data, spec, active, plot, small);
            else DrawCartesian(canvas, data, spec, active, plot, text, small);
            DrawLegend(canvas, data, spec, active, outer, text);
            if (cartesian && spec.CategoryAxisTitle.Length > 0)
                Text(canvas, spec.CategoryAxisTitle, plot.MidX - text.MeasureText(spec.CategoryAxisTitle) / 2, plot.Bottom + 38, text, Ink(spec.Foreground));
            if (cartesian && spec.ValueAxisTitle.Length > 0)
            {
                canvas.Save(); canvas.Translate(17, plot.MidY); canvas.RotateDegrees(-90);
                Text(canvas, spec.ValueAxisTitle, -text.MeasureText(spec.ValueAxisTitle) / 2, 0, text, Ink(spec.Foreground)); canvas.Restore();
            }
        }
        finally { canvas.Restore(); }
    }

    private void DrawCartesian(SKCanvas canvas, ChartData data, ChartSpec spec, int[] active, SKRect plot, SKFont text, SKFont small)
    {
        var count = data.Categories.Length;
        var categorical = spec.Kind is ChartKind.Column or ChartKind.Bar or ChartKind.Combo;
        var displayed = categorical ? Math.Min(512, count) : count;
        var stacked = spec.Grouping != ChartGrouping.Clustered && spec.Kind is ChartKind.Column or ChartKind.Bar or ChartKind.Area;
        var percent = stacked && spec.Grouping == ChartGrouping.PercentStacked;
        var positive = new double[displayed]; var negative = new double[displayed];
        foreach (var index in active)
            for (var p = 0; p < displayed; p++)
                if (data.Series[index].Values[p] is { } value)
                { if (value >= 0) positive[p] += value; else negative[p] += value; }
        double Value(int series, int p)
        {
            var value = data.Series[series].Values[p] ?? 0;
            if (!percent) return value;
            var denominator = value >= 0 ? positive[p] : -negative[p];
            return denominator == 0 ? 0 : value / denominator;
        }
        var primary = active.Where(i => i >= spec.Series.Count || !spec.Series[i].SecondaryAxis).ToArray();
        var secondary = active.Except(primary).ToArray();
        var range = Limits(primary.Length == 0 ? active : primary);
        var secondaryRange = Limits(secondary.Length == 0 ? active : secondary, false);
        if (spec.Minimum is { } min) range.Min = min;
        if (spec.Maximum is { } max) range.Max = max;
        if (range.Max <= range.Min) range.Max = range.Min + 1;
        if (secondary.Length > 0) plot.Right -= 36;
        var xNumeric = data.XValues.Where(v => v is not null).Select(v => v!.Value).ToArray();
        var xRange = spec.Kind == ChartKind.Scatter && xNumeric.Length > 0 ? NiceRange(xNumeric.Min(), xNumeric.Max(), false) : (Min: 0d, Max: (double)Math.Max(1, displayed - 1));
        float Y(double value, bool second = false)
        {
            var limits = second ? secondaryRange : range;
            return plot.Bottom - (float)((value - limits.Min) / (limits.Max - limits.Min) * plot.Height);
        }
        float VX(double value) => plot.Left + (float)((value - range.Min) / (range.Max - range.Min) * plot.Width);
        float X(int p) => spec.Kind == ChartKind.Scatter
            ? plot.Left + (float)(((data.XValues[p] ?? xRange.Min) - xRange.Min) / Math.Max(double.Epsilon, xRange.Max - xRange.Min) * plot.Width)
            : categorical ? plot.Left + plot.Width / displayed * (p + .5f) : plot.Left + plot.Width * p / Math.Max(1, displayed - 1);
        var format = percent ? "0%" : spec.ValueFormat;
        for (var i = 0; i <= 5; i++)
        {
            var v = range.Min + (range.Max - range.Min) * i / 5;
            var label = NumberFormatter.Format(CalcValue.Num(v), format);
            if (spec.Kind == ChartKind.Bar)
            {
                var x = VX(v);
                if (spec.ShowGridLines) Line(canvas, x, plot.Top, x, plot.Bottom, Ink("#E7E6E6"));
                Text(canvas, label, x - small.MeasureText(label) / 2, plot.Bottom + 17, small, Ink("#666666"));
            }
            else
            {
                var y = Y(v);
                if (spec.ShowGridLines) Line(canvas, plot.Left, y, plot.Right, y, Ink("#E7E6E6"));
                Text(canvas, label, plot.Left - small.MeasureText(label) - 7, y + 3, small, Ink("#666666"));
                if (secondary.Length > 0)
                {
                    var secondaryValue = secondaryRange.Min + (secondaryRange.Max - secondaryRange.Min) * i / 5;
                    Text(canvas, NumberFormatter.Format(CalcValue.Num(secondaryValue), spec.ValueFormat), plot.Right + 5, y + 3, small, Ink("#666666"));
                }
            }
        }
        if (spec.Kind == ChartKind.Scatter)
        {
            for (var i = 0; i <= 5; i++)
            {
                var v = xRange.Min + (xRange.Max - xRange.Min) * i / 5;
                var label = NumberFormatter.Format(CalcValue.Num(v), "#,##0.##");
                var x = plot.Left + plot.Width * i / 5;
                Text(canvas, label, x - small.MeasureText(label) / 2, plot.Bottom + 18, small, Ink("#666666"));
            }
        }
        else
        {
            var stride = Math.Max(1, (int)Math.Ceiling(displayed / Math.Max(1, (spec.Kind == ChartKind.Bar ? plot.Height : plot.Width) / (spec.Kind == ChartKind.Bar ? 20 : 72))));
            for (var p = 0; p < displayed; p += stride)
            {
                var label = Ellipsis(data.Categories[p], small, spec.Kind == ChartKind.Bar ? plot.Left - 15 : Math.Max(18, plot.Width / Math.Min(displayed, 8)));
                if (spec.Kind == ChartKind.Bar) Text(canvas, label, plot.Left - small.MeasureText(label) - 7, plot.Top + plot.Height / displayed * (p + .5f) + 3, small, Ink("#666666"));
                else Text(canvas, label, X(p) - small.MeasureText(label) / 2, plot.Bottom + 18, small, Ink("#666666"));
            }
        }
        Line(canvas, plot.Left, plot.Bottom, plot.Right, plot.Bottom, Ink("#BFBFBF"));
        var bottomsPositive = new double[displayed]; var bottomsNegative = new double[displayed];
        var columnCount = active.Count(i => Kind(i) == ChartKind.Column);
        var columnOrdinal = 0;
        canvas.Save(); canvas.ClipRect(new(plot.Left - 1, plot.Top - 1, plot.Right + 1, plot.Bottom + 1));
        try
        {
            foreach (var seriesIndex in active)
            {
                var kind = Kind(seriesIndex);
                var color = SeriesColor(spec, seriesIndex);
                var second = seriesIndex < spec.Series.Count && spec.Series[seriesIndex].SecondaryAxis;
                var baseLine = new double[displayed]; var topLine = new double[displayed];
                for (var p = 0; p < displayed; p++)
                {
                    var value = Value(seriesIndex, p);
                    baseLine[p] = stacked ? value >= 0 ? bottomsPositive[p] : bottomsNegative[p] : 0;
                    topLine[p] = baseLine[p] + value;
                    if (stacked) { if (value >= 0) bottomsPositive[p] = topLine[p]; else bottomsNegative[p] = topLine[p]; }
                }
                if (kind is ChartKind.Column or ChartKind.Bar)
                {
                    var slot = (kind == ChartKind.Bar ? plot.Height : plot.Width) / displayed;
                    var groupWidth = slot / (1 + spec.GapWidth / 100f);
                    var barWidth = stacked ? groupWidth : groupWidth / Math.Max(1, kind == ChartKind.Bar ? active.Length : columnCount);
                    var ordinal = kind == ChartKind.Bar ? Array.IndexOf(active, seriesIndex) : columnOrdinal++;
                    for (var p = 0; p < displayed; p++)
                    {
                        if (data.Series[seriesIndex].Values[p] is null) continue;
                        SKRect bar;
                        if (kind == ChartKind.Bar)
                        {
                            var y = plot.Top + slot * (p + .5f) - groupWidth / 2 + (stacked ? 0 : ordinal * barWidth);
                            bar = new(Math.Min(VX(baseLine[p]), VX(topLine[p])), y, Math.Max(VX(baseLine[p]), VX(topLine[p])), y + Math.Max(.5f, barWidth));
                        }
                        else
                        {
                            var x = X(p) - groupWidth / 2 + (stacked ? 0 : ordinal * barWidth);
                            bar = new(x, Math.Min(Y(baseLine[p], second), Y(topLine[p], second)), x + Math.Max(.5f, barWidth), Math.Max(Y(baseLine[p], second), Y(topLine[p], second)));
                        }
                        Fill(canvas, bar, color); LastRenderedPoints++;
                        if (spec.ShowDataLabels && displayed <= 100)
                        {
                            var label = NumberFormatter.Format(CalcValue.Num(Value(seriesIndex, p)), format);
                            Text(canvas, label, kind == ChartKind.Bar ? bar.Right + 3 : bar.MidX - small.MeasureText(label) / 2,
                                kind == ChartKind.Bar ? bar.MidY + 3 : bar.Top - 4, small, Ink(spec.Foreground));
                        }
                    }
                }
                else
                {
                    var indexes = Envelope(data.Series[seriesIndex].Values, 2048);
                    using var path = new SKPath();
                    var open = false;
                    foreach (var p in indexes)
                    {
                        if (data.Series[seriesIndex].Values[p] is null || spec.Kind == ChartKind.Scatter && data.XValues[p] is null)
                        { open = false; continue; }
                        var x = X(p); var y = Y(topLine[p], second);
                        if (kind != ChartKind.Scatter)
                        {
                            if (!open) path.MoveTo(x, y); else path.LineTo(x, y);
                            open = true;
                        }
                        if ((spec.ShowMarkers && displayed <= 256) || kind == ChartKind.Scatter)
                        { _fill.Color = color; canvas.DrawCircle(x, y, kind == ChartKind.Scatter ? 3 : 2.7f, _fill); }
                        if (spec.ShowDataLabels && displayed <= 60)
                        {
                            var label = NumberFormatter.Format(CalcValue.Num(Value(seriesIndex, p)), format);
                            Text(canvas, label, x - small.MeasureText(label) / 2, y - 7, small, Ink(spec.Foreground));
                        }
                        LastRenderedPoints++;
                    }
                    if (kind == ChartKind.Area)
                    {
                        // Each contiguous run owns its baseline polygon; gaps never become fabricated zero points.
                        var run = new List<int>();
                        foreach (var p in indexes.Append(-1))
                        {
                            if (p >= 0 && data.Series[seriesIndex].Values[p] is not null) { run.Add(p); continue; }
                            if (run.Count == 0) continue;
                            using var area = new SKPath(); area.MoveTo(X(run[0]), Y(baseLine[run[0]], second));
                            foreach (var index in run) area.LineTo(X(index), Y(topLine[index], second));
                            for (var i = run.Count - 1; i >= 0; i--) area.LineTo(X(run[i]), Y(baseLine[run[i]], second));
                            area.Close(); _fill.Color = color.WithAlpha(150); canvas.DrawPath(area, _fill); run.Clear();
                        }
                    }
                    if (kind != ChartKind.Scatter) { _stroke.Color = color; _stroke.StrokeWidth = 2; canvas.DrawPath(path, _stroke); }
                }
            }
        }
        finally { canvas.Restore(); }
        if (displayed < count) Text(canvas, $"Preview: first {displayed:N0} of {count:N0} categories; full data retained.", plot.Left, plot.Top - 5, small, Ink("#777777"));

        ChartKind Kind(int i) => spec.Kind == ChartKind.Combo ? i < spec.Series.Count && spec.Series[i].Kind is { } kind ? kind
            : i == active[^1] ? ChartKind.Line : ChartKind.Column : spec.Kind;
        (double Min, double Max) Limits(int[] series, bool useStack = true)
        {
            if (stacked && useStack) return percent ? (negative.Any(v => v < 0) ? -1 : 0, positive.Any(v => v > 0) ? 1 : 0)
                : NiceRange(negative.Min(), positive.Max());
            var values = series.SelectMany(i => data.Series[i].Values.Take(displayed)).Where(v => v is not null).Select(v => v!.Value).ToArray();
            return values.Length == 0 ? (0, 1) : NiceRange(values.Min(), values.Max());
        }
    }

    private void DrawPie(SKCanvas canvas, ChartData data, ChartSpec spec, int[] active, SKRect plot, SKFont font)
    {
        var diameter = Math.Min(plot.Width, plot.Height);
        var radius = diameter / 2;
        var rings = spec.Kind == ChartKind.Pie ? 1 : active.Length;
        var inner = spec.Kind == ChartKind.Pie ? 0 : radius * spec.HoleSize / 100f;
        var thickness = (radius - inner) / rings;
        for (var ring = 0; ring < rings; ring++)
        {
            var values = data.Series[active[ring]].Values;
            var total = values.Where(v => v is not null).Sum(v => Math.Abs(v!.Value));
            if (total <= 0) continue;
            var rr = radius - ring * thickness - (spec.Kind == ChartKind.Pie ? 0 : thickness / 2);
            var circle = new SKRect(plot.MidX - rr, plot.MidY - rr, plot.MidX + rr, plot.MidY + rr);
            float angle = -90;
            for (var p = 0; p < values.Length; p++)
            {
                if (values[p] is not { } value || value == 0) continue;
                var sweep = (float)(Math.Abs(value) / total * 360);
                var color = Ink(ChartDataResolver.Palette[p % ChartDataResolver.Palette.Length]);
                if (spec.Kind == ChartKind.Pie) { _fill.Color = color; canvas.DrawArc(circle, angle, sweep, true, _fill); }
                else { _stroke.Color = color; _stroke.StrokeWidth = Math.Max(.5f, thickness - 2); canvas.DrawArc(circle, angle, sweep, false, _stroke); }
                if (spec.ShowDataLabels && values.Length <= 24 && ring == 0)
                {
                    var radians = (angle + sweep / 2) * Math.PI / 180;
                    var label = $"{Math.Abs(value) / total:P0}";
                    Text(canvas, label, plot.MidX + (float)Math.Cos(radians) * rr * .72f - font.MeasureText(label) / 2,
                        plot.MidY + (float)Math.Sin(radians) * rr * .72f + 4, font, SKColors.White);
                }
                angle += sweep; LastRenderedPoints++;
            }
        }
    }

    private void DrawRadar(SKCanvas canvas, ChartData data, ChartSpec spec, int[] active, SKRect plot, SKFont font)
    {
        var count = Math.Min(256, data.Categories.Length);
        if (count < 3) { Text(canvas, "Radar requires at least three categories.", plot.Left, plot.MidY, font, Ink("#666666")); return; }
        var radius = Math.Min(plot.Width, plot.Height) / 2 - 15;
        var numbers = active.SelectMany(i => data.Series[i].Values).Where(v => v is not null).Select(v => v!.Value).ToArray();
        var limits = NiceRange(numbers.DefaultIfEmpty(0).Min(), numbers.DefaultIfEmpty(1).Max());
        if (spec.Minimum is { } minimum) limits.Min = minimum;
        if (spec.Maximum is { } maximum) limits.Max = maximum;
        if (limits.Max <= limits.Min) limits.Max = limits.Min + 1;
        if (count < data.Categories.Length)
            Text(canvas, $"Showing {count:N0} of {data.Categories.Length:N0} categories", plot.Left, plot.Bottom + 18, font, Ink("#666666"));
        SKPoint Point(int index, double value)
        {
            var angle = -Math.PI / 2 + index * 2 * Math.PI / count;
            return new(plot.MidX + (float)(Math.Cos(angle) * radius * value), plot.MidY + (float)(Math.Sin(angle) * radius * value));
        }
        for (var ring = 1; ring <= 5; ring++)
        {
            using var path = new SKPath();
            for (var p = 0; p < count; p++) { var point = Point(p, ring / 5d); if (p == 0) path.MoveTo(point); else path.LineTo(point); }
            path.Close(); _stroke.Color = Ink("#E1E1E1"); _stroke.StrokeWidth = 1; canvas.DrawPath(path, _stroke);
        }
        for (var p = 0; p < count; p++)
        {
            var tip = Point(p, 1); Line(canvas, plot.MidX, plot.MidY, tip.X, tip.Y, Ink("#E1E1E1"));
            if (count <= 24) { var label = Ellipsis(data.Categories[p], font, 75); var t = Point(p, 1.14); Text(canvas, label, t.X - font.MeasureText(label) / 2, t.Y + 3, font, Ink(spec.Foreground)); }
        }
        foreach (var i in active)
        {
            using var path = new SKPath();
            for (var p = 0; p < count; p++)
            {
                var value = data.Series[i].Values[p] ?? 0;
                var point = Point(p, Math.Clamp((value - limits.Min) / (limits.Max - limits.Min), 0, 1));
                if (p == 0) path.MoveTo(point); else path.LineTo(point); LastRenderedPoints++;
                if (spec.ShowMarkers) { _fill.Color = SeriesColor(spec, i); canvas.DrawCircle(point, 2.5f, _fill); }
                if (spec.ShowDataLabels && count <= 24 && data.Series[i].Values[p] is not null)
                    Text(canvas, NumberFormatter.Format(CalcValue.Num(value), spec.ValueFormat), point.X + 4, point.Y - 4, font, SeriesColor(spec, i));
            }
            path.Close(); _stroke.Color = SeriesColor(spec, i); _stroke.StrokeWidth = 2; canvas.DrawPath(path, _stroke);
        }
    }

    private void DrawLegend(SKCanvas canvas, ChartData data, ChartSpec spec, int[] active, SKRect outer, SKFont font)
    {
        if (spec.Legend == ChartLegendPosition.None) return;
        var circular = spec.Kind is ChartKind.Pie or ChartKind.Doughnut;
        var labels = circular ? data.Categories : active.Select(i => data.Series[i].Name).ToArray();
        var vertical = spec.Legend is ChartLegendPosition.Right or ChartLegendPosition.Left;
        var x = spec.Legend == ChartLegendPosition.Right ? outer.Right - Math.Min(120, outer.Width * .26f) : 16;
        var y = spec.Legend == ChartLegendPosition.Bottom ? outer.Bottom - 14 : 53;
        if (vertical) y = 65;
        for (var i = 0; i < labels.Length; i++)
        {
            var label = Ellipsis(labels[i], font, vertical ? 92 : 135);
            var width = font.MeasureText(label) + 30;
            if (!vertical && x + width > outer.Right - 10 || vertical && y > outer.Bottom - 20) break;
            var color = circular ? Ink(ChartDataResolver.Palette[i % ChartDataResolver.Palette.Length]) : SeriesColor(spec, active[i]);
            Fill(canvas, new(x, y - 8, x + 11, y + 2), color);
            Text(canvas, label, x + 16, y + 1, font, Ink(spec.Foreground));
            if (vertical) y += 19; else x += width;
        }
    }

    /// <summary>Ordered extrema-preserving line sampling with explicit gaps, bounded by about twice the pixel budget.</summary>
    public static int[] Envelope(IReadOnlyList<double?> values, int budget)
    {
        if (budget < 4) throw new ArgumentOutOfRangeException(nameof(budget));
        if (values.Count <= budget) return Enumerable.Range(0, values.Count).ToArray();
        var result = new SortedSet<int> { 0, values.Count - 1 };
        var block = Math.Max(1, (int)Math.Ceiling(values.Count / (budget / 3d)));
        for (var first = 0; first < values.Count; first += block)
        {
            var end = Math.Min(values.Count, first + block); var min = -1; var max = -1; var gap = -1;
            for (var i = first; i < end; i++)
            {
                if (values[i] is null) { gap = i; continue; }
                if (min < 0 || values[i] < values[min]) min = i;
                if (max < 0 || values[i] > values[max]) max = i;
            }
            if (min >= 0) result.Add(min); if (max >= 0) result.Add(max); if (gap >= 0) result.Add(gap);
        }
        return result.ToArray();
    }

    private static (double Min, double Max) NiceRange(double minimum, double maximum, bool zero = true)
    {
        if (zero) { minimum = Math.Min(0, minimum); maximum = Math.Max(0, maximum); }
        if (minimum == maximum) { minimum -= minimum == 0 ? 0 : Math.Abs(minimum) * .1; maximum += maximum == 0 ? 1 : Math.Abs(maximum) * .1; }
        var rough = (maximum - minimum) / 5; var power = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(double.Epsilon, rough))));
        var fraction = rough / power; var step = (fraction <= 1 ? 1 : fraction <= 2 ? 2 : fraction <= 5 ? 5 : 10) * power;
        if (!double.IsFinite(step) || step <= 0) return (0, 1);
        return (Math.Floor(minimum / step) * step, Math.Ceiling(maximum / step) * step);
    }
    private SKFont Font(float size, bool bold = false) => new(_fonts.Resolve(CellStyle.Default with { Bold = bold }), size);
    private static SKColor Ink(string color) => SKColor.TryParse(color, out var value) ? value : SKColors.Black;
    private static SKColor SeriesColor(ChartSpec spec, int index) => Ink(index < spec.Series.Count ? spec.Series[index].Color : ChartDataResolver.Palette[index % ChartDataResolver.Palette.Length]);
    private void Fill(SKCanvas canvas, SKRect rect, SKColor color) { _fill.Color = color; canvas.DrawRect(rect, _fill); }
    private void Stroke(SKCanvas canvas, SKRect rect, SKColor color) { _stroke.Color = color; _stroke.StrokeWidth = 1; canvas.DrawRect(rect, _stroke); }
    private void Line(SKCanvas canvas, float x, float y, float right, float bottom, SKColor color) { _stroke.Color = color; _stroke.StrokeWidth = 1; canvas.DrawLine(x, y, right, bottom, _stroke); }
    private void Text(SKCanvas canvas, string value, float x, float y, SKFont font, SKColor color) { _fill.Color = color; canvas.DrawText(value, x, y, font, _fill); }
    private static string Ellipsis(string value, SKFont font, float width)
    {
        if (font.MeasureText(value) <= width) return value;
        var low = 0; var high = value.Length;
        while (low < high) { var mid = (low + high + 1) / 2; if (font.MeasureText(value[..mid] + "…") <= width) low = mid; else high = mid - 1; }
        return value[..low] + "…";
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; Data.Clear(); _fill.Dispose(); _stroke.Dispose();
    }
}
