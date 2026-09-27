namespace GridSpace.Layout;

/// <summary>O(k) storage and O(log(n) log(k)) offset lookup, where k is the number of non-default sizes.</summary>
public sealed class AxisLayout
{
    private readonly int[] _indices;
    private readonly double[] _deltas;
    public int Count { get; }
    public double DefaultSize { get; }
    public double Extent => Position(Count);

    public AxisLayout(int count, double defaultSize, IReadOnlyDictionary<int, double> sizes, IReadOnlySet<int> hidden)
    {
        if (count <= 0 || !double.IsFinite(defaultSize) || defaultSize <= 0) throw new ArgumentOutOfRangeException(nameof(count));
        Count = count;
        DefaultSize = defaultSize;
        _indices = sizes.Keys.Concat(hidden).Where(i => i >= 0 && i < count).Distinct().Order().ToArray();
        _deltas = new double[_indices.Length + 1];
        for (var i = 0; i < _indices.Length; i++)
        {
            var index = _indices[i];
            var size = hidden.Contains(index) ? 0 : sizes.GetValueOrDefault(index, defaultSize);
            if (!double.IsFinite(size) || size < 0) size = defaultSize;
            _deltas[i + 1] = _deltas[i] + size - defaultSize;
        }
    }

    public double Position(int index)
    {
        index = Math.Clamp(index, 0, Count);
        var found = Array.BinarySearch(_indices, index);
        var before = found >= 0 ? found : ~found;
        return index * DefaultSize + _deltas[before];
    }
    public double Size(int index) => index < 0 || index >= Count ? 0 : Position(index + 1) - Position(index);

    /// <summary>Returns the visible item at an offset, skipping arbitrarily long runs of hidden items.</summary>
    public int IndexAt(double offset)
    {
        if (!double.IsFinite(offset)) throw new ArgumentOutOfRangeException(nameof(offset));
        offset = Math.Max(0, offset);
        var lo = 0;
        var hi = Count;
        while (lo < hi)
        {
            var middle = lo + (hi - lo) / 2;
            if (Position(middle + 1) <= offset) lo = middle + 1;
            else hi = middle;
        }
        return Math.Min(lo, Count - 1);
    }

    public IEnumerable<int> Visible(double start, double end, int first = 0, int lastExclusive = int.MaxValue)
    {
        if (end <= start) yield break;
        var last = Math.Min(Count, lastExclusive);
        var index = Math.Max(first, IndexAt(start));
        while (index < last && Position(index) < end)
        {
            var size = Size(index);
            if (size > 0) { yield return index; index++; }
            else
            {
                var next = IndexAt(Position(index));
                index = Math.Max(index + 1, next);
            }
        }
    }
}
