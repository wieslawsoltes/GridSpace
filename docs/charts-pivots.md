# Charts, WYSIWYG editing and PivotTables — 0.4 preview

## Drawing interaction

Click a chart to select it and open **Format Chart**. Drag its body to move it; drag any of the eight handles to resize it. Hold Shift while resizing to preserve the original aspect ratio. Geometry stays in worksheet units, so input and rendering agree at different zoom factors and through frozen panes. Pointer movement changes a preview only; release creates one history entry. Escape or lost/cancelled pointer capture discards the preview.

Double-click the title or press F2 to edit it in place. Enter commits and Escape cancels. Arrow keys nudge a selected chart by one worksheet unit; Shift+arrows use ten. Delete removes it. Ctrl+D duplicates it; Ctrl+C/X/V copies, cuts or pastes a portable drawing definition, retaining its original data-sheet binding. Chart cut currently removes the original after successful clipboard assignment, rather than using Excel's deferred move mode. Undo restores it.

The contextual **Chart Design** tab exposes title editing, duplication, deletion, series orientation, labels, legend, and z-order. The inspector edits the title, chart type, grouping, legend, source worksheet and range, category vector, header/hidden-data options, series names/ranges/colors/visibility/order, axes, limits, value format, gap, doughnut hole, dimensions, and colors. Combo series can choose column/line/area and the secondary value axis.

The panel is composed from reusable Uno controls. It is not a modal replacement for on-canvas move/resize/title editing. Full Excel typography, every adornment, selection of individual chart elements, dragging legends/plot areas, drag-to-edit cell-range outlines, and universal pixel-level UI parity are not claimed.

## Chart data and rendering

Nine chart families are implemented: **column, bar, line, area, scatter, pie, doughnut, radar, and combo**. Column/bar/area support clustered or ordinary placement, stacked, and 100% stacked grouping as applicable. Positive and negative stacks use independent cumulative baselines. Line and scatter series retain explicit gaps. Scatter requires numeric category/X values; text-only X values are not guessed as coordinates. Pie uses the first visible series; doughnut uses concentric series rings. Combo uses column/line/area series with primary/secondary numeric axes.

Automatic binding uses the first row or column for categories and all remaining rows or columns for series. Explicit category/series bindings are same-worksheet A1 row/column vectors; cross-sheet charts name their source sheet explicitly. Numeric values remain distinct from text; blanks and errors become missing numeric points, not fabricated zeroes in the ordinary Cartesian series. A chart supports 32 series and 100,000 source-vector cells.

Categorical charts render at most 512 categories with an explicit on-chart notice. Radar previews cap at 256 categories with a notice. Line/scatter use ordered extrema-and-gap envelope sampling to bound drawing work on dense inputs. Exported source vectors and caches are not reduced to the preview sample. Data-label density is bounded for readability. These limits are not a complete chart-layout engine: histogram, waterfall, stock, surface, 3D, box plots, trendlines, error bars, arbitrary per-point styling, every axis mode and every Excel chart feature are still absent.

`ChartDataCache` retains calculated source arrays across chart movement, resizing, selection and appearance changes. A cached PivotChart lookup compares its report identity rather than rebuilding automatic series on every frame. Workbook input revision, binding changes or a replacement PivotTable definition trigger refresh. Both the cache and renderer are single-owner objects with explicit lifetime.

## PivotTable workflows

Select a rectangular source with unique, non-empty column headers and choose **Insert → PivotTable**. The workbench creates a report on a new worksheet. Select report cells for **PivotTable Fields**. Drag a source field into Rows, Columns, Values or Filters, or select the target area and use the keyboard-accessible Add buttons. Reorder/remove dimensions and measures through the field controls.

Each measure chooses **Sum, Count, Count Numbers, Average, Minimum, Maximum, Product, sample/population standard deviation, or sample/population variance**. It also supports normal values and percentages of the row, column, or grand total; captions and number formats are editable. Multiple measures and multiple row/column dimensions are supported. Case-insensitive textual groups remain distinct from numeric groups: numeric `1` and textual `"1"` do not collapse together.

