<p align="center"><img src="src/GridSpace.App/Assets/Icons/icon.svg" width="76" alt="GridSpace" /></p>
<h1 align="center">GridSpace</h1>
<p align="center">A local-first spreadsheet workbench.<br/>Uno Platform · SkiaSharp · C# · Desktop and WebAssembly</p>
<p align="center"><a href="https://github.com/wieslawsoltes/GridSpace/actions/workflows/build.yml"><img src="https://github.com/wieslawsoltes/GridSpace/actions/workflows/build.yml/badge.svg" alt="Build and browser validation" /></a> <a href="https://github.com/wieslawsoltes/GridSpace/actions/workflows/desktop.yml"><img src="https://github.com/wieslawsoltes/GridSpace/actions/workflows/desktop.yml/badge.svg" alt="Desktop builds" /></a> <img src="https://img.shields.io/badge/license-MIT-green" alt="MIT" /></p>
<p align="center"><a href="https://wieslawsoltes.github.io/GridSpace/">Open browser app</a> · <a href="docs/architecture.md">Architecture</a> · <a href="docs/compatibility.md">Compatibility</a> · <a href="docs/parity-data-tools.md">Data tools</a> · <a href="docs/development.md">Development</a></p>

## New in 0.3

Dynamic arrays now spill through the real worksheet: `SEQUENCE`, `FILTER`, `SORT`, `SORTBY`, `UNIQUE`, `TRANSPOSE`, `TAKE`, `DROP`, `HSTACK`, `VSTACK`, `CHOOSECOLS`, `CHOOSEROWS`, array constants, broadcasting, `LET`, and `A1#` references. Spilled followers are protected and display their anchor formula. Supported dynamic arrays export standard XLSX metadata and calculated caches.

Cell-oriented commands retain deltas instead of whole-workbook JSON snapshots; scalar recalculation invalidates dependent cells, and view-only updates reuse geometry and selection summaries. See [array behavior and measured performance](docs/arrays-performance.md) for reproducible results and limits.

## The project

GridSpace combines an Excel-style ribbon, formula bar, worksheet tabs and green selection language with a real C# workbook engine. The worksheet is drawn by **SkiaSharp through Uno's `SKCanvasElement`**, not by an HTML table or a separate JavaScript spreadsheet. Desktop and browser hosts share the model, calculations, transactions and controls.

**0.2.0-alpha.1** adds conditional formatting, compound column filters, stable multi-level sorting and transactional row/column deletion. These additions include reusable editors, live rendering, native persistence, supported XLSX interchange and regression tests. See the [data-tool guide](docs/parity-data-tools.md) for examples and boundaries.

This remains a **functional alpha**, not a complete or pixel-identical Microsoft Excel replacement. Keep original workbooks and read the [compatibility matrix](docs/compatibility.md) before round-tripping XLSX files.

## What works

| Area | Implemented |
| --- | --- |
| Workbook editing | Sparse worksheets, A1 navigation, rectangular selection, direct cell/formula-bar editing, copy/paste, bounded undo/redo, fill-down/right, formula translation and two-value series |
| Presentation | Custom ribbon, vector icons, formula bar, compact scrollbars, sheet tabs and status bar; fonts, wrapping, alignment, simple borders, merges, row/column sizing and AutoFit |
| Conditional formatting | Eleven rule kinds, relative expressions, differential-style priorities, Stop If True, rule management, two/three-color scales and signed data bars |
| Filtering | Searchable value checklist, one/two custom predicates, And/Or within a column, And across columns, wildcard escaping, independent manual/filter visibility and Reapply |
| Sorting | Stable ordered multi-level value sorts, header and case options, blank-last ordering, formula translation and merged-range guards |
| Structural editing | Multi-row/column insertion/deletion; surviving-range contraction; cross-sheet formulas, names, merges, validation, charts, conditional rules and freeze boundaries rebased transactionally |
| Navigation | Sparse viewport rendering through XFD1048576, frozen panes, hidden-row/column geometry, wheel scrolling, zoom, touch panning and keyboard shortcuts |
| Calculations | Arithmetic/comparison expressions, relative/absolute/mixed references, cross-sheet references, names, revision-based caching, bounded evaluation and common functions |
| Worksheet tools | Find/replace, list validation, plain notes, styled table ranges, worksheet insertion/copy/rename/delete, manual hide/unhide |
| Charts | Single-series column, bar, line and pie charts with bounded previews and XLSX chart interchange |
| Files | Native `.gridspace` JSON, CSV/TSV and a documented XLSX subset; explicit browser downloads/file selection and native desktop pickers |
| Recovery | Debounced IndexedDB recovery in the browser; atomic local recovery-file replacement on desktop |

The sample has **Revenue**, **Assumptions** and **Read me** worksheets. Change `Revenue!D6` or an assumption to recalculate the model. Select `D6:D17` and choose **Data → Data Bars** to explore live conditional rendering. Click a table's filter-header button to open the filter editor.

## Run it

The repository pins **.NET SDK 10.0.401** and **Uno SDK 6.7.30** in `global.json`. Managed and native SkiaSharp versions must stay aligned; the renderer uses the Uno-compatible **3.119.2** line.

```bash
git clone https://github.com/wieslawsoltes/GridSpace.git
cd GridSpace
python3 scripts/fetch-assets.py

dotnet run --project src/GridSpace.App -f net10.0-desktop \
  -p:GridSpaceDesktopOnly=true
```

The asset command fetches content-pinned, OFL-licensed Carlito faces from Google Fonts. Every payload is verified against its expected Git blob hash, whether retrieved through the raw endpoint or the blob API fallback. It never copies Microsoft or host-system fonts. Uno and the spreadsheet renderer receive the same font data; logical workbook font names remain in the document.

For the browser:

