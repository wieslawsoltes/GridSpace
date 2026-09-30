using GridSpace.Core;

namespace GridSpace.Editing;

internal sealed record PivotPatch(int Index, PivotTableSpec? Before, PivotTableSpec? After)
{
    public string Id => (After ?? Before)!.Id;
    public long Size { get; } = (Before?.Cache?.EstimatedBytes ?? 0) + (After?.Cache?.EstimatedBytes ?? 0) + PathSize(Before) + PathSize(After) + 2048 + 256L * ((Before?.Values.Count ?? 0) + (After?.Values.Count ?? 0))
        + 2L * ((Before?.FieldNames.Sum(n => n.Length) ?? 0) + (After?.FieldNames.Sum(n => n.Length) ?? 0));
    private static long PathSize(PivotTableSpec? pivot) => pivot?.CollapsedRows.Sum(p =>
        64L + p.Fields.Length * 4L + p.Values.Sum(v => 24L + v.Length * 2L)) ?? 0;
}
