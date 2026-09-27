using System.Xml.Linq;
using GridSpace.Core;
using GridSpace.Formulas;

namespace GridSpace.IO;

public static partial class XlsxWorkbook
{
    private static readonly XNamespace G = "urn:gridspace:worksheet-data-tools:1";
    private const string DataToolsExtension = "{C972DAB5-2714-4C7A-B4BC-48ADB4498D8F}";

    private static XElement? WriteAutoFilter(Worksheet sheet)
    {
        if (sheet.FilterRange is null) return null;
        var range = CellRange.Parse(sheet.FilterRange);
        var result = E("autoFilter", new XAttribute("ref", sheet.FilterRange));
        foreach (var filter in sheet.Filters.OrderBy(f => f.Column))
        {
            var column = E("filterColumn", new XAttribute("colId", filter.Column - range.Left));
            if (filter.Values is not null)
                column.Add(E("filters", new XAttribute("blank", filter.IncludeBlank ? 1 : 0), filter.Values.Select(v => E("filter", new XAttribute("val", v)))));
            else if (filter.First is not null)
            {
                var conditions = E("customFilters", new XAttribute("and", filter.And ? 1 : 0));
                foreach (var condition in new[] { filter.First, filter.Second }.OfType<FilterCondition>())
                {
                    var op = condition.Operator;
                    var value = condition.Value;
                    if (op is FilterOperator.Contains or FilterOperator.DoesNotContain) value = "*" + value + "*";
                    else if (op == FilterOperator.BeginsWith) value += "*";
                    else if (op == FilterOperator.EndsWith) value = "*" + value;
                    else if (op is FilterOperator.Blank or FilterOperator.NotBlank) value = "";
                    var name = op switch
                    {
                        FilterOperator.Contains or FilterOperator.BeginsWith or FilterOperator.EndsWith or FilterOperator.Blank => "equal",
                        FilterOperator.DoesNotContain or FilterOperator.NotBlank => "notEqual",
                        _ => Camel(op)
                    };
                    conditions.Add(E("customFilter", new XAttribute("operator", name), new XAttribute("val", value)));
                }
                column.Add(conditions);
            }
            else continue;
            result.Add(column);
        }
        if (WriteSortState(sheet) is { } sort) result.Add(sort);
        return result;
    }

    private static XElement? WriteSortState(Worksheet sheet)
    {
        if (sheet.SortRange is null || sheet.SortLevels.Count == 0) return null;
        var range = CellRange.Parse(sheet.SortRange);
        var top = Math.Min(range.Bottom, range.Top + (sheet.SortHasHeader ? 1 : 0));
        var data = new CellRange(new CellAddress(top, range.Left), new CellAddress(range.Bottom, range.Right));
        return E("sortState", new XAttribute("ref", data), new XAttribute("caseSensitive", sheet.SortCaseSensitive ? 1 : 0),
            sheet.SortLevels.Select(level => E("sortCondition", new XAttribute("ref", new CellRange(new CellAddress(top, level.Column), new CellAddress(range.Bottom, level.Column))), new XAttribute("descending", level.Descending ? 1 : 0))));
    }