```bash
dotnet workload install wasm-tools --skip-manifest-update
dotnet publish src/GridSpace.App -f net10.0-browserwasm -c Release \
  -o artifacts/publish -p:WasmShellWebAppBasePath=/GridSpace/
python3 scripts/collect-site.py artifacts/publish artifacts/site
python3 scripts/serve-site.py --directory artifacts/site --port 4173
# Open http://127.0.0.1:4173/GridSpace/
```

Python and Node.js are development/test tools only. The deployed application requires no application backend or database server. Browser recovery is local to its origin and profile, not a cloud backup.

## Reusable libraries

Each library is independently packable. App is the platform composition root.

| Package | Target | Responsibility |
| --- | --- | --- |
| `GridSpace.Core` | `net10.0` | Workbook/cell/style/range contracts; axis transforms; conditional/filter/sort models |
| `GridSpace.Formulas` | `net10.0` | Parser, evaluator, reference rewriting, number formatting, conditional formatting and filter predicates |
| `GridSpace.Editing` | `net10.0` | Sessions, transactions, bounded history, structural edits and data-tool operations |
| `GridSpace.IO` | `net10.0` | Native/CSV/XLSX interchange and host storage contract |
| `GridSpace.Layout` | `net10.0` | Sparse axis indexes, frozen panes, hit-testing and viewport geometry |
| `GridSpace.Skia` | `net10.0` | Cell, conditional style, data-bar, chart and selection rendering; owned font resources |
| `GridSpace.Controls` | Uno browser/desktop | Embeddable grid, ribbon, buttons/icons, formula bar/tabs/scrollbars, filter/sort/conditional editors |
| `GridSpace.Workbench` | Uno browser/desktop | Complete workbench, commands, dialogs, filter popup, rule management and local recovery |

CI generates eight `.nupkg` and eight `.snupkg` packages. Generation does not mean publication to nuget.org. The release workflow can publish when `NUGET_API_KEY` is configured.

### Use the engines without a UI

```csharp
using GridSpace.Core;
using GridSpace.Editing;
using GridSpace.Formulas;

var session = new SpreadsheetSession(new Workbook { Title = "Example" });
session.SetInput("21", new CellAddress(0, 0));
session.SetInput("=A1*2", new CellAddress(0, 1));
Console.WriteLine(session.Calculation.Evaluate(session.Sheet, "B1")); // 42

session.SetConditionalFormat(new ConditionalFormatRule
{
    Range = "A1:B1",
    Kind = ConditionalFormatKind.CellValue,
    Comparison = CellComparison.GreaterThan,
    Operand = "30",
    Style = new DifferentialStyle { Background = "#C6EFCE", Bold = true }
});
var rules = new ConditionalFormattingEngine(session.Calculation);
var address = new CellAddress(0, 1);
var displayed = rules.Evaluate(session.Sheet, address,
    session.Calculation.Evaluate(session.Sheet, address));
Console.WriteLine(displayed.Style.Background); // #C6EFCE

session.InsertRows(0, 2); // References and modeled metadata follow the insertion.
session.Undo();
```

### Embed the Uno control

```csharp
using GridSpace.Controls;
using GridSpace.Editing;

var session = new SpreadsheetSession(SampleWorkbook.Create());
var grid = new SpreadsheetGrid { Session = session };
window.Content = grid;
window.Closed += (_, _) => grid.Dispose();
```

A host can use its own command routing, compose the individual data-tool editors, render on another Skia surface, or host `SpreadsheetWorkbench(session, storage)` with its own `IWorkbookStorage`. Web hosts should register available font bytes with `grid.Renderer.Fonts` as the application does. A mutable session has one owning thread; use snapshots for background processing.

## Validation and delivery

```bash
dotnet test tests/GridSpace.Tests -c Release
npm ci
npx playwright install chromium
# Start the local server above, then:
npm run test:browser
```

Tests cover formulas, reference rewriting, rollback/history, native/XLSX interchange, conditional/filter/sort engines, structural metadata, sparse/frozen geometry and actual raster pixels. Independent **Open XML SDK schema validation** supplements the reader/writer round-trip tests; that dependency is test-only.

Browser acceptance uses physical keyboard and pointer events. A `?test=1` read-only snapshot provides model state and current control bounds, not mutation hooks. Tests exercise both the original editing flows and the new rule editor/manager, filter popup, custom-sort editor and structural deletion; screenshots and traces are retained in CI artifacts.

**Build** tests and publishes the actual Uno WebAssembly application, packs libraries, then deploys a successful main-branch build to Pages. The artifact's `build-info.json` commit is verified, and the browser suite runs again against the public URL. **Desktop** builds the shared native host on Linux, Windows and macOS. **Release** creates versioned source/browser archives, packages, checksums and a GitHub release/prerelease.

## Important boundaries

VBA, PivotTables, Power Query, external-data refresh, multiplayer editing, Excel add-ins, complete array/LAMBDA semantics, lossless arbitrary OOXML preservation, full print/page layout and complete virtualized-cell accessibility remain unimplemented. The custom workbench still uses Uno input/dialog primitives. Conditional-format clipboard propagation, arbitrary cell-shift deletion and advanced filter/chart variants also remain outside this increment.

The alpha bounds expensive work: 100,000 cells per bulk edit, conditional range or sort; 100,000 filter data rows; 200,000 stored cells per imported sheet; 256 sheets; and 32 MB native/file import. See [compatibility](docs/compatibility.md) for exact subset behavior and limitations.

## License

GridSpace source is [MIT licensed](LICENSE). Dependencies and fetched font assets retain their own licenses; see [third-party notices](THIRD-PARTY-NOTICES.md). GridSpace is independent of Microsoft. No Microsoft logo, proprietary font or extracted Excel asset is included.
