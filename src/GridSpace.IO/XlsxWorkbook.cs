using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using GridSpace.Core;
using GridSpace.Formulas;

namespace GridSpace.IO;

/// <summary>Office Open XML transitional workbook subset. Never executes macros or follows external relationships.</summary>
public static partial class XlsxWorkbook
{
    private static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace P = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace T = "http://schemas.openxmlformats.org/package/2006/content-types";
    private const string RelBase = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
    private static string F(double value) => value.ToString("G17", CultureInfo.InvariantCulture);
    private static double Number(XAttribute? attribute, double fallback = 0) => double.TryParse(attribute?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : fallback;
    private static int Int(XAttribute? attribute, int fallback = 0) => int.TryParse(attribute?.Value, out var n) ? n : fallback;
    private static XElement E(string name, params object?[] children) => new(S + name, children);
    private static XDocument Xml(ZipArchive zip, string path)
    {
        var entry = zip.GetEntry(path) ?? throw new InvalidDataException("Missing workbook part: " + path);
        if (entry.Length > 16 * 1024 * 1024) throw new InvalidDataException("An XML part exceeds 16 MB.");
        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 16 * 1024 * 1024 });
        return XDocument.Load(reader);
    }
    private static void WritePart(ZipArchive zip, string path, XElement root)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal); using var stream = entry.Open();
        using var writer = XmlWriter.Create(stream, new() { Encoding = new UTF8Encoding(false), Indent = false });
        new XDocument(new XDeclaration("1.0", "utf-8", "yes"), root).Save(writer);
    }
    private static Dictionary<string, string> Relationships(ZipArchive zip, string part)
    {
        var slash = part.LastIndexOf('/'); var folder = slash < 0 ? "" : part[..(slash + 1)]; var file = part[(slash + 1)..];
        var path = folder + "_rels/" + file + ".rels";
        if (zip.GetEntry(path) is null) return [];
        var result = new Dictionary<string, string>();
        foreach (var rel in Xml(zip, path).Root!.Elements(P + "Relationship"))
        {
            if ((string?)rel.Attribute("TargetMode") == "External") continue;
            var target = (string?)rel.Attribute("Target") ?? "";
            var normalized = new Uri(new Uri("https://workbook.invalid/" + part), target).AbsolutePath.TrimStart('/');
            if (!normalized.StartsWith("xl/", StringComparison.Ordinal)) continue;
            result[(string?)rel.Attribute("Id") ?? ""] = Uri.UnescapeDataString(normalized);
        }
        return result;
    }
    public static ImportResult Read(byte[] bytes)
    {
        if (bytes.Length > WorkbookFiles.MaximumFileBytes) throw new InvalidDataException("Workbook exceeds 32 MB.");
        using var input = new MemoryStream(bytes); using var zip = new ZipArchive(input, ZipArchiveMode.Read);
        if (zip.Entries.Count > 4096 || zip.Entries.Sum(e => e.Length) > 128 * 1024 * 1024) throw new InvalidDataException("Expanded workbook exceeds the safety limit.");
        var xml = Xml(zip, "xl/workbook.xml");
        if (xml.Root?.Name.Namespace != S) throw new InvalidDataException("Only transitional XLSX is supported; save as standard Excel Workbook first.");
        if ((string?)xml.Root.Element(S + "workbookPr")?.Attribute("date1904") is "1" or "true") throw new InvalidDataException("The 1904 date system is not supported. Convert the workbook to the 1900 date system first.");
        var strings = zip.GetEntry("xl/sharedStrings.xml") is null ? [] : Xml(zip, "xl/sharedStrings.xml").Root!.Elements(S + "si").Select(si => string.Concat(si.Descendants(S + "t").Select(t => t.Value))).ToArray();
        var styles = ReadStyles(zip); var relationships = Relationships(zip, "xl/workbook.xml");
        var book = new Workbook { Sheets = [], Title = "Imported workbook" }; var warnings = new List<string>();
        foreach (var info in xml.Root.Element(S + "sheets")?.Elements(S + "sheet") ?? [])
        {
            var id = (string?)info.Attribute(R + "id") ?? "";
            if (!relationships.TryGetValue(id, out var path)) throw new InvalidDataException("Unresolved worksheet relationship.");
            var sheet = new Worksheet { Name = (string?)info.Attribute("name") ?? "Sheet" + (book.Sheets.Count + 1) };
            var data = Xml(zip, path); var root = data.Root!;
            foreach (var row in root.Element(S + "sheetData")?.Elements(S + "row") ?? [])
            {
                var rowIndex = Int(row.Attribute("r"), 1) - 1;
                if (row.Attribute("ht") is not null) sheet.RowHeights[rowIndex] = Math.Clamp(Number(row.Attribute("ht")) * 96 / 72, 16, 600);
                if ((string?)row.Attribute("hidden") == "1") sheet.HiddenRows.Add(rowIndex);
                foreach (var cell in row.Elements(S + "c"))
                {
                    var address = CellAddress.Parse((string?)cell.Attribute("r") ?? throw new InvalidDataException("Cell address missing."));
                    var styleIndex = Int(cell.Attribute("s")); var style = styleIndex >= 0 && styleIndex < styles.Count ? styles[styleIndex] : CellStyle.Default;
                    var formula = cell.Element(S + "f"); var value = cell.Element(S + "v")?.Value ?? ""; var type = (string?)cell.Attribute("t");
                    string raw;
                    if (formula is not null)
                    {
                        if (formula.Attribute("t") is { Value: "shared" } && formula.Value == "") { raw = value; warnings.Add("Shared formula followers were imported as cached values; expand shared formulas before importing for full recalculation."); }
                        else if (formula.Attribute("t") is { Value: "array" }) { raw = "=" + formula.Value; warnings.Add("Array/spill formulas are not supported."); }
                        else raw = "=" + formula.Value;
                    }
                    else if (type == "s") { if (!int.TryParse(value, out var n) || n < 0 || n >= strings.Length) throw new InvalidDataException("Invalid shared string index."); raw = "'" + strings[n]; }
                    else if (type == "inlineStr") raw = "'" + string.Concat(cell.Element(S + "is")?.Descendants(S + "t").Select(t => t.Value) ?? []);
                    else if (type is "str" or "d") raw = "'" + value;
                    else if (type == "b") raw = value == "1" ? "TRUE" : "FALSE";
                    else if (type == "e") raw = "=" + value;
                    else raw = value;
                    sheet.Set(address, new() { Input = raw, Style = style });
                    if (sheet.Cells.Count > 200_000) throw new InvalidDataException("Worksheet exceeds the 200,000-cell import limit.");
                }
            }
            foreach (var col in root.Element(S + "cols")?.Elements(S + "col") ?? [])
            {
                var first = Math.Max(0, Int(col.Attribute("min"), 1) - 1); var last = Math.Min(CellAddress.MaxColumns - 1, Int(col.Attribute("max"), 1) - 1);
                for (var c = first; c <= last; c++)
                {
                    if (col.Attribute("width") is not null) sheet.ColumnWidths[c] = Math.Clamp(Number(col.Attribute("width"), 12) * 7 + 5, 24, 1000);
                    if ((string?)col.Attribute("hidden") == "1") sheet.HiddenColumns.Add(c);
                }
            }
            foreach (var merge in root.Element(S + "mergeCells")?.Elements(S + "mergeCell") ?? []) sheet.Merges.Add(CellRange.Parse((string?)merge.Attribute("ref") ?? "A1"));
            var view = root.Element(S + "sheetViews")?.Element(S + "sheetView"); var pane = view?.Element(S + "pane");
            if ((string?)pane?.Attribute("state") is "frozen" or "frozenSplit") { sheet.FrozenRows = Int(pane.Attribute("ySplit")); sheet.FrozenColumns = Int(pane.Attribute("xSplit")); }
            sheet.ShowGridLines = (string?)view?.Attribute("showGridLines") != "0";
            sheet.FilterRange = (string?)root.Element(S + "autoFilter")?.Attribute("ref");
            foreach (var validation in root.Element(S + "dataValidations")?.Elements(S + "dataValidation") ?? [])
            {
                var f = validation.Element(S + "formula1")?.Value ?? ""; var range = (string?)validation.Attribute("sqref") ?? "";
                if ((string?)validation.Attribute("type") == "list" && f.StartsWith('"') && f.EndsWith('"') && !range.Contains(' ')) sheet.ValidationLists[range] = f[1..^1].Split(',');
                else warnings.Add("Only inline list data validation is supported.");
            }
            if (root.Elements(S + "conditionalFormatting").Any()) warnings.Add("Conditional formatting rules are not imported.");
            if (root.Element(S + "tableParts") is not null) warnings.Add("Excel tables are imported as cells; structured references and table metadata are not supported.");
            ReadCharts(zip, path, root, sheet, warnings);
            book.Sheets.Add(sheet);
        }
        foreach (var name in xml.Root.Element(S + "definedNames")?.Elements(S + "definedName") ?? [])
            if (name.Attribute("localSheetId") is null && ((string?)name.Attribute("name")) is { } n && !n.StartsWith("_xlnm.")) book.Names[n] = name.Value;
        book.ActiveSheetIndex = Int(xml.Root.Element(S + "bookViews")?.Element(S + "workbookView")?.Attribute("activeTab"));
        if (zip.Entries.Any(e => e.FullName.Contains("pivot", StringComparison.OrdinalIgnoreCase))) warnings.Add("Pivot tables and data models are not imported.");
        if (zip.Entries.Any(e => e.FullName.Contains("externalLink", StringComparison.OrdinalIgnoreCase))) warnings.Add("External links were not followed.");
        if (zip.Entries.Any(e => e.FullName.Contains("vbaProject", StringComparison.OrdinalIgnoreCase))) warnings.Add("Macros were ignored and will not be saved.");
        warnings.Add("XLSX interoperability is a subset. Use a native .gridspace copy to preserve GridSpace-specific state; keep the original Excel file.");
        book.Attach(); return new(book, warnings.Distinct().ToArray());
    }
    public static byte[] Write(Workbook book)
    {
        book.Attach(); var engine = new CalculationEngine(book); var styles = new List<CellStyle> { CellStyle.Default };
        foreach (var style in book.Sheets.SelectMany(s => s.Cells.Values).Select(c => c.Style).Distinct()) if (!styles.Contains(style)) styles.Add(style);
        if (styles.Count > 10000) throw new InvalidOperationException("XLSX export is limited to 10,000 distinct styles.");
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            var types = new XElement(T + "Types", new XElement(T + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")), new XElement(T + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")));
            void Type(string part, string type) => types.Add(new XElement(T + "Override", new XAttribute("PartName", "/" + part), new XAttribute("ContentType", type)));
            Type("xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"); Type("xl/styles.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml");
            WritePart(zip, "_rels/.rels", new(P + "Relationships", Relationship("rId1", "officeDocument", "xl/workbook.xml")));
            var workbook = E("workbook", new XAttribute(XNamespace.Xmlns + "r", R), E("bookViews", E("workbookView", new XAttribute("activeTab", book.ActiveSheetIndex))), E("sheets"));
            var rels = new XElement(P + "Relationships");
            for (var index = 0; index < book.Sheets.Count; index++)
            {
                var sheet = book.Sheets[index]; var n = index + 1;
                workbook.Element(S + "sheets")!.Add(E("sheet", new XAttribute("name", sheet.Name), new XAttribute("sheetId", n), new XAttribute(R + "id", "rId" + n)));
                rels.Add(Relationship("rId" + n, "worksheet", "worksheets/sheet" + n + ".xml"));
                Type("xl/worksheets/sheet" + n + ".xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml");
                var view = E("sheetView", new XAttribute("workbookViewId", 0), new XAttribute("showGridLines", sheet.ShowGridLines ? 1 : 0));
                if (sheet.FrozenRows > 0 || sheet.FrozenColumns > 0) view.Add(E("pane", new XAttribute("xSplit", sheet.FrozenColumns), new XAttribute("ySplit", sheet.FrozenRows), new XAttribute("topLeftCell", new CellAddress(sheet.FrozenRows, sheet.FrozenColumns)), new XAttribute("state", "frozen")));
                var root = E("worksheet", new XAttribute(XNamespace.Xmlns + "r", R), E("dimension", new XAttribute("ref", sheet.UsedRange)), E("sheetViews", view), E("sheetFormatPr", new XAttribute("defaultRowHeight", 18)));
                var cols = E("cols");
                foreach (var c in sheet.ColumnWidths.Keys.Concat(sheet.HiddenColumns).Distinct().Order()) cols.Add(E("col", new XAttribute("min", c + 1), new XAttribute("max", c + 1), new XAttribute("width", F((sheet.ColumnWidths.GetValueOrDefault(c, 88) - 5) / 7)), new XAttribute("customWidth", 1), sheet.HiddenColumns.Contains(c) ? new XAttribute("hidden", 1) : null));
                if (cols.HasElements) root.Add(cols);
                var data = E("sheetData");
                var grouped = sheet.Cells.Select(p => (Address: CellAddress.Parse(p.Key), Cell: p.Value)).GroupBy(p => p.Address.Row).ToDictionary(g => g.Key, g => g.OrderBy(c => c.Address.Column).ToArray());
                foreach (var r in grouped.Keys.Concat(sheet.RowHeights.Keys).Concat(sheet.HiddenRows).Distinct().Order())
                {
                    var row = E("row", new XAttribute("r", r + 1));
                    if (sheet.RowHeights.TryGetValue(r, out var height)) row.Add(new XAttribute("ht", F(height * 72 / 96)), new XAttribute("customHeight", 1));
                    if (sheet.HiddenRows.Contains(r)) row.Add(new XAttribute("hidden", 1));
                    foreach (var item in grouped.GetValueOrDefault(r, []))
                    {
                        var cell = E("c", new XAttribute("r", item.Address), new XAttribute("s", styles.IndexOf(item.Cell.Style)));
                        var value = engine.Evaluate(sheet, item.Address);
                        if (item.Cell.IsFormula) cell.Add(E("f", item.Cell.Input[1..]));
                        if (value.Kind == ValueKind.Number) cell.Add(E("v", F(value.Number)));
                        else if (value.Kind == ValueKind.Boolean) { cell.Add(new XAttribute("t", "b")); cell.Add(E("v", value.Truth ? "1" : "0")); }
                        else if (value.IsError) { cell.Add(new XAttribute("t", "e")); cell.Add(E("v", value.Text)); }
                        else if (item.Cell.IsFormula) { cell.Add(new XAttribute("t", "str")); cell.Add(E("v", value.ToString())); }
                        else if (value.Kind != ValueKind.Blank) { cell.Add(new XAttribute("t", "inlineStr")); cell.Add(E("is", E("t", new XAttribute(XNamespace.Xml + "space", "preserve"), value.ToString()))); }
                        row.Add(cell);
                    }
                    data.Add(row);
                }
                root.Add(data);
                if (sheet.FilterRange is { } filter) root.Add(E("autoFilter", new XAttribute("ref", filter)));
                if (sheet.Merges.Count > 0) root.Add(E("mergeCells", new XAttribute("count", sheet.Merges.Count), sheet.Merges.Select(m => E("mergeCell", new XAttribute("ref", m)))));
                if (sheet.ValidationLists.Count > 0) root.Add(E("dataValidations", new XAttribute("count", sheet.ValidationLists.Count), sheet.ValidationLists.Select(p => E("dataValidation", new XAttribute("type", "list"), new XAttribute("allowBlank", 1), new XAttribute("showErrorMessage", 1), new XAttribute("sqref", p.Key), E("formula1", "\"" + string.Join(',', p.Value) + "\"")))));
                WriteCharts(zip, sheet, n, root, engine, Type);
                WritePart(zip, "xl/worksheets/sheet" + n + ".xml", root);
            }
            if (book.Names.Count > 0) workbook.Add(E("definedNames", book.Names.Select(p => E("definedName", new XAttribute("name", p.Key), p.Value.TrimStart('=')))));
            workbook.Add(E("calcPr", new XAttribute("calcId", 191029), new XAttribute("fullCalcOnLoad", 1), new XAttribute("forceFullCalc", 1)));
            rels.Add(Relationship("styles", "styles", "styles.xml"));
            WritePart(zip, "xl/workbook.xml", workbook); WritePart(zip, "xl/_rels/workbook.xml.rels", rels);
            WritePart(zip, "xl/styles.xml", WriteStyles(styles)); WritePart(zip, "[Content_Types].xml", types);
        }
        return output.ToArray();
    }
    private static XElement Relationship(string id, string type, string target) => new(P + "Relationship", new XAttribute("Id", id), new XAttribute("Type", RelBase + type), new XAttribute("Target", target));
}
