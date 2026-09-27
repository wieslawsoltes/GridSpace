using System.Globalization;
using System.Text;

namespace GridSpace.Core;

/// <summary>A zero-based address within Excel's worksheet dimensions.</summary>
public readonly record struct CellAddress(int Row, int Column)
{
    public const int MaxRows = 1_048_576;
    public const int MaxColumns = 16_384;
    public bool IsValid => Row >= 0 && Row < MaxRows && Column >= 0 && Column < MaxColumns;
    public override string ToString() => ColumnName(Column) + (Row + 1).ToString(CultureInfo.InvariantCulture);
    public static string ColumnName(int column)
    {
        if (column < 0 || column >= MaxColumns) throw new ArgumentOutOfRangeException(nameof(column));
        var text = new StringBuilder();
        for (var n = column + 1; n > 0; n = (n - 1) / 26) text.Insert(0, (char)('A' + (n - 1) % 26));
        return text.ToString();
    }
    public static CellAddress Parse(string text) => TryParse(text, out var result) ? result : throw new FormatException($"Invalid cell address: {text}");
    public static bool TryParse(string? text, out CellAddress address)
    {
        address = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim().Replace("$", "");
        var i = 0; var column = 0;
        while (i < s.Length && char.IsAsciiLetter(s[i]))
        {
            column = column * 26 + char.ToUpperInvariant(s[i++]) - 'A' + 1;
            if (column > MaxColumns) return false;
        }
        if (i == 0 || i == s.Length || !int.TryParse(s.AsSpan(i), NumberStyles.None, CultureInfo.InvariantCulture, out var row)) return false;
        address = new(row - 1, column - 1);
        return address.IsValid;
    }
}
