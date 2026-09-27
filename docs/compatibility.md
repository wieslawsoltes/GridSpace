# Compatibility and parity ledger

This file describes the implemented alpha, not a promise of complete Excel fidelity. Keep source workbooks when importing or exporting XLSX. Native `.gridspace` is the authoritative format for the features GridSpace models.

## Workbook and UI

| Capability | Status and limits |
| --- | --- |
| Coordinate space | A1 through XFD1048576; sparse storage and viewport rendering |
| Selection | Single rectangular range; keyboard extension; row/column headers; no discontiguous selection model |
| Editing | Direct cell editing, formula bar, commit/cancel, fill handle, formatting, undo/redo |
| Clipboard | Text plus same-session formatting/formula translation; native clipboard access depends on host permissions |
| Cut/move | Cut is copy followed by clear; Excel move-reference semantics are not implemented |
| Font and cell formatting | Font family/size, bold, italic, underline, foreground/background, alignment, wrapping and simple borders |
| Merges | Native and XLSX merges; non-empty overlapping data is rejected rather than silently discarded |
| View | Freeze panes, hidden metadata, scrolling and zoom; no split-window/pane window-management parity |
| Structure | Insert row/column with reference rewriting; worksheet add/copy/rename/delete; no complete delete-row/column reference model |
| Tables | Styled ranges and filter metadata; not structured-reference Excel Tables |
| Filters | Text-contains filtering in a selected column; not Excel's multi-column criteria tree |
| Sort | One selected sort column with optional header in the engine; not multi-key custom sort |
| Validation | Explicit allowed-value lists; not all Excel validation rules |
| Notes | Per-cell plain-text notes; not threaded collaboration comments |
| Charts | One series from the last range column, categories from the first; column/bar/line/pie; limited preview point count |
| Ribbon/dialogs | Custom Office-style shell and reusable controls; not pixel-identical Excel or a complete control-for-control reimplementation |
| Accessibility | Named commands and an active-cell description; no complete virtualized grid automation provider |

## Formula engine

The exact built-in function list is exposed by `CalculationEngine.BuiltInFunctions`. It includes common aggregates, logic, mathematics, text, dates, conditional aggregates, exact lookups and PMT. Function names and numeric input use invariant/English conventions.

Important differences include:

* `VLOOKUP` supports the explicit exact-match form with its final argument false. `MATCH` and `XLOOKUP` have restricted matching/search modes. Approximate, binary and wildcard lookup parity is not complete.
* Dynamic arrays, spill ranges, array constants, LET/LAMBDA, structured references, external workbook links and the complete Excel function catalog are absent.
* Date handling uses .NET/OLE Automation dates. Excel's historical 1900 leap-year anomaly and all locale/calendar details are not reproduced.
* Formula coercion, error propagation, number-format syntax and every function edge case do not have full Excel compatibility. Unsupported functions produce `#NAME?`; resource limits and cycles use explicit engine errors.
* Volatile time functions are refreshed by workbook revision or explicit recalculation; there is no continuously running Excel calculation scheduler.

Do not use this alpha as a validated substitute for financial, regulatory or safety-critical workbook calculations without independently checking results.

## File interchange

| Format | Import | Export |
| --- | --- | --- |
| `.gridspace` / native JSON | Modeled workbook state, schema version 1 | Modeled workbook state; use this for native recovery |
| `.csv`, `.tsv`, `.txt` | Delimited values, quoting and multiline fields; formula-looking text is made literal | Active-sheet calculated values; spreadsheet-formula injection guards for text |
| `.xlsx` | Cells, formulas, supported styles, row/column sizes, merges, selected view metadata, defined names, explicit validation lists and supported charts | A newly generated workbook containing the supported subset |
| `.xls`, `.xlsb`, `.xlsm` | Not supported | Not supported |

XLSX writing is not an in-place, package-preserving edit. Unsupported XML parts, relationships, drawings, macros, pivot caches, external connections, themes, signatures, advanced chart content and unknown extension records are not losslessly retained. Import warnings are surfaced where detected; absence of a warning is not proof of full preservation.

## Safety and scalability bounds

The workbook model limits native input to 32 MB of text, 256 worksheets and 200,000 stored cells per sheet. File imports are bounded to 32 MB before decoding. Most rectangular edit operations and formula range evaluation are capped at 100,000 cells. CSV export is bounded by the entire A1-to-last-used-cell rectangle, not merely the number of populated cells. XLSX processing uses bounded ZIP/XML handling.

Rendering a far-away cell does not allocate the intervening grid. This does not imply million-cell transaction or formula performance: JSON snapshot history, revision-wide cache invalidation and some metadata scans remain document-size-dependent.

## Not implemented

VBA and Office scripts; PivotTables/PivotCharts; Power Query and data models; add-ins; external-data refresh; collaboration and cloud storage; full print/page setup; advanced conditional formatting; all chart types; complete accessibility; encryption/signing; exact Excel keyboard, visual and file parity.
