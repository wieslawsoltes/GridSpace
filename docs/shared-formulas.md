# Shared-formula imports and reusable choice controls

## Shared-formula XLSX imports

XLSX can store one master formula and refer to it from multiple cells. GridSpace resolves these groups in two passes per worksheet, so a follower appearing before the master is still calculated correctly. Only explicitly shared cells participate: ordinary formulas override group membership, and a follower's redundant formula text does not override its master. Row/column offsets use the same reference translator as clipboard and fill operations, retaining mixed/absolute references, quoted sheet names and formula string literals.

The imported cells contain ordinary independent formulas. Edits recalculate them, and export writes their current expressions and calculated values rather than retaining a stale shared cache. Conflicting or unresolved group indexes, malformed master ranges and out-of-range followers use typed cached results with an import warning. Cached text, booleans and errors keep their type; absent caches produce explicit errors rather than silent blanks. What-if data tables use their caches and are not claimed to recalculate.

Expansion is limited across the entire workbook to 33,554,432 UTF-16 characters, in addition to existing input, ZIP, XML and cell limits. A master covering `A1:XFD1048576` never materializes absent cells. This is shared-formula interchange, not a dynamic-array or spill implementation.

```csharp
var imported = WorkbookFiles.Import("model.xlsx", bytes);
var session = new SpreadsheetSession(imported.Workbook);
// Shared followers now contain translated ordinary formulas.
var input = session.Sheet.Get("B2").Input;
session.SetInput("50", CellAddress.Parse("A2"));
var recalculated = session.Calculation.Evaluate(session.Sheet, "B2");
var exported = XlsxWorkbook.Write(session.Book);
```

## Choice controls and keyboard behavior

`OfficeChoiceBox` is a reusable dependency-property-based selector used by the sort, filter and conditional-format editors. Its virtualized list previews choices with arrows, Home/End and Page Up/Down. Enter commits; Escape cancels the preview without cancelling the surrounding tool; Tab commits and advances focus. Preview keyboard events are handled before the built-in list/button handlers and parent-dialog activation. The sort editor uses flexible columns within the available dialog width instead of a horizontal scrollbar over the selector hit targets.

`ItemsSource`, `SelectedIndex` and `SelectedItem` are dependency properties. `SelectionChanged` reports committed changes. `IsDropDownOpen` and `PreviewIndex` distinguish pending selection from committed state. Hosts can compose the control without the workbench.

```csharp
var direction = OfficeForm.Choice("Sort order", new[]
{
    new OfficeChoice<bool>(false, "Smallest to Largest"),
    new OfficeChoice<bool>(true, "Largest to Smallest")
}, selected: false);
direction.SelectionChanged += (_, _) => ApplyDirection(OfficeForm.Value<bool>(direction));
```

## Validation boundary

Regression tests cover explicit open/preview/commit state, Escape rollback, ordered sort-level movement/removal, recalculation after shared-formula import, typed fallback, worksheet-local group IDs and cross-sheet expansion limits. The Open XML SDK validator checks representative exported workbooks independently of GridSpace's reader. The tests do not launch Microsoft Excel or establish complete workbook compatibility.

The implementation follows the shared-formula rules documented in Microsoft's [CellFormula reference](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.spreadsheet.cellformula) and the tunneling semantics of [UIElement.PreviewKeyDown](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement.previewkeydown).
