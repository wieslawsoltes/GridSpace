using System.Text.RegularExpressions;
using GridSpace.Core;

namespace GridSpace.Formulas;

public static class FormulaReferences
{
    private const string SheetPattern = "'(?:[^']|'')+'|[A-Za-z_][A-Za-z0-9_.]*";
    private static readonly Regex Reference = new(
        @"(?<![\w.\]])(?:(?<sheet>" + SheetPattern + @")!)?(?<col>\$?[A-Za-z]{1,3})(?<row>\$?[1-9][0-9]*)(?:\s*:\s*(?:(?<sheet2>" + SheetPattern + @")!)?(?<col2>\$?[A-Za-z]{1,3})(?<row2>\$?[1-9][0-9]*))?(?![\w.(])(?<spill>#)?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

    public static string Translate(string formula, int rows, int columns) => Rewrite(formula, match =>
    {
        string Shift(string suffix)
        {
            var c = match.Groups["col" + suffix].Value;
            var r = match.Groups["row" + suffix].Value;
            if (!CellAddress.TryParse(c + r, out var address)) return Part(match, suffix);
            var target = new CellAddress(address.Row + (r.StartsWith('$') ? 0 : rows), address.Column + (c.StartsWith('$') ? 0 : columns));
            return Render(match, suffix, target);
        }
        var first = Shift("");
        if (!match.Groups["col2"].Success) return first;
        var second = Shift("2");
        return first == "#REF!" || second == "#REF!" ? "#REF!" : first + ":" + second;
    });

    public static string RenameSheet(string formula, string oldName, string newName) => Rewrite(formula, match =>
    {
        string Rename(string suffix)
        {
            var sheet = Unquote(match.Groups["sheet" + suffix].Value);
            return sheet.Equals(oldName, StringComparison.OrdinalIgnoreCase)
                ? "'" + newName.Replace("'", "''") + "'!" + match.Groups["col" + suffix].Value + match.Groups["row" + suffix].Value
                : Part(match, suffix);
        }
        return Rename("") + (match.Groups["col2"].Success ? ":" + Rename("2") : "");
    });

    public static string DeleteSheet(string formula, string deletedName) => Rewrite(formula, match =>
        new[] { "sheet", "sheet2" }.Any(g => Unquote(match.Groups[g].Value).Equals(deletedName, StringComparison.OrdinalIgnoreCase)) ? "#REF!" : match.Value);

    public static string Insert(string formula, string formulaSheet, string editedSheet, bool rows, int position, int count) =>
        Edit(formula, formulaSheet, editedSheet, new AxisEdit(rows, position, count, false));

    public static string Delete(string formula, string formulaSheet, string editedSheet, bool rows, int position, int count) =>
        Edit(formula, formulaSheet, editedSheet, new AxisEdit(rows, position, count, true));

    /// <summary>Transforms a whole A1 range as an interval, not two unrelated endpoints. Quoted literals remain untouched.</summary>
    public static string Edit(string formula, string formulaSheet, string editedSheet, AxisEdit edit) => Rewrite(formula, match =>
    {
        if (!CellAddress.TryParse(match.Groups["col"].Value + match.Groups["row"].Value, out var first)) return match.Value;
        var firstSheet = match.Groups["sheet"].Success ? Unquote(match.Groups["sheet"].Value) : formulaSheet;
        var secondSheet = match.Groups["sheet2"].Success ? Unquote(match.Groups["sheet2"].Value) : firstSheet;
        var hasRange = match.Groups["col2"].Success;
        var second = first;
        if (hasRange && !CellAddress.TryParse(match.Groups["col2"].Value + match.Groups["row2"].Value, out second)) return match.Value;
        if (firstSheet.Equals(editedSheet, StringComparison.OrdinalIgnoreCase) && secondSheet.Equals(firstSheet, StringComparison.OrdinalIgnoreCase))
        {
            var result = edit.Map(new CellRange(first, second));
            if (result is null) return "#REF!";
            return Render(match, "", result.Value.Start) + (hasRange ? ":" + Render(match, "2", result.Value.End) : "");
        }
        // Explicitly qualified endpoints on different sheets are not collapsed into a local range.
        var a = firstSheet.Equals(editedSheet, StringComparison.OrdinalIgnoreCase) ? edit.Map(first) : first;
        var b = secondSheet.Equals(editedSheet, StringComparison.OrdinalIgnoreCase) ? edit.Map(second) : second;
        if (a is null || hasRange && b is null) return "#REF!";
        return Render(match, "", a.Value) + (hasRange ? ":" + Render(match, "2", b!.Value) : "");
    });

    private static string Part(Match match, string suffix) =>
        (match.Groups["sheet" + suffix].Success ? match.Groups["sheet" + suffix].Value + "!" : "") + match.Groups["col" + suffix].Value + match.Groups["row" + suffix].Value;

    private static string Render(Match match, string suffix, CellAddress target)
    {
        if (!target.IsValid) return "#REF!";
        var sheet = match.Groups["sheet" + suffix].Success ? match.Groups["sheet" + suffix].Value + "!" : "";
        return sheet + (match.Groups["col" + suffix].Value.StartsWith('$') ? "$" : "") + CellAddress.ColumnName(target.Column)
            + (match.Groups["row" + suffix].Value.StartsWith('$') ? "$" : "") + (target.Row + 1);
    }

    private static string Unquote(string name) => name.StartsWith('\'') && name.EndsWith('\'') ? name[1..^1].Replace("''", "'") : name;

    private static string Rewrite(string formula, MatchEvaluator evaluator)
    {
        if (!formula.StartsWith('=')) return formula;
        var quoted = new bool[formula.Length];
        var inside = false;
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
        return Reference.Replace(formula, match =>
        {
            if (quoted[match.Index]) return match.Value;
            var replacement = evaluator(match);
            // The spill suffix belongs to the reference, not to a resulting error literal.
            // Keep it when moving/renaming a valid anchor; discard it when that anchor is deleted.
            return replacement == "#REF!" || !match.Groups["spill"].Success || replacement.EndsWith('#')
                ? replacement : replacement + "#";
        });
    }
}
