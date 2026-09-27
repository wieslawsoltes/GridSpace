using System.IO.Compression;
using System.Xml.Linq;
using GridSpace.Core;

namespace GridSpace.IO;

public static partial class XlsxWorkbook
{
    private static string Camel<T>(T value) where T : struct, Enum
    {
        var text = value.ToString();
        return char.ToLowerInvariant(text[0]) + text[1..];
    }

    private static string? ReadRgb(XElement? color)
    {
        var rgb = (string?)color?.Attribute("rgb");
        if (rgb is { Length: 8 }) rgb = rgb[2..];
        if (rgb is not { Length: 6 }) return null;
        var result = "#" + rgb;
        DifferentialStyle.ValidateColor(result);
        return result;
    }

    private static bool Flag(XAttribute? value, bool fallback = false) => value is null ? fallback : value.Value is "1" or "true";
    private static string Rgb(string color) => "FF" + color[1..];

    private static List<DifferentialStyle?> ReadDifferentials(ZipArchive zip, List<string> warnings)
    {
        if (zip.GetEntry("xl/styles.xml") is null) return [];
        var result = new List<DifferentialStyle?>();
        foreach (var dxf in Xml(zip, "xl/styles.xml").Root?.Element(S + "dxfs")?.Elements(S + "dxf") ?? [])
        {
            if (result.Count >= 10000) throw new InvalidDataException("Too many differential styles.");
            var font = dxf.Element(S + "font");
            var fill = dxf.Element(S + "fill");
            var pattern = fill?.Element(S + "patternFill");
            var unsupported = dxf.Elements().Any(e => e.Name != S + "font" && e.Name != S + "fill" && e.Name != S + "numFmt")
                || font?.Elements().Any(e => e.Name != S + "b" && e.Name != S + "i" && e.Name != S + "u" && e.Name != S + "color") == true
                || font?.Element(S + "color") is { } fc && ReadRgb(fc) is null
                || fill is not null && ((string?)pattern?.Attribute("patternType") != "solid" || ReadRgb(pattern.Element(S + "fgColor")) is null);
            if (unsupported)
            {
                warnings.Add("Conditional rules using unsupported differential styles (including theme colors or borders) were skipped.");
                result.Add(null);
                continue;
            }
            bool? FontFlag(string name) => font?.Element(S + name) is { } element ? Flag(element.Attribute("val"), true) : null;
            result.Add(new DifferentialStyle
            {
                Background = ReadRgb(pattern?.Element(S + "fgColor")),
                Foreground = ReadRgb(font?.Element(S + "color")),
                Bold = FontFlag("b"), Italic = FontFlag("i"),
                Underline = font?.Element(S + "u") is { } underline ? (string?)underline.Attribute("val") != "none" : null,
                NumberFormat = (string?)dxf.Element(S + "numFmt")?.Attribute("formatCode")
            });
        }
        return result;
    }

    private static XElement AppendDifferentials(XElement styles, IReadOnlyList<DifferentialStyle> differentials)
    {
        var dxfs = E("dxfs", new XAttribute("count", differentials.Count));
        for (var i = 0; i < differentials.Count; i++)
        {
            var style = differentials[i];
            var font = E("font");
            if (style.Bold is { } bold) font.Add(E("b", new XAttribute("val", bold ? 1 : 0)));
            if (style.Italic is { } italic) font.Add(E("i", new XAttribute("val", italic ? 1 : 0)));
            if (style.Underline is { } underline) font.Add(E("u", new XAttribute("val", underline ? "single" : "none")));
            if (style.Foreground is { } foreground) font.Add(E("color", new XAttribute("rgb", Rgb(foreground))));
            var dxf = E("dxf");
            if (font.HasElements) dxf.Add(font);
            if (style.NumberFormat is { } format) dxf.Add(E("numFmt", new XAttribute("numFmtId", 50000 + i), new XAttribute("formatCode", format)));
            if (style.Background is { } background) dxf.Add(E("fill", E("patternFill", new XAttribute("patternType", "solid"), E("fgColor", new XAttribute("rgb", Rgb(background))))));
            dxfs.Add(dxf);
        }
        styles.Add(dxfs);
        return styles;
    }

