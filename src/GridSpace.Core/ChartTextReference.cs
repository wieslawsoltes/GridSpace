using System.Text.Json.Serialization;

namespace GridSpace.Core;

/// <summary>
/// A single, explicitly named worksheet cell used by chart text. Unlike a display
/// caption, a broken reference stays broken if a sheet or cell is recreated later.
/// </summary>
public sealed record ChartTextReference(string Sheet, string Cell)
{
    [JsonIgnore] public bool IsBroken => Cell == "#REF!";

    public void Validate()
    {
        Workbook.ValidateSheetName(Sheet);
        if (!IsBroken && !TryAddress(Cell, out _))
            throw new ArgumentException("A chart text link must reference one worksheet cell.");
    }

    /// <summary>Parse A1, $A$1, Sheet!A1 or ='Quoted Sheet'!$A$1. Expressions/external books are not executed.</summary>
    public static ChartTextReference Parse(string text, string defaultSheet)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > 128) throw new ArgumentException("Chart text references are limited to 128 characters.");
        var input = text.Trim();
        if (input.StartsWith('=')) input = input[1..].Trim();
        var bang = input.LastIndexOf('!');
        var sheet = defaultSheet;
        if (bang >= 0)
        {
            var token = input[..bang];
            if (token.StartsWith('\''))
            {
                if (token.Length < 2 || !token.EndsWith('\'')) throw new FormatException("Unclosed worksheet name.");
                token = token[1..^1];
                // A quote inside a quoted name must be escaped, never silently removed.
                for (var i = 0; i < token.Length; i++)
                    if (token[i] == '\'' && (++i >= token.Length || token[i] != '\''))
                        throw new FormatException("Escape a quote in a worksheet name by doubling it.");
                sheet = token.Replace("''", "'");
            }
            else
            {
                if (token.Contains('\'')) throw new FormatException("Quote this worksheet name.");
                sheet = token;
            }
            input = input[(bang + 1)..];
        }
        if (!TryAddress(input, out var address)) throw new FormatException("Select a single cell, such as 'Sales'!$B$1.");
        var result = new ChartTextReference(sheet, address.ToString());
        result.Validate();
        return result;
    }

    public string ToFormula()
    {
        Validate();
        if (IsBroken) return "#REF!";
        var address = CellAddress.Parse(Cell);
        return "'" + Sheet.Replace("'", "''") + "'!$" + CellAddress.ColumnName(address.Column) + "$" + (address.Row + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public ChartTextReference RenameSheet(string oldName, string newName) =>
        !IsBroken && Sheet.Equals(oldName, StringComparison.OrdinalIgnoreCase) ? this with { Sheet = newName } : this;

    public ChartTextReference DeleteSheet(string name) =>
        !IsBroken && Sheet.Equals(name, StringComparison.OrdinalIgnoreCase) ? this with { Cell = "#REF!" } : this;

    public ChartTextReference Rebase(string editedSheet, AxisEdit edit)
    {
        if (IsBroken || !Sheet.Equals(editedSheet, StringComparison.OrdinalIgnoreCase)) return this;
        var mapped = edit.Map(CellAddress.Parse(Cell));
        if (mapped is null && !edit.IsDeletion)
            throw new InvalidOperationException("The edit would push a chart text reference outside the worksheet.");
        return this with { Cell = mapped?.ToString() ?? "#REF!" };
    }

    private static bool TryAddress(string? text, out CellAddress address)
    {
        address = default;
        if (string.IsNullOrEmpty(text) || text.Length > 12) return false;
        var i = text[0] == '$' ? 1 : 0;
        var letters = i;
        while (i < text.Length && char.IsAsciiLetter(text[i])) i++;
        if (i == letters || i - letters > 3) return false;
        if (i < text.Length && text[i] == '$') i++;
        var digits = i;
        while (i < text.Length && char.IsAsciiDigit(text[i])) i++;
        return i == text.Length && i > digits && CellAddress.TryParse(text, out address);
    }
}

/// <summary>Shared traversal for structural edits, renames and worksheet duplication.</summary>
public static class ChartTextLinks
{
    public static void Transform(ChartSpec chart, Func<ChartTextReference, ChartTextReference> map)
    {
        if (chart.TitleReference is { } title) chart.TitleReference = map(title);
        if (chart.CategoryAxisTitleReference is { } category) chart.CategoryAxisTitleReference = map(category);
        if (chart.ValueAxisTitleReference is { } value) chart.ValueAxisTitleReference = map(value);
        for (var i = 0; i < chart.Series.Count; i++)
            if (chart.Series[i].NameReference is { } name)
                chart.Series[i] = chart.Series[i] with { NameReference = map(name) };
    }
}
