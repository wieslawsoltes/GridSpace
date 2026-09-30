using System.Globalization;
using GridSpace.Core;
using GridSpace.Formulas;

namespace GridSpace.Editing;

public sealed partial class SpreadsheetSession
{
    public bool IsSpillFollower => Calculation.GetSpill(Sheet, ActiveCell) is { } spill && spill.Anchor != ActiveCell;
    public string FormulaInput => Sheet.Get(Calculation.GetSpill(Sheet, ActiveCell)?.Anchor ?? ActiveCell).Input;
    public long RetainedHistoryBytes => _undo.Concat(_redo).Sum(e => e.Size);

    /// <summary>Prepares and validates all writes before committing. Undo stores only changed immutable cells.</summary>
    public void ApplyCells(string name, IEnumerable<KeyValuePair<CellAddress, Cell>> edits)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(edits);
        var sheet = Sheet;
        var changes = new Dictionary<CellAddress, Cell>();
        var enumerated = 0;
        foreach (var pair in edits)
        {
            if (++enumerated > 100_000) throw new InvalidOperationException("Cell transactions are limited to 100,000 writes.");
            ArgumentNullException.ThrowIfNull(pair.Value);
            ArgumentNullException.ThrowIfNull(pair.Value.Input);
            ArgumentNullException.ThrowIfNull(pair.Value.Style);
            if (!pair.Key.IsValid || pair.Value.Input.Length > 32767) throw new ArgumentException("Cell address or input length is outside the worksheet limits.");
            changes[pair.Key] = pair.Value;
        }
        var patches = changes.Select(p => new CellPatch(p.Key, sheet.Get(p.Key), p.Value)).Where(p => p.Before != p.After).ToArray();
        if (patches.Length == 0) return;
        foreach (var patch in patches)
            if (patch.Before.Input != patch.After.Input && PivotAt(patch.Address) is { } pivot)
                throw new InvalidOperationException("You cannot change a PivotTable result. Edit its source or fields (" + pivot.Name + ").");
        var replacingAnchors = patches.Where(p => p.Before.Input != p.After.Input).Select(p => p.Address).ToHashSet();
        var spills = Calculation.GetSpills(sheet);
        foreach (var patch in patches)
            if (patch.Before.Input != patch.After.Input)
                foreach (var spill in spills)
                    if (spill.Range.Contains(patch.Address) && spill.Anchor != patch.Address && !replacingAnchors.Contains(spill.Anchor))
                        throw new InvalidOperationException("You cannot change part of a spilled array. Edit " + spill.Anchor + " instead.");
        var stored = sheet.Cells.Count;
        foreach (var patch in patches)
        {
            if (patch.Before == Cell.Empty && patch.After != Cell.Empty) stored++;
            if (patch.Before != Cell.Empty && patch.After == Cell.Empty) stored--;
        }
        if (stored > 200_000) throw new InvalidOperationException("The worksheet exceeds the 200,000 stored-cell limit.");
        var selection = Selection;
        foreach (var patch in patches) sheet.Set(patch.Address, patch.After);
        if (_inTransaction) return;
        _undo.Add(new(name, "", "", selection) { Patches = patches, SheetIndex = Book.ActiveSheetIndex });
        _redo.Clear(); TrimHistory(); IsDirty = true; Notify(name, true);
    }

    private void TrimHistory()
    {
        while (_undo.Count > 40 || _undo.Count > 1 && RetainedHistoryBytes > 32L * 1024 * 1024) _undo.RemoveAt(0);
    }

    private void Replay(HistoryEntry entry, bool forward)
    {
        if (entry.Chart is { } chart)
        {
            Book.ActiveSheetIndex = entry.SheetIndex;
            ReplayChart(chart, forward);
            return;
        }
        if (entry.Pivot is { } pivot)
        {
            Book.ActiveSheetIndex = entry.SheetIndex;
            foreach (var cell in entry.Patches ?? []) Sheet.Set(cell.Address, forward ? cell.After : cell.Before);
            ReplayPivot(pivot, forward);
            return;
        }
        if (entry.Patches is not { } patches) { Restore(forward ? entry.After : entry.Before); return; }
        Book.ActiveSheetIndex = entry.SheetIndex;
        foreach (var patch in patches) Sheet.Set(patch.Address, forward ? patch.After : patch.Before);
    }

    private void CheckSpillSelection(CellRange range)
    {
        foreach (var spill in Calculation.GetSpills(Sheet))
            if (spill.Range.Intersects(range) && !range.Contains(spill.Anchor))
                throw new InvalidOperationException("You cannot change part of a spilled array. Select " + spill.Anchor + " to edit its formula.");
    }

    private Cell CopyCell(CellAddress address, CellRange range)
    {
        var cell = Sheet.Get(address);
        var spill = Calculation.GetSpill(Sheet, address);
        if (spill is not null && !range.Contains(spill.Anchor))
            return cell with { Input = LiteralInput(Calculation.Evaluate(Sheet, address)) };
        return cell;
    }

    public static string LiteralInput(CalcValue value) => value.Kind switch
    {
        ValueKind.Text => "'" + value.Text,
        ValueKind.Number => value.Number.ToString("G17", CultureInfo.InvariantCulture),
        _ => value.ToString()
    };
}
