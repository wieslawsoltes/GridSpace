using System.Collections.Immutable;
using GridSpace.Core;

namespace GridSpace.Formulas;

public enum PivotRowKind { Detail, GroupHeader, Subtotal, Collapsed, GrandTotal }
public sealed record PivotRowBand(PivotKey Key, PivotRowKind Kind)
{
    public bool IsChartCategory => Kind is PivotRowKind.Detail or PivotRowKind.Collapsed;
}
public readonly record struct PivotOutlineCell(int Indent, PivotGroupPath? Group, bool Expanded);

/// <summary>Deterministic row-axis presentation over typed prefix groups. Column groups remain flat.</summary>
public static class PivotHierarchy
{
    public static PivotGroupPath Path(PivotTableSpec spec, PivotKey key) => new()
    {
        Fields = spec.Rows.Take(key.Items.Count).ToImmutableArray(),
        Values = key.Items.Select(Literal).ToImmutableArray()
    };

    public static PivotKey Key(PivotGroupPath path) => new(path.Values.Select(CalculationEngine.ParseInput));
    public static bool SamePath(PivotGroupPath a, PivotGroupPath b) => a.Fields.SequenceEqual(b.Fields) && Key(a).Equals(Key(b));
    public static string Literal(CalcValue value) => value.Kind == ValueKind.Text ? "'" + value.Text
        : value.Kind == ValueKind.Number ? value.Number.ToString("G17", System.Globalization.CultureInfo.InvariantCulture) : value.ToString();

    public static bool StartsWith(PivotKey key, PivotKey prefix, int count = -1)
    {
        count = count < 0 ? prefix.Items.Count : count;
        if (key.Items.Count < count || prefix.Items.Count < count) return false;
        for (var i = 0; i < count; i++) if (!SameValue(key.Items[i], prefix.Items[i])) return false;
        return true;
    }

    public static bool SameValue(CalcValue a, CalcValue b) => a.Kind == b.Kind &&
        (a.Kind is ValueKind.Number or ValueKind.Boolean ? a.Number.Equals(b.Number)
            : string.Equals(a.Text ?? "", b.Text ?? "", StringComparison.OrdinalIgnoreCase));

    internal static List<PivotRowBand> CreateBands(IReadOnlyList<PivotKey> keys, PivotTableSpec spec)
    {
        if (spec.Layout == PivotLayout.Tabular && spec.Subtotals == PivotSubtotals.None && spec.CollapsedRows.Count == 0)
            return keys.Select(k => new PivotRowBand(k, PivotRowKind.Detail)).ToList();
        var collapsed = spec.CollapsedRows.Select(Key).ToHashSet();
        var children = new Dictionary<PivotKey, List<PivotKey>>();
        foreach (var key in keys)
        {
            if (key.Items.Count == 0) continue;
            var parent = new PivotKey(key.Items.Take(key.Items.Count - 1));
            if (!children.TryGetValue(parent, out var list)) children.Add(parent, list = []);
            list.Add(key);
        }
        var result = new List<PivotRowBand>();
        if (keys.Count == 1 && keys[0].Items.Count == 0) result.Add(new(PivotKey.Empty, PivotRowKind.Detail));
        else Visit(PivotKey.Empty);
        return result;

        void Visit(PivotKey parent)
        {
            if (!children.TryGetValue(parent, out var next)) return;
            foreach (var key in next)
            {
                if (key.Items.Count == spec.Rows.Count) { result.Add(new(key, PivotRowKind.Detail)); continue; }
                if (collapsed.Contains(key)) { result.Add(new(key, PivotRowKind.Collapsed)); continue; }
                if (spec.Subtotals == PivotSubtotals.Top) result.Add(new(key, PivotRowKind.Subtotal));
                else if (spec.Layout != PivotLayout.Tabular) result.Add(new(key, PivotRowKind.GroupHeader));
                Visit(key);
                if (spec.Subtotals == PivotSubtotals.Bottom) result.Add(new(key, PivotRowKind.Subtotal));
            }
        }
    }
}
