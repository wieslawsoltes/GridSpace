# Changelog

## Unreleased

Direct chart-source editing adds colored worksheet outlines, movable borders and cell-snapped corner resizing. Automatic tables remain automatic; explicit category/value vectors are independently editable. Chart Design provides Edit Source and Customize Series. Previews do not write cells or history; release creates one drawing delta, while Escape, outside release, stale definitions and invalid endpoints cancel safely. Shared pane geometry preserves frozen-pane hit testing. Warm source-binding metadata is cached without cell evaluation. See [chart source editing](docs/chart-source-editing.md) for the interaction contract, reusable APIs and remaining limits.

Adds direct chart source-outline editing with cell-snapped move/resize previews, independent category/value/header grips and retained single-cell vector orientation. Titles, both axis titles and series names support live formatted single-cell references, structural identity maintenance and standard XLSX text-reference caches. Caption-only rebindings preserve numeric arrays.

Fixes analytics-inspector synchronization: coalesced deferred updates, a geometry-only control-preserving path, correct copied-PivotTable ownership and rejected-checkbox rollback. Adds physical-input regressions and fixes a shadowed helper in the chart text-link download test. See [live chart text](docs/chart-text-links.md).

## 0.4.0-alpha.1

Adds Compact/Outline/Tabular row-axis layouts, typed nested collapse state, source-accumulated top/bottom subtotals, direct worksheet expand/collapse gestures, cached display-row provenance, and exact linked-chart detail projection. Supports native persistence and bounded standard XLSX layout/row-item metadata with independent schema tests. Full column hierarchy and every Excel grouping/file behavior are not claimed.

Multi-series editable charts, reusable chart geometry/rendering and inspector controls, typed worksheet-source PivotTables, field lists, immutable source caches, refresh/drill-through, linked charts and standard chart/pivot XLSX parts. Drawing deltas retain calculation/layout caches; repeated PivotChart binding is allocation-free. Adds structural/clipboard/ownership/schema/raster regressions, an analytics sample and benchmark, and physical-input acceptance specifications. Full Uno SDK WebAssembly/native builds and physical-input chart/PivotTable acceptance are integrated in CI. Pivot field dragging uses owned pointer capture, preview feedback and cancellation; Name-box navigation relinquishes drawing selection. Cached field/layout edits reuse the last-refresh source snapshot, and weakly owned field-value catalogs avoid repeated source scans. Explicit Refresh and undo/redo retain coherent report/cache epochs. See [charts and PivotTables](docs/charts-pivots.md).

## 0.3.0-alpha.1

Dynamic array expressions, twelve array functions, lexical LET, virtual spill ownership, # references, blocked/cyclic output detection and protected spill followers. Standard dynamic-array XLSX metadata and cached results roundtrip through the supported formula subset.

Cell edits, formatting, paste, fill and clear use delta history. A bounded mutation journal drives dependency-based scalar invalidation; style-only edits retain calculations. Viewport indexes survive selection, scrolling and cell edits, and selection summaries are cached. The new benchmark executable measures engine time and allocations without brittle CI timing gates.

See [arrays and performance](docs/arrays-performance.md) for behavior, limits, reproducible measurements and remaining compatibility boundaries.

## 0.2.0-alpha.1

### Added

- Eleven conditional-format rule kinds, relative formulas, property-level differential style priorities, Stop If True, two/three-color scales and signed data bars.
- Reusable filter, sort and conditional-rule editors; header filter flyouts; conditional rules manager with edit/delete/reorder.
- Compound multi-column filtering, searchable value sets, And/Or predicates, wildcard escaping and separate manual/filter row visibility.
- Stable multi-level value sorts, header/case options, blank-last ordering and formula translation.
- Bounded multi-row/column deletion and insertion, whole-interval reference transforms, metadata/rule rebasing and atomic rollback on overflow.
- Supported XLSX conditional-rule/differential-style, AutoFilter and sort-state interchange, plus GridSpace visibility metadata.
- Bounded, order-independent shared-formula expansion with mixed/absolute reference translation, recalculation and independent-formula export.
- Typed cached-result recovery for malformed shared groups and unsupported what-if data tables; missing results become explicit errors.
- Reusable virtualized `OfficeChoiceBox` with preview, commit, cancel and owned keyboard routing in nested tool dialogs.
- Independent Open XML SDK validation, engine/raster/interchange regressions and physical-input browser tests for the new tools.
- Data-tool and shared-formula user/developer guides and an updated compatibility ledger.

### Fixed

- Clearing filters no longer unhides manually hidden rows.
- Deleting a worksheet invalidates references so recreating its name cannot reconnect them accidentally.
- Single-cell sort/chart/table commands use the relevant data region rather than decorative title merges.
- Find Previous now searches backward rather than always returning the first match.
- Command search and ribbon route new data tools through the same dispatcher.
- Sort editors use flexible columns rather than clipped, horizontally scrolling choice hit targets.
- Choice keyboard input is handled before native list/button activation and surrounding dialog submission.
- Font asset fetching no longer depends on shared unauthenticated GitHub API quotas; both transports require the pinned content hash.

## 0.1.0-alpha.1

Initial Uno/Skia spreadsheet workbench with eight packable libraries, native/browser hosts, sparse geometry, formulas, transactions, cell editing, formatting, basic data tools/charts, native/CSV/XLSX-subset IO and local recovery. Added CI, GitHub Pages deployment, package generation, release workflow, documentation and pinned application font assets.

No release claims full Excel parity. See the compatibility ledger for remaining boundaries.
