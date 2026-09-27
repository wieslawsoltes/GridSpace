using GridSpace.Core;
using GridSpace.Editing;
using GridSpace.Formulas;
using Xunit;

namespace GridSpace.Tests;

public sealed class DataToolParityTests
{
    private static SpreadsheetSession Data()
    {
        var session = new SpreadsheetSession(new Workbook());
        session.Paste("Region\tSales\tName\nEast\t10\tAmy\nWest\t40\tBen\nEast\t30\tCara\nEast\t30\tDan\nWest\t\tEli");
        session.Select("A1:C6");
        return session;
    }

    [Fact]
    public void FiltersCombineAcrossColumnsAndPreserveManualHiding()
    {
        var session = Data(); session.Sheet.HiddenRows.Add(3);
        session.SetFilter(new ColumnFilter { Column = 0, Values = ["East"] });
        session.SetFilter(new ColumnFilter { Column = 1, First = new FilterCondition(FilterOperator.GreaterThan, "20") });
        Assert.Equal(new[] { 1, 2, 5 }, session.Sheet.FilteredRows.Order().ToArray());
        Assert.True(session.Sheet.IsRowHidden(3)); Assert.False(session.Sheet.IsRowHidden(4));
        session.ClearFilters(1); Assert.Equal(new[] { 2, 5 }, session.Sheet.FilteredRows.Order().ToArray());
        session.ClearFilters(); Assert.Empty(session.Sheet.FilteredRows); Assert.Contains(3, session.Sheet.HiddenRows);
        session.Undo(); Assert.Equal(new[] { 2, 5 }, session.Sheet.FilteredRows.Order().ToArray());
    }

    [Theory]
    [InlineData(FilterOperator.Equal, "A*", "Alpha", true)]
    [InlineData(FilterOperator.Equal, "A?", "Ab", true)]
    [InlineData(FilterOperator.Equal, "A~*", "A*", true)]
    [InlineData(FilterOperator.Equal, "A~*", "Ab", false)]
    [InlineData(FilterOperator.Contains, "PH", "Alpha", true)]
    [InlineData(FilterOperator.BeginsWith, "ph", "Alpha", false)]
    [InlineData(FilterOperator.EndsWith, "ha", "Alpha", true)]
    [InlineData(FilterOperator.Blank, "", "", true)]
    [InlineData(FilterOperator.NotBlank, "", "", false)]
    public void TextPredicatesSupportWildcardsAndEscapes(FilterOperator op, string query, string text, bool expected)
    {
        var predicate = WorksheetFilterEngine.Compile(new ColumnFilter { Column = 0, First = new FilterCondition(op, query) });
        Assert.Equal(expected, predicate(CalcValue.Str(text)));
    }

    [Fact]
    public void TwoConditionsCanUseAndOr()
    {
        var filter = new ColumnFilter { Column = 0, First = new FilterCondition(FilterOperator.LessThan, "15"), Second = new FilterCondition(FilterOperator.GreaterThan, "35"), And = false };
        var predicate = WorksheetFilterEngine.Compile(filter);
        Assert.True(predicate(CalcValue.Num(10))); Assert.True(predicate(CalcValue.Num(40))); Assert.False(predicate(CalcValue.Num(30)));
        Assert.False(WorksheetFilterEngine.Compile(filter with { And = true })(CalcValue.Num(10)));
    }

    [Fact]
    public void SortIsStableUsesMultipleLevelsAndKeepsBlanksLast()
    {
        var session = Data(); session.Sort([new SortLevel(0), new SortLevel(1, true)]);
        Assert.Equal(new[] { "Cara", "Dan", "Amy", "Ben", "Eli" }, Enumerable.Range(1, 5).Select(r => session.Sheet.Get(new CellAddress(r, 2)).Input).ToArray());
        Assert.Equal("Region", session.Sheet.Get("A1").Input);
        session.Undo(); Assert.Equal("Amy", session.Sheet.Get("C2").Input);
        session.Redo(); Assert.Equal("Cara", session.Sheet.Get("C2").Input);
    }

    [Fact]
    public void SortRejectsMergedRegionsWithoutMutation()
    {
        var session = Data(); session.Sheet.Merges.Add(CellRange.Parse("B2:C2"));
        var before = session.Book.ToJson();
        Assert.Throws<InvalidOperationException>(() => session.Sort([new SortLevel(0)]));
        Assert.Equal(before, session.Book.ToJson());
    }

