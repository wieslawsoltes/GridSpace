using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using GridSpace.Core;
using GridSpace.Formulas;

namespace GridSpace.IO;

public static partial class XlsxWorkbook
{
    private static void ReadCharts(ZipArchive zip, string sheetPath, XElement worksheet, Worksheet sheet, List<string> warnings)
    {
        var drawingId = (string?)worksheet.Element(S + "drawing")?.Attribute(R + "id");
        if (drawingId is null || !Relationships(zip, sheetPath).TryGetValue(drawingId, out var drawingPath)) return;
        var drawing = Xml(zip, drawingPath); var rels = Relationships(zip, drawingPath);
        foreach (var anchor in drawing.Root!.Elements().Take(128))
        {
            var chartId = (string?)anchor.Descendants(C + "chart").FirstOrDefault()?.Attribute(R + "id");
            if (chartId is null || !rels.TryGetValue(chartId, out var chartPath)) { warnings.Add("Non-chart drawings are not imported."); continue; }
            try
            {
                var root = Xml(zip, chartPath).Root!;
                var native = root.Element(C + "extLst")?.Elements(C + "ext").FirstOrDefault(e => (string?)e.Attribute("uri") == ChartExtension)?.Element(Analytics + "chart");
                ChartSpec? spec = null;
                if (native is not null)
                {
                    if (native.Value.Length > 128 * 1024) throw new InvalidDataException("Chart metadata is oversized.");
                    spec = JsonSerializer.Deserialize<ChartSpec>(native.Value); spec?.Validate();
                }
                spec ??= ReadStandardChart(root, sheet, warnings);
                if (spec is null) continue;
                var from = anchor.Element(D + "from");
                spec.Column = ParseElementInt(from?.Element(D + "col")); spec.Row = ParseElementInt(from?.Element(D + "row"));
                spec.OffsetX = ParseElementDouble(from?.Element(D + "colOff")) / 9525; spec.OffsetY = ParseElementDouble(from?.Element(D + "rowOff")) / 9525;
                var ext = anchor.Element(D + "ext");
                if (ext is not null) { spec.Width = Number(ext.Attribute("cx"), 480 * 9525) / 9525; spec.Height = Number(ext.Attribute("cy"), 300 * 9525) / 9525; }
                else if (anchor.Element(D + "to") is { } to)
                {
                    double Position(bool rows, int index) => index * (rows ? 24d : 88d) +
                        (rows ? sheet.RowHeights : sheet.ColumnWidths).Where(p => p.Key < index).Sum(p => p.Value - (rows ? 24 : 88));
                    var col = ParseElementInt(to.Element(D + "col")); var row = ParseElementInt(to.Element(D + "row"));
                    spec.Width = Position(false, col) - Position(false, spec.Column) + ParseElementDouble(to.Element(D + "colOff")) / 9525 - spec.OffsetX;
                    spec.Height = Position(true, row) - Position(true, spec.Row) + ParseElementDouble(to.Element(D + "rowOff")) / 9525 - spec.OffsetY;
                }
                spec.Width = Math.Clamp(spec.Width, 120, 4000); spec.Height = Math.Clamp(spec.Height, 100, 4000);
                spec.Validate(); sheet.Charts.Add(spec);
            }
            catch (Exception error) when (error is ArgumentException or FormatException or JsonException or InvalidOperationException)
            { warnings.Add("An unsupported chart was skipped: " + error.Message); }
        }
    }

