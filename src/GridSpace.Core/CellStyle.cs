namespace GridSpace.Core;

public enum CellAlignment { General, Left, Center, Right }
public sealed record CellStyle
{
    public string FontFamily { get; init; } = "Arial";
    public double FontSize { get; init; } = 11;
    public bool Bold { get; init; }
    public bool Italic { get; init; }
    public bool Underline { get; init; }
    public string Foreground { get; init; } = "#242424";
    public string Background { get; init; } = "#FFFFFF";
    public string NumberFormat { get; init; } = "General";
    public CellAlignment Alignment { get; init; }
    public bool Wrap { get; init; }
    public bool Border { get; init; }
    public static CellStyle Default { get; } = new();
}