    private static void WriteConditionalFormats(Worksheet sheet, XElement worksheet, List<DifferentialStyle> differentials)
    {
        foreach (var rule in sheet.ConditionalFormats.OrderBy(r => r.Priority))
        {
            var type = rule.Kind switch
            {
                ConditionalFormatKind.CellValue => "cellIs", ConditionalFormatKind.Expression => "expression",
                ConditionalFormatKind.Top or ConditionalFormatKind.Bottom => "top10",
                ConditionalFormatKind.AboveAverage or ConditionalFormatKind.BelowAverage => "aboveAverage",
                _ => Camel(rule.Kind)
            };
            var element = E("cfRule", new XAttribute("type", type), new XAttribute("priority", rule.Priority));
            if (rule.Kind is ConditionalFormatKind.ColorScale or ConditionalFormatKind.DataBar)
            {
                var visual = E(rule.Kind == ConditionalFormatKind.DataBar ? "dataBar" : "colorScale");
                if (rule.Kind == ConditionalFormatKind.DataBar)
                    visual.Add(new XAttribute("showValue", rule.ShowValue ? 1 : 0), new XAttribute("minLength", 0), new XAttribute("maxLength", 100));
                visual.Add(E("cfvo", new XAttribute("type", "min")));
                if (rule.Kind == ConditionalFormatKind.ColorScale && rule.ThreeColorScale)
                    visual.Add(E("cfvo", new XAttribute("type", "percentile"), new XAttribute("val", 50)));
                visual.Add(E("cfvo", new XAttribute("type", "max")));
                if (rule.Kind == ConditionalFormatKind.ColorScale)
                {
                    visual.Add(E("color", new XAttribute("rgb", Rgb(rule.LowColor))));
                    if (rule.ThreeColorScale) visual.Add(E("color", new XAttribute("rgb", Rgb(rule.MiddleColor))));
                }
                visual.Add(E("color", new XAttribute("rgb", Rgb(rule.HighColor))));
                element.Add(visual);
            }
            else
            {
                var dxf = differentials.IndexOf(rule.Style);
                if (dxf < 0) { dxf = differentials.Count; differentials.Add(rule.Style); }
                element.Add(new XAttribute("dxfId", dxf));
                if (rule.StopIfTrue) element.Add(new XAttribute("stopIfTrue", 1));
                switch (rule.Kind)
                {
                    case ConditionalFormatKind.CellValue:
                        element.Add(new XAttribute("operator", Camel(rule.Comparison)), E("formula", rule.Operand.TrimStart('=')));
                        if (rule.Comparison is CellComparison.Between or CellComparison.NotBetween) element.Add(E("formula", rule.Operand2.TrimStart('=')));
                        break;
                    case ConditionalFormatKind.Expression:
                        element.Add(E("formula", rule.Operand.TrimStart('=')));
                        break;
                    case ConditionalFormatKind.ContainsText:
                        var origin = CellRange.Parse(rule.Range).Normalized.Start;
                        element.Add(new XAttribute("operator", "containsText"), new XAttribute("text", rule.Operand), E("formula", "NOT(ISERROR(SEARCH(\"" + rule.Operand.Replace("\"", "\"\"") + "\"," + origin + ")))"));
                        break;
                    case ConditionalFormatKind.Top:
                    case ConditionalFormatKind.Bottom:
                        element.Add(new XAttribute("rank", rule.Rank), new XAttribute("percent", rule.Percent ? 1 : 0), new XAttribute("bottom", rule.Kind == ConditionalFormatKind.Bottom ? 1 : 0));
                        break;
                    case ConditionalFormatKind.AboveAverage:
                    case ConditionalFormatKind.BelowAverage:
                        element.Add(new XAttribute("aboveAverage", rule.Kind == ConditionalFormatKind.AboveAverage ? 1 : 0));
                        break;
                }
            }
            worksheet.Add(E("conditionalFormatting", new XAttribute("sqref", rule.Range), element));
        }
    }