    private static void ReadFilters(XElement root, Worksheet sheet, List<string> warnings)
    {
        var autoFilter = root.Element(S + "autoFilter");
        if (autoFilter is not null && CellRange.TryParse((string?)autoFilter.Attribute("ref"), out var range))
        {
            sheet.FilterRange = range.ToString();
            foreach (var column in autoFilter.Elements(S + "filterColumn"))
            {
                if (sheet.Filters.Count >= 256) { warnings.Add("Filter columns beyond the 256-column limit were skipped."); break; }
                try
                {
                    var index = range.Left + Int(column.Attribute("colId"), -1);
                    if (index < range.Left || index > range.Right) throw new InvalidDataException("Invalid AutoFilter column offset.");
                    var filter = new ColumnFilter { Column = index };
                    if (column.Element(S + "filters") is { } values)
                    {
                        if (values.Elements().Any(e => e.Name != S + "filter")) throw new NotSupportedException("Date-group filters are not supported.");
                        filter = filter with { Values = values.Elements(S + "filter").Select(v => (string?)v.Attribute("val") ?? "").ToArray(), IncludeBlank = Flag(values.Attribute("blank")) };
                    }
                    else if (column.Element(S + "customFilters") is { } custom)
                    {
                        var conditions = custom.Elements(S + "customFilter").Select(c =>
                        {
                            if (!Enum.TryParse<FilterOperator>((string?)c.Attribute("operator") ?? "equal", true, out var op) || op > FilterOperator.LessThanOrEqual)
                                throw new NotSupportedException("Unsupported custom filter comparison.");
                            return new FilterCondition(op, (string?)c.Attribute("val") ?? "");
                        }).ToArray();
                        if (conditions.Length is < 1 or > 2) throw new InvalidDataException("A custom filter needs one or two conditions.");
                        filter = filter with { First = conditions[0], Second = conditions.Length == 2 ? conditions[1] : null, And = Flag(custom.Attribute("and")) };
                    }
                    else if (column.HasElements) throw new NotSupportedException("Color, icon, top-10 and dynamic filters are not supported.");
                    else continue;
                    filter.Validate();
                    if (sheet.Filters.Any(f => f.Column == index)) throw new InvalidDataException("Duplicate AutoFilter column.");
                    sheet.Filters.Add(filter);
                }
                catch (Exception error) when (error is InvalidDataException or NotSupportedException or ArgumentException)
                {
                    warnings.Add("AutoFilter: " + error.Message);
                }
            }
        }
        var sort = autoFilter?.Element(S + "sortState") ?? root.Element(S + "sortState");
        if (sort is null) return;
        try
        {
            if (Flag(sort.Attribute("columnSort"))) throw new NotSupportedException("Left-to-right sorting is not supported.");
            var area = CellRange.Parse((string?)sort.Attribute("ref") ?? "");
            var levels = new List<SortLevel>();
            foreach (var condition in sort.Elements(S + "sortCondition"))
            {
                if (condition.Attribute("customList") is not null || (string?)condition.Attribute("sortBy") is { } by && by != "value")
                    throw new NotSupportedException("Only value-based sort levels are supported.");
                var column = CellRange.Parse((string?)condition.Attribute("ref") ?? "");
                if (column.Left != column.Right || column.Left < area.Left || column.Right > area.Right) throw new InvalidDataException("Invalid sort column.");
                levels.Add(new SortLevel(column.Left, Flag(condition.Attribute("descending"))));
            }
            if (levels.Count > 64) throw new NotSupportedException("Only 64 sort levels are supported.");
            sheet.SortRange = area.ToString(); sheet.SortHasHeader = false;
            sheet.SortCaseSensitive = Flag(sort.Attribute("caseSensitive")); sheet.SortLevels = levels;
        }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or FormatException)
        {
            warnings.Add("Sort state: " + error.Message);
        }
    }

    // OOXML has one row.hidden flag. This optional extension preserves manual hiding separately in GridSpace-to-GridSpace roundtrips.
    private static void WriteDataToolExtension(Worksheet sheet, XElement root)
    {
        if (sheet.Filters.Count == 0 && sheet.SortLevels.Count == 0 && sheet.ConditionalFormats.Count == 0) return;
        var state = new XElement(G + "state", new XAttribute("manualRows", string.Join(',', sheet.HiddenRows.Order())), new XAttribute("filteredRows", string.Join(',', sheet.FilteredRows.Order())));
        if (sheet.SortRange is not null) state.Add(new XAttribute("sortRange", sheet.SortRange), new XAttribute("sortHasHeader", sheet.SortHasHeader ? 1 : 0));
        foreach (var rule in sheet.ConditionalFormats.Where(r => r.Kind == ConditionalFormatKind.DataBar))
            state.Add(new XElement(G + "bar", new XAttribute("priority", rule.Priority), new XAttribute("range", rule.Range), new XAttribute("negativeColor", rule.LowColor)));
        root.Add(E("extLst", E("ext", new XAttribute("uri", DataToolsExtension), state)));
    }

    private static bool ReadDataToolExtension(XElement root, Worksheet sheet)
    {
        var state = root.Element(S + "extLst")?.Elements(S + "ext").FirstOrDefault(e => (string?)e.Attribute("uri") == DataToolsExtension)?.Element(G + "state");
        if (state is null) return false;
        HashSet<int> Rows(string attribute)
        {
            var result = new HashSet<int>();
            foreach (var value in ((string?)state.Attribute(attribute) ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!int.TryParse(value, out var index) || index < 0 || index >= CellAddress.MaxRows) throw new InvalidDataException("Invalid row index in GridSpace visibility metadata.");
                result.Add(index);
            }
            return result;
        }
        sheet.HiddenRows = Rows("manualRows"); sheet.FilteredRows = Rows("filteredRows");
        if ((string?)state.Attribute("sortRange") is { } sortRange) { sheet.SortRange = CellRange.Parse(sortRange).ToString(); sheet.SortHasHeader = Flag(state.Attribute("sortHasHeader")); }
        foreach (var bar in state.Elements(G + "bar"))
        {
            var index = sheet.ConditionalFormats.FindIndex(r => r.Kind == ConditionalFormatKind.DataBar && r.Priority == Int(bar.Attribute("priority")) && r.Range == (string?)bar.Attribute("range"));
            if (index < 0) continue;
            var color = (string?)bar.Attribute("negativeColor") ?? "#F8696B"; DifferentialStyle.ValidateColor(color);
            sheet.ConditionalFormats[index] = sheet.ConditionalFormats[index] with { LowColor = color };
        }
        return true;
    }

    private static void RestoreFilterVisibility(Workbook book, HashSet<Worksheet> explicitVisibility, List<string> warnings)
    {
        var calculation = new CalculationEngine(book);
        foreach (var sheet in book.Sheets.Where(s => s.Filters.Count > 0 && !explicitVisibility.Contains(s)))
        {
            sheet.FilteredRows = WorksheetFilterEngine.Evaluate(sheet, calculation);
            sheet.HiddenRows.ExceptWith(sheet.FilteredRows);
            warnings.Add("For external XLSX files, manual hiding overlapping filtered-out rows cannot be distinguished from filtering. Keep the original workbook.");
        }
        book.Touch();
    }
}
