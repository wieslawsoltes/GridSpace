using System.Globalization;
using GridSpace.Core;

namespace GridSpace.Formulas;

/// <summary>Single-owner evaluator with bounded dependency tracking and virtual dynamic-array results.</summary>
public sealed partial class CalculationEngine(Workbook workbook)
{
    private readonly record struct CellKey(Worksheet Sheet, CellAddress Address);
    private readonly Dictionary<string, Expr> _parsed = new(StringComparer.Ordinal);
    private readonly Dictionary<CellKey, CalcValue> _cache = [];
    private readonly HashSet<CellKey> _active = [];
    private readonly HashSet<string> _activeNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Stack<CellKey> _stack = [];
    private readonly Dictionary<CellKey, HashSet<CellKey>> _dependencies = [];
    private readonly Dictionary<CellKey, HashSet<CellKey>> _dependents = [];
    private readonly HashSet<CellKey> _volatile = [];
    private readonly Dictionary<string, Func<IReadOnlyList<CalcValue>, CalcValue>> _custom = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<CellMutation> _changes = [];
    private Dictionary<string, CalcValue>? _locals;
    private long _revision = -1;
    private int _budget, _callDepth, _edgeCount;
    private bool _dependencyOverflow;
    public Workbook Workbook { get; } = workbook;
    public int CachedCellCount => _cache.Count;
    public long EvaluatedCellCount { get; private set; }
    public long CacheHitCount { get; private set; }
    public int DependencyEdgeCount => _edgeCount;

