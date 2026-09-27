using System.Text.Json;
using System.Text.Json.Serialization;

namespace GridSpace.Core;

public sealed class Workbook
{
    public int SchemaVersion { get; set; } = 1;
    public string Title { get; set; } = "Book1";
    public List<Worksheet> Sheets { get; set; } = [new()];
    public Dictionary<string, string> Names { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int ActiveSheetIndex { get; set; }
    [JsonIgnore] public long Revision { get; private set; }
    [JsonIgnore] public Worksheet ActiveSheet => Sheets[Math.Clamp(ActiveSheetIndex, 0, Sheets.Count - 1)];
    public Workbook() => Attach();
    public void Touch() => Revision++;
    public Worksheet? FindSheet(string name) => Sheets.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    public void Attach()
    {
        if (Sheets.Count == 0) Sheets.Add(new());
        if (Sheets.Count > 256) throw new InvalidDataException("A maximum of 256 sheets is supported.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sheet in Sheets)
        {
            ValidateSheetName(sheet.Name);
            if (!names.Add(sheet.Name)) throw new InvalidDataException("Worksheet names must be unique.");
            if (sheet.Cells.Count > 200_000) throw new InvalidDataException("The current per-sheet import limit is 200,000 stored cells.");
            foreach (var key in sheet.Cells.Keys) _ = CellAddress.Parse(key);
            sheet.ValidateMetadata();
            sheet.Cells = new(sheet.Cells, StringComparer.OrdinalIgnoreCase);
            sheet.Changed = Touch;
        }
        Names = new(Names, StringComparer.OrdinalIgnoreCase);
        ActiveSheetIndex = Math.Clamp(ActiveSheetIndex, 0, Sheets.Count - 1);
        Touch();
    }
    public static void ValidateSheetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 31 || name.IndexOfAny(['[', ']', ':', '*', '?', '/', '\\']) >= 0 || name.StartsWith('\'') || name.EndsWith('\''))
            throw new ArgumentException("Sheet names must contain 1–31 characters and cannot contain []:*?/\\ or start/end with an apostrophe.");
    }
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = false, MaxDepth = 64 };
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);
    public static Workbook FromJson(string json)
    {
        if (json.Length > 32 * 1024 * 1024) throw new InvalidDataException("Workbook exceeds the 32 MB native-file limit.");
        var book = JsonSerializer.Deserialize<Workbook>(json, JsonOptions) ?? throw new InvalidDataException("Empty workbook.");
        if (book.SchemaVersion != 1) throw new InvalidDataException("Unsupported GridSpace file version.");
        book.Attach();
        return book;
    }
}