    [Fact]
    public void ConditionalPriorityComposesPropertiesAndStops()
    {
        var session = new SpreadsheetSession(new Workbook()); session.SetInput("50");
        session.SetConditionalFormat(new ConditionalFormatRule { Id = "first", Range = "A1", Operand = "10", Style = new DifferentialStyle { Background = "#FF0000" } });
        session.SetConditionalFormat(new ConditionalFormatRule { Id = "second", Range = "A1", Priority = 2, Operand = "20", Style = new DifferentialStyle { Background = "#00FF00", Bold = true } });
        var engine = new ConditionalFormattingEngine(session.Calculation);
        var value = session.Calculation.Evaluate(session.Sheet, "A1");
        var result = engine.Evaluate(session.Sheet, new CellAddress(0, 0), value);
        Assert.Equal("#FF0000", result.Style.Background); Assert.True(result.Style.Bold);
        session.SetConditionalFormat(session.Sheet.ConditionalFormats[0] with { StopIfTrue = true });
        result = engine.Evaluate(session.Sheet, new CellAddress(0, 0), value);
        Assert.False(result.Style.Bold); Assert.Equal("#FFFFFF", session.Sheet.Get("A1").Style.Background);
    }

    [Fact]
    public void FormulaRulesAreRelativeToTopLeftAndRecalculate()
    {
        var session = new SpreadsheetSession(new Workbook()); session.Sheet.Set("A1", "2"); session.Sheet.Set("A2", "12");
        session.SetConditionalFormat(new ConditionalFormatRule { Range = "B1:B2", Kind = ConditionalFormatKind.Expression, Operand = "=$A1>10" });
        var engine = new ConditionalFormattingEngine(session.Calculation);
        Assert.Equal("#FFFFFF", engine.Evaluate(session.Sheet, new CellAddress(0, 1), CalcValue.Blank).Style.Background);
        Assert.Equal("#FFC7CE", engine.Evaluate(session.Sheet, new CellAddress(1, 1), CalcValue.Blank).Style.Background);
        session.SetInput("1", new CellAddress(1, 0));
        Assert.Equal("#FFFFFF", engine.Evaluate(session.Sheet, new CellAddress(1, 1), CalcValue.Blank).Style.Background);
    }

    [Theory]
    [InlineData(ConditionalFormatKind.Top, 10, false)]
    [InlineData(ConditionalFormatKind.Top, 40, true)]
    [InlineData(ConditionalFormatKind.Bottom, 10, true)]
    [InlineData(ConditionalFormatKind.AboveAverage, 40, true)]
    [InlineData(ConditionalFormatKind.BelowAverage, 10, true)]
    [InlineData(ConditionalFormatKind.DuplicateValues, 30, true)]
    [InlineData(ConditionalFormatKind.UniqueValues, 40, true)]
    public void StatisticalRulesEvaluate(ConditionalFormatKind kind, int number, bool expected)
    {
        var session = Data(); session.SetConditionalFormat(new ConditionalFormatRule { Range = "B2:B5", Kind = kind, Rank = 1 });
        var engine = new ConditionalFormattingEngine(session.Calculation);
        var row = Enumerable.Range(1, 4).First(r => session.Calculation.Evaluate(session.Sheet, new CellAddress(r, 1)).Number == number);
        var result = engine.Evaluate(session.Sheet, new CellAddress(row, 1), CalcValue.Num(number));
        Assert.Equal(expected, result.Style.Background == "#FFC7CE");
    }

    [Fact]
    public void DataBarsRepresentSignedValuesAndColorScaleUsesMedian()
    {
        var session = new SpreadsheetSession(new Workbook()); session.Paste("-10\n0\n10");
        session.SetConditionalFormat(new ConditionalFormatRule { Range = "A1:A3", Kind = ConditionalFormatKind.DataBar, ShowValue = false });
        var engine = new ConditionalFormattingEngine(session.Calculation);
        var negative = engine.Evaluate(session.Sheet, new CellAddress(0, 0), CalcValue.Num(-10));
        Assert.Equal(.5, negative.DataBar!.Value.Axis); Assert.Equal(0, negative.DataBar.Value.Start); Assert.Equal(.5, negative.DataBar.Value.End); Assert.True(negative.HideValue);
        session.ClearConditionalFormats(true);
        session.SetConditionalFormat(new ConditionalFormatRule { Range = "A1:A3", Kind = ConditionalFormatKind.ColorScale });
        Assert.Equal("#FFEB84", engine.Evaluate(session.Sheet, new CellAddress(1, 0), CalcValue.Num(0)).Style.Background);
    }

    [Fact]
    public void NewMetadataRoundTripsNativeAndRejectsOversizedRules()
    {
        var session = Data(); session.SetFilter(new ColumnFilter { Column = 0, Values = ["East"] });
        session.SetConditionalFormat(new ConditionalFormatRule { Range = "B2:B6", Kind = ConditionalFormatKind.ColorScale });
        var book = Workbook.FromJson(session.Book.ToJson());
        Assert.Single(book.ActiveSheet.Filters); Assert.Single(book.ActiveSheet.ConditionalFormats); Assert.Equal(2, book.ActiveSheet.FilteredRows.Count);
        Assert.Throws<InvalidDataException>(() => session.SetConditionalFormat(new ConditionalFormatRule { Range = "A1:XFD1048576" }));
    }
}
