# Data tools and structural-edit parity

This increment implements conditional formatting, compound column filters, ordered multi-level sorting, and bounded whole-row/column edits. These are real model/engine/UI/file features, not ribbon placeholders. They do not imply complete Excel compatibility.

## Conditional formatting

Select a rectangular range and choose **Home → Conditional Formatting**, or **Data → New Rule**. The reusable rule editor supports cell comparisons (including between/not-between), formula expressions, contains-text, duplicate/unique values, top/bottom items or percentages, above/below average, two/three-color scales, and signed data bars. Quick commands add a color scale, data bars or duplicate-value highlighting without a dialog.

**Data → Manage Rules** edits, deletes and reorders the current sheet's rules. Lower numeric priorities are evaluated first. Each explicitly assigned differential property wins independently; an unassigned property can come from a later rule or the base cell style. Explicit false values, such as disabling bold, are not mistaken for an unset property. Stop If True terminates evaluation after a matching boolean rule. Visual scales and data bars do not have a Stop If True switch.

Formula rules are relative to the upper-left cell of their applies-to range. For example, applying `=$D6>200` to `B6:I17` highlights each row according to its own column D value. Values remain in the workbook; rendering uses the effective conditional style without mutating the underlying cell style. Editing inputs invalidates the revision-based rule/statistics cache.

Color-scale endpoints currently use the numeric minimum/maximum; a three-color midpoint uses the median. Data bars use a zero axis, with separate positive and negative colors. Custom formula/percentile endpoints other than the median, icon sets, rich differential borders and all Excel rule variants remain outside this implementation.

## Filters

Click a worksheet's filter-header button or choose **Data → Filter**. The filter popup includes ascending/descending sorts, a virtualized searchable value checklist and custom conditions. Checklist search changes what is displayed, not the set of already selected values. More than 10,000 distinct values switches to custom conditions rather than silently presenting an incomplete checklist.

A custom filter supports one or two predicates joined by And/Or. Filters on different columns combine with And. Text conditions support `*`, `?` and `~` escaping. Numeric relational conditions compare numeric cells, not a locale-dependent formatted display string. Explicit value choices compare invariant calculated text. Date groups, color/icon filters, dynamic filters and a complete Excel formatted-value filtering model are not implemented.

**Clear column** removes only that column's criteria. **Data → Clear Filters** removes all criteria. **Reapply** reevaluates them after editing data. The model stores manually hidden rows separately from filtered-out rows, so clearing filters does not expose rows hidden with **View → Hide Rows**. The viewport, scrollbar geometry and keyboard navigation all use the union of these visibility sets.

## Multi-level sort

Select the data rectangle, or a cell inside an existing filter range, then choose **Data → Custom Sort**. Add, remove or reorder sort levels; each level uses a distinct absolute worksheet column and its own direction. Header and case-sensitive options are explicit. Sorts are stable: equal keys retain their original row order, and blank values stay last in either direction.

The operation moves cell records, translates row-relative formulas and reapplies filter criteria as one undoable transaction. It rejects intersecting merged cells instead of silently corrupting their geometry. Left-to-right sorts, custom lists, color/icon sort keys and complete Excel sort semantics for every value coercion are not implemented.

## Structural insertion and deletion

Home and the cell context menu expose Insert/Delete Rows and Columns. The number of edited rows or columns follows the selection. The `AxisEdit` transform is shared by stored cells, ranges, formulas and metadata.

Deleting part of a range contracts it to its surviving interval. Deleting the whole range produces `#REF!`. Absolute-reference markers survive structural edits; `$` affects copying, not whether a reference follows inserted/deleted rows. Quoted string literals are left untouched, and the second endpoint of `Other!A1:A9` inherits the first endpoint's sheet. Cross-sheet formulas, workbook-defined names, merges, chart source ranges/anchors, validation lists, conditional rules and freeze boundaries update in the same transaction. Deleting a filter's header removes that filter instead of silently promoting a data row.

Deleting a worksheet rewrites references to `#REF!` immediately; adding a new sheet with the same name cannot resurrect references to the removed sheet. Insertions that would push stored cells or mapped range metadata outside the worksheet are rejected and rolled back.

This implementation covers whole rows and columns and the parser's A1 reference subset. It does not implement arbitrary cell-shift deletion, discontiguous structural selections, external-workbook/3D references, entire-column/row formula references or full Excel cut/move semantics.

## Reusable components

`GridSpace.Core` defines `AxisEdit`, `ColumnFilter`, `SortLevel`, `ConditionalFormatRule` and `DifferentialStyle`. `GridSpace.Formulas` contains pure `WorksheetFilterEngine` and `ConditionalFormattingEngine` services. `GridSpace.Editing` exposes the transaction operations. `GridSpace.Controls` supplies `FilterEditorControl`, `SortEditorControl` and `ConditionalFormatEditorControl`; hosts can compose them independently of `SpreadsheetWorkbench`.

```csharp
var session = new SpreadsheetSession(SampleWorkbook.Create());
session.Select("B5:I17");
session.SetFilter(new ColumnFilter
{
    Column = 2, // Column C, zero-based.
    Values = ["Americas", "Europe"]
});
session.Sort([new SortLevel(2), new SortLevel(3, Descending: true)]);
session.SetConditionalFormat(new ConditionalFormatRule
{
    Range = "D6:D17",
    Kind = ConditionalFormatKind.DataBar
});
session.DeleteRows(position: 5, count: 2);
session.Undo();
```

## Persistence and limits

Native JSON retains the new model collections. The XLSX subset reads/writes supported conditional rules, differential styles, value/custom filters and sort state. Unsupported rule types, multi-range applies-to lists, theme-based differential styles and advanced thresholds are reported rather than claimed as supported.

OOXML has one row-hidden flag. A GridSpace extension preserves manual versus filtered visibility in GridSpace-to-GridSpace roundtrips. For external XLSX files, manual hiding on a row also excluded by a filter cannot be inferred unambiguously; import surfaces that limitation. The extension also preserves the signed data-bar negative color, while ordinary consumers receive the standard data-bar representation.

Limits are 256 conditional rules and 256 filter columns per sheet, 64 sort levels, 100,000 cells per conditional range/sort operation, 100,000 data rows per filter operation, and 10,000 explicit filter values. The independent Open XML SDK validator is a test dependency only; the runtime writer remains GridSpace's own bounded implementation.

## Validation scope

Tests cover interval transforms (including reversed ranges), quoted/cross-sheet references, data-loss rollback, metadata rebasing, stable sorting, manual/filter visibility, wildcard escaping, priority composition, relative formulas, statistical rules, signed bars, native/XLSX roundtrips and actual raster pixels. Browser acceptance interacts with Uno controls through physical keyboard/pointer input. Its optional geometry snapshot is read-only and available only with `?test=1`; it is not a command-execution bridge.

The test suite is not a claim that Microsoft Excel itself was run in CI or that every Excel workbook is compatible.
