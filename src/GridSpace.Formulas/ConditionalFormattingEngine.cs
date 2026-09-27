using GridSpace.Core;

namespace GridSpace.Formulas;

/// <summary>Revision-cached conditional formatting, independent of UI and rendering APIs.</summary>
public sealed class ConditionalFormattingEngine(CalculationEngine calculation)
{
    private sealed record Statistics(double[] Numbers, Dictionary<string, int> Frequencies)
    {
        public double Minimum => Numbers.Length == 0 ? 0 : Numbers[0];
        public double Maximum => Numbers.Length == 0 ? 0 : Numbers[^1];
        public double Median => Numbers.Length == 0 ? 0 : Numbers.Length % 2 == 1 ? Numbers[Numbers.Length / 2] : Numbers[Numbers.Length / 2 - 1] / 2 + Numbers[Numbers.Length / 2] / 2;
        public double Mean => Numbers.Length == 0 ? 0 : Numbers.Sum(n => n / Numbers.Length);
    }

    private readonly Dictionary<(Worksheet Sheet, string Id), Statistics> _statistics = [];
    private readonly Dictionary<Worksheet, (ConditionalFormatRule Rule, CellRange Range)[]> _rules = [];
    private long _revision = -1;
    public CalculationEngine Calculation { get; } = calculation;

    public ConditionalCellFormat Evaluate(Worksheet sheet, CellAddress address, CalcValue value)
    {
        var original = sheet.Get(address).Style;
        if (sheet.ConditionalFormats.Count == 0) return new(original);
        Prepare();
        if (!_rules.TryGetValue(sheet, out var rules))
        {
            rules = sheet.ConditionalFormats.OrderBy(r => r.Priority).Select(r => (r, CellRange.Parse(r.Range))).ToArray();
            _rules[sheet] = rules;
        }
        var combined = new DifferentialStyle();
        DataBarVisual? bar = null;
        var hideValue = false;
        foreach (var (rule, range) in rules)
        {
            if (!range.Contains(address)) continue;
            if (rule.Kind is ConditionalFormatKind.ColorScale or ConditionalFormatKind.DataBar)
            {
                if (value.Kind != ValueKind.Number) continue;
                var stats = Stats(sheet, rule, range);
                if (rule.Kind == ConditionalFormatKind.ColorScale)
                {
                    var color = ScaleColor(rule, stats, value.Number);
                    combined = combined.FillUnset(new DifferentialStyle { Background = color });
                }
                else if (bar is null)
                {
                    var minimum = Math.Min(0, stats.Minimum);
                    var maximum = Math.Max(0, stats.Maximum);
                    var axis = Fraction(0, minimum, maximum);
                    var end = Fraction(value.Number, minimum, maximum);
                    bar = new(axis, Math.Min(axis, end), Math.Max(axis, end), value.Number < 0 ? rule.LowColor : rule.HighColor);
                    hideValue = !rule.ShowValue;
                }
                continue;
            }
            if (!Matches(sheet, rule, range, address, value)) continue;
            combined = combined.FillUnset(rule.Style);
            if (rule.StopIfTrue) break;
        }
        return new(combined.Apply(original), bar, hideValue);
    }

    private void Prepare()
    {
        if (_revision == Calculation.Workbook.Revision) return;
        _revision = Calculation.Workbook.Revision;
        _statistics.Clear();
        _rules.Clear();
    }

