using System.Text.Json.Serialization;

namespace GridSpace.Core;

public sealed partial class Worksheet
{
    public string Name { get; set; } = "Sheet1";
    public Dictionary<string, Cell> Cells { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<int, double> ColumnWidths { get; set; } = [];
    public Dictionary<int, double> RowHeights { get; set; } = [];
    public HashSet<int> HiddenRows { get; set; } = [];
    public HashSet<int> HiddenColumns { get; set; } = [];
    public HashSet<int> FilteredRows { get; set; } = [];
    public List<ColumnFilter> Filters { get; set; } = [];
    public List<ConditionalFormatRule> ConditionalFormats { get; set; } = [];
    public List<SortLevel> SortLevels { get; set; } = [];
    public string? SortRange { get; set; }
    public bool SortHasHeader { get; set; } = true;
    public bool SortCaseSensitive { get; set; }
    public List<CellRange> Merges { get; set; } = [];
    public List<ChartSpec> Charts { get; set; } = [];
    public Dictionary<string, string[]> ValidationLists { get; set; } = [];
    public int FrozenRows { get; set; }
    public int FrozenColumns { get; set; }
    public bool ShowGridLines { get; set; } = true;
    public bool ShowFormulas { get; set; }
    public string? FilterRange { get; set; }
    public string TabColor { get; set; } = "#107C41";
    [JsonIgnore] public Action? Changed { get; set; }
    [JsonIgnore] internal Action<Worksheet, CellAddress, bool>? CellChanged { get; set; }
    public Cell Get(CellAddress address) => Cells.TryGetValue(address.ToString(), out var value) ? value : Cell.Empty;
    public Cell Get(string address) => Get(CellAddress.Parse(address));
    public void Set(CellAddress address, Cell cell)
    {
        ArgumentNullException.ThrowIfNull(cell);
        ArgumentNullException.ThrowIfNull(cell.Input);
        if (!address.IsValid) throw new ArgumentOutOfRangeException(nameof(address));
        if (cell.Input.Length > 32767) throw new InvalidOperationException("A cell cannot contain more than 32,767 characters.");
        var before = Get(address);
        if (before == cell) return;
        if (string.IsNullOrEmpty(cell.Input) && cell.Style == CellStyle.Default && string.IsNullOrEmpty(cell.Note)) Cells.Remove(address.ToString());
        else Cells[address.ToString()] = cell;
        if (CellChanged is { } changed) changed(this, address, before.Input != cell.Input);
        else Changed?.Invoke();
    }
    public void Set(string address, string input) { var a = CellAddress.Parse(address); Set(a, Get(a) with { Input = input }); }
    public bool IsRowHidden(int row) => HiddenRows.Contains(row) || FilteredRows.Contains(row);
    public double ColumnWidth(int column) => HiddenColumns.Contains(column) ? 0 : ColumnWidths.GetValueOrDefault(column, 88);
    public double RowHeight(int row) => IsRowHidden(row) ? 0 : RowHeights.GetValueOrDefault(row, 24);
    [JsonIgnore] public CellRange UsedRange
    {
        get
        {
            if (Cells.Count == 0) return new(new(0, 0), new(0, 0));
            var addresses = Cells.Keys.Select(CellAddress.Parse).ToArray();
            return new(new(addresses.Min(a => a.Row), addresses.Min(a => a.Column)), new(addresses.Max(a => a.Row), addresses.Max(a => a.Column)));
        }
    }
    public CellAddress MergeAnchor(CellAddress address)
    {
        foreach (var merge in Merges) if (merge.Contains(address)) return new(merge.Top, merge.Left);
        return address;
    }
}
