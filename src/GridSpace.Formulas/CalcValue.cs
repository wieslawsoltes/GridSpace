using System.Globalization;

namespace GridSpace.Formulas;

public enum ValueKind { Blank, Number, Text, Boolean, Error, Array }
public readonly record struct CalcValue
{
    public ValueKind Kind { get; init; }
    public double Number { get; init; }
    public string Text { get; init; }
    public IReadOnlyList<CalcValue>? Items { get; init; }
    public int Columns { get; init; }
    public int Rows => Kind == ValueKind.Array ? (Items?.Count ?? 0) / Math.Max(1, Columns) : 1;
    public int Width => Kind == ValueKind.Array ? Columns : 1;
    public CalcValue Element(int row, int column) => Kind == ValueKind.Array
        ? row >= 0 && row < Rows && column >= 0 && column < Columns ? Items![row * Columns + column] : Error("#N/A")
        : row == 0 && column == 0 ? this : Error("#N/A");
    public bool IsError => Kind == ValueKind.Error;
    public bool IsNumeric => Kind is ValueKind.Number or ValueKind.Boolean or ValueKind.Blank;
    public static CalcValue Blank => new() { Kind = ValueKind.Blank, Text = "" };
    public static CalcValue Num(double n) => double.IsFinite(n) ? new() { Kind = ValueKind.Number, Number = n, Text = "" } : Error("#NUM!");
    public static CalcValue Str(string text) => new() { Kind = ValueKind.Text, Text = text };
    public static CalcValue Bool(bool value) => new() { Kind = ValueKind.Boolean, Number = value ? 1 : 0, Text = "" };
    public static CalcValue Error(string text) => new() { Kind = ValueKind.Error, Text = text };
    public static CalcValue Array(IReadOnlyList<CalcValue> items, int columns)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (columns <= 0 || items.Count == 0 || items.Count % columns != 0) return Error("#CALC!");
        if (items.Count > 100_000) return Error("#LIMIT!");
        return new() { Kind = ValueKind.Array, Items = items, Columns = columns, Text = "" };
    }
    public bool TryNumber(out double number)
    {
        number = Number;
        if (IsNumeric) return true;
        return Kind == ValueKind.Text && double.TryParse(Text, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
    }
    public bool Truth => Kind == ValueKind.Text ? Text.Equals("TRUE", StringComparison.OrdinalIgnoreCase) : !IsError && Number != 0;
    public IEnumerable<CalcValue> Flatten() => Kind == ValueKind.Array ? Items ?? [] : [this];
    public override string ToString() => Kind switch
    {
        ValueKind.Blank => "", ValueKind.Number => Number.ToString("G15", CultureInfo.InvariantCulture),
        ValueKind.Boolean => Number != 0 ? "TRUE" : "FALSE", ValueKind.Array => Items?.FirstOrDefault().ToString() ?? "", _ => Text ?? ""
    };
}
