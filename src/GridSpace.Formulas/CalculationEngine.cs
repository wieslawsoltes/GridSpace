using System.Globalization;
using GridSpace.Core;

namespace GridSpace.Formulas;

/// <summary>Pure managed, deterministic-by-revision evaluation. No host code or network access is possible.</summary>
public sealed partial class CalculationEngine(Workbook workbook)
{
    private readonly Dictionary<string, Expr> _parsed = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CalcValue> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _active = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Func<IReadOnlyList<CalcValue>, CalcValue>> _custom = new(StringComparer.OrdinalIgnoreCase);
    private long _revision = -1;
    private int _budget;
    public Workbook Workbook { get; } = workbook;
    public int CachedCellCount => _cache.Count;
    public void Register(string name, Func<IReadOnlyList<CalcValue>, CalcValue> function) { _custom[name] = function; Invalidate(); }
    public void Invalidate() { _cache.Clear(); _active.Clear(); _revision = Workbook.Revision; }
    private void Prepare() { if (_revision != Workbook.Revision) Invalidate(); if (_active.Count == 0) _budget = 200_000; }
    public CalcValue Evaluate(Worksheet sheet, string address) => Evaluate(sheet, CellAddress.Parse(address));
    public CalcValue Evaluate(Worksheet sheet, CellAddress address)
    {
        Prepare();
        var key = sheet.Name + "!" + address;
        if (_cache.TryGetValue(key, out var cached)) return cached;
        if (--_budget <= 0 || _active.Count > 256) return CalcValue.Error("#LIMIT!");
        if (!_active.Add(key)) return CalcValue.Error("#CYCLE!");
        CalcValue value;
        try
        {
            var input = sheet.Get(address).Input;
            if (input.StartsWith('=')) value = EvaluateFormula(sheet, input, address);
            else if (input.StartsWith('\'')) value = CalcValue.Str(input[1..]);
            else if (string.IsNullOrEmpty(input)) value = CalcValue.Blank;
            else if (double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) value = CalcValue.Num(number);
            else if (input.EndsWith('%') && double.TryParse(input.AsSpan(0, input.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out number)) value = CalcValue.Num(number / 100);
            else if (bool.TryParse(input, out var boolean)) value = CalcValue.Bool(boolean);
            else value = CalcValue.Str(input);
        }
        catch (Exception e) when (e is FormatException or ArgumentException or OverflowException or InvalidOperationException) { value = CalcValue.Error("#VALUE!"); }
        finally { _active.Remove(key); }
        _cache[key] = value;
        return value;
    }
    public CalcValue EvaluateFormula(Worksheet sheet, string formula, CellAddress origin = default)
    {
        Prepare();
        try
        {
            if (!_parsed.TryGetValue(formula, out var expression))
            {
                expression = new FormulaParser(formula).Parse();
                if (_parsed.Count > 4096) _parsed.Clear();
                _parsed[formula] = expression;
            }
            return Eval(expression, sheet, origin);
        }
        catch (Exception e) when (e is FormatException or ArgumentException or OverflowException or InvalidOperationException) { return CalcValue.Error("#VALUE!"); }
    }
    private CalcValue Eval(Expr expression, Worksheet sheet, CellAddress origin)
    {
        if (--_budget < 0) return CalcValue.Error("#LIMIT!");
        switch (expression)
        {
            case LiteralExpr literal: return literal.Value;
            case RefExpr reference:
                var target = reference.Sheet is null ? sheet : Workbook.FindSheet(reference.Sheet);
                return target is null ? CalcValue.Error("#REF!") : Evaluate(target, CellAddress.Parse(reference.Address));
            case RangeExpr range:
                var rangeSheet = range.First.Sheet is null ? sheet : Workbook.FindSheet(range.First.Sheet);
                if (rangeSheet is null || range.Last.Sheet != range.First.Sheet) return CalcValue.Error("#REF!");
                var span = new CellRange(CellAddress.Parse(range.First.Address), CellAddress.Parse(range.Last.Address));
                if (span.Count > 100_000) return CalcValue.Error("#LIMIT!");
                return CalcValue.Array(span.Cells().Select(a => Evaluate(rangeSheet, a)).ToArray(), span.Right - span.Left + 1);
            case NameExpr name:
                if (!Workbook.Names.TryGetValue(name.Name, out var definition)) return CalcValue.Error("#NAME?");
                var nameKey = "@" + name.Name;
                if (!_active.Add(nameKey)) return CalcValue.Error("#CYCLE!");
                try { return EvaluateFormula(sheet, definition, origin); } finally { _active.Remove(nameKey); }
            case UnaryExpr unary:
                var operand = Eval(unary.Operand, sheet, origin);
                if (operand.IsError) return operand;
                if (!operand.TryNumber(out var unaryNumber)) return CalcValue.Error("#VALUE!");
                return CalcValue.Num(unary.Operator == "-" ? -unaryNumber : unary.Operator == "%" ? unaryNumber / 100 : unaryNumber);
            case BinaryExpr binary:
                var left = Eval(binary.Left, sheet, origin); if (left.IsError) return left;
                var right = Eval(binary.Right, sheet, origin); if (right.IsError) return right;
                if (binary.Operator == "&") return CalcValue.Str(left + right.ToString());
                if (binary.Operator is "=" or "<>" or "<" or ">" or "<=" or ">=")
                {
                    var compare = left.TryNumber(out var ln) && right.TryNumber(out var rn) ? ln.CompareTo(rn) : string.Compare(left.ToString(), right.ToString(), StringComparison.OrdinalIgnoreCase);
                    return CalcValue.Bool(binary.Operator switch { "=" => compare == 0, "<>" => compare != 0, "<" => compare < 0, ">" => compare > 0, "<=" => compare <= 0, _ => compare >= 0 });
                }
                if (!left.TryNumber(out var a) || !right.TryNumber(out var b)) return CalcValue.Error("#VALUE!");
                return binary.Operator switch
                {
                    "+" => CalcValue.Num(a + b), "-" => CalcValue.Num(a - b), "*" => CalcValue.Num(a * b),
                    "/" => b == 0 ? CalcValue.Error("#DIV/0!") : CalcValue.Num(a / b), "^" => CalcValue.Num(Math.Pow(a, b)), _ => CalcValue.Error("#VALUE!")
                };
            case CallExpr call: return Call(call, sheet, origin);
            default: return CalcValue.Error("#VALUE!");
        }
    }
}
