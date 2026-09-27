# Compatibility and parity ledger

This ledger describes the implemented 0.2.0 alpha, not complete Excel fidelity. Keep original XLSX files. Native `.gridspace` is authoritative for features modeled by GridSpace.

## Workbook and UI

| Capability | Status and limits |
| --- | --- |
| Coordinate space | A1 through XFD1048576; sparse storage and viewport rendering |
| Selection | One rectangular range, keyboard extension, row/column headers; no discontiguous selection model |
| Editing | Direct cell/formula-bar editing, commit/cancel, fill, formatting, bounded undo/redo |
| Clipboard | Text and same-session cell-style/formula translation; no conditional-rule clipboard propagation |
| Cut/move | Copy then clear; not Excel's reference-preserving move semantics |
| Font/cell style | Family/size, bold/italic/underline, colors, alignment, wrapping, simple borders; no complete rich-text or complex-script typography parity |
| Conditional formatting | Eleven supported rule kinds, property-level priority composition, Stop If True for boolean rules, relative formulas, scales and signed bars; single rectangular applies-to ranges |
| Conditional visuals | Min/max scales; median midpoint for three colors; zero-axis signed bars; no icon sets or general custom thresholds |
| Merges | Native and XLSX merges; overlapping non-empty data rejected; merged sort ranges rejected |
| View | Freeze panes, scrolling, zoom and independent manual/filter visibility; not split-window management parity |
| Structure | Multi-row/column insert/delete with supported-reference and metadata rebasing; no arbitrary cell-shift deletion or 3D/external-reference rewriting |
| Worksheets | Add, copy, rename and delete; deleting a sheet invalidates supported references immediately |
| Tables | Styled ranges and AutoFilter metadata, not structured-reference Excel Tables |
| Filters | Value sets/blank selection, two custom predicates with And/Or, And across columns, wildcards/escaping, reapply and clear; no date-group/color/icon/top-10/dynamic filters |
| Sort | Stable multi-level value sorts, header/case options, blank-last ordering; no left-to-right, custom-list, color or icon sorts |
| Validation | Explicit allowed-value lists, not the full validation-rule catalog |
| Notes | Per-cell plain text, not threaded collaboration comments |
| Charts | One series from the last range column, categories from the first; column/bar/line/pie and bounded preview points |
| Ribbon/dialogs | Custom Office-style shell and reusable compound editors; not pixel-identical Excel or every control recreated from first principles |
| Accessibility | Named commands and active-cell description; no complete virtualized grid automation provider |

The reusable `OfficeChoiceBox` owns dropdown preview/commit/cancel input so nested choices do not submit parent dialogs. Sort levels fit the dialog width and expose reorder/remove controls.

See [Data tools and structural edits](parity-data-tools.md) and [shared formulas and choice controls](shared-formulas.md) for detailed behavior and API examples.

## Formula engine

`CalculationEngine.BuiltInFunctions` exposes the built-in catalog. The engine provides common aggregates, logic, mathematics, text/date functions, conditional aggregates, exact lookups and PMT. Function names and numeric input use invariant/English conventions.

`VLOOKUP` supports its explicit exact-match form. `MATCH` and `XLOOKUP` have restricted matching/search modes. Approximate/binary lookup parity, array constants, spill ranges, LET/LAMBDA, structured references, whole-column/whole-row formula references and external workbook links are absent.

Dates use .NET/OLE Automation dates. Excel's historical 1900 leap-year anomaly, the 1904 date system and all locale/calendar behavior are not reproduced. Number formats, value coercion and every function edge case do not have full Excel parity. Unsupported functions return `#NAME?`; cycles and evaluation budgets have explicit engine errors. Volatile functions refresh on revision changes or explicit recalculation, not through a continuous calculation scheduler.

Conditional rules and filters use this same engine. Value-list filters compare invariant calculated text, not every Excel locale-specific display representation. Statistical conditional rules exclude blanks and distinguish numeric from text values. This alpha is not a validated substitute for independently checked financial, regulatory or safety-critical calculations.

## File interchange

| Format | Import | Export |
| --- | --- | --- |
| `.gridspace` / JSON | Modeled state, including conditional/filter/sort metadata; schema version 1 | Modeled state and native recovery |
| `.csv`, `.tsv`, `.txt` | Quoted delimited values, multiline fields; formula-looking text imported literally | Active-sheet calculated values with text formula-injection guards |
| `.xlsx` | Supported cells/formulas/styles, sizes, merges/view metadata, names, inline validation, charts, conditional rules, differential styles, value/custom filters and sort levels | A newly generated workbook containing the supported subset |
| `.xls`, `.xlsb`, `.xlsm` | Not supported | Not supported |

XLSX writing is not package-preserving editing. Unknown parts, extension records, drawing types, macros, pivot caches, connections, themes, signatures and advanced charts are not losslessly retained. Unsupported conditional types, discontiguous/oversized applies-to ranges, advanced thresholds and theme-based differential styles are skipped with warnings where detected. Valid shared-formula groups expand into independent formulas with relative/mixed/absolute reference translation, even when followers precede the master in XML. Invalid, ambiguous or out-of-range groups retain typed cached results with warnings; missing cached results become explicit errors. What-if data tables retain cached results but are not recalculated. Array/spill formulas remain unsupported. Absence of a warning is not proof of complete preservation.

Standard OOXML encodes one `row.hidden` bit. GridSpace's optional extension preserves manual versus filtered visibility, sort-header metadata and negative data-bar color in its own roundtrips. External XLSX files cannot unambiguously distinguish manual hiding on a row that also fails a filter; import warns about this. Consumers ignoring the extension receive the standard supported workbook representation, not full native state. Unknown external extension data is not retained.

Generated representative files are checked with the Open XML SDK validator in CI. Schema validity and successful self-roundtrip are not a claim that all Excel features were preserved or that Microsoft Excel was launched in CI.

## Bounds and scalability

Native input is bounded to 32 MB of text, 256 worksheets and 200,000 stored cells per sheet. File input is bounded to 32 MB before decoding; ZIP/XML handling has separate expansion/part limits. Shared-formula expansion has an additional workbook-wide budget of 33,554,432 UTF-16 characters and does not allocate absent cells implied by a master range. Most rectangular edits, formula ranges, conditional ranges and sorts are limited to 100,000 cells. Filtering supports at most 100,000 data rows, 256 filter columns and 10,000 explicitly selected values. A sheet has at most 256 conditional rules and 64 sort levels. CSV export measures the full A1-to-last-used-cell rectangle, not merely populated cells.

Rendering a far-away cell does not allocate the intervening grid. This does not imply million-cell transaction or calculation performance: JSON snapshot history, revision-wide cache invalidation and metadata scans remain document-size-dependent. Conditional statistics cache by workbook revision and are recomputed after document edits.

## Remaining major areas

VBA/Office Scripts; PivotTables/PivotCharts; Power Query/data models; add-ins; external-data refresh; collaboration/cloud storage; full print/page setup; dynamic arrays; all chart/format/filter types; complete accessibility; encryption/signing; arbitrary OOXML preservation; exact Excel keyboard, visual and interaction parity.
