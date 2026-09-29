using System.Runtime.CompilerServices;
using GridSpace.Core;

namespace GridSpace.Formulas;

/// <summary>
/// Bounded, read-only filter catalogs for immutable PivotTable snapshots. A layout edit and
/// its filter list always observe the same refresh epoch, never newer worksheet values.
/// Snapshot keys are weak so closing a workbook or pruning history releases its catalogs.
/// </summary>
public static class PivotFieldValues
{
    private const int MaximumCachedFields = 16;
    private const int MaximumDistinctValues = 10_000;
    private static readonly ConditionalWeakTable<PivotCacheSnapshot, Catalog> Catalogs = new();

    public static IReadOnlyList<string> Get(PivotCacheSnapshot snapshot, int field)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if ((uint)field >= (uint)snapshot.Headers.Length) throw new ArgumentOutOfRangeException(nameof(field));
        return Catalogs.GetValue(snapshot, static cache => new Catalog(cache)).Get(snapshot, field);
    }

    private sealed class Catalog
    {
        private readonly Dictionary<int, IReadOnlyList<string>> _fields = [];
        private readonly Queue<int> _insertionOrder = new();
        private readonly object _gate = new();

        public Catalog(PivotCacheSnapshot snapshot) => snapshot.Validate();

        public IReadOnlyList<string> Get(PivotCacheSnapshot snapshot, int field)
        {
            lock (_gate)
            {
                if (_fields.TryGetValue(field, out var cached)) return cached;
                var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var row in snapshot.Rows)
                {
                    unique.Add(CalculationEngine.ParseInput(row[field]).ToString());
                    if (unique.Count > MaximumDistinctValues)
                        throw new InvalidOperationException("Pivot filter selection supports at most 10,000 distinct values. Narrow the source range first.");
                }
                var result = Array.AsReadOnly(unique.Order(StringComparer.OrdinalIgnoreCase).ToArray());
                if (_fields.Count == MaximumCachedFields) _fields.Remove(_insertionOrder.Dequeue());
                _fields.Add(field, result);
                _insertionOrder.Enqueue(field);
                return result;
            }
        }
    }
}
