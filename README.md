<p align="center"><img src="src/GridSpace.App/Assets/Icons/icon.svg" width="76" alt="GridSpace" /></p>
<h1 align="center">GridSpace</h1>
<p align="center">A local-first spreadsheet workbench.<br/>Uno Platform · SkiaSharp · C# · Desktop and WebAssembly</p>
<p align="center"><a href="https://github.com/wieslawsoltes/GridSpace/actions/workflows/build.yml"><img src="https://github.com/wieslawsoltes/GridSpace/actions/workflows/build.yml/badge.svg" alt="Build and browser validation" /></a> <a href="https://github.com/wieslawsoltes/GridSpace/actions/workflows/desktop.yml"><img src="https://github.com/wieslawsoltes/GridSpace/actions/workflows/desktop.yml/badge.svg" alt="Desktop builds" /></a> <img src="https://img.shields.io/badge/license-MIT-green" alt="MIT" /> <a href="https://www.nuget.org/packages/GridSpace.Core"><img src="https://img.shields.io/nuget/vpre/GridSpace.Core.svg?label=NuGet" alt="NuGet" /></a> <a href="https://www.nuget.org/packages/GridSpace.Core"><img src="https://img.shields.io/nuget/dt/GridSpace.Core.svg" alt="Downloads" /></a></p>
<p align="center"><a href="https://wieslawsoltes.github.io/GridSpace/">Open browser app</a> · <a href="docs/architecture.md">Architecture</a> · <a href="docs/compatibility.md">Compatibility</a> · <a href="docs/parity-data-tools.md">Data tools</a> · <a href="docs/development.md">Development</a></p>

## New in 0.4: editable charts and PivotTables

GridSpace adds nine multi-series chart families, on-canvas move/resize/inline title editing, chart inspectors and clipboard operations, and refreshable worksheet-source PivotTables with field lists, filters, multiple measures, weighted totals and cached drill-through. Linked charts follow report refreshes. The XLSX writer emits chart parts, PivotTable definitions and typed source caches rather than only exporting flattened values.

Row hierarchies now have in-grid expansion buttons, parent-label double-click, keyboard/ribbon commands, and source-accumulated subtotals above or below groups. Linked charts exclude duplicate totals. [Hierarchy details](docs/pivot-hierarchy.md).

PivotTable field, filter, measure and layout edits reuse the immutable last-refresh source cache; Refresh explicitly reads current worksheet values. Field dragging owns its pointer gesture, with target highlighting and cancellation, rather than depending on browser-native data transfer. Drawing-to-cell navigation transfers editing ownership back to the grid.

See [the implementation and compatibility guide](docs/charts-pivots.md) for APIs, safeguards, limits, browser interaction coverage and paired performance measurements. Full Uno WebAssembly and native builds, physical-input acceptance, package generation and deployment checks run in CI. **A successful main build deploys Pages; publishing this version to NuGet or creating desktop release assets remains a separate tagged-release operation.**

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
| Charts | Nine multi-series families, explicit bindings, stacking/secondary combo axes, eight-handle WYSIWYG geometry, inline titles, inspector, clipboard and chart XLSX parts |
| PivotTables | Typed multidimensional grouping, multiple measures, Compact/Outline/Tabular row hierarchy, subtotals, collapse/expand, filters, cached layout edits, explicit refresh, protected reports, drill-through and linked charts |
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

## Download

