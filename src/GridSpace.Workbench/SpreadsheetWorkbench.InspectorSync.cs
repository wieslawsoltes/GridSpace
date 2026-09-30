namespace GridSpace.Workbench;

public sealed partial class SpreadsheetWorkbench
{
    private bool _chartInspectorRefreshQueued;
    private Workbook? _inspectedChartBook;
    private Worksheet? _inspectedChartSheet;

    private void QueueChartInspectorRefresh()
    {
        if (_disposed || _chartEditor is null || _chartInspectorRefreshQueued) return;
        _chartInspectorRefreshQueued = true;
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            _chartInspectorRefreshQueued = false;
            if (_disposed || _applyingInspector || _chartEditor is null ||
                Surface.SelectedChart is not { } current) return;

            // Resolve the current owner at execution time, never a captured editor
            // or chart that may have been replaced while the callback was queued.
            Try(() => { BindChartInspector(current); Surface.Invalidate(); });
        }))
            _chartInspectorRefreshQueued = false;
    }

    private static bool IsGeometryOnlyChange(ChartSpec before, ChartSpec after) =>
        before.Series.SequenceEqual(after.Series) && before == (after with
        {
            Row = before.Row, Column = before.Column,
            OffsetX = before.OffsetX, OffsetY = before.OffsetY,
            Width = before.Width, Height = before.Height,
            Series = before.Series
        });
}
