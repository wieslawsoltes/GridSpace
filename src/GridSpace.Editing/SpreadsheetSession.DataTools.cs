using GridSpace.Core;
using GridSpace.Formulas;

namespace GridSpace.Editing;

public sealed partial class SpreadsheetSession
{
    /// <summary>Use the explicit selection, the active filter table, or the active cell's contiguous data region.</summary>
    public CellRange DataRange()
    {
        if (Selection.Count > 1) return Selection.Normalized;
        if (CellRange.TryParse(Sheet.FilterRange, out var filter) && filter.Contains(ActiveCell)) return filter.Normalized;
        var used = Sheet.UsedRange;
        if (Sheet.Get(ActiveCell).Input.Length == 0) return used;
        var populated = Sheet.Cells.Where(p => p.Value.Input.Length > 0).Select(p => CellAddress.Parse(p.Key)).ToHashSet();
        var top = ActiveCell.Row; var bottom = top; var left = ActiveCell.Column; var right = left;
        var changed = true;
        while (changed)
        {
            changed = false;
            if (top > used.Top && Enumerable.Range(left, right - left + 1).Any(c => populated.Contains(new CellAddress(top - 1, c)))) { top--; changed = true; }
            if (bottom < used.Bottom && Enumerable.Range(left, right - left + 1).Any(c => populated.Contains(new CellAddress(bottom + 1, c)))) { bottom++; changed = true; }
            if (left > used.Left && Enumerable.Range(top, bottom - top + 1).Any(r => populated.Contains(new CellAddress(r, left - 1)))) { left--; changed = true; }
            if (right < used.Right && Enumerable.Range(top, bottom - top + 1).Any(r => populated.Contains(new CellAddress(r, right + 1)))) { right++; changed = true; }
            if ((long)(bottom - top + 1) * (right - left + 1) > 100_000) throw new InvalidOperationException("The data region exceeds 100,000 cells. Select a smaller range.");
        }
        return new(new(top, left), new(bottom, right));
    }

    public void SetFilter(ColumnFilter filter, CellRange? range = null)
    {
        filter.Validate();
        var area = (range ?? (CellRange.TryParse(Sheet.FilterRange, out var existing) ? existing : DataRange())).Normalized;
        if (filter.Column < area.Left || filter.Column > area.Right || area.Bottom - area.Top > 100_000)
            throw new InvalidOperationException("Choose a column inside a filter range of at most 100,000 data rows.");
        Perform("Filter column", () =>
        {
            if (Sheet.FilterRange != area.ToString()) Sheet.Filters.Clear();
            Sheet.FilterRange = area.ToString();
            Sheet.Filters.RemoveAll(f => f.Column == filter.Column);
            // Clone mutable arrays so caller mutation cannot bypass revision tracking.
            Sheet.Filters.Add(filter with { Values = filter.Values?.Distinct(StringComparer.OrdinalIgnoreCase).ToArray() });
            ReapplyFiltersCore();
        });
    }

    public void ClearFilters(int? column = null) => Perform("Clear filter", () =>
    {
        if (column is null) Sheet.Filters.Clear();
        else Sheet.Filters.RemoveAll(f => f.Column == column.Value);
        ReapplyFiltersCore();
    });

    public void ReapplyFilters() => Perform("Reapply filters", ReapplyFiltersCore);

    private void ReapplyFiltersCore()
    {
        Book.Touch();
        Sheet.FilteredRows = WorksheetFilterEngine.Evaluate(Sheet, Calculation);
    }

    public void ToggleFilter() => Perform("Toggle filter", () =>
    {
        if (Sheet.FilterRange is null) Sheet.FilterRange = DataRange().ToString();
        else { Sheet.FilterRange = null; Sheet.Filters.Clear(); Sheet.FilteredRows.Clear(); }
    });

