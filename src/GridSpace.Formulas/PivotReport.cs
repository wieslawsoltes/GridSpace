using System.Runtime.CompilerServices;
using GridSpace.Core;

namespace GridSpace.Formulas;

/// <summary>Owned calculation snapshot with explicit display-row provenance for drill-through and charts.</summary>
public sealed class PivotReport
{
    public required PivotTableSpec Definition { get; init; }
    public required PivotSource Source { get; init; }
    public required IReadOnlyList<PivotKey> RowKeys { get; init; }
    public required IReadOnlyList<PivotKey> ColumnKeys { get; init; }
    public required IReadOnlyList<PivotRowBand> RowBands { get; init; }
    public required CalcValue[] Cells { get; init; }
    public required int ColumnCount { get; init; }
    public int RowCount => Cells.Length / ColumnCount;
    public int LabelColumns => Definition.Layout == PivotLayout.Compact ? 1 : Math.Max(1, Definition.Rows.Count);
    public CalcValue this[int row, int column] => Cells[row * ColumnCount + column];
    private Dictionary<(int Row, int Column), PivotOutlineCell>? _outline;

    public IReadOnlyDictionary<(int Row, int Column), PivotOutlineCell> OutlineCells => _outline ??= CreateOutline();

    private Dictionary<(int Row, int Column), PivotOutlineCell> CreateOutline()
    {
        var result = new Dictionary<(int, int), PivotOutlineCell>();
        var shown = new HashSet<PivotKey>();
        for (var r = 0; r < RowBands.Count; r++)
        {
            var band = RowBands[r];
            if (band.Kind == PivotRowKind.GrandTotal || band.Key.Items.Count == 0) continue;
            var depth = band.Key.Items.Count - 1;
            for (var level = 0; level <= depth; level++)
            {
                if (Definition.Layout != PivotLayout.Tabular && level != depth) continue;
                var prefix = new PivotKey(band.Key.Items.Take(level + 1));
                var column = Definition.Layout == PivotLayout.Compact ? 0 : level;
                var expandable = level < Definition.Rows.Count - 1 && shown.Add(prefix);
                var indent = Definition.Layout == PivotLayout.Compact ? level : 0;
                result[(r + 1, column)] = new(indent, expandable ? PivotHierarchy.Path(Definition, prefix) : null, band.Kind != PivotRowKind.Collapsed);
            }
        }
        return result;
    }

    public IReadOnlyList<CalcValue[]> DrillDown(int row, int column)
    {
        if (row < 1 || row >= RowCount || column < LabelColumns || column >= ColumnCount
            || RowBands[row - 1].Kind == PivotRowKind.GroupHeader)
            throw new ArgumentException("Select a PivotTable value, subtotal or collapsed total, not a header or label.");
        var band = RowBands[row - 1];
        var rowKey = band.Kind == PivotRowKind.GrandTotal ? null : band.Key;
        var group = (column - LabelColumns) / Definition.Values.Count;
        var columnKey = group < ColumnKeys.Count ? ColumnKeys[group] : null;
        return Source.Rows.Where(values => Matches(values, Definition.Rows, rowKey) && Matches(values, Definition.Columns, columnKey)).ToArray();
    }

    private static bool Matches(CalcValue[] values, IReadOnlyList<int> fields, PivotKey? key)
    {
        if (key is null) return true;
        for (var i = 0; i < key.Items.Count; i++) if (!PivotHierarchy.SameValue(values[fields[i]], key.Items[i])) return false;
        return true;
    }

    internal static PivotKey Key(CalcValue[] values, IReadOnlyList<int> fields) =>
        fields.Count == 0 ? PivotKey.Empty : new(fields.Select(i => values[i]));
}

/// <summary>
/// Weakly owned, immutable-definition cache shared by input, rendering and linked charts.
/// Session edits replace definitions. Directly mutating a cached definition is unsupported;
/// call Invalidate after such host-side mutation before querying the presentation again.
/// </summary>
public static class PivotReportCache
{
    private static readonly ConditionalWeakTable<PivotTableSpec, PivotReport> Reports = new();
    public static PivotReport Get(PivotTableSpec definition)
    {
        if (definition.NeedsLayoutRefresh || definition.Cache is null)
            throw new InvalidOperationException("Refresh this PivotTable before using its row hierarchy.");
        return Reports.GetValue(definition, PivotEngine.FromCache);
    }
    public static void Invalidate(PivotTableSpec definition) => Reports.Remove(definition);
    public static void Remember(PivotTableSpec definition, PivotReport report)
    {
        Reports.Remove(definition);
        Reports.Add(definition, report);
    }
}
