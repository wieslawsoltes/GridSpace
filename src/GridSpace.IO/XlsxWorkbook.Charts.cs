using System.IO.Compression;
using System.Xml.Linq;
using GridSpace.Core;
using GridSpace.Formulas;

namespace GridSpace.IO;

public static partial class XlsxWorkbook
{
    private static readonly XNamespace C = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace D = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
    private static XElement CE(string name, params object?[] content) => new(C + name, content);
    private static XElement CV(string name, object value) => CE(name, new XAttribute("val", value));
    private static void WriteCharts(ZipArchive zip, Worksheet sheet, int sheetNumber, XElement worksheet, CalculationEngine engine, Action<string, string> type)
    {
        if (sheet.Charts.Count == 0) return;
        var drawing = new XElement(D + "wsDr", new XAttribute(XNamespace.Xmlns + "a", A)); var rels = new XElement(P + "Relationships");
        for (var index = 0; index < sheet.Charts.Count; index++)
        {
            var spec = sheet.Charts[index]; var id = "rId" + (index + 1); var file = $"chart{sheetNumber}_{index + 1}.xml";
            rels.Add(Relationship(id, "chart", "../charts/" + file));
            type("xl/charts/" + file, "application/vnd.openxmlformats-officedocument.drawingml.chart+xml");
            var frame = new XElement(D + "graphicFrame", new XAttribute("macro", ""), new XElement(D + "nvGraphicFramePr", new XElement(D + "cNvPr", new XAttribute("id", index + 2), new XAttribute("name", spec.Title)), new XElement(D + "cNvGraphicFramePr")), new XElement(D + "xfrm", new XElement(A + "off", new XAttribute("x", 0), new XAttribute("y", 0)), new XElement(A + "ext", new XAttribute("cx", 0), new XAttribute("cy", 0))), new XElement(A + "graphic", new XElement(A + "graphicData", new XAttribute("uri", C.NamespaceName), CE("chart", new XAttribute(R + "id", id)))));
            drawing.Add(new XElement(D + "oneCellAnchor", new XElement(D + "from", new XElement(D + "col", spec.Column), new XElement(D + "colOff", 0), new XElement(D + "row", spec.Row), new XElement(D + "rowOff", 0)), new XElement(D + "ext", new XAttribute("cx", (long)(spec.Width * 9525)), new XAttribute("cy", (long)(spec.Height * 9525))), frame, new XElement(D + "clientData")));
            var range = CellRange.Parse(spec.Range); var firstRow = Math.Min(range.Top + 1, range.Bottom); var count = Math.Min(1000, range.Bottom - firstRow + 1);
            var quotedSheet = "'" + sheet.Name.Replace("'", "''") + "'!";
            string Ref(int column) => quotedSheet + "$" + CellAddress.ColumnName(column) + "$" + (firstRow + 1) + ":$" + CellAddress.ColumnName(column) + "$" + (firstRow + count);
            var categoryCache = CE("strCache", CV("ptCount", count)); var valueCache = CE("numCache", CE("formatCode", "General"), CV("ptCount", count));
            for (var i = 0; i < count; i++)
            {
                categoryCache.Add(CE("pt", new XAttribute("idx", i), CE("v", engine.Evaluate(sheet, new CellAddress(firstRow + i, range.Left)).ToString())));
                var value = engine.Evaluate(sheet, new CellAddress(firstRow + i, range.Right));
                valueCache.Add(CE("pt", new XAttribute("idx", i), CE("v", F(value.TryNumber(out var number) ? number : 0))));
            }
            var series = CE("ser", CV("idx", 0), CV("order", 0), CE("tx", CE("v", engine.Evaluate(sheet, new CellAddress(range.Top, range.Right)).ToString())), CE("cat", CE("strRef", CE("f", Ref(range.Left)), categoryCache)), CE("val", CE("numRef", CE("f", Ref(range.Right)), valueCache)));
            var plot = CE("plotArea", CE("layout"));
            var kind = spec.Kind == ChartKind.Line ? "lineChart" : spec.Kind == ChartKind.Pie ? "pieChart" : "barChart";
            var chartType = CE(kind);
            if (kind == "barChart") chartType.Add(CV("barDir", spec.Kind == ChartKind.Bar ? "bar" : "col"));
            if (kind != "pieChart") chartType.Add(CV("grouping", kind == "barChart" ? "clustered" : "standard"));
            chartType.Add(CV("varyColors", kind == "pieChart" ? 1 : 0), series);
            if (kind != "pieChart") chartType.Add(CV("axId", 1), CV("axId", 2));
            plot.Add(chartType);
            if (kind != "pieChart")
            {
                plot.Add(CE("catAx", CV("axId", 1), CE("scaling", CV("orientation", "minMax")), CV("delete", 0), CV("axPos", "b"), CV("crossAx", 2), CV("crosses", "autoZero"), CV("auto", 1), CV("lblAlgn", "ctr"), CV("lblOffset", 100)));
                plot.Add(CE("valAx", CV("axId", 2), CE("scaling", CV("orientation", "minMax")), CV("delete", 0), CV("axPos", "l"), CE("numFmt", new XAttribute("formatCode", "#,##0"), new XAttribute("sourceLinked", 0)), CV("crossAx", 1), CV("crosses", "autoZero"), CV("crossBetween", "between")));
            }
            var title = CE("title", CE("tx", CE("rich", new XElement(A + "bodyPr"), new XElement(A + "lstStyle"), new XElement(A + "p", new XElement(A + "r", new XElement(A + "t", spec.Title))))), CE("layout"), CV("overlay", 0));
            WritePart(zip, "xl/charts/" + file, CE("chartSpace", new XAttribute(XNamespace.Xmlns + "a", A), new XAttribute(XNamespace.Xmlns + "r", R), CE("chart", title, CV("autoTitleDeleted", 0), plot, CV("plotVisOnly", 1))));
        }
        worksheet.Add(E("drawing", new XAttribute(R + "id", "drawing")));
        var drawingFile = "drawing" + sheetNumber + ".xml";
        type("xl/drawings/" + drawingFile, "application/vnd.openxmlformats-officedocument.drawing+xml");
        WritePart(zip, "xl/drawings/" + drawingFile, drawing); WritePart(zip, "xl/drawings/_rels/" + drawingFile + ".rels", rels);
        WritePart(zip, "xl/worksheets/_rels/sheet" + sheetNumber + ".xml.rels", new(P + "Relationships", Relationship("drawing", "drawing", "../drawings/" + drawingFile)));
    }
    private static void ReadCharts(ZipArchive zip, string sheetPath, XElement worksheet, Worksheet sheet, List<string> warnings)
    {
        var drawingId = (string?)worksheet.Element(S + "drawing")?.Attribute(R + "id");
        if (drawingId is null) return;
        if (!Relationships(zip, sheetPath).TryGetValue(drawingId, out var drawingPath)) return;
        var drawing = Xml(zip, drawingPath); var rels = Relationships(zip, drawingPath);
        foreach (var anchor in drawing.Root!.Elements().Take(128))
        {
            var chartId = (string?)anchor.Descendants(C + "chart").FirstOrDefault()?.Attribute(R + "id");
            if (chartId is null || !rels.TryGetValue(chartId, out var chartPath)) { warnings.Add("Non-chart drawings are not imported."); continue; }
            var chart = Xml(zip, chartPath); var category = chart.Descendants(C + "cat").Descendants(C + "f").FirstOrDefault()?.Value; var values = chart.Descendants(C + "val").Descendants(C + "f").FirstOrDefault()?.Value;
            if (category is null || values is null) { warnings.Add("A chart without local range references could not be imported."); continue; }
            try
            {
                var categories = CellRange.Parse(category[(category.LastIndexOf('!') + 1)..]); var valueRange = CellRange.Parse(values[(values.LastIndexOf('!') + 1)..]);
                var row = int.TryParse(anchor.Element(D + "from")?.Element(D + "row")?.Value, out var rr) ? rr : 3;
                var column = int.TryParse(anchor.Element(D + "from")?.Element(D + "col")?.Value, out var cc) ? cc : 8;
                var ext = anchor.Element(D + "ext");
                var kind = chart.Descendants(C + "lineChart").Any() ? ChartKind.Line : chart.Descendants(C + "pieChart").Any() ? ChartKind.Pie : (string?)chart.Descendants(C + "barDir").FirstOrDefault()?.Attribute("val") == "bar" ? ChartKind.Bar : ChartKind.Column;
                sheet.Charts.Add(new() { Title = string.Concat(chart.Descendants(C + "title").Descendants(A + "t").Select(t => t.Value)), Kind = kind, Range = new CellRange(new(Math.Max(0, categories.Top - 1), categories.Left), new(valueRange.Bottom, valueRange.Right)).ToString(), Row = row, Column = column, Width = Math.Clamp(Number(ext?.Attribute("cx"), 440 * 9525) / 9525, 200, 1200), Height = Math.Clamp(Number(ext?.Attribute("cy"), 260 * 9525) / 9525, 150, 800) });
            }
            catch (FormatException) { warnings.Add("An unsupported chart range was skipped."); }
        }
    }
}
