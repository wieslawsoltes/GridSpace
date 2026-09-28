using System.Globalization;
using GridSpace.Core;

namespace GridSpace.Formulas;

/// <summary>Converts the supported formula grammar without rewriting quoted literals or sheet names.</summary>
public static class FormulaNotation
{
    public static string ToOpenXml(string formula)
    {
        try
        {
            var expression = new FormulaParser(formula).Parse();
            return NeedsRewrite(expression) ? Write(expression, true) : formula.TrimStart('=');
        }
        catch (FormatException) { return formula.TrimStart('='); }
    }

    public static string ToNative(string formula)
    {
        try { return "=" + Write(new FormulaParser(formula).Parse(), false); }
        catch (FormatException) { return "=" + formula.TrimStart('='); }
    }

    public static bool IsSupported(string formula)
    {
        try { return Supported(new FormulaParser(formula).Parse()); }
        catch (FormatException) { return false; }
    }

    private static bool NeedsRewrite(Expr expression) => expression switch
    {
        SpillExpr => true,
        CallExpr c => Prefix(c.Name).Length > 0 || c.Arguments.Any(NeedsRewrite),
        BinaryExpr b => NeedsRewrite(b.Left) || NeedsRewrite(b.Right),
        UnaryExpr u => NeedsRewrite(u.Operand),
        _ => false
    };

    private static bool Supported(Expr expression) => expression switch
    {
        CallExpr c => (CalculationEngine.BuiltInFunctions.Contains(c.Name) || c.Name == "ANCHORARRAY") && c.Arguments.All(Supported),
        BinaryExpr b => Supported(b.Left) && Supported(b.Right),
        UnaryExpr u => Supported(u.Operand),
        ArrayExpr a => a.Items.All(Supported),
        _ => true
    };

    private static string Write(Expr expression, bool xml) => expression switch
    {
        LiteralExpr l => l.Value.Kind switch
        {
            ValueKind.Text => "\"" + l.Value.Text.Replace("\"", "\"\"") + "\"",
            ValueKind.Number => l.Value.Number.ToString("G17", CultureInfo.InvariantCulture),
            _ => l.Value.ToString()
        },
        RefExpr r => (r.Sheet is null ? "" : "'" + r.Sheet.Replace("'", "''") + "'!") + r.Address,
        RangeExpr r => Write(r.First, xml) + ":" + (r.Last.Sheet == r.First.Sheet ? r.Last.Address : Write(r.Last, xml)),
        NameExpr n => n.Name,
        SpillExpr s => xml ? "_xlfn.ANCHORARRAY(" + Write(s.Anchor, xml) + ")" : Write(s.Anchor, xml) + "#",
        ArrayExpr a => "{" + string.Join(';', Enumerable.Range(0, a.Items.Count / a.Columns).Select(r => string.Join(',', a.Items.Skip(r * a.Columns).Take(a.Columns).Select(e => Write(e, xml))))) + "}",
        UnaryExpr u => u.Operator == "%" ? "(" + Write(u.Operand, xml) + ")%" : "(" + u.Operator + Write(u.Operand, xml) + ")",
        BinaryExpr b => "(" + Write(b.Left, xml) + b.Operator + Write(b.Right, xml) + ")",
        CallExpr c when !xml && c.Name == "ANCHORARRAY" && c.Arguments is [RefExpr reference] => Write(reference, false) + "#",
        CallExpr c => (xml ? Prefix(c.Name) : "") + c.Name + "(" + string.Join(',', c.Arguments.Select(e => Write(e, xml))) + ")",
        _ => throw new FormatException("Unsupported expression.")
    };

    private static string Prefix(string name) => name switch
    {
        "FILTER" or "SORT" => "_xlfn._xlws.",
        "SEQUENCE" or "SORTBY" or "UNIQUE" or "TAKE" or "DROP" or "HSTACK" or "VSTACK" or "CHOOSECOLS" or "CHOOSEROWS" or "LET" or "ANCHORARRAY" or "XLOOKUP" => "_xlfn.",
        _ => ""
    };
}
