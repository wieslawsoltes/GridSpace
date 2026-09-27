using System.Globalization;
using System.Text;
using GridSpace.Core;

namespace GridSpace.Formulas;

internal abstract record Expr;
internal sealed record LiteralExpr(CalcValue Value) : Expr;
internal sealed record RefExpr(string? Sheet, string Address) : Expr;
internal sealed record RangeExpr(RefExpr First, RefExpr Last) : Expr;
internal sealed record NameExpr(string Name) : Expr;
internal sealed record UnaryExpr(string Operator, Expr Operand) : Expr;
internal sealed record BinaryExpr(string Operator, Expr Left, Expr Right) : Expr;
internal sealed record CallExpr(string Name, IReadOnlyList<Expr> Arguments) : Expr;

internal sealed class FormulaParser
{
    private readonly record struct Token(string Kind, string Text);
    private readonly List<Token> _tokens = [];
    private int _index, _depth;
    private Token Current => _tokens[_index];
    public FormulaParser(string formula)
    {
        var i = formula.StartsWith('=') ? 1 : 0;
        while (i < formula.Length)
        {
            var ch = formula[i];
            if (char.IsWhiteSpace(ch)) { i++; continue; }
            if (_tokens.Count > 4096) throw new FormatException("Formula token limit exceeded.");
            if (ch is '"' or '\'')
            {
                var quote = ch; i++; var value = new StringBuilder(); var closed = false;
                while (i < formula.Length)
                {
                    ch = formula[i++];
                    if (ch != quote) { value.Append(ch); continue; }
                    if (i < formula.Length && formula[i] == quote) { value.Append(quote); i++; continue; }
                    closed = true; break;
                }
                if (!closed) throw new FormatException("Unterminated string.");
                _tokens.Add(new(quote == '"' ? "string" : "id", value.ToString())); continue;
            }
            if (char.IsAsciiDigit(ch) || ch == '.' && i + 1 < formula.Length && char.IsAsciiDigit(formula[i + 1]))
            {
                var start = i++;
                while (i < formula.Length && (char.IsAsciiDigit(formula[i]) || formula[i] == '.')) i++;
                if (i < formula.Length && formula[i] is 'e' or 'E')
                {
                    i++; if (i < formula.Length && formula[i] is '+' or '-') i++;
                    while (i < formula.Length && char.IsAsciiDigit(formula[i])) i++;
                }
                _tokens.Add(new("number", formula[start..i])); continue;
            }
            if (char.IsLetter(ch) || ch is '_' or '$')
            {
                var start = i++;
                while (i < formula.Length && (char.IsLetterOrDigit(formula[i]) || formula[i] is '_' or '$' or '.')) i++;
                _tokens.Add(new("id", formula[start..i])); continue;
            }
            if (ch == '#')
            {
                var start = i++;
                while (i < formula.Length && (char.IsAsciiLetterOrDigit(formula[i]) || formula[i] is '/' or '!' or '?' or '_')) i++;
                _tokens.Add(new("error", formula[start..i])); continue;
            }
            var op = ch.ToString(); i++;
            if (i < formula.Length && (ch == '<' && formula[i] is '>' or '=' || ch == '>' && formula[i] == '=')) op += formula[i++];
            _tokens.Add(new(op, op));
        }
        _tokens.Add(new("eof", ""));
    }
    public Expr Parse()
    {
        var expression = Expression(0);
        if (Current.Kind != "eof") throw new FormatException("Unexpected formula token.");
        return expression;
    }
    private bool Take(string token) { if (Current.Kind != token) return false; _index++; return true; }
    private void Require(string token) { if (!Take(token)) throw new FormatException($"Expected {token}."); }
    private Expr Expression(int minimum)
    {
        if (++_depth > 128) throw new FormatException("Formula nesting limit exceeded.");
        Expr left;
        if (Take("+")) left = new UnaryExpr("+", Expression(6));
        else if (Take("-")) left = new UnaryExpr("-", Expression(6));
        else if (Take("(")) { left = Expression(0); Require(")"); }
        else if (Current.Kind == "number") { left = new LiteralExpr(CalcValue.Num(double.Parse(Current.Text, CultureInfo.InvariantCulture))); _index++; }
        else if (Current.Kind == "string") { left = new LiteralExpr(CalcValue.Str(Current.Text)); _index++; }
        else if (Current.Kind == "error") { left = new LiteralExpr(CalcValue.Error(Current.Text)); _index++; }
        else if (Current.Kind == "id")
        {
            var identifier = Current.Text; _index++;
            if (Take("("))
            {
                var arguments = new List<Expr>();
                if (!Take(")"))
                {
                    do { arguments.Add(Current.Kind is "," or ";" or ")" ? new LiteralExpr(CalcValue.Blank) : Expression(0)); }
                    while (Take(",") || Take(";"));
                    Require(")");
                }
                left = new CallExpr(identifier.ToUpperInvariant(), arguments);
            }
            else if (Take("!"))
            {
                if (Current.Kind != "id" || !CellAddress.TryParse(Current.Text, out _)) throw new FormatException("Expected a cell reference.");
                left = new RefExpr(identifier, Current.Text); _index++;
            }
            else if (CellAddress.TryParse(identifier, out _)) left = new RefExpr(null, identifier);
            else if (identifier.Equals("TRUE", StringComparison.OrdinalIgnoreCase)) left = new LiteralExpr(CalcValue.Bool(true));
            else if (identifier.Equals("FALSE", StringComparison.OrdinalIgnoreCase)) left = new LiteralExpr(CalcValue.Bool(false));
            else left = new NameExpr(identifier);
            if (left is RefExpr first && Take(":"))
            {
                var lastText = Current.Text; _index++;
                string? lastSheet = first.Sheet;
                if (Take("!")) { lastSheet = lastText; lastText = Current.Text; _index++; }
                if (!CellAddress.TryParse(lastText, out _)) throw new FormatException("Invalid range end.");
                left = new RangeExpr(first, new(lastSheet, lastText));
            }
        }
        else throw new FormatException("Expected a value.");
        while (true)
        {
            if (Take("%")) { left = new UnaryExpr("%", left); continue; }
            var op = Current.Kind;
            var precedence = op switch { "=" or "<>" or "<" or ">" or "<=" or ">=" => 1, "&" => 2, "+" or "-" => 3, "*" or "/" => 4, "^" => 5, _ => -1 };
            if (precedence < minimum) break;
            _index++;
            left = new BinaryExpr(op, left, Expression(op == "^" ? precedence : precedence + 1));
        }
        _depth--; return left;
    }
}
