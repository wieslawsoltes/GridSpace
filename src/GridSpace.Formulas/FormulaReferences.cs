using System.Text;
using System.Text.RegularExpressions;
using GridSpace.Core;

namespace GridSpace.Formulas;

public static class FormulaReferences
{
    private static readonly Regex Reference = new(@"(?<![\w.])(?:(?<sheet>'(?:[^']|'')+'|[A-Za-z_][A-Za-z0-9_.]*)!)?(?<col>\$?[A-Za-z]{1,3})(?<row>\$?[1-9][0-9]*)(?![\w.(])", RegexOptions.Compiled, TimeSpan.FromMilliseconds(200));
    public static string Translate(string formula, int rows, int columns) => Rewrite(formula, match =>
    {
        var c = match.Groups["col"].Value; var r = match.Groups["row"].Value;
        if (!CellAddress.TryParse(c + r, out var a)) return match.Value;
        var target = new CellAddress(a.Row + (r.StartsWith('$') ? 0 : rows), a.Column + (c.StartsWith('$') ? 0 : columns));
        return Render(match, target);
    });
    public static string RenameSheet(string formula, string oldName, string newName) => Rewrite(formula, match =>
    {
        var sheet = Unquote(match.Groups["sheet"].Value);
        return sheet.Equals(oldName, StringComparison.OrdinalIgnoreCase) ? "'" + newName.Replace("'", "''") + "'!" + match.Groups["col"].Value + match.Groups["row"].Value : match.Value;
    });
    /// <summary>Rebases references for a row/column insertion, including absolute references and range endpoints.</summary>
    public static string Insert(string formula, string formulaSheet, string editedSheet, bool rows, int position, int count)
    {
        var lastSheet = formulaSheet; var lastEnd = -1;
        return Rewrite(formula, match =>
        {
            var explicitSheet = match.Groups["sheet"].Value;
            // A qualified A1:B2 range inherits the sheet on its first endpoint.
            var isRangeEnd = match.Index > 0 && match.Index <= formula.Length && formula[match.Index - 1] == ':' && lastEnd >= 0;
            var sheet = explicitSheet.Length > 0 ? Unquote(explicitSheet) : isRangeEnd ? lastSheet : formulaSheet;
            lastSheet = sheet; lastEnd = match.Index + match.Length;
            if (!sheet.Equals(editedSheet, StringComparison.OrdinalIgnoreCase)) return match.Value;
            if (!CellAddress.TryParse(match.Groups["col"].Value + match.Groups["row"].Value, out var address)) return match.Value;
            var target = rows ? address with { Row = address.Row >= position ? address.Row + count : address.Row } : address with { Column = address.Column >= position ? address.Column + count : address.Column };
            return Render(match, target);
        });
    }
    private static string Render(Match match, CellAddress target)
    {
        if (!target.IsValid) return "#REF!";
        var sheet = match.Groups["sheet"].Success ? match.Groups["sheet"].Value + "!" : "";
        return sheet + (match.Groups["col"].Value.StartsWith('$') ? "$" : "") + CellAddress.ColumnName(target.Column) + (match.Groups["row"].Value.StartsWith('$') ? "$" : "") + (target.Row + 1);
    }
    private static string Unquote(string name) => name.StartsWith('\'') && name.EndsWith('\'') ? name[1..^1].Replace("''", "'") : name;
    private static string Rewrite(string formula, MatchEvaluator evaluator)
    {
        if (!formula.StartsWith('=')) return formula;
        // Replace inside the original full string, skipping matches covered by double-quoted literals.
        var quoted = new bool[formula.Length]; var inside = false;
        for (var i = 0; i < formula.Length; i++)
        {
            if (formula[i] == '"')
            {
                quoted[i] = true;
                if (inside && i + 1 < formula.Length && formula[i + 1] == '"') { quoted[++i] = true; continue; }
                inside = !inside;
            }
            else quoted[i] = inside;
        }
        return Reference.Replace(formula, match => quoted[match.Index] ? match.Value : evaluator(match));
    }
}
