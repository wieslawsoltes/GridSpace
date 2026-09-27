using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using GridSpace.Core;

namespace GridSpace.Formulas;

/// <summary>Pure filter evaluation. Manual hiding and filter exclusion are deliberately separate.</summary>
public static class WorksheetFilterEngine
{
    public static HashSet<int> Evaluate(Worksheet sheet, CalculationEngine calculation, int? excludingColumn = null)
    {
        if (sheet.FilterRange is null || sheet.Filters.Count == 0) return [];
        var range = CellRange.Parse(sheet.FilterRange);
        if (range.Bottom - range.Top > 100_000) throw new InvalidOperationException("Filtering is limited to 100,000 data rows.");
        var predicates = sheet.Filters.Where(f => f.Column != excludingColumn)
            .Select(f => (f.Column, Match: Compile(f))).ToArray();
        var excluded = new HashSet<int>();
        for (var row = range.Top + 1; row <= range.Bottom; row++)
            foreach (var predicate in predicates)
                if (!predicate.Match(calculation.Evaluate(sheet, new CellAddress(row, predicate.Column))))
                {
                    excluded.Add(row);
                    break;
                }
        return excluded;
    }

    public static Func<CalcValue, bool> Compile(ColumnFilter filter)
    {
        filter.Validate();
        if (filter.Values is not null)
        {
            var accepted = filter.Values.ToHashSet(StringComparer.OrdinalIgnoreCase);
            return value => IsBlank(value) ? filter.IncludeBlank : accepted.Contains(value.ToString());
        }
        if (filter.First is null) return _ => true;
        var first = CompileCondition(filter.First);
        if (filter.Second is null) return first;
        var second = CompileCondition(filter.Second);
        return value => filter.And ? first(value) && second(value) : first(value) || second(value);
    }

    public static bool IsBlank(CalcValue value) => value.Kind == ValueKind.Blank || value.Kind == ValueKind.Text && value.Text.Length == 0;

    private static Func<CalcValue, bool> CompileCondition(FilterCondition condition)
    {
        if (condition.Operator == FilterOperator.Blank) return IsBlank;
        if (condition.Operator == FilterOperator.NotBlank) return value => !IsBlank(value);
        var text = condition.Value;
        var numeric = double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number);
        var pattern = condition.Operator switch
        {
            FilterOperator.Contains or FilterOperator.DoesNotContain => "*" + text + "*",
            FilterOperator.BeginsWith => text + "*",
            FilterOperator.EndsWith => "*" + text,
            _ => text
        };
        var regex = Wildcard(pattern);
        return value =>
        {
            if (condition.Operator is FilterOperator.Contains or FilterOperator.DoesNotContain or FilterOperator.BeginsWith or FilterOperator.EndsWith)
            {
                var matched = !IsBlank(value) && regex.IsMatch(value.ToString());
                return condition.Operator == FilterOperator.DoesNotContain ? !matched : matched;
            }
            if (condition.Operator is FilterOperator.Equal or FilterOperator.NotEqual)
            {
                var matched = numeric && value.Kind == ValueKind.Number ? value.Number == number : regex.IsMatch(value.ToString());
                return condition.Operator == FilterOperator.Equal ? matched : !matched;
            }
            if (IsBlank(value) || value.IsError) return false;
            if (numeric && value.Kind != ValueKind.Number) return false;
            var comparison = numeric ? value.Number.CompareTo(number) : string.Compare(value.ToString(), text, StringComparison.OrdinalIgnoreCase);
            return condition.Operator switch
            {
                FilterOperator.GreaterThan => comparison > 0,
                FilterOperator.GreaterThanOrEqual => comparison >= 0,
                FilterOperator.LessThan => comparison < 0,
                FilterOperator.LessThanOrEqual => comparison <= 0,
                _ => false
            };
        };
    }

    /// <summary>Excel-style *, ? and ~ escaping, implemented with the linear-time nonbacktracking regex engine.</summary>
    private static Regex Wildcard(string input)
    {
        var pattern = new StringBuilder("\\A");
        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];
            if (c == '~' && i + 1 < input.Length) pattern.Append(Regex.Escape(input[++i].ToString()));
            else if (c == '*') pattern.Append(".*");
            else if (c == '?') pattern.Append('.');
            else pattern.Append(Regex.Escape(c.ToString()));
        }
        pattern.Append("\\z");
        return new Regex(pattern.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline | RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(200));
    }
}
