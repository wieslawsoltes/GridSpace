using GridSpace.Core;

namespace GridSpace.Formulas;

public sealed record ChartSeriesData(string Name, string ValuesRange, double?[] Values)
{
    public IReadOnlyList<string>? ReferenceAreas { get; init; }
    public ChartTextReference? NameReference { get; init; }
}
public sealed record ChartData(string SourceSheet, string SourceRange, string CategoriesRange,
    string[] Categories, double?[] XValues, IReadOnlyList<ChartSeriesData> Series)
{
    public IReadOnlyList<string>? CategoryAreas { get; init; }
    public bool HasCompositeCategories { get; init; }
    public ChartTextSnapshot Text { get; init; } = ChartTextSnapshot.Empty;
}

/// <summary>Resolves chart vectors once; rendering, hit testing and XLSX caches share the same data semantics.</summary>
public static class ChartDataResolver
{
    public static readonly string[] Palette = ["#4472C4", "#ED7D31", "#A5A5A5", "#FFC000", "#5B9BD5", "#70AD47", "#8064A2", "#C0504D"];

    public static ChartSpec EffectiveDefinition(Workbook book, Worksheet host, ChartSpec chart)
    {
        if (chart.SourceUnavailable) throw new InvalidOperationException("Chart source data was deleted. Choose a new data source.");
        if (chart.PivotTableId is null) return chart;
        foreach (var sheet in book.Sheets)
            if (sheet.PivotTables.FirstOrDefault(p => p.Id == chart.PivotTableId) is { ChartRange: { } range } pivot)
            {
                if (pivot.NeedsLayoutRefresh || pivot.Cache is null) throw new InvalidOperationException("Refresh the PivotTable before charting its hierarchy.");
                var area = CellRange.Parse(range);
                var labels = pivot.Layout == PivotLayout.Compact ? 1 : Math.Max(1, pivot.Rows.Count);
                var series = Enumerable.Range(area.Left + labels, area.Right - area.Left - labels + 1).Select((column, index) => new ChartSeries
                {
                    Name = sheet.Get(new CellAddress(area.Top, column)).Input.TrimStart('\''),
                    Values = new CellRange(new(area.Top + 1, column), new(area.Bottom, column)).ToString(),
                    Color = chart.Series.Count > index ? chart.Series[index].Color : Palette[index % Palette.Length],
                    Kind = chart.Series.Count > index ? chart.Series[index].Kind : null,
                    SecondaryAxis = chart.Series.Count > index && chart.Series[index].SecondaryAxis,
                    Visible = chart.Series.Count <= index || chart.Series[index].Visible
                }).ToList();
                return chart with { SourceSheet = sheet.Name, Range = range, Categories = null, Series = series, SeriesInRows = false, HasHeaders = true };
            }
        throw new InvalidOperationException("The chart's PivotTable no longer exists. Select another data source.");
    }

