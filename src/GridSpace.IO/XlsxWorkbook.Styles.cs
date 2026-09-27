using System.IO.Compression;
using System.Xml.Linq;
using GridSpace.Core;

namespace GridSpace.IO;

public static partial class XlsxWorkbook
{
    private static readonly Dictionary<int, string> StandardFormats = new() { [0] = "General", [1] = "0", [2] = "0.00", [3] = "#,##0", [4] = "#,##0.00", [9] = "0%", [10] = "0.00%", [11] = "0.00E+00", [14] = "MM/dd/yyyy", [15] = "dd-MMM-yy", [16] = "dd-MMM", [17] = "MMM-yy", [49] = "@" };
    private static List<CellStyle> ReadStyles(ZipArchive zip)
    {
        if (zip.GetEntry("xl/styles.xml") is null) return [CellStyle.Default];
        var root = Xml(zip, "xl/styles.xml").Root!;
        var fonts = root.Element(S + "fonts")?.Elements(S + "font").ToArray() ?? [];
        var fills = root.Element(S + "fills")?.Elements(S + "fill").ToArray() ?? [];
        var borders = root.Element(S + "borders")?.Elements(S + "border").ToArray() ?? [];
        var formats = new Dictionary<int, string>(StandardFormats);
        foreach (var f in root.Element(S + "numFmts")?.Elements(S + "numFmt") ?? []) formats[Int(f.Attribute("numFmtId"))] = (string?)f.Attribute("formatCode") ?? "General";
        string Color(XElement? element, string fallback)
        {
            var rgb = (string?)element?.Attribute("rgb");
            if (rgb is { Length: 8 }) return "#" + rgb[2..];
            if (rgb is { Length: 6 }) return "#" + rgb;
            var indexed = Int(element?.Attribute("indexed"), -1);
            return indexed switch { 0 or 8 => "#000000", 1 or 9 => "#FFFFFF", 2 or 10 => "#FF0000", 3 or 11 => "#00FF00", 4 or 12 => "#0000FF", 5 or 13 => "#FFFF00", _ => fallback };
        }
        var result = new List<CellStyle>();
        foreach (var xf in root.Element(S + "cellXfs")?.Elements(S + "xf") ?? [])
        {
            var fi = Int(xf.Attribute("fontId")); var bi = Int(xf.Attribute("fillId")); var borderIndex = Int(xf.Attribute("borderId"));
            var font = fi >= 0 && fi < fonts.Length ? fonts[fi] : null; var fill = bi >= 0 && bi < fills.Length ? fills[bi] : null;
            var border = borderIndex >= 0 && borderIndex < borders.Length ? borders[borderIndex] : null;
            var alignment = xf.Element(S + "alignment");
            result.Add(new()
            {
                FontFamily = (string?)font?.Element(S + "name")?.Attribute("val") ?? "Arial",
                FontSize = Math.Clamp(Number(font?.Element(S + "sz")?.Attribute("val"), 11), 6, 200),
                Bold = font?.Element(S + "b") is not null, Italic = font?.Element(S + "i") is not null, Underline = font?.Element(S + "u") is not null,
                Foreground = Color(font?.Element(S + "color"), "#242424"), Background = Color(fill?.Element(S + "patternFill")?.Element(S + "fgColor"), "#FFFFFF"),
                NumberFormat = formats.GetValueOrDefault(Int(xf.Attribute("numFmtId")), "General"),
                Wrap = (string?)alignment?.Attribute("wrapText") == "1",
                Border = border?.Elements().Any(e => e.Attribute("style") is not null) == true,
                Alignment = ((string?)alignment?.Attribute("horizontal")) switch { "left" => CellAlignment.Left, "center" => CellAlignment.Center, "right" => CellAlignment.Right, _ => CellAlignment.General }
            });
        }
        return result.Count == 0 ? [CellStyle.Default] : result;
    }
    private static XElement WriteStyles(List<CellStyle> styles)
    {
        string Rgb(string color) => "FF" + (color.Length == 7 && color.StartsWith('#') ? color[1..] : "000000");
        var fonts = E("fonts", new XAttribute("count", styles.Count));
        var fills = E("fills", new XAttribute("count", styles.Count + 2), E("fill", E("patternFill", new XAttribute("patternType", "none"))), E("fill", E("patternFill", new XAttribute("patternType", "gray125"))));
        var formats = E("numFmts", new XAttribute("count", styles.Count));
        var xfs = E("cellXfs", new XAttribute("count", styles.Count));
        for (var i = 0; i < styles.Count; i++)
        {
            var style = styles[i];
            fonts.Add(E("font", style.Bold ? E("b") : null, style.Italic ? E("i") : null, style.Underline ? E("u") : null, E("sz", new XAttribute("val", F(style.FontSize))), E("color", new XAttribute("rgb", Rgb(style.Foreground))), E("name", new XAttribute("val", style.FontFamily))));
            fills.Add(E("fill", E("patternFill", new XAttribute("patternType", "solid"), E("fgColor", new XAttribute("rgb", Rgb(style.Background))), E("bgColor", new XAttribute("indexed", 64)))));
            formats.Add(E("numFmt", new XAttribute("numFmtId", 164 + i), new XAttribute("formatCode", style.NumberFormat)));
            var alignment = E("alignment", new XAttribute("vertical", "center"));
            if (style.Alignment != CellAlignment.General) alignment.Add(new XAttribute("horizontal", style.Alignment.ToString().ToLowerInvariant()));
            if (style.Wrap) alignment.Add(new XAttribute("wrapText", 1));
            xfs.Add(E("xf", new XAttribute("numFmtId", 164 + i), new XAttribute("fontId", i), new XAttribute("fillId", i + 2), new XAttribute("borderId", style.Border ? 1 : 0), new XAttribute("xfId", 0), new XAttribute("applyFont", 1), new XAttribute("applyFill", 1), new XAttribute("applyBorder", 1), new XAttribute("applyNumberFormat", 1), new XAttribute("applyAlignment", 1), alignment));
        }
        var thin = new[] { "left", "right", "top", "bottom" }.Select(n => E(n, new XAttribute("style", "thin"), E("color", new XAttribute("rgb", "FFB7C9BE"))));
        return E("styleSheet", formats, fonts, fills, E("borders", new XAttribute("count", 2), E("border", E("left"), E("right"), E("top"), E("bottom"), E("diagonal")), E("border", thin, E("diagonal"))), E("cellStyleXfs", new XAttribute("count", 1), E("xf", new XAttribute("numFmtId", 0), new XAttribute("fontId", 0), new XAttribute("fillId", 0), new XAttribute("borderId", 0))), xfs, E("cellStyles", new XAttribute("count", 1), E("cellStyle", new XAttribute("name", "Normal"), new XAttribute("xfId", 0), new XAttribute("builtinId", 0))));
    }
}
