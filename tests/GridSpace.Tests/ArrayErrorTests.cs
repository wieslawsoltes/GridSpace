using GridSpace.Core;
using GridSpace.Formulas;
using Xunit;

namespace GridSpace.Tests;

public sealed class ArrayErrorTests
{
    [Theory]
    [InlineData("=HSTACK(#N/A,1)", "#N/A,1", 2)]
    [InlineData("=VSTACK(1,#DIV/0!)", "1,#DIV/0!", 1)]
    [InlineData("=HSTACK(#N/A,{1;2})", "#N/A,1,#N/A,2", 2)]
    [InlineData("=FILTER({1;2},{TRUE;FALSE},#N/A)", "1", 1)]
    public void ErrorElementsAndUnusedFallbackDoNotCollapseValidArrayData(string formula, string expected, int columns)
    {
        var book = new Workbook();
        var value = new CalculationEngine(book).EvaluateFormula(book.ActiveSheet, formula);
        Assert.Equal(ValueKind.Array, value.Kind);
        Assert.Equal(columns, value.Columns);
        Assert.Equal(expected, string.Join(',', value.Items!));
    }

    [Fact]
    public void EmptyFilterStillReturnsItsErrorFallback()
    {
        var book = new Workbook();
        Assert.Equal("#N/A", new CalculationEngine(book).EvaluateFormula(book.ActiveSheet, "=FILTER({1;2},{FALSE;FALSE},#N/A)").ToString());
    }
}
