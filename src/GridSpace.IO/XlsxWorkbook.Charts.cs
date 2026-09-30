using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using GridSpace.Core;
using GridSpace.Formulas;

namespace GridSpace.IO;

public static partial class XlsxWorkbook
{
    private static readonly XNamespace C = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace D = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
    private static readonly XNamespace Analytics = "urn:gridspace:analytics:1";
    private const string ChartExtension = "{F72BCC81-4733-4ED3-B378-53E11267B501}";
    private static XElement CE(string name, params object?[] content) => new(C + name, content);
    private static XElement CV(string name, object value) => CE(name, new XAttribute("val", value));
    private static XElement Solid(string color) => new(A + "solidFill", new XElement(A + "srgbClr", new XAttribute("val", color.TrimStart('#'))));
    private static string ChartReference(string sheet, string vector, IReadOnlyList<string>? areas = null)
    {
        if (areas is { Count: > 0 })
        {
            var references = areas.Select(area => ChartReference(sheet, area)).ToArray();
            var formula = references.Length == 1 ? references[0] : "(" + string.Join(",", references) + ")";
            if (formula.Length > 8192) throw new InvalidOperationException("Chart source is too fragmented for an 8,192-character XLSX reference. Collapse groups or reduce its source range.");
            return formula;
        }
        var range = CellRange.Parse(vector);
        string Cell(CellAddress address) => "$" + CellAddress.ColumnName(address.Column) + "$" + (address.Row + 1);
        return "'" + sheet.Replace("'", "''") + "'!" + Cell(range.Start) + ":" + Cell(range.End);
    }

    private static void WriteCharts(ZipArchive zip, Workbook book, Worksheet sheet, int sheetNumber, XElement worksheet,
        XElement worksheetRelationships, CalculationEngine engine, Action<string, string> type)
    {
        if (sheet.Charts.Count == 0) return;
        var drawing = new XElement(D + "wsDr", new XAttribute(XNamespace.Xmlns + "a", A));
        var drawingRels = new XElement(P + "Relationships");
        for (var index = 0; index < sheet.Charts.Count; index++)
        {
            var spec = sheet.Charts[index];
            var id = "rId" + (index + 1);
            var file = $"chart{sheetNumber}_{index + 1}.xml";
            drawingRels.Add(Relationship(id, "chart", "../charts/" + file));
            type("xl/charts/" + file, "application/vnd.openxmlformats-officedocument.drawingml.chart+xml");
            var frame = new XElement(D + "graphicFrame", new XAttribute("macro", ""),
                new XElement(D + "nvGraphicFramePr", new XElement(D + "cNvPr", new XAttribute("id", index + 2), new XAttribute("name", spec.Title)), new XElement(D + "cNvGraphicFramePr")),
                new XElement(D + "xfrm", new XElement(A + "off", new XAttribute("x", 0), new XAttribute("y", 0)), new XElement(A + "ext", new XAttribute("cx", 0), new XAttribute("cy", 0))),
                new XElement(A + "graphic", new XElement(A + "graphicData", new XAttribute("uri", C.NamespaceName), CE("chart", new XAttribute(R + "id", id)))));
            drawing.Add(new XElement(D + "oneCellAnchor",
                new XElement(D + "from", new XElement(D + "col", spec.Column), new XElement(D + "colOff", (long)(spec.OffsetX * 9525)),
                    new XElement(D + "row", spec.Row), new XElement(D + "rowOff", (long)(spec.OffsetY * 9525))),
                new XElement(D + "ext", new XAttribute("cx", (long)(spec.Width * 9525)), new XAttribute("cy", (long)(spec.Height * 9525))),
                frame, new XElement(D + "clientData")));
            WritePart(zip, "xl/charts/" + file, WriteChartDocument(book, sheet, spec, engine));
        }
        worksheet.Add(E("drawing", new XAttribute(R + "id", "drawing")));
        var drawingFile = "drawing" + sheetNumber + ".xml";
        type("xl/drawings/" + drawingFile, "application/vnd.openxmlformats-officedocument.drawing+xml");
        WritePart(zip, "xl/drawings/" + drawingFile, drawing);
        WritePart(zip, "xl/drawings/_rels/" + drawingFile + ".rels", drawingRels);
        worksheetRelationships.Add(Relationship("drawing", "drawing", "../drawings/" + drawingFile));
    }

