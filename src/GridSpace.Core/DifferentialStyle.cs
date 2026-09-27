namespace GridSpace.Core;

/// <summary>Only explicitly assigned properties participate in rule priority composition.</summary>
public sealed record DifferentialStyle
{
    public string? Background { get; init; }
    public string? Foreground { get; init; }
    public bool? Bold { get; init; }
    public bool? Italic { get; init; }
    public bool? Underline { get; init; }
    public string? NumberFormat { get; init; }

    public CellStyle Apply(CellStyle source) => source with
    {
        Background = Background ?? source.Background,
        Foreground = Foreground ?? source.Foreground,
        Bold = Bold ?? source.Bold,
        Italic = Italic ?? source.Italic,
        Underline = Underline ?? source.Underline,
        NumberFormat = NumberFormat ?? source.NumberFormat
    };

    public DifferentialStyle FillUnset(DifferentialStyle lowerPriority) => new()
    {
        Background = Background ?? lowerPriority.Background,
        Foreground = Foreground ?? lowerPriority.Foreground,
        Bold = Bold ?? lowerPriority.Bold,
        Italic = Italic ?? lowerPriority.Italic,
        Underline = Underline ?? lowerPriority.Underline,
        NumberFormat = NumberFormat ?? lowerPriority.NumberFormat
    };

    public void Validate()
    {
        if (Background is not null) ValidateColor(Background);
        if (Foreground is not null) ValidateColor(Foreground);
        if (NumberFormat?.Length > 512) throw new InvalidDataException("A number format is too long.");
    }

    public static void ValidateColor(string color)
    {
        if (color is null || color.Length != 7 || color[0] != '#' || color.AsSpan(1).ContainsAnyExcept("0123456789abcdefABCDEF"))
            throw new InvalidDataException("Use a six-digit RGB color such as #107C41.");
    }
}
