using System.IO.Compression;
using System.Xml.Linq;
using GridSpace.Core;

namespace GridSpace.IO;

public static partial class XlsxWorkbook
{
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
        var book = new Workbook { Sheets = [], Title = "Imported workbook" };
        var warnings = new List<string>();
        var differentials = ReadDifferentials(zip, warnings);
        var explicitVisibility = new HashSet<Worksheet>();
        long remainingSharedCharacters = 32 * 1024 * 1024;
        foreach (var info in xml.Root.Element(S + "sheets")?.Elements(S + "sheet") ?? [])
        {
            if (book.Sheets.Count >= 256) throw new InvalidDataException("A maximum of 256 sheets is supported.");
            var id = (string?)info.Attribute(R + "id") ?? "";
            if (!relationships.TryGetValue(id, out var path)) throw new InvalidDataException("Unresolved worksheet relationship.");
            var sheet = new Worksheet { Name = (string?)info.Attribute("name") ?? "Sheet" + (book.Sheets.Count + 1) };
            var root = Xml(zip, path).Root!;
            NormalizeWorksheetFormulas(root, warnings, ref remainingSharedCharacters);
            foreach (var row in root.Element(S + "sheetData")?.Elements(S + "row") ?? [])
            {
                var rowIndex = Int(row.Attribute("r"), 1) - 1;
                if (rowIndex < 0 || rowIndex >= CellAddress.MaxRows) throw new InvalidDataException("Invalid worksheet row.");
                if (row.Attribute("ht") is not null) sheet.RowHeights[rowIndex] = Math.Clamp(Number(row.Attribute("ht")) * 96 / 72, 16, 600);
                if (Flag(row.Attribute("hidden"))) sheet.HiddenRows.Add(rowIndex);
                foreach (var cell in row.Elements(S + "c"))
                {
                    var address = CellAddress.Parse((string?)cell.Attribute("r") ?? throw new InvalidDataException("Cell address missing."));
                    var styleIndex = Int(cell.Attribute("s")); var style = styleIndex >= 0 && styleIndex < styles.Count ? styles[styleIndex] : CellStyle.Default;
                    var formula = cell.Element(S + "f"); var value = cell.Element(S + "v")?.Value ?? ""; var type = (string?)cell.Attribute("t");
                    string raw;
                    if (formula is not null)
                    {
                        if (formula.Attribute("t") is { Value: "array" }) { raw = "=" + formula.Value; warnings.Add("Array/spill formulas are not supported."); }
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
                    if (Flag(col.Attribute("hidden"))) sheet.HiddenColumns.Add(c);
                }
            }
            foreach (var merge in root.Element(S + "mergeCells")?.Elements(S + "mergeCell") ?? []) sheet.Merges.Add(CellRange.Parse((string?)merge.Attribute("ref") ?? "A1"));
            var view = root.Element(S + "sheetViews")?.Element(S + "sheetView"); var pane = view?.Element(S + "pane");
            if ((string?)pane?.Attribute("state") is "frozen" or "frozenSplit") { sheet.FrozenRows = Int(pane.Attribute("ySplit")); sheet.FrozenColumns = Int(pane.Attribute("xSplit")); }
            sheet.ShowGridLines = (string?)view?.Attribute("showGridLines") != "0";
            foreach (var validation in root.Element(S + "dataValidations")?.Elements(S + "dataValidation") ?? [])
            {
                var f = validation.Element(S + "formula1")?.Value ?? ""; var range = (string?)validation.Attribute("sqref") ?? "";
                if ((string?)validation.Attribute("type") == "list" && f.StartsWith('"') && f.EndsWith('"') && !range.Contains(' ')) sheet.ValidationLists[range] = f[1..^1].Split(',');
                else warnings.Add("Only inline list data validation is supported.");
            }
            ReadFilters(root, sheet, warnings);
            ReadConditionalFormats(root, sheet, differentials, warnings);
            if (ReadDataToolExtension(root, sheet)) explicitVisibility.Add(sheet);
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
        book.Attach();
        RestoreFilterVisibility(book, explicitVisibility, warnings);
        return new(book, warnings.Distinct().ToArray());
    }
}