    private Statistics Stats(Worksheet sheet, ConditionalFormatRule rule, CellRange range)
    {
        if (_statistics.TryGetValue((sheet, rule.Id), out var statistics)) return statistics;
        var numbers = new List<double>();
        var frequencies = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var cell in range.Cells())
        {
            var value = Calculation.Evaluate(sheet, cell);
            if (value.Kind == ValueKind.Number) numbers.Add(value.Number);
            if (!WorksheetFilterEngine.IsBlank(value))
            {
                var key = Key(value);
                frequencies[key] = frequencies.GetValueOrDefault(key) + 1;
            }
        }
        numbers.Sort();
        statistics = new(numbers.ToArray(), frequencies);
        _statistics[(sheet, rule.Id)] = statistics;
        return statistics;
    }

    private static string Key(CalcValue value) => ((int)value.Kind).ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + value;

    private CalcValue Operand(Worksheet sheet, string expression, CellRange range, CellAddress cell)
    {
        var formula = FormulaReferences.Translate("=" + expression.TrimStart('='), cell.Row - range.Top, cell.Column - range.Left);
        return Calculation.EvaluateFormula(sheet, formula, cell);
    }

    private bool Matches(Worksheet sheet, ConditionalFormatRule rule, CellRange range, CellAddress cell, CalcValue value)
    {
        if (rule.Kind == ConditionalFormatKind.Expression)
        {
            var result = Operand(sheet, rule.Operand, range, cell);
            return !result.IsError && result.Truth;
        }
        if (value.IsError) return false;
        if (rule.Kind == ConditionalFormatKind.ContainsText)
            return value.ToString().Contains(rule.Operand, StringComparison.OrdinalIgnoreCase);
        if (rule.Kind == ConditionalFormatKind.CellValue)
        {
            var right = Operand(sheet, rule.Operand, range, cell);
            if (right.IsError) return false;
            var comparison = Compare(value, right);
            if (rule.Comparison is CellComparison.Between or CellComparison.NotBetween)
            {
                var second = Operand(sheet, rule.Operand2, range, cell);
                if (second.IsError) return false;
                var between = comparison >= 0 && Compare(value, second) <= 0;
                return rule.Comparison == CellComparison.Between ? between : !between;
            }
            return rule.Comparison switch
            {
                CellComparison.Equal => comparison == 0,
                CellComparison.NotEqual => comparison != 0,
                CellComparison.GreaterThan => comparison > 0,
                CellComparison.GreaterThanOrEqual => comparison >= 0,
                CellComparison.LessThan => comparison < 0,
                CellComparison.LessThanOrEqual => comparison <= 0,
                _ => false
            };
        }
        var stats = Stats(sheet, rule, range);
        if (rule.Kind is ConditionalFormatKind.DuplicateValues or ConditionalFormatKind.UniqueValues)
        {
            if (WorksheetFilterEngine.IsBlank(value)) return false;
            var count = stats.Frequencies.GetValueOrDefault(Key(value));
            return rule.Kind == ConditionalFormatKind.DuplicateValues ? count > 1 : count == 1;
        }
        if (value.Kind != ValueKind.Number || stats.Numbers.Length == 0) return false;
        if (rule.Kind == ConditionalFormatKind.AboveAverage) return value.Number > stats.Mean;
        if (rule.Kind == ConditionalFormatKind.BelowAverage) return value.Number < stats.Mean;
        var rank = Math.Clamp(rule.Percent ? (int)Math.Ceiling(stats.Numbers.Length * rule.Rank / 100d) : rule.Rank, 1, stats.Numbers.Length);
        return rule.Kind switch
        {
            ConditionalFormatKind.Top => value.Number >= stats.Numbers[^rank],
            ConditionalFormatKind.Bottom => value.Number <= stats.Numbers[rank - 1],
            _ => false
        };
    }

    private static int Compare(CalcValue left, CalcValue right)
    {
        if (left.TryNumber(out var a) && right.TryNumber(out var b)) return a.CompareTo(b);
        return string.Compare(left.ToString(), right.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static double Fraction(double value, double low, double high)
    {
        if (high == low) return value == 0 ? 0 : .5;
        // Scaling before subtraction avoids overflow for opposite-sign finite extrema.
        var scale = Math.Max(1, Math.Max(Math.Abs(low), Math.Abs(high)));
        return Math.Clamp((value / scale - low / scale) / (high / scale - low / scale), 0, 1);
    }

    private static string ScaleColor(ConditionalFormatRule rule, Statistics stats, double number)
    {
        if (!rule.ThreeColorScale) return Interpolate(rule.LowColor, rule.HighColor, stats.Minimum == stats.Maximum ? .5 : Fraction(number, stats.Minimum, stats.Maximum));
        if (stats.Minimum == stats.Maximum) return rule.MiddleColor;
        return number <= stats.Median
            ? Interpolate(rule.LowColor, rule.MiddleColor, Fraction(number, stats.Minimum, stats.Median))
            : Interpolate(rule.MiddleColor, rule.HighColor, Fraction(number, stats.Median, stats.Maximum));
    }

    private static string Interpolate(string from, string to, double fraction)
    {
        var a = Convert.ToInt32(from[1..], 16);
        var b = Convert.ToInt32(to[1..], 16);
        int Channel(int shift) => (int)Math.Round(((a >> shift) & 255) * (1 - fraction) + ((b >> shift) & 255) * fraction);
        return $"#{Channel(16):X2}{Channel(8):X2}{Channel(0):X2}";
    }
}
