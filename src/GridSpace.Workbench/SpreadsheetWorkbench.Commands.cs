using System.Globalization;

namespace GridSpace.Workbench;

public sealed partial class SpreadsheetWorkbench
{
    private async Task ExecuteCoreAsync(string id)
    {
        // Backstage and command search also enter here; they must use the same data-tool routing as the ribbon.
        if (await ExecuteDataToolAsync(id)) return;
        switch (id)
        {
            case "undo": Session.Undo(); break;
            case "redo": Session.Redo(); break;
            case "copy": await Surface.CopyAsync(); ShowStatus("Copied to clipboard"); break;
            case "cut": await Surface.CopyAsync(true); ShowStatus("Cut to clipboard"); break;
            case "paste": await Surface.PasteAsync(); break;
            case "paste-values": await Surface.PasteAsync(true); break;
            case "bold": { var next = !Session.SelectedStyle.Bold; Session.ApplyStyle(s => s with { Bold = next }); break; }
            case "italic": { var next = !Session.SelectedStyle.Italic; Session.ApplyStyle(s => s with { Italic = next }); break; }
            case "underline": { var next = !Session.SelectedStyle.Underline; Session.ApplyStyle(s => s with { Underline = next }); break; }
            case "wrap": { var next = !Session.SelectedStyle.Wrap; Session.ApplyStyle(s => s with { Wrap = next }); break; }
            case "borders": { var next = !Session.SelectedStyle.Border; Session.ApplyStyle(s => s with { Border = next }); break; }
            case "align-left": Session.ApplyStyle(s => s with { Alignment = CellAlignment.Left }); break;
            case "align-center": Session.ApplyStyle(s => s with { Alignment = CellAlignment.Center }); break;
            case "align-right": Session.ApplyStyle(s => s with { Alignment = CellAlignment.Right }); break;
            case "font": if (await PromptAsync("Font", "Font family", Session.SelectedStyle.FontFamily) is { Length: > 0 } family) Session.ApplyStyle(s => s with { FontFamily = family }); break;
            case "font-size": if (await PromptAsync("Font Size", "Size in points", Session.SelectedStyle.FontSize.ToString(CultureInfo.InvariantCulture)) is { } size) { var number = ParseNumber(size, 4, 200); Session.ApplyStyle(s => s with { FontSize = number }); } break;
            case "font-color": await PickColorAsync(false); break;
            case "fill-color": await PickColorAsync(true); break;
            case "number-format": if (await PromptAsync("Number Format", "General, #,##0.00, $#,##0.00, 0.0%, yyyy-mm-dd", Session.SelectedStyle.NumberFormat) is { } format) Session.ApplyStyle(s => s with { NumberFormat = format }); break;
            case "currency": Session.ApplyStyle(s => s with { NumberFormat = "$#,##0.00" }); break;
            case "percent": Session.ApplyStyle(s => s with { NumberFormat = "0.0%" }); break;
            case "format-cells": await FormatCellsAsync(); break;
            case "cell-style": await CellStyleAsync(); break;
            case "merge": Session.Merge(); break;
            case "unmerge": Session.Unmerge(); break;
            case "clear": Session.Clear(); break;
            case "clear-all": Session.Clear(true); break;
            case "fill-down": Session.FillDown(); break;
            case "fill-right": Session.FillRight(); break;
            case "autosum": Session.AutoSum(); break;
            case "format-table": Session.FormatTable(); break;
            case "insert-row": Session.Insert(true); break;
            case "insert-column": Session.Insert(false); break;
            case "row-height": if (await PromptAsync("Row Height", "Height in pixels", Session.Sheet.RowHeight(Session.ActiveCell.Row).ToString(CultureInfo.InvariantCulture)) is { } height) Session.SetRowHeight(Session.ActiveCell.Row, ParseNumber(height, 16, 600)); break;
            case "column-width": if (await PromptAsync("Column Width", "Width in pixels", Session.Sheet.ColumnWidth(Session.ActiveCell.Column).ToString(CultureInfo.InvariantCulture)) is { } width) Session.SetColumnWidth(Session.ActiveCell.Column, ParseNumber(width, 24, 1000)); break;
            case "autofit": Session.SetColumnWidth(Session.ActiveCell.Column, Surface.Renderer.MeasureColumn(Session, Session.ActiveCell.Column)); break;
            case "sort-ascending": Session.Sort(); break;
            case "sort-descending": Session.Sort(true); break;
            case "find": await FindAsync(false); break;
            case "replace": await FindAsync(true); break;
            case "freeze": Session.Freeze(); break;
            case "freeze-top": Session.Freeze(true); break;
            case "unfreeze": Session.Unfreeze(); break;
            case "gridlines": Session.Perform("Gridlines", () => Session.Sheet.ShowGridLines = !Session.Sheet.ShowGridLines); break;
            case "show-formulas": Session.Perform("Show formulas", () => Session.Sheet.ShowFormulas = !Session.Sheet.ShowFormulas); break;
            case "calculate": Session.Book.Attach(); Session.Notify("Recalculate", true); ShowStatus("Calculation complete"); break;
            case "define-name": if (await PromptAsync("Define Name", "Workbook name for " + Session.Selection, "") is { } name) Session.DefineName(name); break;
            case "name-manager": await MessageAsync("Name Manager", Session.Book.Names.Count == 0 ? "No defined names. Select a range, then choose Define Name." : string.Join("\n\n", Session.Book.Names.Select(p => p.Key + " = " + p.Value))); break;
            case "validation": if (await PromptAsync("Data Validation", "Allowed values, separated by commas", "Yes,No") is { } list) Session.SetValidation(list.Split(',').Select(v => v.Trim()).ToArray()); break;
            case "note": if (await PromptAsync("Cell Note", "Note for " + Session.ActiveCell, Session.Sheet.Get(Session.ActiveCell).Note ?? "", true) is { } note) Session.AddNote(note); break;
            case "delete-note": Session.AddNote(""); break;
            case "chart-column": Session.AddChart(ChartKind.Column); break;
            case "chart-line": Session.AddChart(ChartKind.Line); break;
            case "chart-bar": Session.AddChart(ChartKind.Bar); break;
            case "chart-pie": Session.AddChart(ChartKind.Pie); break;
            case "zoom-in": Surface.SetZoom(Surface.Viewport.Zoom + .1); break;
            case "zoom-out": Surface.SetZoom(Surface.Viewport.Zoom - .1); break;
            case "zoom-reset": Surface.SetZoom(1); break;
            case "add-sheet": Session.AddSheet(); break;
            case "duplicate-sheet": Session.DuplicateSheet(); break;
            case "rename-sheet": if (await PromptAsync("Rename Sheet", "Worksheet name", Session.Sheet.Name) is { } sheetName) Session.RenameSheet(sheetName); break;
            case "delete-sheet": if (await ConfirmAsync("Delete Sheet", "Delete “" + Session.Sheet.Name + "”? References become #REF!. This can be undone.")) Session.DeleteSheet(); break;
            case "sheet-menu": await SheetMenuAsync(); break;
            case "function": if (await PromptAsync("Insert Function", "Formula (for example =SUM(A1:A10))", "=SUM(" + Session.Selection + ")") is { } formula) Session.SetInput(formula); break;
            case "file": await BackstageAsync(); break;
            case "new": if (await CanReplaceAsync()) Session.Load(new Workbook { Title = "Book1" }); break;
            case "sample": if (await CanReplaceAsync()) Session.Load(SampleWorkbook.Create()); break;
            case "open":
                if (!await CanReplaceAsync()) break;
                if (await _storage.OpenAsync() is { } opened)
                {
                    var imported = WorkbookFiles.Import(opened.Name, opened.Bytes); Session.Load(imported.Workbook);
                    ShowStatus("Opened " + opened.Name);
                    if (imported.Warnings.Count > 0) await MessageAsync("Import Notes", string.Join("\n\n", imported.Warnings));
                }
                break;
            case "save": await SaveAsync(".gridspace"); break;
            case "export-xlsx": await SaveAsync(".xlsx"); break;
            case "export-csv": await SaveAsync(".csv"); break;
            case "rename-workbook": if (await PromptAsync("Workbook Name", "Title", Session.Book.Title) is { } title) Session.Perform("Rename workbook", () => Session.Book.Title = title); break;
            case "command-search": await SearchCommandsAsync(); break;
            case "help": await MessageAsync("Working with GridSpace", "Double-click a cell or press F2 to edit. Enter commits; Escape cancels.\n\nArrow keys navigate. Shift+arrows extend a range. Ctrl+C/V/X copies, pastes or cuts; Ctrl+Z/Y undoes or redoes. Ctrl+S downloads the native workbook.\n\nDrag row/column boundaries to resize; double-click a column header to AutoFit. Drag the fill handle to extend formulas or a two-value numeric series.\n\nThe Data tab provides compound filters, ordered Custom Sort levels, conditional rules, scales and data bars. Click a filter-header button or press Alt+Down to edit its criteria. Clear Filters keeps manually hidden rows hidden. Use Reapply after changing filtered data.\n\nConditional formulas are relative to the upper-left applies-to cell. Manage Rules controls priorities and Stop If True. Formatting never overwrites cell values.\n\nHome and the context menu insert/delete selected rows or columns and update supported references. Full-range deletion produces #REF!; undo restores the document.\n\nDynamic arrays: enter =SEQUENCE(4,2), =SORT(A1#) or =LET(data,A1#,FILTER(data,CHOOSECOLS(data,1)>3)). Only the anchor is editable; the blue outline and read-only formula bar identify its output. Clear blocking data to resolve #SPILL!.\n\nXLSX support remains a subset. Keep originals and use .gridspace for native recovery. VBA, PivotTables, Power Query, collaboration and full Excel compatibility are not implemented."); break;
            case "about": await MessageAsync("About GridSpace", "GridSpace 0.3.0-alpha.1\n\nA local-first spreadsheet built with Uno Platform and SkiaSharp. Eight independently packable libraries share the same workbook model, calculation engines and transactions.\n\nThis is an independent implementation, not Microsoft Excel. It does not include Microsoft branding, fonts or proprietary assets.\n\nMIT License · GridSpace contributors"); break;
            default: throw new InvalidOperationException("Unknown command: " + id);
        }
        Surface.RevealSelection();
    }
    private static double ParseNumber(string text, double minimum, double maximum)
    {
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value) || value < minimum || value > maximum) throw new ArgumentException($"Enter a number between {minimum} and {maximum}.");
        return value;
    }
    private async Task SaveAsync(string extension)
    {
        var name = new string(Session.Book.Title.Where(c => !Path.GetInvalidFileNameChars().Contains(c) && c is not '/' and not '\\').ToArray()).Trim();
        if (name.Length == 0) name = "Workbook";
        var bytes = extension == ".xlsx" ? XlsxWorkbook.Write(Session.Book) : extension == ".csv" ? WorkbookFiles.Csv(Session.Book) : WorkbookFiles.Native(Session.Book);
        await _storage.SaveAsync(name + extension, bytes, extension == ".xlsx" ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" : extension == ".csv" ? "text/csv;charset=utf-8" : "application/json");
        if (extension == ".gridspace") Session.MarkSaved();
        ShowStatus(extension == ".gridspace" ? "Native workbook saved" : "Exported " + extension + " subset; native workbook remains authoritative");
    }
    private async Task<bool> CanReplaceAsync() => !Session.IsDirty || await ConfirmAsync("Unsaved Workbook", "Replace the current workbook? Download a native copy first to keep it. The recovery slot will be replaced by the newly opened workbook.");
}