    private static XElement WriteChartDocument(Workbook book, Worksheet host, ChartSpec original, CalculationEngine engine)
    {
        var spec = original;
        ChartData data;
        try
        {
            spec = ChartDataResolver.EffectiveDefinition(book, host, original);
            // Caches represent the complete bound vector. plotVisOnly performs Excel's visibility filtering.
            data = ChartDataResolver.Resolve(book, host, spec with { PlotHiddenCells = true }, engine);
        }
        catch (InvalidOperationException) when (original.SourceUnavailable || original.PivotTableId is not null)
        {
            data = new(host.Name, original.Range, original.Range, [], [], []);
        }
        var plot = CE("plotArea", CE("layout"));
        ChartKind Kind(int i) => spec.Kind == ChartKind.Combo
            ? i < spec.Series.Count && spec.Series[i].Kind is { } kind ? kind : i == data.Series.Count - 1 ? ChartKind.Line : ChartKind.Column
            : spec.Kind;
        bool Secondary(int i) => spec.Kind == ChartKind.Combo && i < spec.Series.Count && spec.Series[i].SecondaryAxis;
        var visible = Enumerable.Range(0, data.Series.Count).Where(i => i >= spec.Series.Count || spec.Series[i].Visible).ToArray();
        if (spec.Kind == ChartKind.Pie) visible = visible.Take(1).ToArray();
        var groups = visible.GroupBy(i => (Kind: Kind(i), Secondary: Secondary(i))).ToArray();
        if (groups.Length == 0) plot.Add(CE("barChart", CV("barDir", "col"), CV("grouping", "clustered"), CV("axId", 1), CV("axId", 2)));
        foreach (var group in groups)
        {
            var name = group.Key.Kind switch
            {
                ChartKind.Column or ChartKind.Bar => "barChart", ChartKind.Line => "lineChart",
                ChartKind.Area => "areaChart", ChartKind.Scatter => "scatterChart",
                ChartKind.Radar => "radarChart", ChartKind.Doughnut => "doughnutChart", _ => "pieChart"
            };
            var circular = group.Key.Kind is ChartKind.Pie or ChartKind.Doughnut;
            var chart = CE(name);
            if (name == "barChart") chart.Add(CV("barDir", group.Key.Kind == ChartKind.Bar ? "bar" : "col"));
            if (name is "barChart" or "lineChart" or "areaChart")
                chart.Add(CV("grouping", spec.Grouping == ChartGrouping.PercentStacked ? "percentStacked"
                    : spec.Grouping == ChartGrouping.Stacked ? "stacked" : name == "barChart" ? "clustered" : "standard"));
            if (name == "scatterChart") chart.Add(CV("scatterStyle", "marker"));
            if (name == "radarChart") chart.Add(CV("radarStyle", spec.ShowMarkers ? "marker" : "standard"));
            chart.Add(CV("varyColors", circular ? 1 : 0));
            foreach (var i in group) chart.Add(WriteSeries(data, spec, i, group.Key.Kind));
            if (spec.ShowDataLabels) chart.Add(CE("dLbls", CV("showLegendKey", 0), CV("showVal", 1),
                CV("showCatName", 0), CV("showSerName", 0), CV("showPercent", 0), CV("showBubbleSize", 0)));
            if (name == "barChart") chart.Add(CV("gapWidth", spec.GapWidth), CV("overlap", spec.Grouping == ChartGrouping.Clustered ? 0 : 100));
            if (name == "doughnutChart") chart.Add(CV("firstSliceAng", 270), CV("holeSize", spec.HoleSize));
            if (!circular) chart.Add(CV("axId", group.Key.Secondary ? 3 : 1), CV("axId", group.Key.Secondary ? 4 : 2));
            plot.Add(chart);
        }
        var hasAxes = groups.Length == 0 || groups.Any(g => g.Key.Kind is not ChartKind.Pie and not ChartKind.Doughnut);
        if (hasAxes)
        {
            plot.Add(ChartAxis(spec, 1, 2, true, false), ChartAxis(spec, 2, 1, false, false));
            if (groups.Any(g => g.Key.Secondary)) plot.Add(ChartAxis(spec, 3, 4, true, true), ChartAxis(spec, 4, 3, false, true));
        }
        var chartElement = CE("chart", ChartTitle(spec.Title), CV("autoTitleDeleted", 0), plot);
        if (spec.Legend != ChartLegendPosition.None)
            chartElement.Add(CE("legend", CV("legendPos", spec.Legend switch { ChartLegendPosition.Top => "t", ChartLegendPosition.Left => "l", ChartLegendPosition.Right => "r", _ => "b" }),
                CE("layout"), CV("overlay", 0)));
        chartElement.Add(CV("plotVisOnly", spec.PlotHiddenCells ? 0 : 1), CV("dispBlanksAs", "gap"), CV("showDLblsOverMax", 0));
        return CE("chartSpace", new XAttribute(XNamespace.Xmlns + "a", A), new XAttribute(XNamespace.Xmlns + "r", R),
            chartElement, CE("spPr", Solid(spec.Background), new XElement(A + "ln", new XElement(A + "noFill"))),
            CE("extLst", CE("ext", new XAttribute("uri", ChartExtension),
                new XElement(Analytics + "chart", JsonSerializer.Serialize(original)))));
    }

    private static XElement ChartTitle(string text) => CE("title", CE("tx", CE("rich", new XElement(A + "bodyPr"),
        new XElement(A + "lstStyle"), new XElement(A + "p", new XElement(A + "r", new XElement(A + "t", text))))),
        CE("layout"), CV("overlay", 0));

