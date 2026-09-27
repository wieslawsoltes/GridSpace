namespace GridSpace.Core;

public sealed record Cell
{
    public string Input { get; init; } = "";
    public CellStyle Style { get; init; } = CellStyle.Default;
    public string? Note { get; init; }
    public bool IsFormula => Input.StartsWith('=');
}