    private static int ParseElementInt(XElement? element) => int.TryParse(element?.Value, out var value) ? value : 0;
    private static double ParseElementDouble(XElement? element) => double.TryParse(element?.Value, System.Globalization.NumberStyles.Float,
        System.Globalization.CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? value : 0;

    private static ChartSpec? ReadStandardChart(XElement root, Worksheet host, List<string> warnings)
    {
        var plot = root.Element(C + "chart")?.Element(C + "plotArea");
        if (plot is null) return null;
        var types = new Dictionary<string, ChartKind> { ["barChart"] = ChartKind.Column, ["lineChart"] = ChartKind.Line, ["areaChart"] = ChartKind.Area,
            ["pieChart"] = ChartKind.Pie, ["doughnutChart"] = ChartKind.Doughnut, ["scatterChart"] = ChartKind.Scatter, ["radarChart"] = ChartKind.Radar };
        var groups = plot.Elements().Where(e => types.ContainsKey(e.Name.LocalName)).ToArray();
        if (groups.Length == 0) { warnings.Add("This chart type is not supported."); return null; }
        ChartKind Kind(XElement group) => group.Name.LocalName == "barChart" && (string?)group.Element(C + "barDir")?.Attribute("val") == "bar" ? ChartKind.Bar : types[group.Name.LocalName];
        var kind = groups.Select(Kind).Distinct().Count() > 1 ? ChartKind.Combo : Kind(groups[0]);
        var heading = ReadChartText(root.Element(C + "chart")?.Element(C + "title")?.Element(C + "tx"), host.Name, warnings);
        var spec = new ChartSpec { Kind = kind, Title = heading.Caption, TitleReference = heading.Reference };
        var primaryAxisIds = groups[0].Elements(C + "axId").Select(e => (string?)e.Attribute("val")).ToArray();
        var primaryAxes = primaryAxisIds.ToHashSet();
        CellRange? union = null;
        foreach (var group in groups)
        {
            foreach (var series in group.Elements(C + "ser"))
            {
                var valuesText = series.Element(C + (kind == ChartKind.Scatter ? "yVal" : "val"))?.Descendants(C + "f").FirstOrDefault()?.Value;
                var categoriesText = series.Element(C + (kind == ChartKind.Scatter ? "xVal" : "cat"))?.Descendants(C + "f").FirstOrDefault()?.Value;
                if (!TryChartVector(valuesText, host.Name, out var source, out var values) || !TryChartVector(categoriesText, source, out var categorySource, out var categories)
                    || source != categorySource || spec.SourceSheet is not null && spec.SourceSheet != source)
                { warnings.Add("A chart series with unsupported or mixed-sheet ranges was skipped."); continue; }
                spec.SourceSheet = source; spec.Categories ??= categories.ToString();
                var name = ReadChartText(series.Element(C + "tx"), source, warnings);
                var color = (string?)series.Element(C + "spPr")?.Descendants(A + "srgbClr").FirstOrDefault()?.Attribute("val");
                spec.Series.Add(new ChartSeries
                {
                    Name = name.Caption, NameReference = name.Reference, Values = values.ToString(), Color = color is { Length: 6 } ? "#" + color : ChartDataResolver.Palette[spec.Series.Count % ChartDataResolver.Palette.Length],
                    Kind = kind == ChartKind.Combo && Kind(group) is ChartKind.Column or ChartKind.Line or ChartKind.Area ? Kind(group) : null,
                    SecondaryAxis = !group.Elements(C + "axId").Any(e => primaryAxes.Contains((string?)e.Attribute("val")))
                });
                var left = Math.Min(values.Left, categories.Left); var top = Math.Min(values.Top, categories.Top);
                var right = Math.Max(values.Right, categories.Right); var bottom = Math.Max(values.Bottom, categories.Bottom);
                union = union is { } u ? new CellRange(new(Math.Min(u.Top, top), Math.Min(u.Left, left)), new(Math.Max(u.Bottom, bottom), Math.Max(u.Right, right)))
                    : new(new(top, left), new(bottom, right));
            }
        }
        if (spec.Series.Count == 0) return null;
        spec.Range = union!.Value.ToString(); spec.HasHeaders = false;
        var grouping = (string?)groups[0].Element(C + "grouping")?.Attribute("val");
        spec.Grouping = grouping == "stacked" ? ChartGrouping.Stacked : grouping == "percentStacked" ? ChartGrouping.PercentStacked : ChartGrouping.Clustered;
        spec.HoleSize = Int(groups[0].Element(C + "holeSize")?.Attribute("val"), 55);
        spec.GapWidth = Int(groups[0].Element(C + "gapWidth")?.Attribute("val"), 120);
        spec.ShowDataLabels = groups.SelectMany(g => g.Descendants(C + "showVal")).Any(e => Flag(e.Attribute("val")));
        var chart = root.Element(C + "chart")!;
        spec.PlotHiddenCells = chart.Element(C + "plotVisOnly") is { } visible && !Flag(visible.Attribute("val"));
        spec.Legend = chart.Element(C + "legend") is not { } legend ? ChartLegendPosition.None :
            (string?)legend.Element(C + "legendPos")?.Attribute("val") switch { "l" => ChartLegendPosition.Left, "r" => ChartLegendPosition.Right, "t" => ChartLegendPosition.Top, _ => ChartLegendPosition.Bottom };
        XElement? FindAxis(int ordinal, string fallback) => ordinal < primaryAxisIds.Length
            ? plot.Elements().FirstOrDefault(e => e.Name.LocalName is "catAx" or "valAx" or "dateAx" && (string?)e.Element(C + "axId")?.Attribute("val") == primaryAxisIds[ordinal])
            : plot.Elements(C + fallback).FirstOrDefault();
        var axis = FindAxis(1, "valAx");
        var categoryAxis = FindAxis(0, "catAx");
        spec.Minimum = axis?.Element(C + "scaling")?.Element(C + "min")?.Attribute("val") is { } min ? Number(min) : null;
        spec.Maximum = axis?.Element(C + "scaling")?.Element(C + "max")?.Attribute("val") is { } max ? Number(max) : null;
        spec.ValueFormat = (string?)axis?.Element(C + "numFmt")?.Attribute("formatCode") ?? "General";
        var valueTitle = ReadChartText(axis?.Element(C + "title")?.Element(C + "tx"), host.Name, warnings);
        var categoryTitle = ReadChartText(categoryAxis?.Element(C + "title")?.Element(C + "tx"), host.Name, warnings);
        spec.ValueAxisTitle = valueTitle.Caption; spec.ValueAxisTitleReference = valueTitle.Reference;
        spec.CategoryAxisTitle = categoryTitle.Caption; spec.CategoryAxisTitleReference = categoryTitle.Reference;
        spec.ShowGridLines = axis?.Element(C + "majorGridlines") is not null;
        return spec;
    }

    private static (string Caption, ChartTextReference? Reference) ReadChartText(XElement? text, string defaultSheet, List<string> warnings)
    {
        var reference = text?.Element(C + "strRef");
        var caption = reference?.Element(C + "strCache")?.Elements(C + "pt").FirstOrDefault(p => (string?)p.Attribute("idx") == "0")?.Element(C + "v")?.Value
            ?? text?.Element(C + "v")?.Value ?? string.Concat(text?.Descendants(A + "t").Select(t => t.Value) ?? []);
        // The fallback literal has the model's caption bound. A valid cell link still
        // resolves its full formatted value when rendered; no chart is dropped for a long cached caption.
        if (caption.Length > 1024) caption = caption[..1024];
        if (reference is null) return (caption, null);
        var formula = reference.Element(C + "f")?.Value;
        try
        {
            if (formula == "#REF!") return (caption, new(defaultSheet, "#REF!"));
            return (caption, ChartTextReference.Parse(formula ?? "", defaultSheet));
        }
        catch (Exception error) when (error is ArgumentException or FormatException)
        {
            warnings.Add("Chart text has an unsupported reference; its cached caption was retained: " + error.Message);
            return (caption, null);
        }
    }

    private static bool TryChartVector(string? formula, string fallbackSheet, out string sheet, out CellRange range)
    {
        sheet = fallbackSheet; range = default;
        if (string.IsNullOrWhiteSpace(formula) || formula.Contains('[')) return false;
        var bang = formula.LastIndexOf('!');
        if (bang >= 0)
        {
            sheet = formula[..bang];
            if (sheet.StartsWith('\'')) { if (!sheet.EndsWith('\'')) return false; sheet = sheet[1..^1].Replace("''", "'"); }
            formula = formula[(bang + 1)..];
        }
        return CellRange.TryParse(formula, out range) && (range.Left == range.Right || range.Top == range.Bottom) && range.Count <= 100_000;
    }
}
