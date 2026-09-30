using GridSpace.Core;
using System.Text.Json;

namespace GridSpace.Editing;

public sealed partial class SpreadsheetSession
{
    public ChartSpec? FindChart(string? id) => id is null ? null : Sheet.Charts.FirstOrDefault(c => c.Id == id);

    public string AddChart(ChartSpec chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        chart.Validate();
        if (Sheet.Charts.Count >= 128 || Sheet.Charts.Any(c => c.Id == chart.Id))
            throw new InvalidOperationException("A sheet supports 128 uniquely identified charts.");
        CommitChart("Insert chart", new(-1, Sheet.Charts.Count, null, chart.CloneDocument()));
        return chart.Id;
    }

    public void UpdateChart(string id, Func<ChartSpec, ChartSpec> update, string name = "Format chart")
    {
        ArgumentNullException.ThrowIfNull(update);
        var index = Sheet.Charts.FindIndex(c => c.Id == id);
        if (index < 0) throw new InvalidOperationException("The chart no longer exists.");
        var before = Sheet.Charts[index].CloneDocument();
        var after = update(before.CloneDocument()) ?? throw new ArgumentException("A chart edit cannot return null.");
        if (after.Id != id) throw new ArgumentException("A chart edit cannot change its identity.");
        after.Validate();
        if (before with { Series = after.Series } == after && before.Series.SequenceEqual(after.Series)) return;
        CommitChart(name, new(index, index, before, after.CloneDocument()));
    }

    public void DeleteChart(string id)
    {
        var index = Sheet.Charts.FindIndex(c => c.Id == id);
        if (index >= 0) CommitChart("Delete chart", new(index, -1, Sheet.Charts[index].CloneDocument(), null));
    }

    public string DuplicateChart(string id)
    {
        var chart = FindChart(id) ?? throw new InvalidOperationException("Select a chart first.");
        return AddChart(chart.CloneDocument() with { Id = Guid.NewGuid().ToString("N"), OffsetX = chart.OffsetX + 20, OffsetY = chart.OffsetY + 20 });
    }

    public const string ChartClipboardPrefix = "GridSpace.Chart/1\n";

    /// <summary>Portable drawing clipboard payload. Data references stay bound to the original worksheet.</summary>
    public string CopyChart(string id)
    {
        var chart = FindChart(id) ?? throw new InvalidOperationException("Select a chart first.");
        var json = JsonSerializer.Serialize(chart.CloneDocument() with { SourceSheet = chart.SourceSheet ?? Sheet.Name });
        if (json.Length > 131072) throw new InvalidOperationException("Chart clipboard data exceeds 128 KiB.");
        return ChartClipboardPrefix + json;
    }

    public string PasteChart(string text, CellAddress? destination = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!text.StartsWith(ChartClipboardPrefix, StringComparison.Ordinal) || text.Length > 131072 + ChartClipboardPrefix.Length)
            throw new ArgumentException("Invalid chart clipboard payload.");
        ChartSpec chart;
        try { chart = JsonSerializer.Deserialize<ChartSpec>(text[ChartClipboardPrefix.Length..]) ?? throw new ArgumentException("The clipboard has no chart."); }
        catch (JsonException error) { throw new ArgumentException("Invalid chart clipboard document.", nameof(text), error); }
        chart.Validate();
        var target = destination ?? ActiveCell;
        if (!target.IsValid) throw new ArgumentOutOfRangeException(nameof(destination));
        return AddChart(chart with { Id = Guid.NewGuid().ToString("N"), Row = target.Row, Column = target.Column, OffsetX = 8, OffsetY = 8 });
    }

    public void OrderChart(string id, bool front)
    {
        var index = Sheet.Charts.FindIndex(c => c.Id == id);
        if (index < 0) return;
        var target = front ? Sheet.Charts.Count - 1 : 0;
        if (target == index) return;
        var chart = Sheet.Charts[index].CloneDocument();
        CommitChart(front ? "Bring chart to front" : "Send chart to back", new(index, target, chart, chart));
    }

    private void CommitChart(string name, ChartPatch patch)
    {
        ReplayChart(patch, true);
        if (_inTransaction) return;
        _undo.Add(new(name, "", "", Selection) { Chart = patch, SheetIndex = Book.ActiveSheetIndex });
        _redo.Clear(); TrimHistory(); IsDirty = true; Notify(name, true);
    }

    private void ReplayChart(ChartPatch patch, bool forward)
    {
        Sheet.Charts.RemoveAll(c => c.Id == patch.Id);
        var document = forward ? patch.After : patch.Before;
        if (document is not null) Sheet.Charts.Insert(Math.Clamp(forward ? patch.AfterIndex : patch.BeforeIndex, 0, Sheet.Charts.Count), document.CloneDocument());
        Book.TouchDrawings();
    }
}
