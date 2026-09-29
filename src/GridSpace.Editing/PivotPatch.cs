using GridSpace.Core;

namespace GridSpace.Editing;

internal sealed record PivotPatch(int Index, PivotTableSpec? Before, PivotTableSpec? After)
{
    public string Id => (After ?? Before)!.Id;
    public long Size { get; } = (Before?.Cache?.EstimatedBytes ?? 0) + (After?.Cache?.EstimatedBytes ?? 0) + 2048 + 256L * ((Before?.Values.Count ?? 0) + (After?.Values.Count ?? 0))
        + 2L * ((Before?.FieldNames.Sum(n => n.Length) ?? 0) + (After?.FieldNames.Sum(n => n.Length) ?? 0));
}