    public static ChartData Resolve(Workbook book, Worksheet host, ChartSpec definition, CalculationEngine calculation)
    {
        var spec = EffectiveDefinition(book, host, definition);
        spec.Validate();
        var sheet = spec.SourceSheet is { } sourceName ? book.FindSheet(sourceName) ?? throw new InvalidOperationException("Chart source worksheet was not found.") : host;
        var range = CellRange.Parse(spec.Range);
        var start = spec.SeriesInRows ? range.Left + (spec.HasHeaders ? 1 : 0) : range.Top + (spec.HasHeaders ? 1 : 0);
        var end = spec.SeriesInRows ? range.Right : range.Bottom;
        var categoriesRange = spec.Categories ?? (spec.SeriesInRows
            ? new CellRange(new(range.Top, Math.Min(start, end)), new(range.Top, end)).ToString()
            : new CellRange(new(Math.Min(start, end), range.Left), new(end, range.Left)).ToString());
        var categories = CellRange.Parse(categoriesRange).Cells().ToArray();
        var definitions = spec.Series.Count > 0 ? spec.Series.Select(s => (s.Name, s.Values, s.NameReference)).ToArray()
            : AutoSeries().ToArray();
        if (definitions.Length > 32) throw new InvalidOperationException("A chart supports 32 series. Choose a smaller range or explicit series.");
        var pivot = definition.PivotTableId is null ? null : sheet.PivotTables.First(p => p.Id == definition.PivotTableId);
        var report = pivot is null ? null : PivotReportCache.Get(pivot);
        bool CategoryRow(int index)
        {
            if (report is null) return true;
            var row = categories[index].Row - pivot!.Anchor.Row - 1;
            return row >= 0 && row < report.RowBands.Count && report.RowBands[row].IsChartCategory;
        }
        var positions = Enumerable.Range(0, categories.Length).Where(i => CategoryRow(i) && (spec.PlotHiddenCells ||
            !sheet.IsRowHidden(categories[i].Row) && !sheet.HiddenColumns.Contains(categories[i].Column))).ToArray();
        var categoryValues = positions.Select(i => calculation.Evaluate(sheet, categories[i])).ToArray();
        var result = new List<ChartSeriesData>();
        var consumed = categories.Length;
        foreach (var (caption, valuesRange, nameReference) in definitions)
        {
            var addresses = CellRange.Parse(valuesRange).Cells().ToArray();
            consumed += addresses.Length;
            if (consumed > 100_000) throw new InvalidOperationException("Chart vectors exceed 100,000 cells.");
            var values = positions.Select(i =>
            {
                if (i >= addresses.Length) return (double?)null;
                var address = addresses[i];
                if (!spec.PlotHiddenCells && (sheet.IsRowHidden(address.Row) || sheet.HiddenColumns.Contains(address.Column))) return null;
                var value = calculation.Evaluate(sheet, address);
                return value.Kind == ValueKind.Number && double.IsFinite(value.Number) ? value.Number : (double?)null;
            }).ToArray();
            var name = nameReference is null ? caption : ChartTextResolver.Resolve(book, nameReference, calculation);
            result.Add(new(name, valuesRange, values) { NameReference = nameReference, ReferenceAreas = report is null ? null : Areas(addresses, positions) });
        }
        return new(sheet.Name, range.ToString(), categoriesRange,
            categoryValues.Select((v, index) =>
            {
                if (definition.PivotTableId is null) return v.ToString();
                var address = categories[positions[index]];
                return report!.RowBands[address.Row - pivot!.Anchor.Row - 1].Key.Label;

            }).ToArray(),
            categoryValues.Select(v => v.Kind == ValueKind.Number ? v.Number : (double?)null).ToArray(), result) { CategoryAreas = report is null ? null : Areas(categories, positions), HasCompositeCategories = pivot?.Rows.Count > 1, Text = ChartTextResolver.Resolve(book, spec, calculation) };

        IEnumerable<(string Name, string Values, ChartTextReference? NameReference)> AutoSeries()
        {
            if (end < start) yield break;
            var firstSeries = spec.SeriesInRows ? range.Top + 1 : range.Left + 1;
            var lastSeries = spec.SeriesInRows ? range.Bottom : range.Right;
            for (var i = firstSeries; i <= lastSeries; i++)
            {
                var header = spec.SeriesInRows ? new CellAddress(i, range.Left) : new CellAddress(range.Top, i);
                var name = spec.HasHeaders ? calculation.Evaluate(sheet, header).ToString() : "Series " + (i - firstSeries + 1);
                var values = spec.SeriesInRows ? new CellRange(new(i, start), new(i, end)) : new CellRange(new(start, i), new(end, i));
                yield return (name.Length == 0 ? "Series " + (i - firstSeries + 1) : name, values.ToString(),
                    spec.HasHeaders ? new ChartTextReference(sheet.Name, header.ToString()) : null);
            }
        }
    }
    /// <summary>Caption-only drawing edits keep category/X/value arrays and their source evaluation intact.</summary>
    public static ChartData RefreshText(Workbook book, Worksheet host, ChartSpec chart, ChartData data, CalculationEngine calculation)
    {
        var effective = EffectiveDefinition(book, host, chart);
        var series = data.Series.Select((previous, index) =>
        {
            var item = index < effective.Series.Count ? effective.Series[index] : null;
            var link = item?.NameReference ?? (item is null ? previous.NameReference : null);
            var name = link is null ? item?.Name ?? previous.Name : ChartTextResolver.Resolve(book, link, calculation);
            return previous with { Name = name, NameReference = link };
        }).ToArray();
        return data with { Series = series, Text = ChartTextResolver.Resolve(book, effective, calculation) };
    }

