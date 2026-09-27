<p align="center"><img src="src/GridSpace.App/Assets/Icons/icon.svg" width="76" alt="GridSpace" /></p>
<h1 align="center">GridSpace</h1>
<p align="center">A local-first spreadsheet workbench.<br/>Uno Platform · SkiaSharp · C# · Desktop and WebAssembly</p>
<p align="center"><a href="https://github.com/wieslawsoltes/GridSpace/actions/workflows/build.yml"><img src="https://github.com/wieslawsoltes/GridSpace/actions/workflows/build.yml/badge.svg" alt="Build and browser validation" /></a> <a href="https://github.com/wieslawsoltes/GridSpace/actions/workflows/desktop.yml"><img src="https://github.com/wieslawsoltes/GridSpace/actions/workflows/desktop.yml/badge.svg" alt="Desktop builds" /></a> <img src="https://img.shields.io/badge/license-MIT-green" alt="MIT" /></p>
<p align="center"><a href="https://wieslawsoltes.github.io/GridSpace/">Open browser app</a> · <a href="docs/architecture.md">Architecture</a> · <a href="docs/compatibility.md">Compatibility</a> · <a href="docs/development.md">Development</a></p>

## The project

GridSpace combines an Excel-style ribbon, formula bar, worksheet tabs and green selection language with a real C# workbook engine. The worksheet is drawn by **SkiaSharp through Uno's `SKCanvasElement`**, not by an HTML table or a separate JavaScript spreadsheet. The desktop and browser hosts use the same model, calculation engine, transaction layer and controls.

The repository is an **early functional alpha**, not a complete or pixel-identical Microsoft Excel replacement. Unsupported features are not represented as completed functionality. Keep original workbooks and read the [compatibility matrix](docs/compatibility.md) before round-tripping XLSX files.

## What works

| Area | Implemented |
| --- | --- |
| Workbook editing | Sparse worksheets, A1 navigation, range selection, direct cell and formula-bar editing, copy/paste, undo/redo, fill-down/right, formula translation and two-value numeric series |
| Presentation | Custom ribbon, vector icons, formula bar, compact scrollbars, sheet tabs and status bar; formatting, wrapping, alignment, borders, merged cells, row/column sizing and AutoFit |
| Navigation | Excel worksheet coordinate bounds, viewport culling, frozen panes, hidden-row/column geometry, wheel scrolling, zoom, touch panning and keyboard shortcuts |
| Calculations | Arithmetic and comparison expressions, cell/range references, absolute/mixed references, cross-sheet references, names, revision-based caching, bounded evaluation and built-in functions |
| Data tools | Sort, text-contains filtering, list validation, find/replace, cell notes, styled table ranges, worksheet insertion/copy/rename/delete |
| Charts | Single-series column, bar, line and pie charts with bounded previews and XLSX chart interchange |
| Files | Native `.gridspace` JSON, CSV/TSV and a documented XLSX subset; browser file selection/downloads and native desktop pickers |
| Recovery | Debounced IndexedDB recovery in the browser and atomic replacement of a local recovery file on desktop |

The sample workbook contains **Revenue**, **Assumptions** and **Read me** worksheets. Change `Revenue!D6` or an assumption to exercise recalculation and chart updates.

## Run it

The repository pins **.NET SDK 10.0.401** and **Uno SDK 6.7.30** in `global.json`. Managed and native SkiaSharp versions must remain aligned; the renderer currently uses the Uno-compatible **3.119.2** line.

```bash
git clone https://github.com/wieslawsoltes/GridSpace.git
cd GridSpace
python3 scripts/fetch-assets.py

dotnet run --project src/GridSpace.App -f net10.0-desktop \
  -p:GridSpaceDesktopOnly=true
```

The asset command downloads content-pinned, OFL-licensed Carlito faces from the Google Fonts repository. It never copies Microsoft or host-system fonts. The application supplies the same font data to Uno and the spreadsheet renderer; logical workbook font names remain unchanged in the document.

For the browser:

```bash
dotnet workload install wasm-tools --skip-manifest-update
dotnet publish src/GridSpace.App -f net10.0-browserwasm -c Release \
  -o artifacts/publish -p:WasmShellWebAppBasePath=/GridSpace/
python3 scripts/collect-site.py artifacts/publish artifacts/site
python3 scripts/serve-site.py --directory artifacts/site --port 4173
# Open http://127.0.0.1:4173/GridSpace/
```

