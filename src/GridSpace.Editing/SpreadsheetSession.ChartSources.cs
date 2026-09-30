using GridSpace.Core;

namespace GridSpace.Editing;

public sealed partial class SpreadsheetSession
{
    /// <summary>
    /// Commits a source preview only against the same live chart definition. Intervening edits,
    /// undo, loads, sheet changes and report-owned bindings cannot be overwritten by a stale drag.
    /// Uses drawing-delta history; source cells and formula/geometry revisions remain untouched.
    /// </summary>
    public void CommitChartSourceEdit(ChartSpec expected, ChartSourceBinding binding, CellRange range)
    {
        ArgumentNullException.ThrowIfNull(expected);
        if (!ReferenceEquals(FindChart(expected.Id), expected))
            throw new InvalidOperationException("The chart changed while its source was being edited. Start the edit again.");
        UpdateChart(expected.Id, chart => ChartSourceEditing.Replace(chart, Sheet.Name, binding, range), "Change chart source");
    }
}
