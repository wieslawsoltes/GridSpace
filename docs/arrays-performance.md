# Dynamic arrays and performance — 0.3.0-alpha.1

## Array calculation and editing

GridSpace evaluates rectangular arrays without creating stored cells for their output. Only the anchor retains formula text; spill followers show calculated values and a read-only copy of the anchor formula in the formula bar. A blue outline identifies the active spilled range. Editing, deleting or filling part of the output is rejected unless the operation also replaces the anchor. A selection copied without its anchor copies the calculated follower values. Native files store formulas, not disposable spill caches.

Implemented array functions are `SEQUENCE`, `FILTER`, `SORT`, `SORTBY`, `UNIQUE`, `TRANSPOSE`, `TAKE`, `DROP`, `HSTACK`, `VSTACK`, `CHOOSECOLS` and `CHOOSEROWS`. `LET` provides sequential lexical bindings. Rectangular constants, arithmetic/comparison/concatenation broadcasting, array `IF`/`IFERROR` and spill references such as `A1#` work in the same evaluator. Scalar cell references to an anchor return its top-left value; `A1#` returns the entire output.

```text
A1: =SEQUENCE(4,2)
D1: =LET(data,A1#,FILTER(data,CHOOSECOLS(data,1)>3))
G1: =SUM(D1#)
```

This yields 1–8 in `A1:B4`, the last two rows in `D1:E2`, and 26 in `G1`. Replacing A1 with `=SEQUENCE(3,2)` shrinks both outputs and changes G1 to 11. No obsolete values remain in the document. `SORT`/`SORTBY` are stable with blank-last ordering; `UNIQUE` compares typed vectors case-insensitively and supports exactly-once selection. Stack operations pad mismatched tails with `#N/A`.

Occupied cells, intersecting merges and worksheet boundaries block output with `#SPILL!`. Direct/indirect output-precedent cycles return `#CYCLE!`. A result is limited to 100,000 cells and total virtual output to 200,000 cells per workbook. Evaluation also charges an element-work budget. Spill candidates are indexed per worksheet and resolved independently of the viewport, including follower-first reads and cross-sheet references.

Spill reconciliation is conservative after input edits: all possible array anchors are reconsidered, and their old/new output dependencies can invalidate scalar users. This does not claim incremental per-element array calculation. LAMBDA, implicit intersection `@`, every Excel array function, all scalar-function lifting, legacy fixed-size CSE behavior, structured references and external-workbook arrays are not implemented.

## XLSX representation

Export emits ordinary OOXML array formulas, calculated follower cells and standard XLDAPR cell metadata in `xl/metadata.xml`. Future-function prefixes and the file representation of `A1#` (`_xlfn.ANCHORARRAY(A1)`) are translated structurally, preserving string literals and quoted sheet names. Followers retain their own cell styling. The writer uses a dictionary-based style index rather than searching all styles for each exported cell.

Import resolves the actual metadata type/record indexes instead of assuming a particular numeric `cm` value. Supported, bounded dynamic-array groups become live formulas and virtual output; cached follower inputs are removed, but formatting remains. Conflicting ownership, unsupported functions and malformed groups do not silently discard independent formulas. Unsupported dynamic arrays and legacy CSE groups retain typed caches with warnings. This is a supported-subset import/export, not arbitrary package-preserving XLSX editing. Tests validate representative generated files with the independent Open XML SDK Office 2019 schema validator; they do not launch Microsoft Excel.

## Performance changes

**Delta cell transactions.** `SpreadsheetSession.ApplyCells` fully enumerates and validates up to 100,000 writes before mutation. Cell edits, formatting, paste, fill and clear retain immutable before/after records only for changed cells. Undo and redo keep the existing workbook, worksheet and calculator objects. Structural/compound metadata operations retain the snapshot fallback. The history bound remains 40 entries and approximately 32 MB; one oversized entry can be retained. The size estimate is not a measurement of the entire managed heap.

**Dependency-based scalar invalidation.** `Worksheet.Set` emits typed input-versus-style mutations to a bounded 8,192-entry workbook ring journal. Calculators consume only the suffix after their revision. An input edit invalidates its transitive users; a style-only edit retains calculated results. A structural change, journal overrun or dependency-cap overflow requests full invalidation. Dependencies are tracked dynamically, including blank precedents, and obsolete antecedent edges are detached when a formula is reevaluated. Empty cells are not cached merely because the viewport visits them. Parsed expressions, calculated values and dependency edges are bounded.

**View reuse.** Sparse viewport indexes are rebuilt only when the worksheet instance or structural revision changes. Selection, wheel scrolling, zoom and ordinary cell edits reuse them. Selection summaries are cached by workbook/sheet identity, revision and range; a single-cell selection returns immediately. Empty model reads share an immutable empty cell. Recovery writes are still debounced snapshots, so these improvements do not eliminate every document-sized operation.

All model mutation remains single-owner. Direct edits to mutable dictionaries or metadata bypass the cell journal and must be followed by `Workbook.Touch()` or `Attach()` before derived state is read. The implementation does not support concurrent live session mutation.

## Reproduce the measurements

```bash
dotnet run --project tools/GridSpace.Benchmarks -c Release -- 10000
```

The same harness was run against the verified 0.2.0 `a09d2634` source and this implementation, using .NET 10.0.12 on the same Debian x64 container. Each workbook had 10,000 input cells and 10,000 independent formulas. Five warm-up operations precede each scenario; GC preparation happens outside measured samples. Allocations use `GC.GetAllocatedBytesForCurrentThread`. Baseline/new JSON observations are retained in [benchmarks](benchmarks/). These are single-process microbenchmark observations, not a general speed guarantee, a browser rendering benchmark, or a measure of array-heavy workloads.

| Scenario | Samples | 0.2 median | 0.3 median | 0.2 allocation/op | 0.3 allocation/op |
| --- | ---: | ---: | ---: | ---: | ---: |
| Edit one cell and read its dependent | 30 | 25.7043 ms | 0.0185 ms | 21,418,499 B | 5,712 B |
| Edit one cell and undo | 20 | 45.0135 ms | 0.0199 ms | 30,612,117 B | 5,848 B |
| Unchanged selection summary | 100 | 1.0517 ms | 0.0001 ms | 43,736 B | 0 B |

The cached summary result is near the measurement floor; the useful invariant is **zero allocations and no repeated calculation**, not a precise ratio. A deterministic regression warms 1,000 independent formulas, edits one input and verifies exactly two evaluations (the input and its dependent) when all formulas are read again. Cell undo preserves object identities. CI records benchmark JSON but does not use machine-sensitive elapsed-time thresholds as correctness gates.

## References and remaining boundary

Array behavior follows Microsoft's [dynamic arrays and spilled array behavior](https://support.microsoft.com/en-us/excel/dynamic-array-formulas-and-spilled-array-behavior), [SEQUENCE](https://support.microsoft.com/en-us/excel/functions/sequence-function) and [FILTER](https://support.microsoft.com/en-us/excel/functions/filter-function) documentation. File notation is cross-checked against XlsxWriter's [formula documentation](https://xlsxwriter.readthedocs.io/working_with_formulas.html) and metadata implementation. GridSpace's runtime implementation remains C# and does not depend on XlsxWriter.

VBA/Office Scripts, PivotTables, Power Query, collaboration, full printing, full accessibility, every formula/chart/filter variant, arbitrary lossless OOXML preservation and exact Excel interaction/visual parity remain outside this increment. See [compatibility](compatibility.md).
