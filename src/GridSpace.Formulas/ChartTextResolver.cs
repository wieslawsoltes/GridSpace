using GridSpace.Core;

namespace GridSpace.Formulas;

/// <summary>Nullable entries mean use the current literal caption, not a stale cached copy of it.</summary>
public sealed record ChartTextSnapshot(string? Title, string? CategoryAxisTitle, string? ValueAxisTitle)
{
    public static ChartTextSnapshot Empty { get; } = new(null, null, null);
}

/// <summary>Resolves formula results and number formats without changing source cells or chart metadata.</summary>
public static class ChartTextResolver
{
    public static string Resolve(Workbook book, ChartTextReference reference, CalculationEngine calculation)
    {
        ArgumentNullException.ThrowIfNull(reference);
        reference.Validate();
        if (reference.IsBroken || book.FindSheet(reference.Sheet) is not { } sheet) return "#REF!";
        var address = CellAddress.Parse(reference.Cell);
        return NumberFormatter.Format(calculation.Evaluate(sheet, address), sheet.Get(address).Style.NumberFormat);
    }

    public static ChartTextSnapshot Resolve(Workbook book, ChartSpec chart, CalculationEngine calculation) =>
        chart.TitleReference is null && chart.CategoryAxisTitleReference is null && chart.ValueAxisTitleReference is null
            ? ChartTextSnapshot.Empty
            : new(chart.TitleReference is { } title ? Resolve(book, title, calculation) : null,
                chart.CategoryAxisTitleReference is { } category ? Resolve(book, category, calculation) : null,
                chart.ValueAxisTitleReference is { } value ? Resolve(book, value, calculation) : null);
}
