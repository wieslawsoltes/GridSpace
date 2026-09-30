# PivotTable row hierarchy

GridSpace 0.4 adds hierarchical row presentation to the worksheet-source PivotTable engine. The aggregation cache, generated report, direct editing controls, linked charts and file representation share explicit display-row provenance. This is a supported implementation, not complete Excel PivotTable parity.

## Direct worksheet editing

Add two or more fields to Rows in **PivotTable Fields**. Choose **Compact**, **Outline** or **Tabular** in Report layout, then choose None, Top or Bottom in Subtotals. Compact indents levels in one label column; Outline uses a separate label column per field with parent bands; Tabular puts field labels in separate columns on each detail row, with optional label repetition.

The worksheet paints small plus/minus buttons beside expandable parent labels. Click a button or double-click the parent label to expand or collapse that typed group. Selecting a parent label and pressing **Ctrl+Alt+Left** collapses it; **Ctrl+Alt+Right** expands it. The contextual **PivotTable Analyze** ribbon and field list also expose Expand all and Collapse all. Collapse all folds the outermost level; Expand all removes all retained collapse paths.

Pointer press records the target but does not rebuild or select the report. Release commits one undoable change only while still over that target. Escape, lost capture or movement beyond the click threshold cancels. Delaying mutation until release avoids reflowing the inspector beneath an active click. Double-tap on an expansion glyph is consumed after its ordinary clicks, rather than applying an additional third toggle.

The renderer, hit tester and read-only browser diagnostics use the same bounds and indentation, including zoom and frozen panes. Numeric-value double-click remains Show Details; compact layout uses one label column when deciding whether the pointer targets a value.

## Typed paths and subtotal correctness

A collapse path contains the source-relative field indexes plus typed input literals for its prefix. Numeric `1`, textual `"1"`, blank, boolean and error members do not collide because they happen to have similar displayed labels. Textual keys compare case-insensitively. A child named `A` under North can be collapsed independently from `A` under South. Collapsing and expanding North retains the child's previous state.

Reordering row dimensions discards paths that no longer match the row-axis prefix. Source-column insertion rebases field identities where the source range expands. Replacing the captured source schema invalidates incompatible cached records; a live refresh also discards paths whose field-header identities changed. Display labels are never used as persistent IDs.

Every applicable parent prefix receives observations from the source rows. Subtotals therefore use the same compensated sum and Welford statistics as ordinary groups. Average, variance and standard deviation are not calculated by averaging child totals. Grand totals receive each source row exactly once, independently of the number of visible subtotal bands. Percent-of-row, percent-of-column and percent-of-grand-total use the corresponding raw-aggregate denominator.

The current subtotal policy is uniform: each non-leaf row level uses the measure's aggregate, either above or below the group. Per-field mixed positions, multiple custom subtotal functions, date/number bucketing, arbitrary manual member grouping and column-axis hierarchy are not implemented.

## Cached epochs, ownership and undo

Collapse/expand and presentation changes call `ReconfigurePivotTable`, using the report's immutable last-refresh source cache. Editing a source cell does not silently change the values seen by collapse or drill-through. **Refresh** explicitly captures new source values. Undo and redo restore the report output and its corresponding cache/definition together.

Before expansion writes anything, the engine calculates and validates the entire replacement rectangle. It refuses to overwrite unrelated occupied cells, another PivotTable, merges, spilled arrays or report sources. A rejected expansion leaves the existing report, document state and history intact. Generated cells remain protected from partial input edits.

`PivotReport.RowBands` distinguishes Detail, GroupHeader, Subtotal, Collapsed and GrandTotal rows. Show Details uses that row provenance, not a display-row index into the original leaf list. A collapsed group and its subtotal select all contributing source records under that prefix, intersected with the selected column group and report filters. Group headings have no aggregate and reject drill-through.

## Linked charts without duplicate totals

Linked charts project only detail rows and collapsed aggregates. They exclude group headers, subtotal rows and grand totals. Expanding a group replaces its chart point with its children; collapsing it replaces those children with their group aggregate. A chart never plots both a subtotal and all of its details as ordinary categories.

Category captions include the full visible field path, for example `North / A`. Bound value arrays and the exact worksheet areas are cached together. Cosmetic chart edits retain those arrays; a new report definition invalidates the projection. The chart still uses GridSpace's supported chart families and ordinary chart rendering, not Excel's complete PivotChart field-button interface.

## XLSX representation and its boundary

Standard PivotTable XML includes layout flags, subtotal settings, row-item order/type, typed cache references and supported top-level hidden-detail flags. The cache records remain the captured source, not an aggregation of report cells. The native JSON format and GridSpace's existing extension retain all nested typed collapse paths and the repeat-label setting.

