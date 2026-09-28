using GridSpace.Core;

namespace GridSpace.Formulas;

public sealed partial class CalculationEngine
{
    public const int MaximumArrayCells = 100_000;
    public const int MaximumSpillCells = 200_000;
    private readonly HashSet<CellKey> _candidates = [];
    private readonly Dictionary<Worksheet, CellKey[]> _candidateRows = [];
    private readonly HashSet<CellKey> _resolvedSpills = [];
    private readonly HashSet<CellKey> _resolvingSpills = [];
    private readonly Dictionary<CellKey, SpillInfo> _spillCells = [];
    private readonly Dictionary<CellKey, SpillInfo> _spills = [];
    private bool _spillsDirty = true, _buildingSpills;
    public int SpilledCellCount => _spillCells.Count;

    private void ResetSpills(bool rebuildCandidates)
    {
        _spills.Clear(); _spillCells.Clear(); _resolvedSpills.Clear(); _resolvingSpills.Clear();
        _spillsDirty = true; _candidateRows.Clear();
        if (!rebuildCandidates) return;
        _candidates.Clear();
        foreach (var sheet in Workbook.Sheets)
            foreach (var pair in sheet.Cells)
                if (pair.Value.IsFormula) UpdateCandidate(new(sheet, CellAddress.Parse(pair.Key)));
    }

    private void UpdateCandidate(CellKey key)
    {
        _candidates.Remove(key);
        var input = key.Sheet.Get(key.Address).Input;
        if (!input.StartsWith('=')) return;
        try { if (MaySpill(Parse(input))) _candidates.Add(key); }
        catch (FormatException) { /* Invalid syntax is reported by ordinary cell evaluation. */ }
    }

    private bool MaySpill(Expr expression) => expression switch
    {
        RangeExpr or SpillExpr or ArrayExpr or NameExpr => true,
        BinaryExpr b => MaySpill(b.Left) || MaySpill(b.Right),
        UnaryExpr u => MaySpill(u.Operand),
        CallExpr c when ArrayOperations.FunctionNames.Contains(c.Name) || c.Name is "LET" or "ANCHORARRAY" || _custom.ContainsKey(c.Name) => true,
        CallExpr c when c.Name is "IF" or "IFS" or "IFERROR" => c.Arguments.Any(MaySpill),
        _ => false
    };

    private void EnsureSpills()
    {
        if (!_spillsDirty || _buildingSpills) return;
        _spillsDirty = false; _buildingSpills = true;
        try
        {
            foreach (var group in _candidates.GroupBy(k => k.Sheet))
                _candidateRows[group.Key] = group.OrderBy(k => k.Address.Row).ThenBy(k => k.Address.Column).ToArray();
            foreach (var sheet in Workbook.Sheets)
                if (_candidateRows.TryGetValue(sheet, out var anchors))
                    foreach (var key in anchors) ResolveAnchor(key);
        }
        finally { _buildingSpills = false; }
    }

    private void ResolvePossibleOwners(CellKey cell)
    {
        if (!_candidateRows.TryGetValue(cell.Sheet, out var anchors)) return;
        foreach (var candidate in anchors)
        {
            if (candidate.Address.Row > cell.Address.Row) break;
            if (candidate.Address.Column > cell.Address.Column || _resolvingSpills.Contains(candidate)) continue;
            // No supported rectangular result can reach a cell farther away than the array-cell budget.
            if ((long)(cell.Address.Row - candidate.Address.Row + 1) * (cell.Address.Column - candidate.Address.Column + 1) > MaximumArrayCells) continue;
            ResolveAnchor(candidate);
            if (_spillCells.ContainsKey(cell)) break;
        }
    }