    public void Register(string name, Func<IReadOnlyList<CalcValue>, CalcValue> function)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(function);
        _custom[name] = function;
        Invalidate();
    }

    public void Invalidate()
    {
        _cache.Clear(); _dependencies.Clear(); _dependents.Clear(); _volatile.Clear();
        _edgeCount = 0; _dependencyOverflow = false; _revision = Workbook.Revision;
        ResetSpills(rebuildCandidates: true);
    }

    private void Prepare()
    {
        if (_revision == Workbook.Revision) return;
        _changes.Clear();
        if (_revision < 0 || _dependencyOverflow || !Workbook.TryGetCellMutations(_revision, _changes)) Invalidate();
        else
        {
            var inputs = _changes.Where(c => c.InputChanged).Select(c => new CellKey(c.Sheet, c.Address)).Distinct().ToArray();
            if (inputs.Length > 0)
            {
                // Spill geometry depends on occupied cells as well as formula precedents. Reconcile it conservatively.
                InvalidateCells(inputs.Concat(_volatile).Concat(_spillCells.Keys).Concat(_candidates));
                foreach (var key in inputs) UpdateCandidate(key);
                ResetSpills(rebuildCandidates: false);
            }
            _revision = Workbook.Revision;
        }
        _changes.Clear();
    }

    private void InvalidateCells(IEnumerable<CellKey> roots)
    {
        var pending = new Queue<CellKey>(roots);
        var seen = new HashSet<CellKey>();
        while (pending.TryDequeue(out var key))
        {
            if (!seen.Add(key)) continue;
            _cache.Remove(key);
            if (_dependents.TryGetValue(key, out var users))
                foreach (var user in users) pending.Enqueue(user);
        }
    }

    private void Track(CellKey precedent)
    {
        if (_stack.Count == 0 || _dependencyOverflow) return;
        var user = _stack.Peek();
        if (!_dependencies.TryGetValue(user, out var inputs)) _dependencies[user] = inputs = [];
        if (!inputs.Add(precedent)) return;
        if (++_edgeCount > 500_000) { _dependencyOverflow = true; return; }
        if (!_dependents.TryGetValue(precedent, out var users)) _dependents[precedent] = users = [];
        users.Add(user);
    }

    private void Detach(CellKey key)
    {
        if (!_dependencies.Remove(key, out var inputs)) return;
        _edgeCount -= inputs.Count;
        foreach (var input in inputs)
            if (_dependents.TryGetValue(input, out var users))
            {
                users.Remove(key);
                if (users.Count == 0) _dependents.Remove(input);
            }
    }

    private void Enter()
    {
        if (_callDepth++ != 0) return;
        try { Prepare(); _budget = 1_000_000; EnsureSpills(); }
        catch { _callDepth--; throw; }
    }

    public CalcValue Evaluate(Worksheet sheet, string address) => Evaluate(sheet, CellAddress.Parse(address));
    public CalcValue Evaluate(Worksheet sheet, CellAddress address)
    {
        Enter();
        try { return Read(new(sheet, address)); }
        finally { _callDepth--; }
    }

    private CalcValue Read(CellKey key)
    {
        Track(key);
        if (!key.Address.IsValid) return CalcValue.Error("#REF!");
        if (_buildingSpills && key.Sheet.Get(key.Address).Input.Length == 0)
            ResolvePossibleOwners(key);
        if (_spillCells.TryGetValue(key, out var owner))
        {
            Track(new(key.Sheet, owner.Anchor));
            if (_active.Contains(new(key.Sheet, owner.Anchor))) return CalcValue.Error("#CYCLE!");
            return owner.Values.Element(key.Address.Row - owner.Anchor.Row, key.Address.Column - owner.Anchor.Column);
        }
        if (_buildingSpills && _candidates.Contains(key) && !_active.Contains(key)) ResolveAnchor(key);
        var value = Raw(key);
        return value.Kind == ValueKind.Array ? value.Element(0, 0) : value;
    }

    private CalcValue Raw(CellKey key)
    {
        if (_cache.TryGetValue(key, out var cached)) { CacheHitCount++; return cached; }
        if (--_budget <= 0 || _active.Count >= 256) return CalcValue.Error("#LIMIT!");
        if (!_active.Add(key)) return CalcValue.Error("#CYCLE!");
        Detach(key); _stack.Push(key);
        var savedLocals = _locals; _locals = null;
        CalcValue value;
        var input = key.Sheet.Get(key.Address).Input;
        try
        {
            EvaluatedCellCount++;
            value = input.StartsWith('=') ? Eval(Parse(input), key.Sheet, key.Address) : ParseInput(input);
        }
        catch (Exception e) when (e is FormatException or ArgumentException or OverflowException or InvalidOperationException)
        { value = CalcValue.Error("#VALUE!"); }
        finally { _locals = savedLocals; _stack.Pop(); _active.Remove(key); }
        // Blank coordinates must not fill a cache as the user scrolls through an otherwise empty sheet.
        if (input.Length != 0 && _cache.Count < 200_000) _cache[key] = value;
        return value;
    }

    internal static CalcValue ParseInput(string input)
    {
        if (input.Length == 0) return CalcValue.Blank;
        if (input[0] == '\'') return CalcValue.Str(input[1..]);
        if (input[0] == '#' && input.EndsWith('!') || input is "#N/A" or "#NAME?") return CalcValue.Error(input);
        if (double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out var n)) return CalcValue.Num(n);
        if (input.EndsWith('%') && double.TryParse(input.AsSpan(0, input.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out n)) return CalcValue.Num(n / 100);
        return bool.TryParse(input, out var boolean) ? CalcValue.Bool(boolean) : CalcValue.Str(input);
    }

    private Expr Parse(string formula)
    {
        if (_parsed.TryGetValue(formula, out var expression)) return expression;
        expression = new FormulaParser(formula).Parse();
        if (_parsed.Count >= 4096) _parsed.Clear();
        _parsed[formula] = expression;
        return expression;
    }

    public CalcValue EvaluateFormula(Worksheet sheet, string formula, CellAddress origin = default)
    {
        Enter();
        try { return Eval(Parse(formula), sheet, origin); }
        catch (Exception e) when (e is FormatException or ArgumentException or OverflowException or InvalidOperationException)
        { return CalcValue.Error("#VALUE!"); }
        finally { _callDepth--; }
    }

    private CalcValue Eval(Expr expression, Worksheet sheet, CellAddress origin)
    {
        if (--_budget < 0) return CalcValue.Error("#LIMIT!");
        var result = EvalCore(expression, sheet, origin);
        if (result.Kind == ValueKind.Array && (_budget -= result.Items!.Count) < 0) return CalcValue.Error("#LIMIT!");
        return result;
    }

    private CalcValue EvalCore(Expr expression, Worksheet sheet, CellAddress origin)
    {
        switch (expression)
        {
            case LiteralExpr literal: return literal.Value;
            case ArrayExpr array: return CalcValue.Array(array.Items.Select(e => Eval(e, sheet, origin)).ToArray(), array.Columns);
            case SpillExpr spill:
                var spillSheet = spill.Anchor.Sheet is null ? sheet : Workbook.FindSheet(spill.Anchor.Sheet);
                return spillSheet is null ? CalcValue.Error("#REF!") : ReadSpill(new(spillSheet, CellAddress.Parse(spill.Anchor.Address)));
            case RefExpr reference:
                var target = reference.Sheet is null ? sheet : Workbook.FindSheet(reference.Sheet);
                return target is null ? CalcValue.Error("#REF!") : Read(new(target, CellAddress.Parse(reference.Address)));
            case RangeExpr range:
                var rangeSheet = range.First.Sheet is null ? sheet : Workbook.FindSheet(range.First.Sheet);
                if (rangeSheet is null || range.Last.Sheet != range.First.Sheet) return CalcValue.Error("#REF!");
                var span = new CellRange(CellAddress.Parse(range.First.Address), CellAddress.Parse(range.Last.Address));
                if (span.Count > 100_000) return CalcValue.Error("#LIMIT!");
                var items = new CalcValue[(int)span.Count]; var index = 0;
                foreach (var address in span.Cells()) items[index++] = Read(new(rangeSheet, address));
                return CalcValue.Array(items, span.Right - span.Left + 1);
            case NameExpr name:
                if (_locals?.TryGetValue(name.Name, out var local) == true) return local;
                if (!Workbook.Names.TryGetValue(name.Name, out var definition)) return CalcValue.Error("#NAME?");
                if (_activeNames.Count >= 128) return CalcValue.Error("#LIMIT!");
                if (!_activeNames.Add(name.Name)) return CalcValue.Error("#CYCLE!");
                try { return Eval(Parse(definition), sheet, origin); } finally { _activeNames.Remove(name.Name); }
            case UnaryExpr unary:
                return ArrayOperations.Map(Eval(unary.Operand, sheet, origin), v =>
                    v.IsError ? v : !v.TryNumber(out var n) ? CalcValue.Error("#VALUE!") : CalcValue.Num(unary.Operator == "-" ? -n : unary.Operator == "%" ? n / 100 : n));
            case BinaryExpr binary:
                var left = Eval(binary.Left, sheet, origin);
                var right = Eval(binary.Right, sheet, origin);
                return ArrayOperations.Zip(left, right, (a, b) => Binary(binary.Operator, a, b));
            case CallExpr call: return Call(call, sheet, origin);
            default: return CalcValue.Error("#VALUE!");
        }
    }

    private static CalcValue Binary(string op, CalcValue left, CalcValue right)
    {
        if (left.IsError) return left;
        if (right.IsError) return right;
        if (op == "&") return CalcValue.Str(left.ToString() + right);
        if (op is "=" or "<>" or "<" or ">" or "<=" or ">=")
        {
            var compare = left.TryNumber(out var ln) && right.TryNumber(out var rn) ? ln.CompareTo(rn) : string.Compare(left.ToString(), right.ToString(), StringComparison.OrdinalIgnoreCase);
            return CalcValue.Bool(op switch { "=" => compare == 0, "<>" => compare != 0, "<" => compare < 0, ">" => compare > 0, "<=" => compare <= 0, _ => compare >= 0 });
        }
        if (!left.TryNumber(out var a) || !right.TryNumber(out var b)) return CalcValue.Error("#VALUE!");
        return op switch
        {
            "+" => CalcValue.Num(a + b), "-" => CalcValue.Num(a - b), "*" => CalcValue.Num(a * b),
            "/" => b == 0 ? CalcValue.Error("#DIV/0!") : CalcValue.Num(a / b), "^" => CalcValue.Num(Math.Pow(a, b)), _ => CalcValue.Error("#VALUE!")
        };
    }
}