    public IReadOnlyList<string> FilterValues(int column)
    {
        var range = CellRange.TryParse(Sheet.FilterRange, out var r) ? r : DataRange();
        if (range.Bottom - range.Top > 100_000) throw new InvalidOperationException("The filter range exceeds 100,000 data rows.");
        var excluded = WorksheetFilterEngine.Evaluate(Sheet, Calculation, column);
        return Enumerable.Range(range.Top + 1, Math.Max(0, range.Bottom - range.Top))
            .Where(row => !excluded.Contains(row))
            .Select(row => Calculation.Evaluate(Sheet, new CellAddress(row, column)).ToString())
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).Take(10001).ToArray();
    }

    public void Sort(IReadOnlyList<SortLevel> levels, bool header = true, bool caseSensitive = false, CellRange? range = null)
    {
        var area = (range ?? DataRange()).Normalized;
        RejectPivotWrite(area);
        if (levels.Count is < 1 or > 64 || levels.Any(l => l.Column < area.Left || l.Column > area.Right) || levels.Select(l => l.Column).Distinct().Count() != levels.Count)
            throw new ArgumentException("Choose 1–64 distinct sort columns inside the selected data range.");
        if (area.Count > 100_000) throw new InvalidOperationException("Sorting is limited to 100,000 cells.");
        if (Sheet.Merges.Any(m => m.Intersects(area))) throw new InvalidOperationException("Unmerge cells in the sort range first.");
        if (Calculation.GetSpills(Sheet).Any(s => s.Range.Intersects(area)))
            throw new InvalidOperationException("A sort cannot move part of a spilled array. Sort its source data or use SORT instead.");
        var first = area.Top + (header ? 1 : 0);
        if (first > area.Bottom) return;
        var keys = levels.ToArray();
        var data = Enumerable.Range(first, area.Bottom - first + 1).Select(row => new
        {
            Row = row,
            Keys = keys.Select(l => Calculation.Evaluate(Sheet, new CellAddress(row, l.Column))).ToArray(),
            Cells = Enumerable.Range(area.Left, area.Right - area.Left + 1).Select(c => Sheet.Get(new CellAddress(row, c))).ToArray()
        }).ToArray();
        var comparer = Comparer<int>.Create((a, b) =>
        {
            for (var i = 0; i < keys.Length; i++)
            {
                var left = data[a].Keys[i]; var right = data[b].Keys[i];
                var blankA = WorksheetFilterEngine.IsBlank(left); var blankB = WorksheetFilterEngine.IsBlank(right);
                var comparison = blankA != blankB ? blankA ? 1 : -1 : CompareSortValues(left, right, caseSensitive) * (keys[i].Descending ? -1 : 1);
                if (comparison != 0) return comparison;
            }
            return data[a].Row.CompareTo(data[b].Row);
        });
        var ordered = Enumerable.Range(0, data.Length).Order(comparer).Select(i => data[i]).ToArray();
        Perform("Custom sort", () =>
        {
            for (var i = 0; i < ordered.Length; i++)
                for (var column = 0; column < ordered[i].Cells.Length; column++)
                {
                    var cell = ordered[i].Cells[column];
                    Sheet.Set(new CellAddress(first + i, area.Left + column), cell with
                    {
                        Input = FormulaReferences.Translate(cell.Input, first + i - ordered[i].Row, 0)
                    });
                }
            Sheet.SortLevels = keys.ToList();
            Sheet.SortRange = area.ToString();
            Sheet.SortHasHeader = header;
            Sheet.SortCaseSensitive = caseSensitive;
            ReapplyFiltersCore();
        });
    }

    private static int CompareSortValues(CalcValue a, CalcValue b, bool caseSensitive)
    {
        static int TypeRank(CalcValue v) => v.Kind switch { ValueKind.Number => 0, ValueKind.Text => 1, ValueKind.Boolean => 2, ValueKind.Error => 3, _ => 4 };
        var kind = TypeRank(a).CompareTo(TypeRank(b));
        if (kind != 0) return kind;
        return a.Kind is ValueKind.Number or ValueKind.Boolean ? a.Number.CompareTo(b.Number)
            : string.Compare(a.ToString(), b.ToString(), caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
    }

    public void SetConditionalFormat(ConditionalFormatRule rule)
    {
        rule.Validate();
        Perform("Conditional formatting", () =>
        {
            var index = Sheet.ConditionalFormats.FindIndex(r => r.Id == rule.Id);
            if (index >= 0) Sheet.ConditionalFormats[index] = rule;
            else Sheet.ConditionalFormats.Add(rule);
        });
    }

    public void RemoveConditionalFormat(string id) => Perform("Remove conditional rule", () => Sheet.ConditionalFormats.RemoveAll(r => r.Id == id));
    public void ClearConditionalFormats(bool entireSheet = false) => Perform("Clear conditional rules", () => Sheet.ConditionalFormats.RemoveAll(r => entireSheet || CellRange.Parse(r.Range).Intersects(Selection)));

    public void MoveConditionalFormat(string id, int direction) => Perform("Reorder conditional rules", () =>
    {
        var ordered = Sheet.ConditionalFormats.OrderBy(r => r.Priority).ToList();
        var index = ordered.FindIndex(r => r.Id == id);
        var target = index + Math.Sign(direction);
        if (index < 0 || target < 0 || target >= ordered.Count) return;
        (ordered[index], ordered[target]) = (ordered[target], ordered[index]);
        Sheet.ConditionalFormats = ordered.Select((rule, i) => rule with { Priority = i + 1 }).ToList();
    });
}
