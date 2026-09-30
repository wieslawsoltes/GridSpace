using GridSpace.Core;
using System.Collections.Immutable;

namespace GridSpace.Formulas;

/// <summary>Typed, case-insensitive dimension vector; numeric and textual 1 never collapse into one group.</summary>
public sealed class PivotKey : IEquatable<PivotKey>
{
    public static PivotKey Empty { get; } = new([]);
    private readonly CalcValue[] _items;
    public IReadOnlyList<CalcValue> Items { get; }
    private readonly int _hash;
    public PivotKey(IEnumerable<CalcValue> items)
    {
        _items = items.ToArray();
        Items = Array.AsReadOnly(_items);
        var hash = new HashCode();
        foreach (var item in Items)
        {
            hash.Add(item.Kind);
            if (item.Kind is ValueKind.Number or ValueKind.Boolean) hash.Add(item.Number);
            else hash.Add(item.Text ?? "", StringComparer.OrdinalIgnoreCase);
        }
        _hash = hash.ToHashCode();
    }
    public bool Equals(PivotKey? other)
    {
        if (other is null || _hash != other._hash || _items.Length != other._items.Length) return false;
        for (var i = 0; i < _items.Length; i++)
        {
            var a = _items[i]; var b = other._items[i];
            if (a.Kind != b.Kind) return false;
            if (a.Kind is ValueKind.Number or ValueKind.Boolean)
            {
                if (!a.Number.Equals(b.Number)) return false;
            }
            else if (!string.Equals(a.Text ?? "", b.Text ?? "", StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }
    public override bool Equals(object? obj) => obj is PivotKey key && Equals(key);
    public override int GetHashCode() => _hash;
    public string Label => string.Join(" / ", Items.Select(v => v.Kind == ValueKind.Blank ? "(blank)" : v.ToString()));
}

public sealed record PivotSource(string[] Headers, IReadOnlyList<CalcValue[]> Rows);

/// <summary>Single-pass hash aggregation. Every source cell is evaluated once per captured snapshot.</summary>
public static class PivotEngine
{
    public static PivotSource Capture(Workbook book, PivotTableSpec spec, CalculationEngine calculation)
    {
        spec.Validate();
        var sheet = book.FindSheet(spec.SourceSheet) ?? throw new InvalidOperationException("PivotTable source worksheet was not found.");
        var range = CellRange.Parse(spec.SourceRange);
        var width = range.Right - range.Left + 1;
        var headers = Enumerable.Range(range.Left, width).Select(c => calculation.Evaluate(sheet, new CellAddress(range.Top, c)).ToString().Trim()).ToArray();
        if (headers.Any(string.IsNullOrEmpty) || headers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != width)
            throw new InvalidOperationException("PivotTable source headers must be nonempty and unique.");

        var rows = new List<CalcValue[]>();
        for (var r = range.Top + 1; r <= range.Bottom; r++)
        {
            if (!spec.IncludeHiddenRows && sheet.IsRowHidden(r)) continue;
            var values = new CalcValue[width];
            for (var c = 0; c < width; c++) values[c] = calculation.Evaluate(sheet, new CellAddress(r, range.Left + c));
            if (values.All(IsBlank)) continue;
            rows.Add(values);
        }
        return new(headers, rows);
    }

    public static PivotReport Calculate(Workbook book, PivotTableSpec spec, CalculationEngine calculation) =>
        Build(Capture(book, spec, calculation), spec);

    public static PivotCacheSnapshot ToCache(PivotSource source)
    {
        static string Literal(CalcValue value) => value.Kind == ValueKind.Text ? "'" + value.ToString()
            : value.Kind == ValueKind.Number ? value.Number.ToString("G17", System.Globalization.CultureInfo.InvariantCulture) : value.ToString();
        var result = new PivotCacheSnapshot
        {
            Headers = source.Headers.ToImmutableArray(),
            Rows = source.Rows.Select(row => row.Select(Literal).ToImmutableArray()).ToImmutableArray()
        };
        result.Validate();
        return result;
    }

    public static PivotSource FromCache(PivotCacheSnapshot cache)
    {
        cache.Validate();
        return new(cache.Headers.ToArray(), cache.Rows.Select(row => row.Select(CalculationEngine.ParseInput).ToArray()).ToArray());
    }

    public static PivotReport FromCache(PivotTableSpec spec) => spec.Cache is { } cache
        ? Build(FromCache(cache), spec)
        : throw new InvalidOperationException("Refresh the PivotTable once before showing cached details.");

    public static PivotReport Build(PivotSource source, PivotTableSpec definition)
    {
        definition.Validate();
        if (source.Headers.Length != CellRange.Parse(definition.SourceRange).Right - CellRange.Parse(definition.SourceRange).Left + 1
            || source.Headers.Any(string.IsNullOrWhiteSpace)
            || source.Headers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != source.Headers.Length
            || (long)(source.Rows.Count + 1) * source.Headers.Length > 200_000)
            throw new ArgumentException("Invalid PivotTable source shape or headers.");
        var spec = definition.CloneDocument();
        var filters = spec.Filters.Select(f => (f.Field, Values: f.Values.ToHashSet(StringComparer.OrdinalIgnoreCase))).ToArray();
        source = new(source.Headers, source.Rows.Where(row =>
        {
            if (row.Length != source.Headers.Length) throw new ArgumentException("Inconsistent source row width.");
            return !filters.Any(f => !f.Values.Contains(row[f.Field].ToString()));
        }).ToArray());
        var rowKeys = new Dictionary<PivotKey, int>();
        var columnKeys = new Dictionary<PivotKey, int>();
        var buckets = new Dictionary<(int Row, int Column), Accumulator[]>();
        var rowTotals = new Dictionary<int, Accumulator[]>();
        var columnTotals = new Dictionary<int, Accumulator[]>();
        var hierarchical = spec.Layout != PivotLayout.Tabular || spec.Subtotals != PivotSubtotals.None || spec.CollapsedRows.Count > 0;
        var aggregateCells = 0;
        var total = Create();
        foreach (var row in source.Rows)
        {
            var ck = PivotReport.Key(row, spec.Columns);
            if (!columnKeys.TryGetValue(ck, out var ci)) columnKeys.Add(ck, ci = columnKeys.Count);
            // Prefixes receive source observations directly. Average/variance/product subtotals
            // must never be computed from already aggregated child display values.
            for (var depth = hierarchical ? Math.Min(1, spec.Rows.Count) : spec.Rows.Count; depth <= spec.Rows.Count; depth++)
            {
                var rk = depth == 0 ? PivotKey.Empty : new PivotKey(spec.Rows.Take(depth).Select(f => row[f]));
                if (!rowKeys.TryGetValue(rk, out var ri)) rowKeys.Add(rk, ri = rowKeys.Count);
                Add(Get(buckets, (ri, ci)), row);
                Add(Get(rowTotals, ri), row);
            }
            Add(Get(columnTotals, ci), row);
            Add(total, row);
        }
        if (rowKeys.Count == 0) rowKeys.Add(PivotKey.Empty, 0);
        if (columnKeys.Count == 0) columnKeys.Add(PivotKey.Empty, 0);
        var ordered = rowKeys.Keys.OrderBy(k => k, KeyComparer.Instance).ToArray();
        var rows = ordered.Where(k => k.Items.Count == spec.Rows.Count || k.Items.Count == 0).ToArray();
        var columns = columnKeys.Keys.OrderBy(k => k, KeyComparer.Instance).ToArray();
        if (!spec.SortAscending) { Array.Reverse(ordered); Array.Reverse(rows); Array.Reverse(columns); }
        var bands = PivotHierarchy.CreateBands(ordered, spec);
        var labels = spec.Layout == PivotLayout.Compact ? 1 : Math.Max(1, spec.Rows.Count);
        var totalColumn = spec.RowGrandTotals && spec.Columns.Count > 0;
        var totalRow = spec.ColumnGrandTotals && spec.Rows.Count > 0;
        if (totalRow) bands.Add(new(PivotKey.Empty, PivotRowKind.GrandTotal));
        var width = labels + (columns.Length + (totalColumn ? 1 : 0)) * spec.Values.Count;
        var height = 1 + bands.Count;
        if ((long)width * height > 100_000 || definition.Anchor.Column + width > CellAddress.MaxColumns
            || definition.Anchor.Row + height > CellAddress.MaxRows)
            throw new InvalidOperationException("PivotTable output exceeds the worksheet or 100,000-cell result limit.");
        var cells = Enumerable.Repeat(CalcValue.Blank, width * height).ToArray();
        void Set(int r, int c, CalcValue value) => cells[r * width + c] = value;
        for (var i = 0; i < labels; i++) Set(0, i, CalcValue.Str(spec.Rows.Count == 0 ? "Values"
            : spec.Layout == PivotLayout.Compact ? "Row Labels" : source.Headers[spec.Rows[i]]));
        for (var c = 0; c < columns.Length + (totalColumn ? 1 : 0); c++)
        {
            for (var v = 0; v < spec.Values.Count; v++)
            {
                var name = ValueCaption(source.Headers, spec.Values[v]);
                var group = c == columns.Length ? "Grand Total" : columns[c].Label;
                Set(0, labels + c * spec.Values.Count + v, CalcValue.Str(group.Length == 0 ? name : group + " · " + name));
            }
        }
        for (var r = 0; r < bands.Count; r++)
        {
            var band = bands[r];
            var isTotalRow = band.Kind == PivotRowKind.GrandTotal;
            if (isTotalRow) Set(r + 1, 0, CalcValue.Str("Grand Total"));
            else if (band.Key.Items.Count == 0) Set(r + 1, 0, CalcValue.Str(spec.Rows.Count == 0 ? "Total" : "(blank)"));
            else
            {
                var depth = band.Key.Items.Count - 1;
                for (var l = 0; l <= depth; l++)
                {
                    if (spec.Layout != PivotLayout.Tabular && l != depth) continue;
                    if (spec.Layout == PivotLayout.Tabular && !spec.RepeatRowLabels && l < depth && r > 0
                        && PivotHierarchy.StartsWith(bands[r - 1].Key, band.Key, l + 1)) continue;
                    var label = band.Key.Items[l];
                    if (label.Kind == ValueKind.Blank) label = CalcValue.Str("(blank)");
                    if (l == depth && band.Kind == PivotRowKind.Subtotal) label = CalcValue.Str(label + " Total");
                    Set(r + 1, spec.Layout == PivotLayout.Compact ? 0 : l, label);
                }
            }
            if (band.Kind == PivotRowKind.GroupHeader) continue;
            for (var c = 0; c < columns.Length + (totalColumn ? 1 : 0); c++)
            {
                var isTotalColumn = c == columns.Length;
                var ri = isTotalRow ? -1 : rowKeys[band.Key];
                var ci = isTotalColumn ? -1 : columnKeys[columns[c]];
                var bucket = isTotalRow ? isTotalColumn ? total : columnTotals.GetValueOrDefault(ci)
                    : isTotalColumn ? rowTotals.GetValueOrDefault(ri) : buckets.GetValueOrDefault((ri, ci));
                for (var v = 0; v < spec.Values.Count; v++)
                {
                    var field = spec.Values[v];
                    var value = (bucket?[v] ?? new Accumulator()).Result(field.Aggregate);
                    var denominator = field.ShowAs switch
                    {
                        PivotShowAs.PercentOfGrandTotal => total[v].Result(field.Aggregate),
                        PivotShowAs.PercentOfRow => (isTotalRow ? total[v] : rowTotals.GetValueOrDefault(ri)?[v] ?? new Accumulator()).Result(field.Aggregate),
                        PivotShowAs.PercentOfColumn => (isTotalColumn ? total[v] : columnTotals.GetValueOrDefault(ci)?[v] ?? new Accumulator()).Result(field.Aggregate),
                        _ => CalcValue.Num(1)
                    };
                    if (field.ShowAs != PivotShowAs.Normal && !value.IsError)
                        value = denominator.IsError ? denominator : denominator.Kind != ValueKind.Number || denominator.Number == 0
                            ? CalcValue.Error("#DIV/0!") : CalcValue.Num(value.Number / denominator.Number);
                    Set(r + 1, labels + c * spec.Values.Count + v, value);
                }
            }
        }
        return new() { Definition = spec, Source = source, RowKeys = rows, ColumnKeys = columns,
            RowBands = bands.AsReadOnly(), ColumnCount = width, Cells = cells };

        Accumulator[] Create()
        {
            aggregateCells += spec.Values.Count;
            if (aggregateCells > 400_000) throw new InvalidOperationException("PivotTable hierarchy exceeds the 400,000-accumulator budget.");
            return Enumerable.Range(0, spec.Values.Count).Select(_ => new Accumulator()).ToArray();
        }
        Accumulator[] Get<T>(Dictionary<T, Accumulator[]> table, T key) where T : notnull
        {
            if (!table.TryGetValue(key, out var value)) table.Add(key, value = Create());
            return value;
        }
        void Add(Accumulator[] bucket, CalcValue[] row)
        {
            for (var i = 0; i < spec.Values.Count; i++) bucket[i].Add(row[spec.Values[i].Field]);
        }
    }

    public static string ValueCaption(IReadOnlyList<string> headers, PivotValueField field) =>
        string.IsNullOrWhiteSpace(field.Caption) ? field.Aggregate + " of " + headers[field.Field] : field.Caption;
    private static bool IsBlank(CalcValue value) => value.Kind == ValueKind.Blank || value.Kind == ValueKind.Text && value.Text.Length == 0;

    private sealed class Accumulator
    {
        private long _count, _numeric;
        private double _sum, _compensation, _mean, _m2, _min = double.PositiveInfinity, _max = double.NegativeInfinity, _product = 1;
        private CalcValue? _error;
        public void Add(CalcValue value)
        {
            if (IsBlank(value)) return;
            _count++;
            if (value.IsError) { _error ??= value; return; }
            if (value.Kind != ValueKind.Number) return;
            var n = value.Number; _numeric++;
            var adjusted = n - _compensation; var next = _sum + adjusted;
            _compensation = (next - _sum) - adjusted; _sum = next;
            var delta = n - _mean; _mean += delta / _numeric; _m2 += delta * (n - _mean);
            _min = Math.Min(_min, n); _max = Math.Max(_max, n); _product *= n;
        }
        public CalcValue Result(PivotAggregate kind)
        {
            if (kind == PivotAggregate.Count) return CalcValue.Num(_count);
            if (kind == PivotAggregate.CountNumbers) return CalcValue.Num(_numeric);
            if (_error is { } error) return error;
            if (kind == PivotAggregate.Sum) return CalcValue.Num(_sum);
            if (_numeric == 0) return kind is PivotAggregate.Average or PivotAggregate.Variance or PivotAggregate.PopulationVariance
                or PivotAggregate.StandardDeviation or PivotAggregate.PopulationStandardDeviation ? CalcValue.Error("#DIV/0!") : CalcValue.Num(0);
            return kind switch
            {
                PivotAggregate.Average => CalcValue.Num(_mean),
                PivotAggregate.Min => CalcValue.Num(_min),
                PivotAggregate.Max => CalcValue.Num(_max),
                PivotAggregate.Product => CalcValue.Num(_product),
                PivotAggregate.Variance => _numeric < 2 ? CalcValue.Error("#DIV/0!") : CalcValue.Num(Math.Max(0, _m2) / (_numeric - 1)),
                PivotAggregate.PopulationVariance => CalcValue.Num(Math.Max(0, _m2) / _numeric),
                PivotAggregate.StandardDeviation => _numeric < 2 ? CalcValue.Error("#DIV/0!") : CalcValue.Num(Math.Sqrt(Math.Max(0, _m2) / (_numeric - 1))),
                PivotAggregate.PopulationStandardDeviation => CalcValue.Num(Math.Sqrt(Math.Max(0, _m2) / _numeric)),
                _ => CalcValue.Error("#VALUE!")
            };
        }
    }

    private sealed class KeyComparer : IComparer<PivotKey>
    {
        public static KeyComparer Instance { get; } = new();
        public int Compare(PivotKey? x, PivotKey? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1; if (y is null) return 1;
            for (var i = 0; i < Math.Min(x.Items.Count, y.Items.Count); i++)
            {
                var a = x.Items[i]; var b = y.Items[i];
                var result = a.Kind == ValueKind.Number && b.Kind == ValueKind.Number ? a.Number.CompareTo(b.Number)
                    : a.Kind == ValueKind.Blank ? b.Kind == ValueKind.Blank ? 0 : 1 : b.Kind == ValueKind.Blank ? -1
                    : a.Kind != b.Kind ? a.Kind.CompareTo(b.Kind) : string.Compare(a.ToString(), b.ToString(), StringComparison.OrdinalIgnoreCase);
                if (result != 0) return result;
            }
            return x.Items.Count.CompareTo(y.Items.Count);
        }
    }
}
