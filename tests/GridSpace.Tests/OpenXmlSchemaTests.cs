using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using GridSpace.Core;
using GridSpace.Editing;
using GridSpace.IO;
using Xunit;

namespace GridSpace.Tests;

/// <summary>Independent OOXML validation complements our own reader/writer roundtrip tests.</summary>
public sealed class OpenXmlSchemaTests
{
    [Fact]
    public void ConditionalRulesFiltersAndSortsProduceSchemaValidWorkbooks()
    {
        var session = new SpreadsheetSession(new Workbook());
        session.Paste("Region\tAmount\tStatus\nEast\t3\tActive\nWest\t2\tOpen\nEast\t1\tOpen");
        session.Select("A1:C4");
        session.SetFilter(new ColumnFilter { Column = 0, Values = ["East"], IncludeBlank = true });
        session.SetFilter(new ColumnFilter { Column = 1, First = new FilterCondition(FilterOperator.GreaterThan, "0"), Second = new FilterCondition(FilterOperator.LessThan, "4"), And = true });
        session.Sort([new SortLevel(0), new SortLevel(1, true)]);
        var priority = 0;
        foreach (var kind in Enum.GetValues<ConditionalFormatKind>())
        {
            session.SetConditionalFormat(new ConditionalFormatRule
            {
                Range = "B2:B4", Kind = kind, Priority = ++priority,
                Operand = kind == ConditionalFormatKind.Expression ? "$B2>1" : kind == ConditionalFormatKind.ContainsText ? "Open" : "1",
                Style = new DifferentialStyle { Background = "#FFC7CE", Foreground = "#9C0006", Bold = true, NumberFormat = "0.00" }
            });
        }
        session.Select("C2:C4"); session.SetValidation(["Open", "Active"]);
        Validate(session.Book);
    }

    [Theory]
    [InlineData(ChartKind.Column)] [InlineData(ChartKind.Bar)] [InlineData(ChartKind.Line)] [InlineData(ChartKind.Pie)]
    public void SampleChartsAndFrozenPanesProduceSchemaValidWorkbooks(ChartKind kind)
    {
        var book = SampleWorkbook.Create(); book.ActiveSheet.Charts[0].Kind = kind;
        book.ActiveSheet.FrozenRows = 5; book.ActiveSheet.FrozenColumns = 2;
        Validate(book);
    }

    private static void Validate(Workbook workbook)
    {
        using var memory = new MemoryStream(XlsxWorkbook.Write(workbook));
        using var document = SpreadsheetDocument.Open(memory, false);
        var errors = new OpenXmlValidator(FileFormatVersions.Office2019).Validate(document)
            .Select(e => $"{e.Part?.Uri} {e.Path?.XPath}: {e.Description}").ToArray();
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors));
    }
}
