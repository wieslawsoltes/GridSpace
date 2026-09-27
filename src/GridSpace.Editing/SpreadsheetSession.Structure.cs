using GridSpace.Core;
using GridSpace.Formulas;

namespace GridSpace.Editing;

public sealed partial class SpreadsheetSession
{
    public void InsertRows(int position, int count = 1) => ApplyAxisEdit(new AxisEdit(true, position, count, false));
    public void InsertColumns(int position, int count = 1) => ApplyAxisEdit(new AxisEdit(false, position, count, false));
    public void DeleteRows(int position, int count = 1) => ApplyAxisEdit(new AxisEdit(true, position, count, true));
    public void DeleteColumns(int position, int count = 1) => ApplyAxisEdit(new AxisEdit(false, position, count, true));
    public void Delete(bool rows)
    {
        if (rows) DeleteRows(Selection.Top, Selection.Bottom - Selection.Top + 1);
        else DeleteColumns(Selection.Left, Selection.Right - Selection.Left + 1);
    }

    public void Insert(bool rows)
    {
        if (rows) InsertRows(Selection.Top, Selection.Bottom - Selection.Top + 1);
        else InsertColumns(Selection.Left, Selection.Right - Selection.Left + 1);
    }

    private void ApplyAxisEdit(AxisEdit edit)
    {
        Perform((edit.IsDeletion ? "Delete " : "Insert ") + (edit.Rows ? "rows" : "columns"), () =>
        {
            var name = Sheet.Name;
            var moved = new Dictionary<string, Cell>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, cell) in Sheet.Cells)
            {
                var address = edit.Map(CellAddress.Parse(key));
                if (address is null)
                {
                    if (!edit.IsDeletion) throw new InvalidOperationException("Insertion would move stored cells outside the worksheet.");
                    continue;
                }
                moved.Add(address.Value.ToString(), cell);
            }
            Sheet.Cells = moved;
            TransformMetadata(edit);
            RewriteWorkbookReferences((formula, originSheet) => FormulaReferences.Edit(formula, originSheet, name, edit));
            var selection = edit.Map(Selection);
            var fallback = edit.Rows ? new CellAddress(Math.Min(edit.Position, CellAddress.MaxRows - 1), Selection.Left)
                : new CellAddress(Selection.Top, Math.Min(edit.Position, CellAddress.MaxColumns - 1));
            Selection = selection ?? new CellRange(fallback, fallback);
            ReapplyFiltersCore();
        });
    }

    private void TransformMetadata(AxisEdit edit)
    {
        CellRange? MapRange(CellRange range)
        {
            var mapped = edit.Map(range);
            if (mapped is null && !edit.IsDeletion) throw new InvalidOperationException("Insertion would move worksheet metadata outside the worksheet.");
            return mapped;
        }
        string? MapText(string? text) => text is null ? null : MapRange(CellRange.Parse(text))?.ToString();
        Dictionary<int, double> Sizes(Dictionary<int, double> input)
        {
            var result = new Dictionary<int, double>();
            foreach (var (index, value) in input)
            {
                var mapped = edit.MapIndex(index);
                if (mapped is not null) result[mapped.Value] = value;
                else if (!edit.IsDeletion) throw new InvalidOperationException("Insertion would move row/column sizes outside the worksheet.");
            }
            return result;
        }
        HashSet<int> Indices(HashSet<int> input)
        {
            var result = new HashSet<int>();
            foreach (var index in input)
            {
                var mapped = edit.MapIndex(index);
                if (mapped is not null) result.Add(mapped.Value);
                else if (!edit.IsDeletion) throw new InvalidOperationException("Insertion would move hidden rows/columns outside the worksheet.");
            }
            return result;
        }
        Sheet.Merges = Sheet.Merges.Select(MapRange).Where(r => r is { Count: > 1 }).Select(r => r!.Value).ToList();
        if (edit.Rows)
        {
            Sheet.RowHeights = Sizes(Sheet.RowHeights);
            Sheet.HiddenRows = Indices(Sheet.HiddenRows);
            Sheet.FilteredRows = Indices(Sheet.FilteredRows);
            Sheet.FrozenRows = edit.MapBoundary(Sheet.FrozenRows);
        }
        else
        {
            Sheet.ColumnWidths = Sizes(Sheet.ColumnWidths);
            Sheet.HiddenColumns = Indices(Sheet.HiddenColumns);
            Sheet.FrozenColumns = edit.MapBoundary(Sheet.FrozenColumns);
            Sheet.Filters = Sheet.Filters.Select(f => edit.MapIndex(f.Column) is { } c ? f with { Column = c } : null).OfType<ColumnFilter>().ToList();
            Sheet.SortLevels = Sheet.SortLevels.Select(s => edit.MapIndex(s.Column) is { } c ? s with { Column = c } : null).OfType<SortLevel>().ToList();
        }
        // Removing an AutoFilter header invalidates the filter rather than silently treating a data row as its header.
        var deletesHeader = edit.IsDeletion && edit.Rows && CellRange.TryParse(Sheet.FilterRange, out var oldFilter)
            && oldFilter.Top >= edit.Position && oldFilter.Top < edit.EndExclusive;
        Sheet.FilterRange = deletesHeader ? null : MapText(Sheet.FilterRange);
        if (Sheet.FilterRange is null) { Sheet.Filters.Clear(); Sheet.FilteredRows.Clear(); }
        Sheet.SortRange = MapText(Sheet.SortRange);
        if (Sheet.SortRange is null) Sheet.SortLevels.Clear();
        var validations = new Dictionary<string, string[]>();
        foreach (var (range, values) in Sheet.ValidationLists)
            if (MapText(range) is { } mapped) validations[mapped] = values;
        Sheet.ValidationLists = validations;
        var rules = new List<ConditionalFormatRule>();
        foreach (var rule in Sheet.ConditionalFormats)
        {
            var old = CellRange.Parse(rule.Range);
            if (MapRange(old) is not { } mapped) continue;
            // When the rule's top/left anchor is deleted, first move the relative formula to the first surviving old cell.
            var rowShift = edit.IsDeletion && edit.Rows && old.Top >= edit.Position && old.Top < edit.EndExclusive ? edit.EndExclusive - old.Top : 0;
            var colShift = edit.IsDeletion && !edit.Rows && old.Left >= edit.Position && old.Left < edit.EndExclusive ? edit.EndExclusive - old.Left : 0;
            var updated = rule with { Range = mapped.ToString() };
            if (rule.Kind is ConditionalFormatKind.Expression or ConditionalFormatKind.CellValue)
                updated = updated with
                {
                    Operand = FormulaReferences.Translate("=" + rule.Operand.TrimStart('='), rowShift, colShift),
                    Operand2 = FormulaReferences.Translate("=" + rule.Operand2.TrimStart('='), rowShift, colShift)
                };
            rules.Add(updated);
        }
        Sheet.ConditionalFormats = rules;
        var charts = new List<ChartSpec>();
        foreach (var chart in Sheet.Charts)
        {
            if (MapText(chart.Range) is not { } range) continue;
            var anchor = edit.Map(new CellAddress(chart.Row, chart.Column));
            if (anchor is null && !edit.IsDeletion) throw new InvalidOperationException("Insertion would move a chart outside the worksheet.");
            chart.Range = range;
            if (edit.Rows) chart.Row = anchor?.Row ?? Math.Min(edit.Position, CellAddress.MaxRows - 1);
            else chart.Column = anchor?.Column ?? Math.Min(edit.Position, CellAddress.MaxColumns - 1);
            charts.Add(chart);
        }
        Sheet.Charts = charts;
    }

    private void RewriteWorkbookReferences(Func<string, string, string> transform)
    {
        foreach (var sheet in Book.Sheets)
        {
            foreach (var key in sheet.Cells.Keys.ToArray())
            {
                var cell = sheet.Cells[key];
                if (cell.IsFormula) sheet.Cells[key] = cell with { Input = transform(cell.Input, sheet.Name) };
            }
            sheet.ConditionalFormats = sheet.ConditionalFormats.Select(rule => rule.Kind is ConditionalFormatKind.Expression or ConditionalFormatKind.CellValue
                ? rule with { Operand = transform("=" + rule.Operand.TrimStart('='), sheet.Name), Operand2 = transform("=" + rule.Operand2.TrimStart('='), sheet.Name) }
                : rule).ToList();
        }
        foreach (var key in Book.Names.Keys.ToArray())
            Book.Names[key] = transform("=" + Book.Names[key].TrimStart('='), Sheet.Name).TrimStart('=');
    }
}
