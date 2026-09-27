using System.Text;
using GridSpace.Core;
using GridSpace.Editing;
using GridSpace.Formulas;

namespace GridSpace.IO;

public sealed record ImportResult(Workbook Workbook, IReadOnlyList<string> Warnings);
public static class WorkbookFiles
{
    public const int MaximumFileBytes = 32 * 1024 * 1024;
    public static ImportResult Import(string name, byte[] bytes)
    {
        if (bytes.Length > MaximumFileBytes) throw new InvalidDataException("The file exceeds the 32 MB import limit.");
        var extension = Path.GetExtension(name).ToLowerInvariant();
        if (extension == ".xlsx") return XlsxWorkbook.Read(bytes);
        var text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
        if (extension is ".gridspace" or ".json") return new(Workbook.FromJson(text), []);
        if (extension is not ".csv" and not ".tsv" and not ".txt") throw new InvalidDataException("Open .xlsx, .csv, .tsv or .gridspace files. Legacy .xls and macro-enabled files are not supported.");
        var separator = extension == ".tsv" || text.Split('\n')[0].Contains('\t') ? '\t' : ',';
        var rows = DelimitedText.Parse(text, separator); var book = new Workbook { Title = Path.GetFileNameWithoutExtension(name) };
        for (var r = 0; r < rows.Count; r++) for (var c = 0; c < rows[r].Length; c++)
        {
            // Imported CSV is data, not trusted executable spreadsheet formulas.
            var value = rows[r][c];
            if (value.StartsWith('=') || value.StartsWith('@')) value = "'" + value;
            book.ActiveSheet.Set(new(r, c), new() { Input = value });
        }
        return new(book, ["CSV/TSV contains values only. Formula-looking imported text is treated as literal data."]);
    }
    public static byte[] Native(Workbook book) => Encoding.UTF8.GetBytes(book.ToJson());
    public static byte[] Csv(Workbook book, char separator = ',')
    {
        var sheet = book.ActiveSheet; var range = sheet.UsedRange;
        if (range.Count > 200_000) throw new InvalidOperationException("CSV export is limited to a 200,000-cell used rectangle.");
        var engine = new CalculationEngine(book);
        var rows = Enumerable.Range(0, range.Bottom + 1).Select(r => Enumerable.Range(0, range.Right + 1).Select(c =>
        {
            var value = engine.Evaluate(sheet, new(r, c)); var text = value.ToString();
            return value.Kind == ValueKind.Text && text.Length > 0 && "=+-@\t\r".Contains(text[0]) ? "'" + text : text;
        }));
        return Encoding.UTF8.GetBytes(DelimitedText.Write(rows, separator));
    }
}