A shared field-item hidden-detail flag cannot express different states for the same member under different parents. GridSpace does not emit an incorrect global flag for that nested case. The saved row projection and extension retain its exact state for GridSpace; arbitrary Excel refresh behavior and every external per-field layout combination are not claimed. Imported standard layouts require explicit conversion refresh before cached hierarchy editing, charting or PivotTable export, while their saved worksheet cells remain available.

Hierarchy chart values use exact same-sheet union references to their detail/collapsed worksheet rows, excluding subtotal and heading gaps. A composite category caption cannot be represented correctly by referring only to one field-label column, so export writes standard literal category captions. Those captions are a snapshot, not live formulas responding to arbitrary outside edits in Excel. Native linkage refreshes them when GridSpace refreshes or reconfigures the report. Import of arbitrary external charts with union/literal bindings remains incomplete.

References exceeding the 8,192-character export bound are rejected with an actionable message; the writer does not silently turn a fragmented hierarchy into a continuous range that double-counts totals. Independent Open XML schema tests cover all nine layout/subtotal combinations and collapsed chart projection. Schema validation does not mean Microsoft Excel was run in CI or that arbitrary XLSX packages are preserved losslessly.

## Reuse and performance

```csharp
using GridSpace.Core;
using GridSpace.Formulas;

var pivot = session.Sheet.PivotTables[0];
session.ReconfigurePivotTable(pivot with
{
    Layout = PivotLayout.Compact,
    Subtotals = PivotSubtotals.Bottom
});

pivot = session.Sheet.PivotTables[0];
var path = PivotHierarchy.Path(pivot, new PivotKey([CalcValue.Str("North")]));
session.TogglePivotGroup(pivot.Id, path);
session.Undo();

var report = PivotReportCache.Get(session.Sheet.PivotTables[0]);
var rowKinds = report.RowBands;
```

`PivotReportCache` is weakly keyed by immutable-by-convention definition identity. `OutlineCells` is constructed once, and the renderer caches report location metadata rather than reparsing a range string per cell. Warm label/outline reads are allocation-free under the deterministic regression workload. Hosts that mutate a definition directly must invalidate its report cache; normal session commands replace definitions.

Hierarchy construction still aggregates source records and writes output deltas; it is not incremental per-node aggregation. Limits are 8 row dimensions, 400,000 accumulator cells, 100,000 output cells, and 10,000 collapsed paths with at most 512 KB of path metadata, in addition to the existing source/cache limits. One large retained undo record remains subject to the documented history policy. Live session mutation has a single owner.

Run `dotnet run --project tools/GridSpace.AnalyticsBenchmarks -c Release -- 10000` to record aggregation, cached presentation and collapse/expand observations. Elapsed times are workload- and machine-dependent, not browser frame-time guarantees. Browser acceptance uses actual pointer/keyboard input; the optional diagnostics expose no document-mutation hook.

## Recorded engine observations

The same .NET 10.0.12 Debian x64 run used 10,000 source records, 50 outer groups, 10 second-level groups and two measures. The paired layout scenarios differ only in whether they recapture the live source or reuse its immutable snapshot. Raw observations are in [0.4-hierarchy.json](benchmarks/0.4-hierarchy.json).

| Scenario | Median per operation | Allocated bytes per operation |
| --- | ---: | ---: |
| Reconfigure with live source recapture | 36.9843 ms | 15,323,024 |
| Reconfigure from existing source cache | 18.2468 ms | 14,056,088 |
| Build compact hierarchy from captured values | 5.5374 ms | 8,904,336 |
| Collapse all then expand all (one pair) | 31.8942 ms | 30,461,408 |
| Warm hierarchy presentation lookup | Below useful timing resolution | 0 |
| Warm PivotChart binding lookup | Below useful timing resolution | 0 |

These observations do not measure a browser frame, UI-thread responsiveness or arbitrary workbook speed. Cached layout avoids source capture but still aggregates and writes owned output deltas; collapse/expand is not allocation-free. Zero-allocation warm presentation/binding paths have deterministic regression checks rather than machine-sensitive timing gates.

## References

The interaction vocabulary follows Microsoft's [PivotTable layout guide](https://support.microsoft.com/en-us/excel/design-the-layout-and-format-of-a-pivottable) and [expand/collapse and Show Details guide](https://support.microsoft.com/en-us/excel/expand-collapse-or-show-details-in-a-pivottable-or-pivotchart). File attributes are checked against Microsoft's [PivotField specification](https://learn.microsoft.com/en-us/openspecs/office_standards/ms-xlsx/4af2951d-0cec-463d-b5cc-c58fbfab90d8) and Open XML [Item](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.spreadsheet.item) and [RowItem](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.spreadsheet.rowitem) references. These are compatibility inputs, not an assertion of Microsoft affiliation or full Excel equivalence.
