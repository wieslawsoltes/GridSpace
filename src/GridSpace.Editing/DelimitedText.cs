using System.Text;

namespace GridSpace.Editing;

/// <summary>RFC-4180-style fields with quoted separators, escaped quotes and embedded line breaks.</summary>
public static class DelimitedText
{
    public static List<string[]> Parse(string text, char separator = '\t', int maximumCells = 200_000)
    {
        var rows = new List<string[]>(); var row = new List<string>(); var field = new StringBuilder();
        var quoted = false; var count = 0;
        void Field() { row.Add(field.ToString()); field.Clear(); if (++count > maximumCells) throw new InvalidDataException("Cell import limit exceeded."); }
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '"')
            {
                if (quoted && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (quoted || field.Length == 0) quoted = !quoted;
                else field.Append(c);
            }
            else if (!quoted && c == separator) Field();
            else if (!quoted && c is '\r' or '\n')
            {
                Field(); rows.Add([.. row]); row.Clear();
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
            }
            else field.Append(c);
        }
        if (quoted) throw new InvalidDataException("Unterminated quoted field.");
        if (field.Length > 0 || row.Count > 0 || rows.Count == 0) { Field(); rows.Add([.. row]); }
        return rows;
    }
    public static string Write(IEnumerable<IEnumerable<string>> rows, char separator = '\t') => string.Join("\r\n", rows.Select(row => string.Join(separator, row.Select(v => Quote(v, separator)))));
    private static string Quote(string value, char separator) => value.IndexOfAny([separator, '"', '\r', '\n']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
}
