using System.Globalization;
using GridSpace.Core;

namespace GridSpace.Editing;

/// <summary>Deterministic editable source, refreshable PivotTable and linked chart examples.</summary>
public static class AnalyticsSampleWorkbook
{
    public static Workbook Create()
    {
        var book = new Workbook { Title = "Sales analytics" };
        var source = book.ActiveSheet; source.Name = "Sales";
        string[] headers = ["Region", "Product", "Quarter", "Revenue", "Units"];
        for (var c = 0; c < headers.Length; c++)
        {
            source.Set(new CellAddress(0, c), new Cell { Input = headers[c], Style = CellStyle.Default with
                { Bold = true, Background = "#107C41", Foreground = "#FFFFFF" } });
            source.ColumnWidths[c] = c < 3 ? 110 : 100;
        }
        string[] regions = ["Americas", "Europe", "Asia Pacific"];
        string[] products = ["Studio", "Enterprise"];
        var row = 1;
        for (var q = 0; q < 4; q++)
        for (var r = 0; r < regions.Length; r++)
        for (var p = 0; p < products.Length; p++)
        {
            var units = 90 + q * 18 + r * 11 + p * 45;
            string[] values = [regions[r], products[p], "Q" + (q + 1),
                (units * (p == 0 ? 249 : 699)).ToString(CultureInfo.InvariantCulture), units.ToString(CultureInfo.InvariantCulture)];
            for (var c = 0; c < values.Length; c++)
                source.Set(new CellAddress(row, c), new Cell { Input = values[c],
                    Style = CellStyle.Default with { Background = row % 2 == 0 ? "#F0F6F2" : "#FFFFFF", NumberFormat = c == 3 ? "$#,##0" : "General" } });
            row++;
        }
        source.FilterRange = $"A1:E{row}"; source.FrozenRows = 1;
        var session = new SpreadsheetSession(book);
        session.AddSheet("Pivot analysis");
        var spec = new PivotTableSpec
        {
            Name = "RegionalRevenue", SourceSheet = source.Name, SourceRange = $"A1:E{row}",
            Destination = "B5", Rows = [0], Columns = [2],
            Values = [new() { Field = 3, Caption = "Revenue", NumberFormat = "$#,##0" }]
        };
        session.SetPivotTable(spec);
        session.Sheet.Set(new CellAddress(1, 1), new Cell { Input = "Revenue by region and quarter", Style = CellStyle.Default with { FontSize = 20, Bold = true, Foreground = "#107C41" } });
        session.Sheet.Set("B3", "Select a value for the field list; double-click a value to show its source records.");
        session.Sheet.Merges.Add(CellRange.Parse("B2:G2"));
        session.Sheet.Merges.Add(CellRange.Parse("B3:G3"));
        session.Sheet.RowHeights[1] = 34; session.Sheet.RowHeights[2] = 28;
        session.Sheet.ColumnWidths[1] = 135;
        for (var c = 2; c <= 6; c++) session.Sheet.ColumnWidths[c] = 130;
        session.AddChart(new ChartSpec
        {
            Title = "Regional revenue", Kind = ChartKind.Column, SourceSheet = session.Sheet.Name,
            PivotTableId = spec.Id, Range = session.Sheet.PivotTables[0].ChartRange!,
            Row = 12, Column = 1, Width = 690, Height = 340, ValueFormat = "$#,##0", ShowDataLabels = false
        });
        session.AddChart(new ChartSpec
        {
            Title = "Quarterly profile", Kind = ChartKind.Line, SourceSheet = session.Sheet.Name,
            Range = "B5:F8", SeriesInRows = true, Row = 12, Column = 7, Width = 500, Height = 340,
            ValueFormat = "$#,##0"
        });
        session.Select("B5");
        // Return a document without the sample-construction undo history.
        book.Attach();
        return book;
    }
}