Aggregation uses typed hash keys, compensated sums and Welford statistics. Grand totals are calculated from source accumulators, not by averaging already aggregated averages. Report filters are applied before accumulation. The implemented layout is a flattened tabular header with repeated row labels, not Excel's complete compact/outline/subtotal hierarchy.

Refresh is explicit. Source edits do not silently rebuild reports or change their detail records. Each report stores immutable, typed source values from the last refresh. Double-click a numeric report cell, or use **Show Details**, to create a worksheet containing contributing records from that same snapshot. Filtered-out source records remain in the cache so a subsequent filter change can use the full captured source. A live refresh captures the current source again.

Report values are owned output. Partial input edits, sorts, merges, pastes or structural edits through the report are rejected. Styling remains available, although refresh reapplies its header, stripe, totals and measure-number styles. A refresh fully computes and checks replacement output before clearing or writing cells. Expansion cannot overwrite occupied cells, another report, merged cells, spilled arrays or any PivotTable source range. Undo/redo records output deltas and immutable cache/definition snapshots instead of serializing unrelated worksheets.

A linked PivotChart follows the report's current category/measure region and omits grand-total rows/columns. Copied report sheets receive independent report/chart IDs and corrected self-sheet bindings. Source-schema changes rebase referenced fields and invalidate an incompatible cached schema; refresh is required before saving/drilling through that cache. Deleting a referenced source header, selected field, or source worksheet is rejected rather than silently corrupting its reports.

Limits: 32 reports per sheet; 256 source fields; 8 row and 8 column dimensions; 16 measures; 32 report filters; 10,000 explicitly selected values per filter; 200,000 cached/source cells and 100,000 output cells per report. Date/number grouping, subtotals, expand/collapse, calculated fields/items, slicers, timelines, OLAP/data models and arbitrary external sources are not implemented.

## Native and XLSX persistence

Native JSON includes drawing definitions and PivotTable configuration, owned output, and immutable source caches. Drawing-only revisions are transient and do not force formula recalculation. Existing schema-1 files without the new fields retain default values.

Chart XLSX parts contain real multi-series chart definitions, styles, caches, axes, legend/labels, grouping, and one-cell drawing anchors with offsets and sizes. The reader also accepts supported two-cell anchor geometry. Native GridSpace extensions retain identities and options not represented by the currently supported standard subset; supported external definitions can be read without those extensions.

Pivot export writes **workbook cache registrations, `pivotCacheDefinition`, typed shared items, `pivotCacheRecords`, `pivotTableDefinition`, and the required package relationships**, alongside worksheet result cells. Chart and PivotTable relationships coexist in the same worksheet relationship part. Cache records use the last-refresh snapshot, not fresh source values paired with stale report cells.

The ordinary worksheet-source PivotTable reader accepts supported row/column/data/page fields and aggregate/show-as settings. Unsupported external layouts preserve their worksheet values and require an explicit refresh before converting to GridSpace's flattened report or enabling drill-through. It does not pretend to preserve arbitrary grouped, subtotalled, OLAP or calculated-field definitions. Unknown content remains subject to the existing non-lossless XLSX boundary.

A GridSpace-linked PivotChart is currently exported as a **standard chart over PivotTable result cells**, plus the GridSpace linkage extension. Full native Excel PivotChart field buttons, pivot chart filters and every pivot-specific chart record are not emitted.

Representative outputs are independently checked by the Open XML SDK Office 2019 validator. This checks package/schema constraints, not Excel application behavior. Microsoft Excel itself was not run during this increment.

## Reusable API

