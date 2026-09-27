using System.Globalization;
using GridSpace.Core;
using GridSpace.Formulas;

namespace GridSpace.Editing;

/// <summary>The editing boundary shared by native, browser and headless clients.</summary>
public sealed partial class SpreadsheetSession
{
    private sealed record HistoryEntry(string Name, string Before, string After, CellRange Selection);
    private readonly List<HistoryEntry> _undo = [];
    private readonly Stack<HistoryEntry> _redo = [];
    private bool _inTransaction;
    public Workbook Book { get; private set; }
    public CalculationEngine Calculation { get; private set; }
    public Worksheet Sheet => Book.ActiveSheet;
    public CellRange Selection { get; private set; } = new(new(0, 0), new(0, 0));
    public CellAddress ActiveCell => Sheet.MergeAnchor(Selection.Start);
    public CellStyle SelectedStyle => Sheet.Get(ActiveCell).Style;
    public bool IsDirty { get; private set; }
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string UndoName => _undo.LastOrDefault()?.Name ?? "";
    public ClipboardBlock? Clipboard { get; private set; }
    public event EventHandler<SessionChangedEventArgs>? Changed;
    public SpreadsheetSession(Workbook book) { Book = book; Book.Attach(); Calculation = new(book); }
    public void Notify(string reason = "Selection", bool documentChanged = false) => Changed?.Invoke(this, new(reason, documentChanged));
    public void MarkSaved() { IsDirty = false; Notify("Saved"); }
    public void Select(CellRange range)
    {
        if (!range.Start.IsValid || !range.End.IsValid) throw new ArgumentOutOfRangeException(nameof(range));
        Selection = range; Notify();
    }
    public void Select(string address) => Select(CellRange.Parse(address));
    public void Move(int rows, int columns, bool extend = false)
    {
        var from = extend ? Selection.End : ActiveCell;
        var to = new CellAddress(Math.Clamp(from.Row + rows, 0, CellAddress.MaxRows - 1), Math.Clamp(from.Column + columns, 0, CellAddress.MaxColumns - 1));
        Select(new(extend ? Selection.Start : to, to));
    }
    public void Perform(string name, Action action)
    {
        if (_inTransaction) { action(); return; }
        var before = Book.ToJson(); var selection = Selection; _inTransaction = true;
        try
        {
            action(); Book.Attach(); var after = Book.ToJson();
            if (before == after) return;
            _undo.Add(new(name, before, after, selection)); _redo.Clear();
            while (_undo.Count > 40 || _undo.Count > 1 && _undo.Sum(e => (long)e.Before.Length + e.After.Length) > 32 * 1024 * 1024) _undo.RemoveAt(0);
            IsDirty = true;
        }
        catch { Restore(before); Selection = selection; throw; }
        finally { _inTransaction = false; Notify(name, true); }
    }
    private void Restore(string json) { Book = Workbook.FromJson(json); Calculation = new(Book); }
    public void Undo()
    {
        if (!CanUndo) return;
        var entry = _undo[^1]; _undo.RemoveAt(_undo.Count - 1); _redo.Push(entry);
        Restore(entry.Before); Selection = entry.Selection; IsDirty = true; Notify("Undo " + entry.Name, true);
    }
    public void Redo()
    {
        if (!CanRedo) return;
        var entry = _redo.Pop(); _undo.Add(entry); Restore(entry.After); Selection = entry.Selection; IsDirty = true; Notify("Redo " + entry.Name, true);
    }
    public void Load(Workbook book)
    {
        book.Attach(); Book = book; Calculation = new(book); _undo.Clear(); _redo.Clear(); Clipboard = null;
        Selection = new(new(0, 0), new(0, 0)); IsDirty = false; Notify("Open workbook", true);
    }
    public void SetInput(string input, CellAddress? target = null)
    {
        var address = Sheet.MergeAnchor(target ?? ActiveCell);
        foreach (var (range, values) in Sheet.ValidationLists)
            if (CellRange.Parse(range).Contains(address) && input != "" && !values.Contains(input, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException("Choose a permitted value: " + string.Join(", ", values));
        Perform("Edit " + address, () => Sheet.Set(address, Sheet.Get(address) with { Input = input }));
    }
    public void ApplyStyle(Func<CellStyle, CellStyle> transform) => Perform("Format cells", () =>
    {
        foreach (var address in Selection.Cells()) Sheet.Set(address, Sheet.Get(address) with { Style = transform(Sheet.Get(address).Style) });
    });
    public void Clear(bool formats = false) => Perform(formats ? "Clear all" : "Clear contents", () =>
    {
        foreach (var key in Sheet.Cells.Keys.Where(k => Selection.Contains(CellAddress.Parse(k))).ToArray())
        {
            var a = CellAddress.Parse(key); Sheet.Set(a, formats ? new() : Sheet.Get(a) with { Input = "" });
        }
    });
    public ClipboardBlock Copy()
    {
        var range = Selection.Normalized;
        var cells = range.Cells().Select(Sheet.Get).ToArray();
        var text = DelimitedText.Write(Enumerable.Range(range.Top, range.Bottom - range.Top + 1).Select(r => Enumerable.Range(range.Left, range.Right - range.Left + 1).Select(c => Calculation.Evaluate(Sheet, new(r, c)).ToString())));
        Clipboard = new(Sheet.Name, range, cells, text); Notify("Copied"); return Clipboard;
    }
    public void Paste(string text, bool valuesOnly = false)
    {
        var target = ActiveCell;
        if (!valuesOnly && Clipboard is { } block && block.Text == text)
        {
            var width = block.Source.Right - block.Source.Left + 1; var height = block.Source.Bottom - block.Source.Top + 1;
            CheckExtent(target, height, width);
            Perform("Paste", () =>
            {
                for (var r = 0; r < height; r++) for (var c = 0; c < width; c++)
                {
                    var cell = block.Cells[r * width + c];
                    Sheet.Set(new(target.Row + r, target.Column + c), cell with { Input = FormulaReferences.Translate(cell.Input, target.Row - block.Source.Top, target.Column - block.Source.Left) });
                }
            });
            Select(new(target, new(target.Row + height - 1, target.Column + width - 1))); return;
        }
        var rows = DelimitedText.Parse(text); var cols = rows.Max(r => r.Length); CheckExtent(target, rows.Count, cols);
        Perform("Paste text", () => { for (var r = 0; r < rows.Count; r++) for (var c = 0; c < rows[r].Length; c++)
        {
            var address = new CellAddress(target.Row + r, target.Column + c); Sheet.Set(address, Sheet.Get(address) with { Input = rows[r][c] });
        } });
        Select(new(target, new(target.Row + rows.Count - 1, target.Column + cols - 1)));
    }
    private static void CheckExtent(CellAddress target, int rows, int columns)
    {
        if ((long)rows * columns > 100_000 || target.Row + rows > CellAddress.MaxRows || target.Column + columns > CellAddress.MaxColumns)
            throw new InvalidOperationException("Paste exceeds the worksheet or the 100,000-cell operation limit.");
    }
    public void Fill(CellRange source, CellRange destination, bool series = true)
    {
        var original = source.Cells().ToDictionary(a => a, Sheet.Get);
        _ = destination.Cells().Count();
        Perform("Fill cells", () =>
        {
            var height = source.Bottom - source.Top + 1; var width = source.Right - source.Left + 1;
            var first = Calculation.Evaluate(Sheet, new(source.Top, source.Left));
            var next = Calculation.Evaluate(Sheet, new(Math.Min(source.Top + 1, source.Bottom), source.Left));
            var useSeries = series && width == 1 && height == 2 && first.Kind == ValueKind.Number && next.Kind == ValueKind.Number;
            foreach (var a in destination.Cells())
            {
                if (source.Contains(a)) continue;
                var origin = new CellAddress(source.Top + ((a.Row - source.Top) % height + height) % height, source.Left + ((a.Column - source.Left) % width + width) % width);
                var cell = original[origin];
                var input = useSeries ? (first.Number + (a.Row - source.Top) * (next.Number - first.Number)).ToString("G15", CultureInfo.InvariantCulture) : FormulaReferences.Translate(cell.Input, a.Row - origin.Row, a.Column - origin.Column);
                Sheet.Set(a, cell with { Input = input });
            }
        });
        Select(destination);
    }
    public void FillDown() { if (Selection.Bottom > Selection.Top) Fill(new(new(Selection.Top, Selection.Left), new(Selection.Top, Selection.Right)), Selection, false); }
    public void FillRight() { if (Selection.Right > Selection.Left) Fill(new(new(Selection.Top, Selection.Left), new(Selection.Bottom, Selection.Left)), Selection, false); }
    public string SelectionSummary()
    {
        var numbers = Sheet.Cells.Keys.Select(CellAddress.Parse).Where(Selection.Contains).Select(a => Calculation.Evaluate(Sheet, a)).Where(v => v.Kind == ValueKind.Number).Select(v => v.Number).ToArray();
        return numbers.Length < 2 ? "" : $"Average: {numbers.Average():#,##0.##}     Count: {numbers.Length:N0}     Sum: {numbers.Sum():#,##0.##}";
    }
}
