using System.Globalization;
using GridSpace.Core;

namespace GridSpace.Editing;

public static class SampleWorkbook
{
    public static Workbook Create()
    {
        var book = new Workbook { Title = "Revenue overview", Sheets = [new() { Name = "Revenue" }, new() { Name = "Assumptions" }, new() { Name = "Read me" }] };
        book.Attach(); var sheet = book.Sheets[0];
        sheet.ColumnWidths[0] = 28; sheet.ColumnWidths[1] = 112; sheet.ColumnWidths[2] = 112;
        sheet.ColumnWidths[3] = 80; sheet.ColumnWidths[4] = 92; sheet.ColumnWidths[5] = 116;
        sheet.ColumnWidths[6] = 106; sheet.ColumnWidths[7] = 98; sheet.ColumnWidths[8] = 104;
        sheet.RowHeights[0] = 16; sheet.RowHeights[1] = 37; sheet.RowHeights[2] = 25; sheet.RowHeights[3] = 14; sheet.RowHeights[4] = 28;
        Put(sheet, "B2", "Revenue overview", new() { FontSize = 23, Bold = true, Foreground = "#107C41" });
        sheet.Merges.Add(CellRange.Parse("B2:H2"));
        Put(sheet, "B3", "FY 2026  ·  Illustrative sales data  ·  All amounts in USD", new() { Foreground = "#666666", FontSize = 11 });
        sheet.Merges.Add(CellRange.Parse("B3:I3"));
        var headings = new[] { "Month", "Region", "Units", "Unit price", "Revenue", "Costs", "Profit", "Margin" };
        for (var c = 0; c < headings.Length; c++) Put(sheet, new CellAddress(4, c + 1).ToString(), headings[c], new() { Bold = true, Background = "#107C41", Foreground = "#FFFFFF" });
        var units = new[] { 126, 142, 158, 151, 174, 190, 182, 204, 216, 230, 248, 271 };
        for (var i = 0; i < 12; i++)
        {
            var row = i + 6; var style = new CellStyle { Background = i % 2 == 0 ? "#F0F6F2" : "#FFFFFF" };
            var values = new[] { new DateTime(2026, i + 1, 1).ToString("MMM", CultureInfo.InvariantCulture), i % 3 == 0 ? "Americas" : i % 3 == 1 ? "Europe" : "Asia Pacific", units[i].ToString(CultureInfo.InvariantCulture), "=Assumptions!$B$3", $"=D{row}*E{row}", $"=F{row}*Assumptions!$B$4", $"=F{row}-G{row}", $"=IFERROR(H{row}/F{row},0)" };
            for (var c = 0; c < values.Length; c++) Put(sheet, new CellAddress(row - 1, c + 1).ToString(), values[c], style with { NumberFormat = c == 7 ? "0.0%" : c is >= 3 and <= 6 ? "#,##0" : "General", Foreground = c == 6 ? "#107C41" : "#242424" });
        }
        for (var c = 1; c <= 8; c++)
        {
            var letter = CellAddress.ColumnName(c); var input = c == 1 ? "Total" : c == 2 || c == 4 ? "" : c == 8 ? "=H18/F18" : $"=SUM({letter}6:{letter}17)";
            Put(sheet, new CellAddress(17, c).ToString(), input, new() { Bold = true, Background = "#DCEDE2", Border = true, NumberFormat = c == 8 ? "0.0%" : "#,##0" });
        }
        sheet.RowHeights[17] = 30;
        Put(sheet, "B21", "Try it: change a unit count or the assumptions. Revenue, profit and the chart recalculate.", new() { Foreground = "#666666", Italic = true });
        sheet.Merges.Add(CellRange.Parse("B21:I21"));
        sheet.FilterRange = "B5:I17";
        sheet.Charts.Add(new() { Title = "Monthly revenue", Range = "B5:F17", Row = 4, Column = 10, Width = 430, Height = 286, Kind = ChartKind.Column });
        var assumptions = book.Sheets[1]; assumptions.ColumnWidths[0] = 230; assumptions.ColumnWidths[1] = 135;
        Put(assumptions, "A1", "Model assumptions", new() { FontSize = 20, Bold = true, Foreground = "#107C41" });
        assumptions.Set("A3", "Unit selling price (USD)"); Put(assumptions, "B3", "1250", new() { Background = "#FFF2CC", NumberFormat = "$#,##0" });
        assumptions.Set("A4", "Cost as a share of revenue"); Put(assumptions, "B4", "0.62", new() { Background = "#FFF2CC", NumberFormat = "0.0%" });
        assumptions.Set("A6", "Yellow cells are editable model inputs.");
        var readme = book.Sheets[2]; readme.ColumnWidths[0] = 640;
        string[] lines = ["Welcome to GridSpace", "A local-first spreadsheet built with Uno Platform and SkiaSharp.", "Double-click a cell or press F2 to edit. Enter commits; Escape cancels.", "Use arrows, Shift+arrows, Tab, Ctrl+C/V/Z/Y and the formula bar.", "Drag a column or row boundary to resize; drag the fill handle to extend values.", "Use File to open/save GridSpace, CSV and supported XLSX workbooks.", "Native GridSpace files preserve all implemented features.", "XLSX interoperability is a documented subset, not a lossless Excel roundtrip.", "No account, backend, telemetry, macro execution or cloud connection is required.", "Sample figures are fictional and are not investment or business advice."];
        for (var i = 0; i < lines.Length; i++) Put(readme, "A" + (i + 1), lines[i], new() { FontSize = i == 0 ? 22 : 12, Bold = i == 0, Foreground = i == 0 ? "#107C41" : "#242424" });
        book.Attach(); return book;
    }
    private static void Put(Worksheet sheet, string address, string input, CellStyle style) => sheet.Set(CellAddress.Parse(address), new() { Input = input, Style = style });
}
