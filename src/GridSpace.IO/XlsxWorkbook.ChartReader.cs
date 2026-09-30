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
        var spec = new ChartSpec { Kind = kind, Title = string.Concat(root.Element(C + "chart")?.Element(C + "title")?.Descendants(A + "t").Select(t => t.Value) ?? []) };
        var primaryAxes = groups[0].Elements(C + "axId").Select(e => (string?)e.Attribute("val")).ToHashSet();
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
                var name = series.Element(C + "tx")?.Element(C + "v")?.Value ??
                    series.Element(C + "tx")?.Descendants(C + "pt").FirstOrDefault()?.Element(C + "v")?.Value ?? "Series " + (spec.Series.Count + 1);
                var color = (string?)series.Element(C + "spPr")?.Descendants(A + "srgbClr").FirstOrDefault()?.Attribute("val");
                spec.Series.Add(new ChartSeries
                {
                    Name = name, Values = values.ToString(), Color = color is { Length: 6 } ? "#" + color : ChartDataResolver.Palette[spec.Series.Count % ChartDataResolver.Palette.Length],
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
        var axis = plot.Elements(C + "valAx").FirstOrDefault();
        spec.Minimum = axis?.Element(C + "scaling")?.Element(C + "min")?.Attribute("val") is { } min ? Number(min) : null;
        spec.Maximum = axis?.Element(C + "scaling")?.Element(C + "max")?.Attribute("val") is { } max ? Number(max) : null;
        spec.ValueFormat = (string?)axis?.Element(C + "numFmt")?.Attribute("formatCode") ?? "General";
        spec.ValueAxisTitle = string.Concat(axis?.Element(C + "title")?.Descendants(A + "t").Select(t => t.Value) ?? []);
        spec.CategoryAxisTitle = string.Concat(plot.Element(C + "catAx")?.Element(C + "title")?.Descendants(A + "t").Select(t => t.Value) ?? []);
        spec.ShowGridLines = axis?.Element(C + "majorGridlines") is not null;
        return spec;
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
