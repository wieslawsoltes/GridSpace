namespace GridSpace.Core;

public enum ConditionalFormatKind
{
    CellValue, Expression, ContainsText, DuplicateValues, UniqueValues,
    Top, Bottom, AboveAverage, BelowAverage, ColorScale, DataBar
}

public enum CellComparison { Equal, NotEqual, GreaterThan, GreaterThanOrEqual, LessThan, LessThanOrEqual, Between, NotBetween }

/// <summary>A single-rectangle conditional rule. Lower numeric priority wins; formulas are relative to the range's upper-left cell.</summary>
public sealed record ConditionalFormatRule
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Range { get; init; } = "A1";
    public ConditionalFormatKind Kind { get; init; }
    public CellComparison Comparison { get; init; } = CellComparison.GreaterThan;
    public string Operand { get; init; } = "0";
    public string Operand2 { get; init; } = "0";
    public DifferentialStyle Style { get; init; } = new() { Background = "#FFC7CE", Foreground = "#9C0006" };
    public int Priority { get; init; } = 1;
    public bool StopIfTrue { get; init; }
    public int Rank { get; init; } = 10;
    public bool Percent { get; init; }
    public string LowColor { get; init; } = "#F8696B";
    public string MiddleColor { get; init; } = "#FFEB84";
    public string HighColor { get; init; } = "#63BE7B";
    public bool ThreeColorScale { get; init; } = true;
    public bool ShowValue { get; init; } = true;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || Id.Length > 128) throw new InvalidDataException("Invalid conditional-format rule identifier.");
        if (!CellRange.TryParse(Range, out var range) || range.Count > 100_000)
            throw new InvalidDataException("Conditional-format ranges are limited to 100,000 cells.");
        if (!Enum.IsDefined(Kind) || !Enum.IsDefined(Comparison) || Priority < 1 || Rank < 1 || Rank > (Percent ? 100 : 1000))
            throw new InvalidDataException("Invalid conditional-format rule options.");
        if (Operand is null || Operand2 is null || Operand.Length > 8192 || Operand2.Length > 8192)
            throw new InvalidDataException("Conditional formulas are limited to 8,192 characters.");
        if (Style is null) throw new InvalidDataException("A conditional rule must have a differential style.");
        Style.Validate();
        DifferentialStyle.ValidateColor(LowColor);
        DifferentialStyle.ValidateColor(MiddleColor);
        DifferentialStyle.ValidateColor(HighColor);
    }
}
