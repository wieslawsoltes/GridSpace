using System.Text.Json.Serialization;

namespace GridSpace.Core;

public enum PivotAggregate { Sum, Count, CountNumbers, Average, Min, Max, Product, StandardDeviation, PopulationStandardDeviation, Variance, PopulationVariance }
public enum PivotLayout { Tabular, Outline, Compact }
public enum PivotSubtotals { None, Top, Bottom }
public enum PivotShowAs { Normal, PercentOfRow, PercentOfColumn, PercentOfGrandTotal }
public sealed record PivotValueField
{
    public int Field { get; init; }
    public PivotAggregate Aggregate { get; init; }
    public PivotShowAs ShowAs { get; init; }
    public string Caption { get; init; } = "";
    public string NumberFormat { get; init; } = "#,##0.##";
}
public sealed record PivotFilter
{
    public int Field { get; init; }
    public string[] Values { get; init; } = [];
}

/// <summary>A refreshable, local worksheet-source PivotTable definition. Field indexes are source-relative.</summary>
public sealed record PivotTableSpec
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "PivotTable1";
    public string SourceSheet { get; set; } = "Sheet1";
    public string SourceRange { get; set; } = "A1:C10";
    public string Destination { get; set; } = "A1";
    public List<int> Rows { get; set; } = [];
    public List<int> Columns { get; set; } = [];
    public List<PivotValueField> Values { get; set; } = [];
    public List<PivotFilter> Filters { get; set; } = [];
    public PivotLayout Layout { get; set; }
    public PivotSubtotals Subtotals { get; set; }
    public bool RepeatRowLabels { get; set; } = true;
    public List<PivotGroupPath> CollapsedRows { get; set; } = [];
    public bool RowGrandTotals { get; set; } = true;
    public bool ColumnGrandTotals { get; set; } = true;
    public bool SortAscending { get; set; } = true;
    public bool IncludeHiddenRows { get; set; } = true;
    public string? OutputRange { get; set; }
    public string? ChartRange { get; set; }
    public bool NeedsLayoutRefresh { get; set; }
    public PivotCacheSnapshot? Cache { get; set; }
    public string[] FieldNames { get; set; } = [];
    public int LastSourceRowCount { get; set; }
    [JsonIgnore] public CellAddress Anchor => CellAddress.Parse(Destination);

    public PivotTableSpec CloneDocument() => this with
    {
        Rows = Rows.ToList(), Columns = Columns.ToList(), Values = Values.ToList(),
        Filters = Filters.Select(f => f with { Values = f.Values.ToArray() }).ToList(),
        FieldNames = FieldNames.ToArray(), CollapsedRows = CollapsedRows.ToList()
    };

    /// <summary>Discard collapse paths whose source-field prefix no longer describes the row axis.</summary>
    public void NormalizeCollapseState() => CollapsedRows = CollapsedRows
        .Where(p => p is not null && p.IsCompatible(Rows)).ToList();

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || Id.Length > 128 || string.IsNullOrWhiteSpace(Name)
            || Name.Length > 128 || (!char.IsLetter(Name[0]) && Name[0] != '_') || CellAddress.TryParse(Name, out _) || Name.Any(c => !char.IsLetterOrDigit(c) && c != '_'))
            throw new ArgumentException("PivotTable names use letters, digits and underscores (at most 128 characters).");
        Workbook.ValidateSheetName(SourceSheet);
        var range = CellRange.Parse(SourceRange);
        _ = Anchor;
        if (range.Count > 200_000 || range.Bottom == range.Top) throw new ArgumentException("A PivotTable source requires a header and data, at most 200,000 cells.");
        if (Rows is null || Columns is null || Values is null || Filters is null || FieldNames is null || CollapsedRows is null)
            throw new ArgumentException("PivotTable collections cannot be null.");
        if (!Enum.IsDefined(Layout) || !Enum.IsDefined(Subtotals)) throw new ArgumentException("Unknown PivotTable layout.");
        if (CollapsedRows.Count > 10_000) throw new ArgumentException("A report supports at most 10,000 collapsed groups.");
        long pathBytes = 0;
        foreach (var path in CollapsedRows)
        {
            if (path is null || !path.IsCompatible(Rows) || path.Values.IsDefault || path.Values.Length != path.Fields.Length
                || path.Values.Any(v => v is null || v.Length > 32767)) throw new ArgumentException("Invalid collapsed row-group path.");
            pathBytes += path.Values.Sum(v => (long)v.Length * 2) + path.Fields.Length * 4L;
        }
        if (pathBytes > 512 * 1024) throw new ArgumentException("Collapsed group metadata exceeds 512 KB.");
        if (Rows.Count > 8 || Columns.Count > 8 || Values.Count is < 1 or > 16 || Filters.Count > 32)
            throw new ArgumentException("Use at most 8 row/column levels, 16 values and 32 report filters.");
        var width = range.Right - range.Left + 1;
        if (width > 256) throw new ArgumentException("PivotTables support at most 256 source fields.");
        Cache?.Validate();
        if (Cache is not null && Cache.Headers.Length != width) throw new ArgumentException("Pivot cache width differs from its source.");
        if (Values.Any(v => v is null || v.Caption is null || v.NumberFormat is null || v.Caption.Length > 1024 || v.NumberFormat.Length > 256))
            throw new ArgumentException("Invalid PivotTable value metadata.");
        if (Filters.Any(f => f is null || f.Values is null || f.Values.Length > 10_000 || f.Values.Any(v => v is null || v.Length > 32767)))
            throw new ArgumentException("A report filter supports at most 10,000 non-null values.");
        var axes = Rows.Concat(Columns).Concat(Filters.Select(f => f.Field)).ToArray();
        if (axes.Distinct().Count() != axes.Length) throw new ArgumentException("A field can belong to only one row, column or filter area.");
        if (axes.Concat(Values.Select(v => v.Field)).Any(f => f < 0 || f >= width))
            throw new ArgumentException("A PivotTable field is outside the source range.");
        if (Values.Any(v => !Enum.IsDefined(v.Aggregate) || !Enum.IsDefined(v.ShowAs))) throw new ArgumentException("Unknown value aggregation.");
        if (Filters.Any(f => f.Values is null || f.Values.Length > 10_000)) throw new ArgumentException("A report filter supports at most 10,000 values.");
        if (OutputRange is not null && CellRange.Parse(OutputRange).Count > 100_000) throw new ArgumentException("Pivot output is limited to 100,000 cells.");
        if (ChartRange is not null) _ = CellRange.Parse(ChartRange);
    }
}
