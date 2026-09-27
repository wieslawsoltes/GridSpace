using System.IO.Compression;
using System.Xml.Linq;
using GridSpace.Core;
using GridSpace.Formulas;

namespace GridSpace.IO;

public static partial class XlsxWorkbook
{
    public static byte[] Write(Workbook book)
    {
        book.Attach(); var engine = new CalculationEngine(book); var styles = new List<CellStyle> { CellStyle.Default };
        var differentials = new List<DifferentialStyle>();
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
                foreach (var r in grouped.Keys.Concat(sheet.RowHeights.Keys).Concat(sheet.HiddenRows).Concat(sheet.FilteredRows).Distinct().Order())
                {
                    var row = E("row", new XAttribute("r", r + 1));
                    if (sheet.RowHeights.TryGetValue(r, out var height)) row.Add(new XAttribute("ht", F(height * 72 / 96)), new XAttribute("customHeight", 1));
                    if (sheet.IsRowHidden(r)) row.Add(new XAttribute("hidden", 1));
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
                if (WriteAutoFilter(sheet) is { } filter) root.Add(filter);
                else if (WriteSortState(sheet) is { } sort) root.Add(sort);
                if (sheet.Merges.Count > 0) root.Add(E("mergeCells", new XAttribute("count", sheet.Merges.Count), sheet.Merges.Select(m => E("mergeCell", new XAttribute("ref", m)))));
                WriteConditionalFormats(sheet, root, differentials);
                if (sheet.ValidationLists.Count > 0) root.Add(E("dataValidations", new XAttribute("count", sheet.ValidationLists.Count), sheet.ValidationLists.Select(p => E("dataValidation", new XAttribute("type", "list"), new XAttribute("allowBlank", 1), new XAttribute("showErrorMessage", 1), new XAttribute("sqref", p.Key), E("formula1", "\"" + string.Join(',', p.Value) + "\"")))));
                WriteCharts(zip, sheet, n, root, engine, Type);
                WriteDataToolExtension(sheet, root);
                WritePart(zip, "xl/worksheets/sheet" + n + ".xml", root);
            }
            if (book.Names.Count > 0) workbook.Add(E("definedNames", book.Names.Select(p => E("definedName", new XAttribute("name", p.Key), p.Value.TrimStart('=')))));
            workbook.Add(E("calcPr", new XAttribute("calcId", 191029), new XAttribute("fullCalcOnLoad", 1), new XAttribute("forceFullCalc", 1)));
            rels.Add(Relationship("styles", "styles", "styles.xml"));
            WritePart(zip, "xl/workbook.xml", workbook); WritePart(zip, "xl/_rels/workbook.xml.rels", rels);
            WritePart(zip, "xl/styles.xml", AppendDifferentials(WriteStyles(styles), differentials)); WritePart(zip, "[Content_Types].xml", types);
        }
        return output.ToArray();
    }
}
