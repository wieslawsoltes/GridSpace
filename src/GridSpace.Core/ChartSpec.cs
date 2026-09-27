namespace GridSpace.Core;

public enum ChartKind { Column, Line, Bar, Pie }
public sealed record ChartSpec
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "Chart title";
    public ChartKind Kind { get; set; }
    public string Range { get; set; } = "A1:B5";
    public int Row { get; set; } = 3;
    public int Column { get; set; } = 8;
    public double Width { get; set; } = 440;
    public double Height { get; set; } = 260;
}
