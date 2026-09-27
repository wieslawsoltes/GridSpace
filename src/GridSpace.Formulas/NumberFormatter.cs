using System.Globalization;

namespace GridSpace.Formulas;

public static class NumberFormatter
{
    public static string Format(CalcValue value, string? format)
    {
        if (value.Kind != ValueKind.Number) return value.ToString();
        if (string.IsNullOrEmpty(format) || format is "General" or "@") return value.ToString();
        try
        {
            var lower = format.ToLowerInvariant();
            if (lower.Contains('y') || lower.Contains('d'))
            {
                var dateFormat = format.Replace("yyyy", "yyyy").Replace("mm", "MM").Replace("m/d", "M/d");
                return DateTime.FromOADate(value.Number).ToString(dateFormat, CultureInfo.InvariantCulture);
            }
            return value.Number.ToString(format, CultureInfo.InvariantCulture);
        }
        catch (FormatException) { return value.ToString(); }
        catch (ArgumentException) { return value.ToString(); }
    }
}