```csharp
var session = new SpreadsheetSession(book);
var chart = new ChartSpec
{
    SourceSheet = "Sales",
    Range = "A1:C20",
    Categories = "A2:A20",
    Kind = ChartKind.Combo,
    Series =
    [
        new() { Name = "Revenue", Values = "B2:B20", Color = "#4472C4" },
        new() { Name = "Units", Values = "C2:C20", Kind = ChartKind.Line,
                SecondaryAxis = true, Color = "#ED7D31" }
    ]
};
session.AddChart(chart);
session.UpdateChart(chart.Id, c => c with { Width = 720, Title = "Quarterly results" });
session.Undo();

// Field indexes are zero-based and relative to SourceRange.
session.AddSheet("Report");
var pivot = new PivotTableSpec
{
    Name = "SalesByRegion", SourceSheet = "Sales", SourceRange = "A1:C20",
    Destination = "B3", Rows = [0],
    Values = [new() { Field = 1, Aggregate = PivotAggregate.Sum }]
};
session.SetPivotTable(pivot);
session.AddPivotChart(pivot.Id);
session.RefreshPivotTable(pivot.Id);
```

`ChartSpec`, `ChartSeries`, `PivotTableSpec`, and `PivotCacheSnapshot` are in Core. `ChartDataResolver`, `ChartDataCache`, and `PivotEngine` are UI-independent. `ChartGeometry` shares input and rendering geometry. `ChartRenderer` can render on any Skia canvas. `ChartEditorControl` and `PivotFieldListControl` can be embedded without the workbench.

**Help → Charts & Pivots** opens `AnalyticsSampleWorkbook.Create()`: editable source data, a cached/refreshable report, and two chart examples.

## Validation and performance

Run:

```bash
dotnet test tests/GridSpace.Tests -c Release
dotnet run --project tools/GridSpace.AnalyticsBenchmarks -c Release -- 10000
npm run test:browser
```

The benchmark uses 10,000 fact rows, 50 row groups, 10 column groups and two measures. It separately records capture/aggregation, aggregation of a captured source, cached binding, drawing edits and XLSX export. Timings are workload-specific CPU observations; they are not GPU frame times or a universal performance claim. CI records them without machine-dependent timing gates. Deterministic tests verify object identity, evaluation counts, allocation-free cached PivotChart lookups and bounded delta history.

This preview was validated locally with the engine/raster/Open XML tests and C# API compilation of the Controls/Workbench sources against the published Uno assemblies. Normal Uno SDK browser/native builds, physical-input browser acceptance, and Excel application interoperability remain pending in this delivery environment. The browser runner was blocked by an administrator policy; no browser test pass is claimed. Four physical-input acceptance tests are committed to the source for the normal CI build.


### Recorded local observations

One standalone .NET 10.0.12 / Debian x64 run, 10,000 rows (40,004 stored source cells), 50 row groups, 10 column groups, two measures:

| Operation | Median | Allocated bytes per operation |
| --- | ---: | ---: |
| `pivot-capture-and-aggregate` | 23.937500 ms | 9,788,024 |
| `pivot-aggregate-captured-values` | 7.862400 ms | 8,003,736 |
| `cached-pivot-chart-binding` | 0.000133 ms | 0 |
| `chart-update-and-cached-data` | 0.006645 ms | 2,304 |
| `xlsx-chart-and-pivot-cache-export` | 374.019700 ms | 88,854,752 |

The cached-binding time is near the measurement floor; the useful invariant is zero allocation and no source recomputation. The two data resolutions in the complete harness correspond to the two distinct charts. Raw observations are in `docs/benchmarks/charts-pivots-0.4-local.json`. Export still builds bounded XML trees and has substantial allocation; it is not advertised as streaming or constant-memory.

## Sources

Behavior and packaging were checked against Microsoft's PivotTable and chart documentation and the Open XML object model:

- https://learn.microsoft.com/en-us/office/open-xml/spreadsheet/working-with-pivottables
- https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.spreadsheet.pivottabledefinition
- https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.spreadsheet.pivotcachedefinition
- https://support.microsoft.com/en-us/office/create-a-pivottable-to-analyze-worksheet-data-a9a84538-bfe9-40a9-a8e9-f99134456576

No Microsoft binary, logo, proprietary font or extracted application asset was added.
