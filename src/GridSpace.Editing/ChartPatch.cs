using GridSpace.Core;

namespace GridSpace.Editing;

internal sealed record ChartPatch(int BeforeIndex, int AfterIndex, ChartSpec? Before, ChartSpec? After)
{
    public string Id => (After ?? Before)!.Id;
    public long Size => 1024 + 2L * ((Before?.Title.Length ?? 0) + (After?.Title.Length ?? 0))
        + 512L * ((Before?.Series.Count ?? 0) + (After?.Series.Count ?? 0));
}
