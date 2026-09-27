# Changelog

## 0.2.0-alpha.1

### Added

- Eleven conditional-format rule kinds, relative formulas, property-level differential style priorities, Stop If True, two/three-color scales and signed data bars.
- Reusable filter, sort and conditional-rule editors; header filter flyouts; conditional rules manager with edit/delete/reorder.
- Compound multi-column filtering, searchable value sets, And/Or predicates, wildcard escaping and separate manual/filter row visibility.
- Stable multi-level value sorting with header/case options, blank-last ordering and formula translation.
- Bounded multi-row/column deletion and insertion, whole-interval reference transforms, metadata/rule rebasing and atomic rollback on overflow.
- Supported XLSX conditional-rule/differential-style, AutoFilter and sort-state interchange, plus GridSpace visibility metadata.
- Independent Open XML SDK validation, engine/raster/interchange regressions and physical-input browser tests for the new tools.
- Data-tool user/developer guide and updated compatibility ledger.

### Fixed

- Clearing filters no longer unhides manually hidden rows.
- Deleting a worksheet invalidates references so recreating its name cannot reconnect them accidentally.
- Single-cell sort/chart/table commands use the relevant data region rather than decorative title merges.
- Find Previous now searches backward rather than always returning the first match.
- Command search and ribbon route new data tools through the same dispatcher.
- Font asset fetching no longer depends on shared unauthenticated GitHub API quotas; both transports require the pinned content hash.

## 0.1.0-alpha.1

Initial Uno/Skia spreadsheet workbench with eight packable libraries, native/browser hosts, sparse geometry, formulas, transactions, cell editing, formatting, basic data tools/charts, native/CSV/XLSX-subset IO and local recovery. Added CI, GitHub Pages deployment, package generation, release workflow, documentation and pinned application font assets.

Neither version has complete Excel feature, file, keyboard, accessibility or pixel-level parity. See [compatibility](docs/compatibility.md).