    private static XElement ChartAxis(ChartSpec spec, int id, int cross, bool category, bool secondary)
    {
        var numeric = !category || spec.Kind == ChartKind.Scatter;
        var scale = CE("scaling", CV("orientation", "minMax"));
        if (!category && !secondary) { if (spec.Maximum is { } max) scale.Add(CV("max", F(max))); if (spec.Minimum is { } min) scale.Add(CV("min", F(min))); }
        var axis = CE(numeric ? "valAx" : "catAx", CV("axId", id), scale, CV("delete", category && secondary ? 1 : 0),
            CV("axPos", category ? spec.Kind == ChartKind.Bar ? "l" : "b" : spec.Kind == ChartKind.Bar ? "b" : secondary ? "r" : "l"));
        if (!category && spec.ShowGridLines && !secondary) axis.Add(CE("majorGridlines"));
        var title = category ? spec.CategoryAxisTitle : spec.ValueAxisTitle;
        if (title.Length > 0 && !secondary) axis.Add(ChartTitle(title));
        if (numeric) axis.Add(CE("numFmt", new XAttribute("formatCode", spec.Grouping == ChartGrouping.PercentStacked ? "0%" : category ? "General" : spec.ValueFormat), new XAttribute("sourceLinked", 0)));
        axis.Add(CV("majorTickMark", "none"), CV("minorTickMark", "none"), CV("tickLblPos", "nextTo"),
            CV("crossAx", cross), CV("crosses", secondary ? "max" : "autoZero"));
        if (numeric) axis.Add(CV("crossBetween", spec.Kind == ChartKind.Bar || spec.Kind == ChartKind.Column || spec.Kind == ChartKind.Combo ? "between" : "midCat"));
        else axis.Add(CV("auto", 1), CV("lblAlgn", "ctr"), CV("lblOffset", 100));
        return axis;
    }

    private static XElement WriteSeries(ChartData data, ChartSpec spec, int index, ChartKind kind)
    {
        var vector = data.Series[index];
        var color = index < spec.Series.Count ? spec.Series[index].Color : ChartDataResolver.Palette[index % ChartDataResolver.Palette.Length];
        var series = CE("ser", CV("idx", index), CV("order", index), CE("tx", CE("v", vector.Name)));
        if (kind is ChartKind.Line or ChartKind.Scatter or ChartKind.Radar)
        {
            series.Add(CE("spPr", new XElement(A + "ln", new XAttribute("w", 25400), Solid(color))));
            series.Add(CE("marker", CV("symbol", spec.ShowMarkers || kind == ChartKind.Scatter ? "circle" : "none"),
                CV("size", 5), CE("spPr", Solid(color), new XElement(A + "ln", Solid(color)))));
        }
        else series.Add(CE("spPr", Solid(color), new XElement(A + "ln", new XElement(A + "noFill"))));
        if (kind is ChartKind.Pie or ChartKind.Doughnut)
            for (var i = 0; i < data.Categories.Length; i++)
                series.Add(CE("dPt", CV("idx", i), CE("spPr", Solid(ChartDataResolver.Palette[i % ChartDataResolver.Palette.Length]))));
        if (kind == ChartKind.Scatter) series.Add(CE("xVal", NumericReference(data.SourceSheet, data.CategoriesRange, data.XValues, data.CategoryAreas)));
        else
        {
            var cache = CE("strCache", CV("ptCount", data.Categories.Length),
                data.Categories.Select((text, i) => CE("pt", new XAttribute("idx", i), CE("v", text))));
            // A flattened hierarchy caption combines multiple typed field values. A
            // single-column strRef would change those labels when Excel recalculates.
            series.Add(CE("cat", data.HasCompositeCategories || data.Categories.Length == 0
                ? CE("strLit", cache.Elements().ToArray())
                : CE("strRef", CE("f", ChartReference(data.SourceSheet, data.CategoriesRange, data.CategoryAreas)), cache)));
        }
        series.Add(CE(kind == ChartKind.Scatter ? "yVal" : "val", NumericReference(data.SourceSheet, vector.ValuesRange, vector.Values, vector.ReferenceAreas)));
        if (kind is ChartKind.Line or ChartKind.Scatter) series.Add(CV("smooth", 0));
        return series;
    }

    private static XElement NumericReference(string sheet, string range, double?[] values, IReadOnlyList<string>? areas = null) =>
        values.Length == 0 ? CE("numLit", CE("formatCode", "General"), CV("ptCount", 0)) :
        CE("numRef", CE("f", ChartReference(sheet, range, areas)), CE("numCache", CE("formatCode", "General"), CV("ptCount", values.Length),
            values.Select((v, i) => v is null ? null : CE("pt", new XAttribute("idx", i), CE("v", F(v.Value))))));

}