    private static IReadOnlyList<string> Areas(CellAddress[] addresses, int[] positions)
    {
        var result = new List<string>();
        CellAddress? start = null, end = null;
        foreach (var index in positions)
        {
            if (index >= addresses.Length) break;
            var next = addresses[index];
            if (end is { } previous && !(previous.Column == next.Column && previous.Row + 1 == next.Row
                || previous.Row == next.Row && previous.Column + 1 == next.Column))
            { result.Add(new CellRange(start!.Value, previous).ToString()); start = null; }
            start ??= next; end = next;
        }
        if (start is { } first) result.Add(new CellRange(first, end!.Value).ToString());
        return result;
    }

}

/// <summary>Cache lifetime belongs to one owner. Drawing-only edits/gestures do not invalidate bound values.</summary>
public sealed class ChartDataCache
{
    private sealed record Entry(Workbook Book, Worksheet Host, CalculationEngine Engine, long Revision,
        ChartSpec Binding, PivotTableSpec? Pivot, ChartData Data);
    private readonly Dictionary<string, Entry> _entries = [];
    public long ResolveCount { get; private set; }
    public long TextRefreshCount { get; private set; }
    public ChartData Get(Workbook book, Worksheet host, ChartSpec chart, CalculationEngine calculation)
    {
        var pivot = FindPivot(book, host, chart);
        if (_entries.TryGetValue(chart.Id, out var entry) && ReferenceEquals(entry.Book, book) && ReferenceEquals(entry.Host, host)
            && ReferenceEquals(entry.Engine, calculation) && entry.Revision == book.Revision
            && ReferenceEquals(entry.Pivot, pivot) && SameVectors(entry.Binding, chart))
        {
            if (SameText(entry.Binding, chart)) return entry.Data;
            var renamed = ChartDataResolver.RefreshText(book, host, chart, entry.Data, calculation);
            _entries[chart.Id] = entry with { Binding = chart.CloneDocument(), Data = renamed };
            TextRefreshCount++;
            return renamed;
        }
        if (_entries.Count >= 128) _entries.Clear();
        var data = ChartDataResolver.Resolve(book, host, chart, calculation);
        ResolveCount++;
        _entries[chart.Id] = new(book, host, calculation, book.Revision, chart.CloneDocument(), pivot, data);
        return data;
    }
    private static PivotTableSpec? FindPivot(Workbook book, Worksheet host, ChartSpec chart)
    {
        if (chart.PivotTableId is null) return null;
        var source = host;
        if (chart.SourceSheet is { } name && !host.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
        {
            source = null;
            foreach (var sheet in book.Sheets)
                if (sheet.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) { source = sheet; break; }
        }
        if (source is not null)
            foreach (var pivot in source.PivotTables) if (pivot.Id == chart.PivotTableId) return pivot;
        return null;
    }
    private static bool SameVectors(ChartSpec a, ChartSpec b)
    {
        if (a.SourceUnavailable != b.SourceUnavailable || a.PivotTableId != b.PivotTableId || a.SourceSheet != b.SourceSheet || a.Range != b.Range || a.Categories != b.Categories || a.SeriesInRows != b.SeriesInRows
            || a.HasHeaders != b.HasHeaders || a.PlotHiddenCells != b.PlotHiddenCells || a.Series.Count != b.Series.Count) return false;
        for (var i = 0; i < a.Series.Count; i++) if (a.Series[i].Values != b.Series[i].Values) return false;
        return true;
    }
    private static bool SameText(ChartSpec a, ChartSpec b)
    {
        if (a.TitleReference != b.TitleReference || a.CategoryAxisTitleReference != b.CategoryAxisTitleReference
            || a.ValueAxisTitleReference != b.ValueAxisTitleReference) return false;
        for (var i = 0; i < a.Series.Count; i++)
            if (a.Series[i].Name != b.Series[i].Name || a.Series[i].NameReference != b.Series[i].NameReference) return false;
        return true;
    }
    public void Clear() => _entries.Clear();
}
