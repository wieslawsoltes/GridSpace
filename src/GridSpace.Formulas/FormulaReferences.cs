using System.Text;
using System.Text.RegularExpressions;
using GridSpace.Core;

namespace GridSpace.Formulas;

public static class FormulaReferences
{
    private static readonly Regex Reference = new(@"(?<![\w.])(?:(?<sheet>'(?:[^']|'')+'|[A-Za-z_][A-Za-z0-9_.]*)!)?(?<col>\$?[A-Za-z]{1,3})(?<row>\$?[1-9][0-9]*)(?![\w.(])", RegexOptions.Compiled, TimeSpan.FromMilliseconds(200));
    /// <summary>Translates relative A1 references while leaving quoted string literals untouched.</summary>
    public static string Translate(string formula, int rows, int columns) => Rewrite(formula, match =>
    {
        var c = match.Groups["col"].Value; var r = match.Groups["row"].Value;
        if (!CellAddress.TryParse(c + r, out var a)) return match.Value;
        var target = new CellAddress(a.Row + (r.StartsWith('$') ? 0 : rows), a.Column + (c.StartsWith('$') ? 0 : columns));
        if (!target.IsValid) return "#REF!";
        var sheet = match.Groups["sheet"].Success ? match.Groups["sheet"].Value + "!" : "";
        return sheet + (c.StartsWith('$') ? "$" : "") + CellAddress.ColumnName(target.Column) + (r.StartsWith('$') ? "$" : "") + (target.Row + 1);
    });
    public static string RenameSheet(string formula, string oldName, string newName) => Rewrite(formula, match =>
    {
        var sheet = match.Groups["sheet"].Value;
        if (sheet.StartsWith('\'')) sheet = sheet[1..^1].Replace("''", "'");
        return sheet.Equals(oldName, StringComparison.OrdinalIgnoreCase) ? "'" + newName.Replace("'", "''") + "'!" + match.Groups["col"].Value + match.Groups["row"].Value : match.Value;
    });
    private static string Rewrite(string formula, MatchEvaluator evaluator)
    {
        if (!formula.StartsWith('=')) return formula;
        var result = new StringBuilder(); var start = 0; var quoted = false;
        for (var i = 0; i < formula.Length; i++)
        {
            if (formula[i] != '"') continue;
            if (quoted && i + 1 < formula.Length && formula[i + 1] == '"') { i++; continue; }
            result.Append(quoted ? formula[start..(i + 1)] : Reference.Replace(formula[start..i], evaluator));
            start = i + 1; quoted = !quoted;
        }
        result.Append(quoted ? formula[start..] : Reference.Replace(formula[start..], evaluator));
        return result.ToString();
    }
}
