using System.Text.Json;
using GridSpace.Core;
using GridSpace.Formulas;
using GridSpace.Layout;
using GridSpace.Skia;
using Windows.Foundation;

namespace GridSpace.App;

internal sealed partial class BrowserDiagnostics
{
    private void WriteActiveCellGeometry(Utf8JsonWriter json)
    {
        var bounds = _workbench.Surface.Viewport.CellBounds(_session.ActiveCell);
        var origin = _workbench.Surface.TransformToVisual(_workbench).TransformPoint(new Point(0, 0));
        json.WriteStartObject("activeCellBounds");
        json.WriteNumber("x", origin.X + bounds.X); json.WriteNumber("y", origin.Y + bounds.Y);
        json.WriteNumber("width", bounds.Width); json.WriteNumber("height", bounds.Height);
        json.WriteEndObject();
    }

    private void WritePivotHierarchy(Utf8JsonWriter json, PivotTableSpec pivot)
    {
        json.WriteString("layout", pivot.Layout.ToString());
        json.WriteString("subtotals", pivot.Subtotals.ToString());
        json.WriteNumber("collapsed", pivot.CollapsedRows.Count);
        json.WriteStartArray("toggles");
        if (pivot.Cache is not null && !pivot.NeedsLayoutRefresh)
        {
            var view = _workbench.Surface.Viewport;
            var origin = _workbench.Surface.TransformToVisual(_workbench).TransformPoint(new Point(0, 0));
            var report = PivotReportCache.Get(pivot);
            var anchor = pivot.Anchor;
            var emitted = 0;
            foreach (var pane in view.Panes())
            {
                foreach (var row in view.Rows.Visible((pane.Clip.Y - pane.OriginY) / view.Zoom,
                    (pane.Clip.Bottom - pane.OriginY) / view.Zoom, Math.Max(anchor.Row + 1, pane.FirstRow),
                    Math.Min(anchor.Row + report.RowCount, pane.LastRow)))
                for (var column = Math.Max(anchor.Column, pane.FirstColumn); column < Math.Min(anchor.Column + report.LabelColumns, pane.LastColumn); column++)
                {
                    if (emitted >= 256 || !report.OutlineCells.TryGetValue((row - anchor.Row, column - anchor.Column), out var outline) || outline.Group is null) continue;
                    var address = new CellAddress(row, column);
                    var bounds = PivotOutlineGeometry.ToggleBounds(view.RangeBounds(new(address, address), pane), outline, view.Zoom);
                    var x = bounds.X + bounds.Width / 2; var y = bounds.Y + bounds.Height / 2;
                    if (!pane.Clip.Contains(x, y) || ChartGeometry.HitTest(_session.Sheet.Charts, view, x, y, _workbench.Surface.SelectedChartId) is not null) continue;
                    json.WriteStartObject();
                    json.WriteString("address", address.ToString());
                    json.WriteString("path", PivotHierarchy.Key(outline.Group).Label);
                    json.WriteBoolean("expanded", outline.Expanded);
                    json.WriteNumber("x", bounds.X + origin.X); json.WriteNumber("y", bounds.Y + origin.Y);
                    json.WriteNumber("width", bounds.Width); json.WriteNumber("height", bounds.Height);
                    json.WriteEndObject(); emitted++;
                }
            }
        }
        json.WriteEndArray();
    }
}
