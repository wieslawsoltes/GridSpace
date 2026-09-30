# Direct chart source editing

## Worksheet workflow

Select an ordinary chart, then choose **Chart Design → Edit Source**. The worksheet scrolls to its local source without deselecting the chart or closing its inspector. Drag the colored range border to move the source; drag the start or end corner to resize it. The drawing itself remains above these overlays and owns pointer input wherever it covers a source border. Move the chart aside when its own handle obscures a source grip.

An automatic chart shows its entire data table in purple. Resizing that table preserves automatic binding: adding or removing data rows changes points, while adding or removing series columns changes automatic series. The transposed mode uses the corresponding rows/columns. Existing header and orientation settings are preserved.

**Chart Design → Customize Series** changes automatic bindings into explicit category and value vectors as one undoable command. Categories appear in orange and each value vector uses a series color. Vectors move independently, retain their original row or column orientation when resized, and do not overwrite other bindings. Automatic header names remain live cell-linked names when customized; independent single-cell name grips retarget those links without changing value ranges. See [live chart text](chart-text-links.md).

Shrinking a vector to one cell retains an orientation hint so a subsequent drag extends along the same axis. This survives undo/redo, native save/load and GridSpace's supported XLSX roundtrip. A larger vector always derives its axis from the actual range, overriding old hints. The hint is native editing metadata carried by the existing GridSpace XLSX extension: a plain external one-cell chart reference does not itself specify an axis.

Category and value lengths are deliberately independent for explicit series, matching the existing binding model. Extending only a value vector does not invent category labels. Extend the category vector separately when adding corresponding categories.

## Gesture contract

A press captures one pointer and records the original chart definition, source binding and cell position. Movement beyond six DIPs begins a preview. The preview snaps to cells, not arbitrary pixel offsets, and recalculates bound chart data only when that snapped range changes. Source cell values, cell styles, the workbook's input revision, geometry revision and undo history remain unchanged during preview.

Release inside the worksheet commits one drawing-delta history entry. Escape, pointer-capture loss, unloading, another document edit, switching worksheets, changing zoom before release, or releasing outside the worksheet cancels. Another pointer cannot move or commit the captured gesture. A moved range clamps as a whole at worksheet edges rather than truncating its shape; crossing resize endpoints normalizes the resulting range.

Invalid previews never fall back to committing an earlier valid endpoint. Rectangular automatic sources must provide categories, data points and 1–32 series. Category and value vectors share a 100,000-source-cell budget, in addition to the existing per-range bounds. Invalid shape, oversized sources or stale chart definitions fail before source/history mutation.

Edge scrolling operates while a source drag is active. It uses the same sparse axis layout and frozen-pane geometry as rendering and hit testing. Off-screen clipped edges never become artificial resize handles. Low-zoom overlapping grips select the nearest endpoint. Captured source gestures outrank underlying filter-header controls and do not open a filter popup mid-drag. All source editing happens through the public session transaction API; no browser mutation bridge is used.

## PivotTable and cross-sheet boundary

PivotChart source ranges are owned by the report and have no worksheet source grips. Change their fields, filters or expanded groups in **PivotTable Fields** instead. The new Customize Series command refuses to detach PivotCharts.

A chart whose data comes from another sheet does not show those references on the wrong worksheet. Its source remains editable through the inspector's existing address controls. Cross-sheet source picking while retaining a chart on another sheet and arbitrary discontiguous range picking are not implemented. Supported single-cell text links can reference another worksheet through the inspector.

## Performance and persistence

`ChartSourceEditing` computes bounded binding metadata and range transforms without evaluating cells. The renderer caches these bindings by chart-definition identity, host sheet name and structure revision. A deterministic test checks that 1,000 warm metadata lookups allocate no bytes and evaluate no cells. This is not a claim that painting or chart-source recalculation is allocation-free.

Once a gesture changes a snapped range, the existing chart-data cache resolves that preview. It does not maintain a separate growing history of previews. Chart-only commits retain scalar calculation and sparse viewport state. Undo/redo uses existing drawing deltas, and native and supported XLSX persistence retain the updated ordinary chart bindings. One-cell orientation hints are optional, backward-compatible fields; no new workbook format or schema version is introduced.

```csharp
using GridSpace.Core;
using GridSpace.Editing;

var chart = session.FindChart(chartId)!;
var source = ChartSourceEditing.Bindings(chart, session.Sheet.Name)[0];
var preview = ChartSourceEditing.Transform(source, ChartSourceHandle.End, rowDelta: 4, columnDelta: 0);
// The original live definition is the optimistic concurrency token.
session.CommitChartSourceEdit(chart, source, preview);
session.Undo();
```

The reusable Uno control exposes `RevealChartSource()`. `ChartSourceGeometry` is UI-independent and supplies the same pane ownership and hit regions used for drawing and browser diagnostics. Existing keyboard-accessible inspector fields remain an alternative to pointer dragging.

## Verification

Engine tests cover bounds, vector orientation, automatic/transposed/headerless sources, stale updates, combined budgets, input/geometry revision preservation, undo/redo, metadata caching, native/XLSX reference roundtrips, frozen panes and clipped grips. Scalar-vector tests cover shrinking and re-expanding through persistence and history. Browser specifications send real pointer/keyboard input for preview, commit, cancellation, moving sources, explicit series, invalid endpoints, PivotChart protection, edge scrolling, zoom changes and transposed categories over filter headers. Diagnostics expose read-only geometry and observable state, not setters. GitHub Actions is the build and runtime-test environment for this increment; supplementary local browser navigation was blocked before application execution and is not counted as a pass.

This work extends the existing chart editor. It does not implement full Excel chart-element, advanced PivotTable, printing or accessibility parity, and does not claim to resolve the separately recorded intermittent zero-length ImageData observation tracked in issue #5.