Every [release](https://github.com/wieslawsoltes/GridSpace/releases/latest) ships a self-contained, single-file desktop app — no .NET install needed:

| OS | x64 | Arm64 |
| --- | --- | --- |
| Windows | `GridSpace-<version>-win-x64.zip` | `GridSpace-<version>-win-arm64.zip` |
| macOS | `GridSpace-<version>-osx-x64.tar.gz` | `GridSpace-<version>-osx-arm64.tar.gz` |
| Linux | `GridSpace-<version>-linux-x64.tar.gz` | `GridSpace-<version>-linux-arm64.tar.gz` |

Extract and run `GridSpace` (`GridSpace.exe` on Windows). Builds are not code-signed yet: on macOS clear the quarantine flag with `xattr -d com.apple.quarantine GridSpace`; on Windows choose **More info → Run anyway** in SmartScreen. Verify downloads against `SHA256SUMS.txt`.

The libraries below are published to [NuGet.org](https://www.nuget.org/packages?q=GridSpace), e.g. `dotnet add package GridSpace.Core --prerelease`.

## NuGet packages

GridSpace ships as eight MIT-licensed packages, all versioned together. Six target plain `net10.0` and have no UI dependency: `GridSpace.Core`, `Formulas`, `Editing`, `IO` and `Layout` are pure .NET, and `GridSpace.Skia` adds only SkiaSharp. The two Uno Platform packages (`Controls`, `Workbench`) target `net10.0-desktop` and `net10.0-browserwasm`. Symbols are published to nuget.org as `.snupkg` with SourceLink. `GridSpace.App` is the platform composition root and is not packaged.

```sh
dotnet add package GridSpace.Core --prerelease
```

| Package | Version | Downloads | Description |
| :--- | :--- | :--- | :--- |
| [GridSpace.Core](https://www.nuget.org/packages/GridSpace.Core) | [![NuGet](https://img.shields.io/nuget/vpre/GridSpace.Core.svg)](https://www.nuget.org/packages/GridSpace.Core) | [![Downloads](https://img.shields.io/nuget/dt/GridSpace.Core.svg)](https://www.nuget.org/packages/GridSpace.Core) | Sparse workbook/cell/style/range model, A1 addressing, axis edits and conditional/filter/sort models |
| [GridSpace.Formulas](https://www.nuget.org/packages/GridSpace.Formulas) | [![NuGet](https://img.shields.io/nuget/vpre/GridSpace.Formulas.svg)](https://www.nuget.org/packages/GridSpace.Formulas) | [![Downloads](https://img.shields.io/nuget/dt/GridSpace.Formulas.svg)](https://www.nuget.org/packages/GridSpace.Formulas) | Bounded, non-eval formula parser and calculation engine with dynamic arrays, reference rewriting, number formats, conditional formatting and filter predicates |
| [GridSpace.Editing](https://www.nuget.org/packages/GridSpace.Editing) | [![NuGet](https://img.shields.io/nuget/vpre/GridSpace.Editing.svg)](https://www.nuget.org/packages/GridSpace.Editing) | [![Downloads](https://img.shields.io/nuget/dt/GridSpace.Editing.svg)](https://www.nuget.org/packages/GridSpace.Editing) | Transactional sessions, bounded undo/redo, fill, clipboard, structural edits and data tools |
| [GridSpace.IO](https://www.nuget.org/packages/GridSpace.IO) | [![NuGet](https://img.shields.io/nuget/vpre/GridSpace.IO.svg)](https://www.nuget.org/packages/GridSpace.IO) | [![Downloads](https://img.shields.io/nuget/dt/GridSpace.IO.svg)](https://www.nuget.org/packages/GridSpace.IO) | Bounded XLSX, CSV/TSV and native interchange with fidelity warnings; host storage contract |
| [GridSpace.Layout](https://www.nuget.org/packages/GridSpace.Layout) | [![NuGet](https://img.shields.io/nuget/vpre/GridSpace.Layout.svg)](https://www.nuget.org/packages/GridSpace.Layout) | [![Downloads](https://img.shields.io/nuget/dt/GridSpace.Layout.svg)](https://www.nuget.org/packages/GridSpace.Layout) | Sparse axis indexes, viewport geometry, frozen panes and hit testing |
| [GridSpace.Skia](https://www.nuget.org/packages/GridSpace.Skia) | [![NuGet](https://img.shields.io/nuget/vpre/GridSpace.Skia.svg)](https://www.nuget.org/packages/GridSpace.Skia) | [![Downloads](https://img.shields.io/nuget/dt/GridSpace.Skia.svg)](https://www.nuget.org/packages/GridSpace.Skia) | Viewport-culled SkiaSharp cell, conditional style, data-bar, chart and selection rendering; owned font resources |
| [GridSpace.Controls](https://www.nuget.org/packages/GridSpace.Controls) | [![NuGet](https://img.shields.io/nuget/vpre/GridSpace.Controls.svg)](https://www.nuget.org/packages/GridSpace.Controls) | [![Downloads](https://img.shields.io/nuget/dt/GridSpace.Controls.svg)](https://www.nuget.org/packages/GridSpace.Controls) | Embeddable Uno spreadsheet grid, ribbon, formula bar, sheet tabs, scrollbars and filter/sort/conditional editors |
| [GridSpace.Workbench](https://www.nuget.org/packages/GridSpace.Workbench) | [![NuGet](https://img.shields.io/nuget/vpre/GridSpace.Workbench.svg)](https://www.nuget.org/packages/GridSpace.Workbench) | [![Downloads](https://img.shields.io/nuget/dt/GridSpace.Workbench.svg)](https://www.nuget.org/packages/GridSpace.Workbench) | Complete Excel-style workbench: commands, dialogs, filter popup, rule management and local recovery |

Dependencies (from project references): `Core ← Formulas ← Editing ← IO`, `Core ← Layout`, `Layout + Editing ← Skia ← Controls`, `Controls + IO ← Workbench`. A mutable `SpreadsheetSession` has one owning thread; use snapshots (for example `Workbook.ToJson`/`FromJson`) for background processing.

### GridSpace.Core

The sparse spreadsheet document model: workbooks, worksheets keyed by A1 address, immutable cell records and styles, ranges, merges, charts, validation lists, frozen panes, and the models for conditional formats, column filters and sort levels. It also owns JSON persistence. No dependencies and no UI.

```sh
dotnet add package GridSpace.Core --prerelease
```

**Key types**

- `Workbook` – `Sheets`, `Names`, `ActiveSheet`, `FindSheet`, `ToJson` / `FromJson`
- `Worksheet` – `Get`/`Set`, `UsedRange`, `Merges`, `FrozenRows`/`FrozenColumns`, `ConditionalFormats`, `Filters`
- `Cell`, `CellStyle` – immutable input + style records (`Cell.IsFormula`)
- `CellAddress` / `CellRange` – A1 parsing and formatting up to `XFD1048576`
- `ConditionalFormatRule`, `DifferentialStyle`, `ColumnFilter`, `SortLevel`, `ChartSpec` – data-tool models

**Usage**

```csharp
using GridSpace.Core;

var book = new Workbook { Title = "Budget" };
var sheet = book.ActiveSheet;
sheet.Set("A1", "Item");
sheet.Set("B1", "Cost");
sheet.Set("A2", "Rent");
sheet.Set("B2", "1200");
sheet.Set("B3", "=SUM(B2:B2)");
sheet.Set(CellAddress.Parse("A1"), sheet.Get("A1") with { Style = CellStyle.Default with { Bold = true } });
sheet.Merges.Add(CellRange.Parse("C1:D1"));
sheet.FrozenRows = 1;

CellRange used = sheet.UsedRange;
Console.WriteLine($"{used} ({used.Count} cells); B3 is a formula: {sheet.Get("B3").IsFormula}");
var copy = Workbook.FromJson(book.ToJson());   // native .gridspace JSON
```

### GridSpace.Formulas

A bounded, non-eval formula engine: parser, evaluator with revision-based caching and dependency invalidation, common functions, dynamic arrays that spill (`SEQUENCE`, `FILTER`, `SORT`, `UNIQUE`, `LET`, `A1#`…), cross-sheet references and names, reference rewriting for fills and structural edits, Excel-style number formatting, conditional-format evaluation and filter predicates. Depends on `GridSpace.Core`; no UI.

```sh
dotnet add package GridSpace.Formulas --prerelease
```

**Key types**

- `CalculationEngine` – `Evaluate`, `EvaluateFormula`, `Register` (custom functions), `GetSpill`, `Invalidate`, `BuiltInFunctions`
- `CalcValue` – number/text/boolean/error/array result (`Num`, `Str`, `Error`, `Array`, `TryNumber`)
- `FormulaReferences` – `Translate`, `RenameSheet`, `Insert`/`Delete` reference rewriting
- `NumberFormatter.Format` – format codes such as `0.00`, `#,##0`, `0%`
- `ConditionalFormattingEngine`, `WorksheetFilterEngine` – rule and filter evaluation

**Usage**

```csharp
using GridSpace.Core;
using GridSpace.Formulas;

var book = new Workbook();
var sheet = book.ActiveSheet;
sheet.Set("A1", "21");
sheet.Set("A2", "=A1*2");
sheet.Set("B1", "=SEQUENCE(3)");                 // spills into B1:B3

var calc = new CalculationEngine(book);
calc.Register("DOUBLE", args => args[0].TryNumber(out var n) ? CalcValue.Num(n * 2) : CalcValue.Error("#VALUE!"));

Console.WriteLine(NumberFormatter.Format(calc.Evaluate(sheet, "A2"), "0.00"));   // 42.00
Console.WriteLine(calc.EvaluateFormula(sheet, "=DOUBLE(A1)+SUM(B1#)"));          // 48
Console.WriteLine(calc.GetSpill(sheet, CellAddress.Parse("B1"))?.Range);          // B1:B3
Console.WriteLine(FormulaReferences.Translate("=A1*$B$2", rows: 1, columns: 0)); // =A2*$B$2
```

### GridSpace.Editing

The UI-independent editing session. Every command runs as a transaction with delta-based, bounded undo/redo; structural row/column edits rebase formulas, names, merges, validation, charts, conditional rules and freeze boundaries. It also provides clipboard blocks, fill/series, find/replace, sheet operations, filtering, multi-level sorting and a sample workbook. Depends on `GridSpace.Formulas`; no UI.

```sh
dotnet add package GridSpace.Editing --prerelease
```

**Key types**

- `SpreadsheetSession` – `Book`, `Sheet`, `Calculation`, `Selection`, `SetInput`, `ApplyStyle`, `Undo`/`Redo`, `Changed`
- Structure and tools – `InsertRows`/`DeleteColumns`, `Fill`, `Paste`, `Sort`, `SetFilter`, `SetConditionalFormat`, `AddChart`
- `DelimitedText` – bounded TSV/CSV `Parse`/`Write`
- `SampleWorkbook.Create()` – the demo Revenue/Assumptions workbook

**Usage**

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

### GridSpace.IO

File interchange: native `.gridspace` JSON, CSV/TSV (values only; formula-looking text is neutralized) and a documented XLSX subset including styles, merges, shared and dynamic-array formulas, conditional formatting, filters and charts. Imports are size-bounded and return warnings for anything not preserved. `IWorkbookStorage` is the host contract used by the workbench. Depends on `GridSpace.Editing`; no UI.

```sh
dotnet add package GridSpace.IO --prerelease
```

**Key types**

- `WorkbookFiles` – `Import(name, bytes)` by extension, `Native`, `Csv`
- `XlsxWorkbook` – `Read(bytes)` / `Write(book)`
- `ImportResult` – imported `Workbook` plus `Warnings`
- `IWorkbookStorage`, `OpenedFile` – open/save pickers and recovery persistence for hosts

**Usage**

```csharp
using GridSpace.Core;
using GridSpace.Editing;
using GridSpace.IO;

ImportResult imported = WorkbookFiles.Import("sales.xlsx", File.ReadAllBytes("sales.xlsx"));
foreach (var warning in imported.Warnings) Console.WriteLine(warning);

var session = new SpreadsheetSession(imported.Workbook);
session.SetInput("=SUM(B2:B13)", CellAddress.Parse("B14"));

File.WriteAllBytes("sales-out.xlsx", XlsxWorkbook.Write(session.Book));
File.WriteAllBytes("sales.csv", WorkbookFiles.Csv(session.Book));        // active sheet, calculated values
File.WriteAllBytes("sales.gridspace", WorkbookFiles.Native(session.Book));
```

### GridSpace.Layout

Renderer-independent grid geometry for a 1,048,576 × 16,384 sheet: sparse axis indexes over custom sizes and hidden rows/columns, zoom and scrolling, frozen-pane splitting, cell/range bounds and hit testing of cells and headers. Use it to build your own grid view on any surface. Depends on `GridSpace.Core`; no UI.

```sh
dotnet add package GridSpace.Layout --prerelease
```

**Key types**

- `GridViewport` – `Refresh(sheet)`, `ScrollTo`, `Zoom`, `Panes()`, `CellBounds`, `HitTest`, `EnsureVisible`
- `AxisLayout` – `Position`, `Size`, `IndexAt`, `Visible` over sparse sizes
- `GridPane`, `GridRect`, `GridHit` / `GridHitKind` – geometry results

**Usage**

```csharp
using GridSpace.Core;
using GridSpace.Layout;

var sheet = new Worksheet { FrozenRows = 1, FrozenColumns = 1 };
sheet.ColumnWidths[0] = 160;
sheet.HiddenRows.Add(4);

var viewport = new GridViewport { Width = 1280, Height = 720, Zoom = 1.25 };
viewport.Refresh(sheet);                       // rebuild axis indexes from sizes/hidden/frozen state
viewport.ScrollTo(0, 2_000);
foreach (GridPane pane in viewport.Panes())
    Console.WriteLine($"rows {pane.FirstRow}-{pane.LastRow}, columns {pane.FirstColumn}-{pane.LastColumn}");

GridHit hit = viewport.HitTest(300, 200);      // Cell, ColumnHeader, RowHeader or Corner
GridRect bounds = viewport.CellBounds(new CellAddress(hit.Row, hit.Column));
```

### GridSpace.Skia

Draws a session's visible cells onto any `SKCanvas`: values and number formats, fonts, wrapping, alignment, borders, merges, conditional styles, data bars, headers, frozen panes, selection and charts, culled to the viewport. `TypefaceCatalog` owns registered font data so hosts (especially WebAssembly) render consistently. Depends on `GridSpace.Layout`, `GridSpace.Editing` and SkiaSharp; no UI framework.

```sh
dotnet add package GridSpace.Skia --prerelease
```

**Key types**

- `SpreadsheetRenderer` – `Render(canvas, session, viewport)`, `MeasureColumn` (AutoFit), `Fonts`
- `TypefaceCatalog` – `Register(family, regular, bold, italic, boldItalic, aliases)`, `Resolve(style)`

**Usage**

```csharp
using GridSpace.Editing;
using GridSpace.Layout;
using GridSpace.Skia;
using SkiaSharp;

var session = new SpreadsheetSession(SampleWorkbook.Create());
using var renderer = new SpreadsheetRenderer();
renderer.Fonts.Register("Carlito", regular, bold, italic, boldItalic, "Arial", "Calibri"); // font file bytes

var viewport = new GridViewport { Width = 1200, Height = 800 };
viewport.Refresh(session.Sheet);

using var surface = SKSurface.Create(new SKImageInfo(1200, 800));
renderer.Render(surface.Canvas, session, viewport);
using var png = surface.Snapshot().Encode(SKEncodedImageFormat.Png, 100);
File.WriteAllBytes("sheet.png", png.ToArray());
```

### GridSpace.Controls

Uno Platform building blocks: `SpreadsheetGrid` (Skia-drawn through `SKCanvasElement`, with in-cell editing, selection, clipboard, zoom, touch panning and keyboard navigation), a data-driven `RibbonControl`, formula bar, sheet tabs, status bar, scroll bars, vector icons, and the filter, sort and conditional-format editors. Compose them with your own command routing. Depends on `GridSpace.Skia` and Uno Platform (Skia renderer).

```sh
dotnet add package GridSpace.Controls --prerelease
```

**Key types**

- `SpreadsheetGrid` – `Session`, `Renderer`, `Viewport`, `BeginEdit`, `CopyAsync`/`PasteAsync`, `SetZoom`, `EnableDataToolInteractions`
- `RibbonControl` – `SetTabs(RibbonTab[])`, `CommandRequested`; `RibbonTab`/`RibbonGroup`/`RibbonCommand` records
- `FormulaBarControl`, `WorksheetTabsControl`, `SpreadsheetStatusBar`, `SheetScrollBar`
- `FilterEditorControl`, `SortEditorControl`, `ConditionalFormatEditorControl` – `BuildFilter`/`BuildLevels`/`BuildRule`

**Usage**

```csharp
using GridSpace.Controls;
using GridSpace.Editing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

var session = new SpreadsheetSession(SampleWorkbook.Create());
var sheetView = new SpreadsheetGrid { Session = session };

var ribbon = new RibbonControl();
ribbon.SetTabs([new RibbonTab("Home", [new RibbonGroup("Font", [new RibbonCommand("bold", "Bold", OfficeIconKind.Bold)])])]);
ribbon.CommandRequested += id =>
{
    if (id != "bold") return;
    var bold = !session.SelectedStyle.Bold;
    session.ApplyStyle(style => style with { Bold = bold });
};

var root = new Grid { RowDefinitions = { new RowDefinition { Height = GridLength.Auto }, new RowDefinition() } };
Grid.SetRow(sheetView, 1);
root.Children.Add(ribbon);
root.Children.Add(sheetView);
window.Content = root;
window.Closed += (_, _) => sheetView.Dispose();
```

### GridSpace.Workbench

The complete Excel-style shell used by the app: ribbon (`WorkbookRibbon.Create()`), formula bar, grid, sheet tabs, status bar, context menus, dialogs, filter popup, conditional-rule manager, custom sort and debounced local recovery. Platform file pickers and recovery persistence come from your `IWorkbookStorage` implementation. Web hosts should register font bytes with `Surface.Renderer.Fonts`. Depends on `GridSpace.Controls`, `GridSpace.IO` and Uno Platform.

```sh
dotnet add package GridSpace.Workbench --prerelease
```

**Key types**

- `SpreadsheetWorkbench` – `SpreadsheetWorkbench(session, storage)`, `Session`, `Surface`, `ExecuteAsync(commandId)`, `ShowStatus`
- `WorkbookRibbon.Create()` – the default tab/group/command set
- `GridSpace.IO.IWorkbookStorage` – host services the workbench calls

**Usage**

```csharp
using GridSpace.Core;
using GridSpace.Editing;
using GridSpace.IO;
using GridSpace.Workbench;

IWorkbookStorage storage = new MyWorkbookStorage(); // OpenAsync, SaveAsync, Read/WriteRecoveryAsync
var recovery = await storage.ReadRecoveryAsync();
var book = string.IsNullOrWhiteSpace(recovery) ? SampleWorkbook.Create() : Workbook.FromJson(recovery);

var workbench = new SpreadsheetWorkbench(new SpreadsheetSession(book), storage);
workbench.Surface.Renderer.Fonts.Register("Carlito", regular, bold, italic, boldItalic, "Arial", "Calibri", "Aptos");
window.Content = workbench;
window.Closed += (_, _) => workbench.Dispose();
```

## Validation and delivery

```bash
dotnet test tests/GridSpace.Tests -c Release
npm ci
npx playwright install chromium
# Start the local server above, then:
npm run test:browser
```

Tests cover formulas, reference rewriting, rollback/history, native/XLSX interchange, conditional/filter/sort engines, structural metadata, sparse/frozen geometry and actual raster pixels. Independent **Open XML SDK schema validation** supplements the reader/writer round-trip tests; that dependency is test-only.

Browser acceptance uses physical keyboard and pointer events. A `?test=1` read-only snapshot provides model state and current control bounds, not mutation hooks. Tests exercise both the original editing flows and the new rule editor/manager, filter popup, custom-sort editor and structural deletion, chart manipulation/title/inspector editing, PivotTable creation/drill-through, field dragging/cancellation, cached refresh epochs and chart-to-cell navigation; screenshots and traces are retained in CI artifacts.

**Build** tests and publishes the actual Uno WebAssembly application, packs libraries, then deploys a successful main-branch build to Pages. The artifact's `build-info.json` commit is verified, and the browser suite runs again against the public URL. **Desktop** builds the shared native host on Linux, Windows and macOS. **Release** runs for `v*` tags or a supplied manual version. It reruns tests and browser acceptance, publishes self-contained single-file desktop executables for Windows, macOS and Linux (x64 and arm64), and creates versioned source/browser archives, packages with symbols and checksums. Tags attach all assets to a GitHub release/prerelease and publish the packages to NuGet.org with Trusted Publishing from the protected `nuget` environment. Manual runs are dry runs: they build and upload every asset as workflow artifacts but publish nothing.

## Important boundaries

VBA, advanced PivotTable date/number bucketing/column hierarchy/OLAP, Power Query, external-data refresh, multiplayer editing, Excel add-ins, complete array/LAMBDA semantics, lossless arbitrary OOXML preservation, full print/page layout and complete virtualized-cell accessibility remain unimplemented. The custom workbench still uses Uno input/dialog primitives. Conditional-format clipboard propagation, arbitrary cell-shift deletion and advanced filter/chart variants also remain outside this increment.

The alpha bounds expensive work: 100,000 cells per bulk edit, conditional range or sort; 100,000 filter data rows; 200,000 stored cells per imported sheet; 256 sheets; and 32 MB native/file import. See [compatibility](docs/compatibility.md) for exact subset behavior and limitations.

## License

GridSpace source is [MIT licensed](LICENSE). Dependencies and fetched font assets retain their own licenses; see [third-party notices](THIRD-PARTY-NOTICES.md). GridSpace is independent of Microsoft. No Microsoft logo, proprietary font or extracted Excel asset is included.
