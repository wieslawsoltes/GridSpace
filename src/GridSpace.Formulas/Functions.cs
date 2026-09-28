using System.Globalization;
using System.Text.RegularExpressions;
using GridSpace.Core;

namespace GridSpace.Formulas;

public sealed partial class CalculationEngine
{
    public static IReadOnlyList<string> BuiltInFunctions { get; } = ["SUM", "AVERAGE", "MIN", "MAX", "COUNT", "COUNTA", "COUNTBLANK", "PRODUCT", "SUMPRODUCT", "IF", "IFS", "IFERROR", "AND", "OR", "NOT", "TRUE", "FALSE", "ABS", "SQRT", "ROUND", "ROUNDUP", "ROUNDDOWN", "INT", "MOD", "POWER", "EXP", "LN", "LOG", "SIN", "COS", "TAN", "PI", "SIGN", "CONCAT", "CONCATENATE", "TEXTJOIN", "LEFT", "RIGHT", "MID", "LEN", "TRIM", "UPPER", "LOWER", "SUBSTITUTE", "REPLACE", "FIND", "SEARCH", "EXACT", "VALUE", "TEXT", "DATE", "DAY", "MONTH", "YEAR", "TODAY", "NOW", "WEEKDAY", "SUMIF", "COUNTIF", "AVERAGEIF", "VLOOKUP", "XLOOKUP", "INDEX", "MATCH", "ROW", "COLUMN", "ISBLANK", "ISNUMBER", "ISTEXT", "ISERROR", "PMT", "SEQUENCE", "FILTER", "SORT", "SORTBY", "UNIQUE", "TRANSPOSE", "TAKE", "DROP", "HSTACK", "VSTACK", "CHOOSECOLS", "CHOOSEROWS", "LET"];
    private CalcValue Call(CallExpr call, Worksheet sheet, CellAddress origin)
    {
        var expressions = call.Arguments;
        CalcValue At(int index) => index < expressions.Count ? Eval(expressions[index], sheet, origin) : CalcValue.Blank;
        if (call.Name is "NOW" or "TODAY" || _custom.ContainsKey(call.Name))
            if (_stack.TryPeek(out var volatileCell)) _volatile.Add(volatileCell);
        if (call.Name == "ANCHORARRAY")
        {
            if (expressions is not [RefExpr reference]) return CalcValue.Error("#REF!");
            var target = reference.Sheet is null ? sheet : Workbook.FindSheet(reference.Sheet);
            return target is null ? CalcValue.Error("#REF!") : ReadSpill(new(target, CellAddress.Parse(reference.Address)));
        }
        if (call.Name == "LET") return Let(expressions, sheet, origin);
        if (call.Name == "IF")
        {
            if (expressions.Count is < 2 or > 3) return CalcValue.Error("#VALUE!");
            var condition = At(0);
            if (condition.Kind == ValueKind.Array) return ArrayOperations.If(condition, At(1), expressions.Count > 2 ? At(2) : CalcValue.Bool(false));
            return condition.IsError ? condition : condition.Truth ? At(1) : expressions.Count > 2 ? At(2) : CalcValue.Bool(false);
        }
        if (call.Name == "IFERROR")
        {
            var result = At(0);
            if (result.Kind == ValueKind.Array && result.Items!.Any(v => v.IsError))
                return ArrayOperations.Zip(result, At(1), (a, b) => a.IsError ? b : a);
            return result.IsError ? At(1) : result;
        }
        if (call.Name == "IFS") { for (var i = 0; i + 1 < expressions.Count; i += 2) { var condition = At(i); if (condition.IsError) return condition; if (condition.Truth) return At(i + 1); } return CalcValue.Error("#N/A"); }
        if (call.Name == "ISERROR") return CalcValue.Bool(At(0).IsError);
        if (call.Name is "ROW" or "COLUMN")
        {
            var address = expressions.FirstOrDefault() is RefExpr r ? CellAddress.Parse(r.Address) : origin;
            return CalcValue.Num(call.Name == "ROW" ? address.Row + 1 : address.Column + 1);
        }
        var args = expressions.Select(e => Eval(e, sheet, origin)).ToArray();
        if (_custom.TryGetValue(call.Name, out var custom)) return custom(args);
        if (ArrayOperations.FunctionNames.Contains(call.Name)) return ArrayOperations.Call(call.Name, args);
        var flat = args.SelectMany(v => v.Flatten()).ToArray();
        var error = flat.FirstOrDefault(v => v.IsError); if (error.IsError) return error;
        CalcValue V(int i) => i < args.Length ? args[i] : CalcValue.Blank;
        double N(int i, double fallback = 0) => i < args.Length ? args[i].TryNumber(out var n) ? n : throw new FormatException("Numeric argument expected.") : fallback;
        string S(int i) => V(i).ToString();
        var numbers = flat.Where(v => v.Kind == ValueKind.Number).Select(v => v.Number).ToArray();
        var d = NForDate;
        switch (call.Name)
        {
            case "SUM": return CalcValue.Num(numbers.Sum());
            case "AVERAGE": return numbers.Length == 0 ? CalcValue.Error("#DIV/0!") : CalcValue.Num(numbers.Average());
            case "MIN": return CalcValue.Num(numbers.DefaultIfEmpty().Min());
            case "MAX": return CalcValue.Num(numbers.DefaultIfEmpty().Max());
            case "COUNT": return CalcValue.Num(numbers.Length);
            case "COUNTA": return CalcValue.Num(flat.Count(v => v.Kind != ValueKind.Blank));
            case "COUNTBLANK": return CalcValue.Num(flat.Count(v => v.Kind == ValueKind.Blank || v.Kind == ValueKind.Text && v.Text == ""));
            case "PRODUCT": return CalcValue.Num(numbers.Length == 0 ? 0 : numbers.Aggregate(1d, (a, b) => a * b));
            case "SUMPRODUCT":
                var arrays = args.Select(v => v.Flatten().ToArray()).ToArray();
                if (arrays.Length == 0 || arrays.Any(a => a.Length != arrays[0].Length)) return CalcValue.Error("#VALUE!");
                return CalcValue.Num(Enumerable.Range(0, arrays[0].Length).Sum(i => arrays.Aggregate(1d, (p, array) => p * (array[i].TryNumber(out var n) ? n : 0))));
            case "AND": return CalcValue.Bool(flat.All(v => v.Truth));
            case "OR": return CalcValue.Bool(flat.Any(v => v.Truth));
            case "NOT": return CalcValue.Bool(!V(0).Truth);
            case "TRUE": return CalcValue.Bool(true);
            case "FALSE": return CalcValue.Bool(false);
            case "ABS": return CalcValue.Num(Math.Abs(N(0)));
            case "SQRT": return CalcValue.Num(Math.Sqrt(N(0)));
            case "ROUND": return CalcValue.Num(Round(N(0), (int)N(1), 0));
            case "ROUNDUP": return CalcValue.Num(Round(N(0), (int)N(1), 1));
            case "ROUNDDOWN": return CalcValue.Num(Round(N(0), (int)N(1), -1));
            case "INT": return CalcValue.Num(Math.Floor(N(0)));
            case "SIGN": return CalcValue.Num(Math.Sign(N(0)));
            case "MOD": return N(1) == 0 ? CalcValue.Error("#DIV/0!") : CalcValue.Num(N(0) - N(1) * Math.Floor(N(0) / N(1)));
            case "POWER": return CalcValue.Num(Math.Pow(N(0), N(1)));
            case "EXP": return CalcValue.Num(Math.Exp(N(0)));
            case "LN": return CalcValue.Num(Math.Log(N(0)));
            case "LOG": return CalcValue.Num(Math.Log(N(0), N(1, 10)));
            case "SIN": return CalcValue.Num(Math.Sin(N(0)));
            case "COS": return CalcValue.Num(Math.Cos(N(0)));
            case "TAN": return CalcValue.Num(Math.Tan(N(0)));
            case "PI": return CalcValue.Num(Math.PI);
            case "CONCAT": case "CONCATENATE": return CalcValue.Str(string.Concat(flat.Select(v => v.ToString())));
            case "TEXTJOIN": return CalcValue.Str(string.Join(S(0), args.Skip(2).SelectMany(v => v.Flatten()).Where(v => !V(1).Truth || v.ToString() != "").Select(v => v.ToString())));
            case "LEN": return CalcValue.Num(S(0).Length);
            case "TRIM": return CalcValue.Str(Regex.Replace(S(0).Trim(' '), " +", " "));
            case "UPPER": return CalcValue.Str(S(0).ToUpperInvariant());
            case "LOWER": return CalcValue.Str(S(0).ToLowerInvariant());
            case "LEFT": return N(1, 1) < 0 ? CalcValue.Error("#VALUE!") : CalcValue.Str(S(0)[..Math.Min(S(0).Length, (int)N(1, 1))]);
            case "RIGHT": return N(1, 1) < 0 ? CalcValue.Error("#VALUE!") : CalcValue.Str(S(0)[Math.Max(0, S(0).Length - (int)N(1, 1))..]);
            case "MID":
                if (N(1) < 1 || N(2) < 0) return CalcValue.Error("#VALUE!");
                var start = Math.Min(S(0).Length, (int)N(1) - 1); return CalcValue.Str(S(0).Substring(start, Math.Min((int)N(2), S(0).Length - start)));
            case "SUBSTITUTE":
                if (S(1) == "") return V(0);
                if (args.Length < 4) return CalcValue.Str(S(0).Replace(S(1), S(2), StringComparison.Ordinal));
                var occurrence = (int)N(3); if (occurrence < 1) return CalcValue.Error("#VALUE!");
                var index = -S(1).Length;
                for (var i = 0; i < occurrence; i++) { index = S(0).IndexOf(S(1), index + S(1).Length, StringComparison.Ordinal); if (index < 0) return V(0); }
                return CalcValue.Str(S(0)[..index] + S(2) + S(0)[(index + S(1).Length)..]);
            case "REPLACE":
                if (N(1) < 1 || N(2) < 0) return CalcValue.Error("#VALUE!");
                var replacementStart = Math.Min(S(0).Length, (int)N(1) - 1);
                return CalcValue.Str(S(0)[..replacementStart] + S(3) + S(0)[Math.Min(S(0).Length, replacementStart + (int)N(2))..]);
            case "FIND": case "SEARCH":
                if (N(2, 1) < 1 || N(2, 1) > S(1).Length + 1) return CalcValue.Error("#VALUE!");
                var found = S(1).IndexOf(S(0), (int)N(2, 1) - 1, call.Name == "FIND" ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
                return found < 0 ? CalcValue.Error("#VALUE!") : CalcValue.Num(found + 1);
            case "EXACT": return CalcValue.Bool(S(0) == S(1));
            case "VALUE": return CalcValue.Num(N(0));
            case "TEXT": return CalcValue.Str(NumberFormatter.Format(V(0), S(1)));
            case "DATE":
                var year = (int)N(0); if (year is >= 0 and < 1900) year += 1900;
                return CalcValue.Num(new DateTime(year, 1, 1).AddMonths((int)N(1) - 1).AddDays((int)N(2) - 1).ToOADate());
            case "DAY": return CalcValue.Num(d(N(0)).Day);
            case "MONTH": return CalcValue.Num(d(N(0)).Month);
            case "YEAR": return CalcValue.Num(d(N(0)).Year);
            case "TODAY": return CalcValue.Num(DateTime.Today.ToOADate());
            case "NOW": return CalcValue.Num(DateTime.Now.ToOADate());
            case "WEEKDAY": return CalcValue.Num(N(1, 1) == 2 ? ((int)d(N(0)).DayOfWeek + 6) % 7 + 1 : (int)d(N(0)).DayOfWeek + 1);
            case "ISBLANK": return CalcValue.Bool(V(0).Kind == ValueKind.Blank);
            case "ISNUMBER": return CalcValue.Bool(V(0).Kind == ValueKind.Number);
            case "ISTEXT": return CalcValue.Bool(V(0).Kind == ValueKind.Text);
            case "COUNTIF": case "SUMIF": case "AVERAGEIF":
                var criteriaRange = V(0).Flatten().ToArray(); var sumRange = args.Length > 2 ? V(2).Flatten().ToArray() : criteriaRange;
                if (sumRange.Length != criteriaRange.Length) return CalcValue.Error("#VALUE!");
                var values = Enumerable.Range(0, criteriaRange.Length).Where(i => Matches(criteriaRange[i], V(1))).ToArray();
                if (call.Name == "COUNTIF") return CalcValue.Num(values.Length);
                var selected = values.Select(i => sumRange[i]).Where(v => v.Kind == ValueKind.Number).Select(v => v.Number).ToArray();
                return call.Name == "AVERAGEIF" ? selected.Length == 0 ? CalcValue.Error("#DIV/0!") : CalcValue.Num(selected.Average()) : CalcValue.Num(selected.Sum());
            case "INDEX":
                var data = V(0); var width = Math.Max(1, data.Columns); var offset = ((int)N(1) - 1) * width + (int)N(2, 1) - 1;
                var items = data.Flatten().ToArray(); return offset >= 0 && offset < items.Length && N(2, 1) <= width ? items[offset] : CalcValue.Error("#REF!");
            case "MATCH":
                if (N(2, 1) != 0) return CalcValue.Error("#N/A");
                var match = V(1).Flatten().ToList().FindIndex(v => Equal(v, V(0)));
                return match < 0 ? CalcValue.Error("#N/A") : CalcValue.Num(match + 1);
            case "VLOOKUP":
                if (args.Length < 4 || V(3).Truth) return CalcValue.Error("#N/A");
                var table = V(1); var col = (int)N(2); if (col < 1 || col > table.Columns || table.Items is null) return CalcValue.Error("#REF!");
                for (var i = 0; i < table.Items.Count; i += table.Columns) if (Equal(table.Items[i], V(0))) return table.Items[i + col - 1];
                return CalcValue.Error("#N/A");
            case "XLOOKUP":
                if (N(4) != 0 || args.Length > 5 && N(5, 1) != 1) return CalcValue.Error("#N/A");
                var lookup = V(1).Flatten().ToArray(); var returns = V(2).Flatten().ToArray(); if (lookup.Length != returns.Length) return CalcValue.Error("#VALUE!");
                for (var i = 0; i < lookup.Length; i++) if (Equal(lookup[i], V(0))) return returns[i];
                return args.Length > 3 ? V(3) : CalcValue.Error("#N/A");
            case "PMT":
                var rate = N(0); var periods = N(1); if (periods == 0) return CalcValue.Error("#DIV/0!");
                if (rate == 0) return CalcValue.Num(-(N(2) + N(3)) / periods);
                var factor = Math.Pow(1 + rate, periods); return CalcValue.Num(-(rate * (N(2) * factor + N(3))) / ((1 + rate * N(4)) * (factor - 1)));
            default: return CalcValue.Error("#NAME?");
        }
    }
    private CalcValue Let(IReadOnlyList<Expr> expressions, Worksheet sheet, CellAddress origin)
    {
        if (expressions.Count < 3 || expressions.Count % 2 != 1 || expressions.Count > 253) return CalcValue.Error("#VALUE!");
        var previous = _locals;
        _locals = previous is null ? new(StringComparer.OrdinalIgnoreCase) : new(previous, StringComparer.OrdinalIgnoreCase);
        try
        {
            for (var i = 0; i < expressions.Count - 1; i += 2)
            {
                if (expressions[i] is not NameExpr name || name.Name.Equals("R", StringComparison.OrdinalIgnoreCase) || name.Name.Equals("C", StringComparison.OrdinalIgnoreCase)) return CalcValue.Error("#NAME?");
                var value = Eval(expressions[i + 1], sheet, origin);
                _locals[name.Name] = value;
            }
            return Eval(expressions[^1], sheet, origin);
        }
        finally { _locals = previous; }
    }
    private static DateTime NForDate(double serial) => DateTime.FromOADate(serial);
    private static double Round(double value, int digits, int direction)
    {
        if (digits is < -100 or > 100) throw new ArgumentOutOfRangeException(nameof(digits));
        var factor = Math.Pow(10, digits); var scaled = value * factor;
        return (direction == 0 ? Math.Round(scaled, MidpointRounding.AwayFromZero) : direction > 0 ? Math.Sign(scaled) * Math.Ceiling(Math.Abs(scaled)) : Math.Truncate(scaled)) / factor;
    }
    private static bool Equal(CalcValue a, CalcValue b) => a.TryNumber(out var x) && b.TryNumber(out var y) ? x == y : a.ToString().Equals(b.ToString(), StringComparison.OrdinalIgnoreCase);
    private static bool Matches(CalcValue value, CalcValue criteria)
    {
        var text = criteria.ToString(); var op = "=";
        foreach (var candidate in new[] { ">=", "<=", "<>", ">", "<", "=" }) if (text.StartsWith(candidate, StringComparison.Ordinal)) { op = candidate; text = text[candidate.Length..]; break; }
        var comparison = value.TryNumber(out var a) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var b) ? a.CompareTo(b) : string.Compare(value.ToString(), text, StringComparison.OrdinalIgnoreCase);
        if ((op is "=" or "<>") && (text.Contains('*') || text.Contains('?')))
        {
            var pattern = "^" + Regex.Escape(text).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            var matches = Regex.IsMatch(value.ToString(), pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            return op == "=" ? matches : !matches;
        }
        return op switch { ">=" => comparison >= 0, "<=" => comparison <= 0, "<>" => comparison != 0, ">" => comparison > 0, "<" => comparison < 0, _ => comparison == 0 };
    }
}