Python 3 is used only by the build/development scripts. The deployed application does not require Python, Node.js, a database server or an application backend. Browser recovery is local to the origin and browser profile; it is not a cloud backup.

## Reusable libraries

Each library is independently packable. The application is only a host and composition root.

| Package | Target | Responsibility |
| --- | --- | --- |
| `GridSpace.Core` | `net10.0` | Workbook, worksheet, cell, style, range and chart contracts |
| `GridSpace.Formulas` | `net10.0` | Parser, evaluator, built-ins, formatting and reference rewriting |
| `GridSpace.Editing` | `net10.0` | Editing sessions, transactions, bounded history and workbook operations |
| `GridSpace.IO` | `net10.0` | Native/CSV/XLSX interchange and host storage contract |
| `GridSpace.Layout` | `net10.0` | Sparse axis indexes, frozen panes, hit-testing and viewport geometry |
| `GridSpace.Skia` | `net10.0` | Cell, chart and selection rendering; owned typeface catalog |
| `GridSpace.Controls` | Uno browser/desktop | Embeddable grid, custom ribbon, buttons, icons, formula bar, tabs and scrollbars |
| `GridSpace.Workbench` | Uno browser/desktop | Complete workbench, command routing, dialogs and recovery scheduling |

CI produces `.nupkg` and symbol packages in the **GridSpace-packages** artifact. Package generation is not a claim that the packages have been published to nuget.org. The release workflow can publish them when a `NUGET_API_KEY` repository secret is configured.

### Use the engine without a UI

```csharp
using GridSpace.Core;
using GridSpace.Editing;
using GridSpace.Formulas;

var book = new Workbook { Title = "Example" };
var session = new SpreadsheetSession(book);
session.SetInput("21", new CellAddress(0, 0));
session.SetInput("=A1*2", new CellAddress(0, 1));

Console.WriteLine(session.Calculation.Evaluate(session.Sheet, "B1")); // 42
session.Select("A1:B1");
session.ApplyStyle(style => style with { Bold = true });
session.Undo();

// Extension functions are registered by a trusted host, not by workbook text.
session.Calculation.Register("DOUBLE", args =>
    args.Count == 1 && args[0].TryNumber(out var value)
        ? CalcValue.Num(value * 2)
        : CalcValue.Error("#VALUE!"));
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

A host can supply its own ribbon and command routing, use `SpreadsheetRenderer` on another Skia surface, or compose `SpreadsheetWorkbench(session, storage)` with an implementation of `IWorkbookStorage`. Model mutation is single-owner; marshal edits to the owning UI thread rather than sharing a live mutable session between threads.

## Validation and delivery

```bash
dotnet test tests/GridSpace.Tests -c Release
npm ci
npx playwright install chromium
# Start the local server above, then:
npm run test:browser
```

The engine suite covers formula evaluation, reference rewriting, transactions, interchange, sparse geometry, frozen panes, file bounds and headless rendering. Browser acceptance tests perform actual pointer/keyboard input, read only an opt-in diagnostic snapshot, save screenshots and verify exported data.

**Build** tests and publishes the real Uno application, packs libraries, then deploys the verified browser artifact to Pages. It compares the artifact's `build-info.json` commit with the source commit and repeats browser acceptance tests against the public site. **Desktop** builds the shared host on Linux, Windows and macOS. **Release** creates versioned browser/source archives, packages, checksums and a GitHub prerelease/release.

## Important boundaries

There is no VBA runtime, PivotTable engine, Power Query, external-data connectivity, multiplayer editing, Excel add-in host, dynamic-array spill engine, complete OOXML preservation or full print/page-layout engine. Some interactions and dialogs use Uno's platform controls under the custom workbench. Full spreadsheet-cell accessibility and exact Excel keyboard/visual parity remain unfinished.

The alpha deliberately bounds expensive operations: 100,000 cells per bulk operation/range evaluation, 200,000 stored cells per imported sheet, 256 sheets, 32 MB native/file import, and a 200,000-cell CSV rectangle measured from A1. These are implementation safety limits, not Excel's storage limits. See [compatibility](docs/compatibility.md) for details.

## License

GridSpace source is [MIT licensed](LICENSE). Uno Platform, SkiaSharp, test dependencies and fetched font assets retain their own licenses. See [third-party notices](THIRD-PARTY-NOTICES.md). GridSpace is independent of Microsoft; Microsoft Excel and related names belong to their respective owners. No Microsoft logo, proprietary font or extracted Excel asset is included.
