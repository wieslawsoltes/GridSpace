using GridSpace.Core;
using GridSpace.Formulas;

namespace GridSpace.Editing;

public sealed partial class SpreadsheetSession
{
    public PivotTableSpec? PivotAt(CellAddress address) => Sheet.PivotTables.FirstOrDefault(p =>
        p.OutputRange is { } text && CellRange.Parse(text).Contains(address));
    public PivotTableSpec? ActivePivot => PivotAt(ActiveCell);

    /// <summary>Calculates and checks the entire replacement before mutating owned report cells. History is O(output), not O(workbook).</summary>
    public PivotReport SetPivotTable(PivotTableSpec definition)
    {
        // A live refresh captures a replacement cache, not the previous schema's cache.
        ArgumentNullException.ThrowIfNull(definition);
        definition = definition with { Cache = null };
        definition.Validate();
        definition = definition.CloneDocument();
        var original = Sheet.PivotTables.FirstOrDefault(p => p.Id == definition.Id);
        if (original is null && Sheet.PivotTables.Count >= 32) throw new InvalidOperationException("A sheet supports at most 32 PivotTables.");
        if (Book.Sheets.SelectMany(s => s.PivotTables).Any(p => p.Id != definition.Id && p.Name.Equals(definition.Name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("PivotTable names must be unique within the workbook.");
        if (original is null && Book.Sheets.Where(s => s != Sheet).SelectMany(s => s.PivotTables).Any(p => p.Id == definition.Id))
            throw new InvalidOperationException("PivotTable identifiers must be unique within the workbook.");
        var source = PivotEngine.Capture(Book, definition, Calculation);
        var report = PivotEngine.Build(source, definition);
        var anchor = definition.Anchor;
        var output = new CellRange(anchor, new(anchor.Row + report.RowCount - 1, anchor.Column + report.ColumnCount - 1));
        var oldRange = original?.OutputRange is { } old ? CellRange.Parse(old) : (CellRange?)null;
        foreach (var pivot in Book.Sheets.SelectMany(s => s.PivotTables).Append(definition))
            if (pivot.SourceSheet.Equals(Sheet.Name, StringComparison.OrdinalIgnoreCase) && CellRange.Parse(pivot.SourceRange).Intersects(output))
                throw new InvalidOperationException("A PivotTable cannot overwrite a source data range.");
        if (Sheet.PivotTables.Any(p => p.Id != definition.Id && p.OutputRange is { } r && CellRange.Parse(r).Intersects(output))
            || Sheet.Merges.Any(r => r.Intersects(output)) || Calculation.GetSpills(Sheet).Any(p => p.Range.Intersects(output)))
            throw new InvalidOperationException("PivotTable output overlaps another report, merged range or spilled array.");
        foreach (var address in output.Cells())
            if (!(oldRange?.Contains(address) ?? false) && Sheet.Get(address).Input.Length > 0)
                throw new InvalidOperationException("PivotTable output would overwrite " + address + ". Choose an empty destination.");
        var after = definition.CloneDocument() with
        {
            OutputRange = output.ToString(),
            NeedsLayoutRefresh = false,
            Cache = PivotEngine.ToCache(source),
            ChartRange = new CellRange(anchor, new(anchor.Row + report.RowKeys.Count, anchor.Column + report.LabelColumns
                + report.ColumnKeys.Count * definition.Values.Count - 1)).ToString(),
            FieldNames = report.Source.Headers.ToArray(),
            LastSourceRowCount = report.Source.Rows.Count
        };
        var changes = new Dictionary<CellAddress, Cell>();
        if (oldRange is { } previous) foreach (var address in previous.Cells()) changes[address] = Cell.Empty;
        for (var r = 0; r < report.RowCount; r++)
        for (var c = 0; c < report.ColumnCount; c++)
        {
            var address = new CellAddress(anchor.Row + r, anchor.Column + c);
            var header = r == 0;
            var grand = r > report.RowKeys.Count || c >= report.LabelColumns + report.ColumnKeys.Count * definition.Values.Count;
            var format = c < report.LabelColumns ? "General" : definition.Values[(c - report.LabelColumns) % definition.Values.Count] is { } value
                ? value.ShowAs == PivotShowAs.Normal ? value.NumberFormat : "0.00%" : "General";
            var style = Sheet.Get(address).Style with
            {
                Bold = header || grand, Background = header ? "#107C41" : grand ? "#DCEDE2" : r % 2 == 0 ? "#F0F6F2" : "#FFFFFF",
                Foreground = header ? "#FFFFFF" : "#242424", NumberFormat = header ? "General" : format
            };
            var input = LiteralInput(report[r, c]);
            if (input.Length > 32767) throw new InvalidOperationException("A PivotTable output value is too long.");
            changes[address] = new Cell { Input = input, Style = style };
        }
        var patches = changes.Select(p => new CellPatch(p.Key, Sheet.Get(p.Key), p.Value)).Where(p => p.Before != p.After).ToArray();
        var removed = patches.Count(p => p.Before != Cell.Empty && p.After == Cell.Empty);
        var added = patches.Count(p => p.Before == Cell.Empty && p.After != Cell.Empty);
        if (Sheet.Cells.Count - removed + added > 200_000) throw new InvalidOperationException("PivotTable output exceeds the worksheet stored-cell limit.");
        var index = original is null ? Sheet.PivotTables.Count : Sheet.PivotTables.IndexOf(original);
        var metadata = new PivotPatch(index, original?.CloneDocument(), after);
        foreach (var patch in patches) Sheet.Set(patch.Address, patch.After);
        ReplayPivot(metadata, true);
        if (!_inTransaction)
        {
            _undo.Add(new("Refresh " + after.Name, "", "", Selection) { Patches = patches, Pivot = metadata, SheetIndex = Book.ActiveSheetIndex });
            _redo.Clear(); TrimHistory(); IsDirty = true; Notify("Refresh " + after.Name, true);
        }
        return report;
    }

    public void RefreshPivotTable(string id)
    {
        var pivot = Sheet.PivotTables.FirstOrDefault(p => p.Id == id) ?? throw new InvalidOperationException("Select a PivotTable first.");
        SetPivotTable(pivot);
    }

    public void RefreshAllPivotTables() => Perform("Refresh all PivotTables", () =>
    {
        var selected = Book.ActiveSheetIndex;
        for (var i = 0; i < Book.Sheets.Count; i++)
        {
            Book.ActiveSheetIndex = i;
            foreach (var pivot in Sheet.PivotTables.ToArray()) SetPivotTable(pivot);
        }
        Book.ActiveSheetIndex = selected;
    });

    public void RemovePivotTable(string id)
    {
        var pivot = Sheet.PivotTables.FirstOrDefault(p => p.Id == id);
        if (pivot is null) return;
        var patches = pivot.OutputRange is { } range ? CellRange.Parse(range).Cells()
            .Select(a => new CellPatch(a, Sheet.Get(a), Cell.Empty)).Where(p => p.Before != p.After).ToArray() : [];
        var metadata = new PivotPatch(Sheet.PivotTables.IndexOf(pivot), pivot.CloneDocument(), null);
        foreach (var patch in patches) Sheet.Set(patch.Address, patch.After);
        ReplayPivot(metadata, true);
        if (_inTransaction) return;
        _undo.Add(new("Remove PivotTable", "", "", Selection) { Patches = patches, Pivot = metadata, SheetIndex = Book.ActiveSheetIndex });
        _redo.Clear(); TrimHistory(); IsDirty = true; Notify("Remove PivotTable", true);
    }

    public void DrillDownPivot(CellAddress address)
    {
        var pivot = PivotAt(address) ?? throw new InvalidOperationException("Select a PivotTable value.");
        if (pivot.NeedsLayoutRefresh) throw new InvalidOperationException("Refresh this imported PivotTable before drilling through its converted layout.");
        var report = PivotEngine.FromCache(pivot);
        var rows = report.DrillDown(address.Row - pivot.Anchor.Row, address.Column - pivot.Anchor.Column);
        if ((long)(rows.Count + 1) * report.Source.Headers.Length > 100_000)
            throw new InvalidOperationException("Drill-through exceeds the 100,000-cell operation limit.");
        Perform("Show PivotTable details", () =>
        {
            AddSheet(UniqueSheetName("Details"));
            for (var c = 0; c < report.Source.Headers.Length; c++)
                Sheet.Set(new CellAddress(0, c), new Cell { Input = "'" + report.Source.Headers[c], Style = CellStyle.Default with { Bold = true, Background = "#DCEDE2" } });
            for (var r = 0; r < rows.Count; r++) for (var c = 0; c < rows[r].Length; c++)
                Sheet.Set(new CellAddress(r + 1, c), new Cell { Input = LiteralInput(rows[r][c]) });
        });
    }

    public string AddPivotChart(string id)
    {
        var pivot = Sheet.PivotTables.FirstOrDefault(p => p.Id == id) ?? throw new InvalidOperationException("Select a PivotTable first.");
        if (pivot.ChartRange is null) throw new InvalidOperationException("Refresh the PivotTable before charting it.");
        var range = CellRange.Parse(pivot.ChartRange);
        return AddChart(new ChartSpec
        {
            Title = pivot.Name, Kind = ChartKind.Column, Range = pivot.ChartRange, SourceSheet = Sheet.Name, PivotTableId = pivot.Id,
            Row = range.Top + 1, Column = Math.Min(range.Right + 2, CellAddress.MaxColumns - 1), Width = 600, Height = 340
        });
    }

    private void ReplayPivot(PivotPatch patch, bool forward)
    {
        Sheet.PivotTables.RemoveAll(p => p.Id == patch.Id);
        var document = forward ? patch.After : patch.Before;
        if (document is not null) Sheet.PivotTables.Insert(Math.Min(patch.Index, Sheet.PivotTables.Count), document.CloneDocument());
        Book.TouchDrawings();
    }
}