    private static void ReadConditionalFormats(XElement worksheet, Worksheet sheet, IReadOnlyList<DifferentialStyle?> differentials, List<string> warnings)
    {
        foreach (var block in worksheet.Elements(S + "conditionalFormatting"))
        {
            var range = (string?)block.Attribute("sqref") ?? "";
            if (!CellRange.TryParse(range, out var area) || area.Count > 100_000)
            {
                warnings.Add("Conditional formatting on discontiguous or oversized ranges was skipped.");
                continue;
            }
            foreach (var element in block.Elements(S + "cfRule"))
            {
                if (sheet.ConditionalFormats.Count >= 256) { warnings.Add("Conditional-format rules beyond the 256-rule sheet limit were skipped."); break; }
                try
                {
                    var type = (string?)element.Attribute("type") ?? "";
                    var kind = type switch
                    {
                        "cellIs" => ConditionalFormatKind.CellValue, "expression" => ConditionalFormatKind.Expression,
                        "containsText" => ConditionalFormatKind.ContainsText, "duplicateValues" => ConditionalFormatKind.DuplicateValues,
                        "uniqueValues" => ConditionalFormatKind.UniqueValues,
                        "top10" => Flag(element.Attribute("bottom")) ? ConditionalFormatKind.Bottom : ConditionalFormatKind.Top,
                        "aboveAverage" => Flag(element.Attribute("aboveAverage"), true) ? ConditionalFormatKind.AboveAverage : ConditionalFormatKind.BelowAverage,
                        "colorScale" => ConditionalFormatKind.ColorScale, "dataBar" => ConditionalFormatKind.DataBar,
                        _ => throw new NotSupportedException("Conditional rule type " + type + " is not supported.")
                    };
                    if (element.Attribute("stdDev") is not null || Flag(element.Attribute("equalAverage"))) throw new NotSupportedException("Average-rule standard deviation/equality options are not supported.");
                    var index = Int(element.Attribute("dxfId"), -1);
                    if (index >= differentials.Count || index >= 0 && differentials[index] is null) throw new NotSupportedException("An unsupported differential style was skipped.");
                    var formulas = element.Elements(S + "formula").Select(f => f.Value).ToArray();
                    var comparison = CellComparison.GreaterThan;
                    if (kind == ConditionalFormatKind.CellValue && !Enum.TryParse((string?)element.Attribute("operator"), true, out comparison)) throw new NotSupportedException("Unknown conditional comparison operator.");
                    var rule = new ConditionalFormatRule
                    {
                        Range = area.ToString(), Kind = kind, Comparison = comparison,
                        Priority = Math.Max(1, Int(element.Attribute("priority"), sheet.ConditionalFormats.Count + 1)),
                        StopIfTrue = Flag(element.Attribute("stopIfTrue")),
                        Style = index < 0 ? new DifferentialStyle() : differentials[index]!,
                        Operand = kind == ConditionalFormatKind.ContainsText ? (string?)element.Attribute("text") ?? "" : formulas.FirstOrDefault() ?? "0",
                        Operand2 = formulas.Skip(1).FirstOrDefault() ?? "0",
                        Rank = Int(element.Attribute("rank"), 10), Percent = Flag(element.Attribute("percent"))
                    };
                    if (kind is ConditionalFormatKind.ColorScale or ConditionalFormatKind.DataBar)
                    {
                        var visual = element.Element(S + type) ?? throw new InvalidDataException("Missing conditional-format visual.");
                        var thresholds = visual.Elements(S + "cfvo").ToArray();
                        var colors = visual.Elements(S + "color").Select(c => ReadRgb(c) ?? throw new NotSupportedException("Theme-based conditional colors are not supported.")).ToArray();
                        if (thresholds.Length is < 2 or > 3 || (string?)thresholds[0].Attribute("type") != "min" || (string?)thresholds[^1].Attribute("type") != "max")
                            throw new NotSupportedException("Only min/max conditional-scale endpoints are supported.");
                        if (thresholds.Length == 3 && ((string?)thresholds[1].Attribute("type") != "percentile" || Number(thresholds[1].Attribute("val")) != 50))
                            throw new NotSupportedException("Only a 50th-percentile color-scale midpoint is supported.");
                        if (kind == ConditionalFormatKind.ColorScale)
                        {
                            if (colors.Length != thresholds.Length) throw new InvalidDataException("Invalid color scale.");
                            rule = rule with { LowColor = colors[0], MiddleColor = colors.Length == 3 ? colors[1] : "#FFEB84", HighColor = colors[^1], ThreeColorScale = colors.Length == 3 };
                        }
                        else
                        {
                            if (colors.Length != 1 || thresholds.Length != 2) throw new InvalidDataException("Invalid data bar.");
                            rule = rule with { HighColor = colors[0], ShowValue = Flag(visual.Attribute("showValue"), true) };
                            if (Int(visual.Attribute("minLength"), 10) != 0 || Int(visual.Attribute("maxLength"), 90) != 100)
                                warnings.Add("Imported data bars use full-range lengths; custom minimum/maximum display lengths are not retained.");
                        }
                    }
                    rule.Validate();
                    sheet.ConditionalFormats.Add(rule);
                }
                catch (Exception error) when (error is InvalidDataException or NotSupportedException or FormatException or ArgumentException)
                {
                    warnings.Add("Conditional formatting: " + error.Message);
                }
            }
        }
    }
}
