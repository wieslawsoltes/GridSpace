# Changelog

## 0.4.0-alpha.1 — local preview

Multi-series editable charts, reusable chart geometry/rendering and inspector controls, typed worksheet-source PivotTables, field lists, immutable source caches, refresh/drill-through, linked charts and standard chart/pivot XLSX parts. Drawing deltas retain calculation/layout caches; repeated PivotChart binding is allocation-free. Adds structural/clipboard/ownership/schema/raster regressions, an analytics sample and benchmark, and physical-input acceptance specifications. Full Uno SDK/browser runtime validation and upstream delivery remain pending for this preview. See [charts and PivotTables](docs/charts-pivots.md).


## 0.3.0-alpha.1

Dynamic array expressions, twelve array functions, lexical LET, virtual spill ownership, # references, blocked/cyclic output detection and protected spill followers. Standard dynamic-array XLSX metadata and cached results roundtrip through the supported formula subset.

Cell edits, formatting, paste, fill and clear use delta history. A bounded mutation journal drives dependency-based scalar invalidation; style-only edits retain calculations. Viewport indexes survive selection, scrolling and cell edits, and selection summaries are cached. The new benchmark executable measures engine time and allocations without brittle CI timing gates.

See [arrays and performance](docs/arrays-performance.md) for behavior, limits, reproducible measurements and remaining compatibility boundaries.

## 0.2.0-alpha.1

### Added

- Eleven conditional-format rule kinds, relative formulas, property-level differential style priorities, Stop If True, two/three-color scales and signed data bars.
- Reusable filter, sort and conditional-rule editors; header filter flyouts; conditional rules manager with edit/delete/reorder.
- Compound multi-column filtering, searchable value sets, And/Or predicates, wildcard escaping and separate manual/filter row visibility.
- Stable multi-level value sorting with header/case options, blank-last ordering and formula translation.
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