    private void ResolveAnchor(CellKey key)
    {
        if (_resolvedSpills.Contains(key) || !_resolvingSpills.Add(key)) return;
        try
        {
            if (_resolvingSpills.Count > 128) { _cache[key] = CalcValue.Error("#LIMIT!"); return; }
            var value = Raw(key);
            if (value.Kind != ValueKind.Array) return;
            if (_dependencyOverflow) { _cache[key] = CalcValue.Error("#LIMIT!"); return; }
            var bottom = (long)key.Address.Row + value.Rows - 1;
            var right = (long)key.Address.Column + value.Columns - 1;
            if (bottom >= CellAddress.MaxRows || right >= CellAddress.MaxColumns)
            { _cache[key] = CalcValue.Error("#SPILL!"); return; }
            if (_spillCells.Count + value.Items!.Count > MaximumSpillCells)
            { _cache[key] = CalcValue.Error("#LIMIT!"); return; }
            var range = new CellRange(key.Address, new((int)bottom, (int)right));
            if (key.Sheet.Merges.Any(m => m.Intersects(range)))
            { _cache[key] = CalcValue.Error("#SPILL!"); return; }
            foreach (var address in range.Cells())
                if (address != key.Address && key.Sheet.Get(address).Input.Length > 0 || _spillCells.ContainsKey(new(key.Sheet, address)))
                { _cache[key] = CalcValue.Error("#SPILL!"); return; }
            if (DependsOnOutput(key, range))
            { _cache[key] = CalcValue.Error("#CYCLE!"); return; }
            // Referenced empty cells have the numeric zero value when materialized as array output.
            if (value.Items.Any(v => v.Kind == ValueKind.Blank))
                value = CalcValue.Array(value.Items.Select(v => v.Kind == ValueKind.Blank ? CalcValue.Num(0) : v).ToArray(), value.Columns);
            var spill = new SpillInfo(key.Address, range, value);
            _cache[key] = value; _spills[key] = spill;
            foreach (var address in range.Cells()) _spillCells[new(key.Sheet, address)] = spill;
        }
        finally { _resolvingSpills.Remove(key); _resolvedSpills.Add(key); }
    }

    private bool DependsOnOutput(CellKey anchor, CellRange output)
    {
        var pending = new Stack<CellKey>(); pending.Push(anchor);
        var visited = new HashSet<CellKey>();
        while (pending.TryPop(out var key))
        {
            if (!visited.Add(key) || !_dependencies.TryGetValue(key, out var inputs)) continue;
            foreach (var input in inputs)
            {
                if (ReferenceEquals(input.Sheet, anchor.Sheet) && output.Contains(input.Address)) return true;
                pending.Push(input);
            }
        }
        return false;
    }

    private CalcValue ReadSpill(CellKey key)
    {
        Track(key);
        if (_resolvingSpills.Contains(key) || _active.Contains(key)) return CalcValue.Error("#CYCLE!");
        if (_buildingSpills && _candidates.Contains(key)) ResolveAnchor(key);
        if (_spills.TryGetValue(key, out var spill)) return spill.Values;
        var value = Raw(key);
        return value.IsError ? value : CalcValue.Error("#REF!");
    }

    public bool IsDynamicFormula(string formula)
    {
        try { return MaySpill(Parse(formula)); }
        catch (FormatException) { return false; }
    }

    public SpillInfo? GetSpill(Worksheet sheet, CellAddress address)
    {
        Enter();
        try { return _spillCells.GetValueOrDefault(new(sheet, address)); }
        finally { _callDepth--; }
    }

    public IReadOnlyList<SpillInfo> GetSpills(Worksheet sheet)
    {
        Enter();
        try { return _spills.Where(p => ReferenceEquals(p.Key.Sheet, sheet)).Select(p => p.Value).ToArray(); }
        finally { _callDepth--; }
    }

    public CellRange CalculatedUsedRange(Worksheet sheet)
    {
        var used = sheet.UsedRange;
        foreach (var spill in GetSpills(sheet))
            used = new(new(Math.Min(used.Top, spill.Range.Top), Math.Min(used.Left, spill.Range.Left)), new(Math.Max(used.Bottom, spill.Range.Bottom), Math.Max(used.Right, spill.Range.Right)));
        return used;
    }
}
