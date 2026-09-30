using GridSpace.Core;
using GridSpace.Formulas;

namespace GridSpace.Editing;

public sealed partial class SpreadsheetSession
{
    public PivotTableSpec? PivotAt(CellAddress address) => Sheet.PivotTables.FirstOrDefault(p =>
        p.OutputRange is { } text && CellRange.Parse(text).Contains(address));
    public PivotTableSpec? ActivePivot => PivotAt(ActiveCell);

    /// <summary>Calculates and checks the entire replacement before mutating owned report cells. History is O(output), not O(workbook).</summary>
    public PivotReport SetPivotTable(PivotTableSpec definition) => ApplyPivotTable(definition, refreshSource: true);

    /// <summary>
    /// Rebuilds a report from its immutable last-refresh cache. Source/capture-policy changes
    /// explicitly acquire a new cache; ordinary field, filter, measure and layout edits do not.
    /// Caller-supplied caches are never trusted as the source of an existing report.
    /// </summary>
    public PivotReport ReconfigurePivotTable(PivotTableSpec definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var original = Sheet.PivotTables.FirstOrDefault(p => p.Id == definition.Id)
            ?? throw new InvalidOperationException("Create the PivotTable before changing its layout.");
        var refresh = !original.SourceSheet.Equals(definition.SourceSheet, StringComparison.OrdinalIgnoreCase)
            || CellRange.Parse(original.SourceRange).Normalized != CellRange.Parse(definition.SourceRange).Normalized
            || original.IncludeHiddenRows != definition.IncludeHiddenRows;
        return ApplyPivotTable(definition, refresh);
    }

    private PivotReport ApplyPivotTable(PivotTableSpec definition, bool refreshSource)
    {
        // A live refresh captures a replacement cache, not the previous schema's cache.
        ArgumentNullException.ThrowIfNull(definition);
        definition = (definition with { Cache = null }).CloneDocument();
        definition.NormalizeCollapseState();
        definition.Validate();
        var original = Sheet.PivotTables.FirstOrDefault(p => p.Id == definition.Id);
        if (original is null && Sheet.PivotTables.Count >= 32) throw new InvalidOperationException("A sheet supports at most 32 PivotTables.");
        if (Book.Sheets.SelectMany(s => s.PivotTables).Any(p => p.Id != definition.Id && p.Name.Equals(definition.Name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("PivotTable names must be unique within the workbook.");
        if (original is null && Book.Sheets.Where(s => s != Sheet).SelectMany(s => s.PivotTables).Any(p => p.Id == definition.Id))
            throw new InvalidOperationException("PivotTable identifiers must be unique within the workbook.");
        PivotCacheSnapshot? cache = null;
        if (!refreshSource)
        {
            if (original is null || original.NeedsLayoutRefresh || original.Cache is null)
                throw new InvalidOperationException("Refresh this PivotTable before changing its cached layout.");
            cache = original.Cache;
        }
        var source = cache is null ? PivotEngine.Capture(Book, definition, Calculation) : PivotEngine.FromCache(cache);
        if (refreshSource && original is not null)
            definition.CollapsedRows.RemoveAll(path => path.Fields.Any(field => field >= original.FieldNames.Length
                || !source.Headers[field].Equals(original.FieldNames[field], StringComparison.OrdinalIgnoreCase)));
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
            Cache = cache ?? PivotEngine.ToCache(source),
            ChartRange = new CellRange(anchor, new(anchor.Row + report.RowBands.Count(b => b.Kind != PivotRowKind.GrandTotal), anchor.Column + report.LabelColumns
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
            var grand = r > 0 && report.RowBands[r - 1].Kind is PivotRowKind.Subtotal or PivotRowKind.Collapsed or PivotRowKind.GrandTotal || c >= report.LabelColumns + report.ColumnKeys.Count * definition.Values.Count;
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
        PivotReportCache.Remember(Sheet.PivotTables[index], report);
        if (!_inTransaction)
        {
            var action = (refreshSource ? "Refresh " : "Layout ") + after.Name;
            _undo.Add(new(action, "", "", Selection) { Patches = patches, Pivot = metadata, SheetIndex = Book.ActiveSheetIndex });
            _redo.Clear(); TrimHistory(); IsDirty = true; Notify(action, true);
        }
        return report;
    }

    /// <summary>Expand or collapse a typed group from the report's last-refresh snapshot, never from live source edits.</summary>
    public void TogglePivotGroup(string id, PivotGroupPath group)
    {
        ArgumentNullException.ThrowIfNull(group);
        var pivot = Sheet.PivotTables.FirstOrDefault(p => p.Id == id) ?? throw new InvalidOperationException("Select a PivotTable first.");
        var report = PivotReportCache.Get(pivot);
        if (!group.IsCompatible(pivot.Rows) || group.Values.IsDefault || group.Values.Length != group.Fields.Length)
            throw new ArgumentException("The row group no longer belongs to this PivotTable.");
        var key = PivotHierarchy.Key(group);
        if (!report.RowKeys.Any(leaf => PivotHierarchy.StartsWith(leaf, key)))
            throw new InvalidOperationException("This row group no longer exists in the filtered report.");
        var next = pivot.CloneDocument();
        var removed = next.CollapsedRows.RemoveAll(path => PivotHierarchy.SamePath(path, group));
        if (removed == 0) next.CollapsedRows.Add(group);
        var updated = ReconfigurePivotTable(next);
        var target = updated.OutlineCells.FirstOrDefault(p => p.Value.Group is { } path && PivotHierarchy.SamePath(path, group));
        if (target.Value.Group is not null)
        {
            var address = new CellAddress(next.Anchor.Row + target.Key.Row, next.Anchor.Column + target.Key.Column);
            Select(new CellRange(address, address));
        }
    }

    public void SetPivotGroupsExpanded(string id, bool expanded)
    {
        var pivot = Sheet.PivotTables.FirstOrDefault(p => p.Id == id) ?? throw new InvalidOperationException("Select a PivotTable first.");
        var report = PivotReportCache.Get(pivot);
        var next = pivot.CloneDocument();
        next.CollapsedRows = expanded || pivot.Rows.Count < 2 ? [] : report.RowKeys
            .Where(k => k.Items.Count > 0).Select(k => new PivotKey(k.Items.Take(1))).Distinct()
            .Select(k => PivotHierarchy.Path(pivot, k)).ToList();
        ReconfigurePivotTable(next);
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
        var report = PivotReportCache.Get(pivot);
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
        if (pivot.NeedsLayoutRefresh || pivot.Cache is null || pivot.ChartRange is null) throw new InvalidOperationException("Refresh the PivotTable before charting it.");
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
