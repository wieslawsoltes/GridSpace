using System.Text.RegularExpressions;
using GridSpace.Core;
using GridSpace.Formulas;

namespace GridSpace.Editing;

public sealed partial class SpreadsheetSession
{
    public void AddSheet(string? name = null) => Perform("Insert worksheet", () =>
    {
        name ??= UniqueSheetName("Sheet");
        Workbook.ValidateSheetName(name);
        if (Book.FindSheet(name) is not null) throw new InvalidOperationException("A sheet with this name already exists.");
        Book.Sheets.Add(new() { Name = name });
        Book.ActiveSheetIndex = Book.Sheets.Count - 1;
        Selection = new(new(0, 0), new(0, 0));
    });
    private string UniqueSheetName(string prefix)
    {
        for (var i = 1; i < 10000; i++)
        {
            var name = prefix[..Math.Min(prefix.Length, 25)] + i;
            if (Book.FindSheet(name) is null) return name;
        }
        throw new InvalidOperationException("Could not allocate a sheet name.");
    }
    public void SwitchSheet(int index)
    {
        if (index < 0 || index >= Book.Sheets.Count) return;
        Book.ActiveSheetIndex = index;
        Selection = new(new(0, 0), new(0, 0));
        Notify("Switch worksheet");
    }
    public void RenameSheet(string name) => Perform("Rename worksheet", () =>
    {
        Workbook.ValidateSheetName(name);
        var old = Sheet.Name;
        if (Book.FindSheet(name) is { } other && other != Sheet) throw new InvalidOperationException("A sheet with this name already exists.");
        RewriteWorkbookReferences((formula, _) => FormulaReferences.RenameSheet(formula, old, name));
        foreach (var host in Book.Sheets)
        {
            foreach (var chart in host.Charts)
                if (!chart.SourceUnavailable && chart.SourceSheet?.Equals(old, StringComparison.OrdinalIgnoreCase) == true) chart.SourceSheet = name;
            foreach (var pivot in host.PivotTables)
                if (pivot.SourceSheet.Equals(old, StringComparison.OrdinalIgnoreCase)) pivot.SourceSheet = name;
        }
        Sheet.Name = name;
    });
    public void DuplicateSheet() => Perform("Duplicate worksheet", () =>
    {
        var single = new Workbook { Sheets = [Sheet] };
        var copy = Workbook.FromJson(single.ToJson()).Sheets[0];
        copy.Name = UniqueSheetName(Sheet.Name + " ");
        var ids = copy.PivotTables.ToDictionary(p => p.Id, _ => Guid.NewGuid().ToString("N"));
        var names = Book.Sheets.SelectMany(s => s.PivotTables).Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        copy.PivotTables = copy.PivotTables.Select(p =>
        {
            var name = p.Name; var number = 1;
            while (!names.Add(name)) name = p.Name[..Math.Min(110, p.Name.Length)] + "_Copy" + number++;
            return p with { Id = ids[p.Id], Name = name, SourceSheet = p.SourceSheet.Equals(Sheet.Name, StringComparison.OrdinalIgnoreCase) ? copy.Name : p.SourceSheet };
        }).ToList();
        copy.Charts = copy.Charts.Select(c => c with { Id = Guid.NewGuid().ToString("N"),
            SourceSheet = c.SourceSheet?.Equals(Sheet.Name, StringComparison.OrdinalIgnoreCase) == true ? copy.Name : c.SourceSheet,
            PivotTableId = c.PivotTableId is { } id && ids.TryGetValue(id, out var mapped) ? mapped : c.PivotTableId }).ToList();
        Book.Sheets.Insert(Book.ActiveSheetIndex + 1, copy);
        Book.ActiveSheetIndex++;
    });
    public void DeleteSheet() => Perform("Delete worksheet", () =>
    {
        if (Book.Sheets.Count == 1) throw new InvalidOperationException("A workbook must have at least one worksheet.");
        var deleted = Sheet.Name;
        if (Book.Sheets.Where(s => s != Sheet).SelectMany(s => s.PivotTables).Any(p => p.SourceSheet.Equals(deleted, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Remove the dependent PivotTables before deleting their source worksheet.");
        foreach (var host in Book.Sheets.Where(s => s != Sheet))
            foreach (var chart in host.Charts)
                if (chart.SourceSheet?.Equals(deleted, StringComparison.OrdinalIgnoreCase) == true) chart.SourceUnavailable = true;
        Book.Sheets.RemoveAt(Book.ActiveSheetIndex);
        Book.ActiveSheetIndex = Math.Min(Book.ActiveSheetIndex, Book.Sheets.Count - 1);
        RewriteWorkbookReferences((formula, _) => FormulaReferences.DeleteSheet(formula, deleted));
        Selection = new(new(0, 0), new(0, 0));
    });
    public void SetColumnWidth(int column, double width) => Perform("Column width", () => Sheet.ColumnWidths[column] = Math.Clamp(width, 24, 1000));
    public void SetRowHeight(int row, double height) => Perform("Row height", () => Sheet.RowHeights[row] = Math.Clamp(height, 16, 600));
    public void Merge() => Perform("Merge cells", () =>
    {
        if (Selection.Count == 1) return;
        var range = Selection.Normalized;
        RejectPivotWrite(range);
        if (Sheet.Merges.Any(m => m.Intersects(range))) throw new InvalidOperationException("Unmerge overlapping cells first.");
        if (range.Cells().Skip(1).Any(a => Sheet.Get(a).Input != "")) throw new InvalidOperationException("Only the upper-left cell may contain data. Clear other cells before merging.");
        Sheet.Merges.Add(range);
        var first = new CellAddress(range.Top, range.Left);
        Sheet.Set(first, Sheet.Get(first) with { Style = Sheet.Get(first).Style with { Alignment = CellAlignment.Center } });
    });
    public void Unmerge() => Perform("Unmerge cells", () => Sheet.Merges.RemoveAll(m => m.Intersects(Selection)));
    public void Freeze(bool firstRow = false) => Perform("Freeze panes", () =>
    {
        Sheet.FrozenRows = firstRow ? 1 : ActiveCell.Row;
        Sheet.FrozenColumns = firstRow ? 0 : ActiveCell.Column;
    });
    public void Unfreeze() => Perform("Unfreeze panes", () => { Sheet.FrozenRows = 0; Sheet.FrozenColumns = 0; });
    public void Sort(bool descending = false, bool header = true) => Sort([new SortLevel(ActiveCell.Column, descending)], header);
    public void Filter(int column, string query)
    {
        if (query.Length == 0) ClearFilters(column);
        else SetFilter(new ColumnFilter { Column = column, First = new FilterCondition(FilterOperator.Contains, query) });
    }
    public void FormatTable() => Perform("Format as table", () =>
    {
        var range = DataRange();
        foreach (var address in range.Cells())
        {
            var first = address.Row == range.Top;
            var style = Sheet.Get(address).Style with
            {
                Background = first ? "#107C41" : (address.Row - range.Top) % 2 == 1 ? "#E8F2EC" : "#FFFFFF",
                Foreground = first ? "#FFFFFF" : "#242424", Bold = first
            };
            Sheet.Set(address, Sheet.Get(address) with { Style = style });
        }
        if (Sheet.FilterRange != range.ToString()) { Sheet.Filters.Clear(); Sheet.FilteredRows.Clear(); }
        Sheet.FilterRange = range.ToString();
    });
    public CellAddress? Find(string query, bool next = true)
    {
        if (query.Length == 0) return null;
        var found = Sheet.Cells.Keys.Select(CellAddress.Parse).OrderBy(a => a.Row).ThenBy(a => a.Column)
            .Where(a => Sheet.Get(a).Input.Contains(query, StringComparison.OrdinalIgnoreCase) || Calculation.Evaluate(Sheet, a).ToString().Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (found.Length == 0) return null;
        var current = (long)ActiveCell.Row * CellAddress.MaxColumns + ActiveCell.Column;
        var result = next ? found.FirstOrDefault(a => (long)a.Row * CellAddress.MaxColumns + a.Column > current, found[0])
            : found.LastOrDefault(a => (long)a.Row * CellAddress.MaxColumns + a.Column < current, found[^1]);
        Select(new CellRange(result, result));
        return result;
    }
    public int ReplaceAll(string find, string replacement)
    {
        if (find == "") return 0;
        var count = 0;
        Perform("Replace all", () =>
        {
            foreach (var key in Sheet.Cells.Keys.ToArray())
            {
                var cell = Sheet.Cells[key];
                if (!cell.Input.Contains(find, StringComparison.OrdinalIgnoreCase)) continue;
                if (PivotAt(CellAddress.Parse(key)) is not null) continue;
                Sheet.Set(CellAddress.Parse(key), cell with { Input = cell.Input.Replace(find, replacement, StringComparison.OrdinalIgnoreCase) });
                count++;
            }
        });
        return count;
    }
    public void AutoSum()
    {
        var end = ActiveCell;
        var first = end.Row - 1;
        while (first >= 0 && Sheet.Get(new CellAddress(first, end.Column)).Input != "") first--;
        if (first == end.Row - 1) { SetInput("=SUM(A1:A1)"); return; }
        SetInput($"=SUM({new CellAddress(first + 1, end.Column)}:{new CellAddress(end.Row - 1, end.Column)})");
    }
    public void DefineName(string name)
    {
        if (!Regex.IsMatch(name, @"^[A-Za-z_][A-Za-z0-9_.]*$") || CellAddress.TryParse(name, out _)) throw new ArgumentException("Use a name such as SalesTotal, not a cell address.");
        Perform("Define name", () => Book.Names[name] = "'" + Sheet.Name.Replace("'", "''") + "'!" + Selection);
    }
    public void SetValidation(string[] values) => Perform("Data validation", () => Sheet.ValidationLists[Selection.ToString()] = values.Where(v => v.Length > 0).Distinct().ToArray());
    public void AddChart(ChartKind kind)
    {
        var range = Selection.Count == 1 ? Sheet.UsedRange : Selection.Normalized;
        AddChart(new ChartSpec { Title = "Chart title", Kind = kind, Range = range.ToString(),
            Row = range.Top + 2, Column = Math.Min(range.Right + 2, CellAddress.MaxColumns - 1) });
    }
    public void AddNote(string note) => Perform("Cell note", () => Sheet.Set(ActiveCell, Sheet.Get(ActiveCell) with { Note = note }));
}
