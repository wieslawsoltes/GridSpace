namespace GridSpace.Core;

/// <summary>A bounded insertion/deletion transform shared by references, cells and worksheet metadata.</summary>
public readonly record struct AxisEdit
{
    public bool Rows { get; }
    public int Position { get; }
    public int Count { get; }
    public bool IsDeletion { get; }
    public int Limit => Rows ? CellAddress.MaxRows : CellAddress.MaxColumns;
    public int EndExclusive => Position + Count;

    public AxisEdit(bool rows, int position, int count, bool isDeletion)
    {
        var limit = rows ? CellAddress.MaxRows : CellAddress.MaxColumns;
        if (position < 0 || position >= limit || count < 1 || count > limit - position)
            throw new ArgumentOutOfRangeException(nameof(count), "The structural edit must fit inside the worksheet.");
        Rows = rows;
        Position = position;
        Count = count;
        IsDeletion = isDeletion;
    }

    public int? MapIndex(int index)
    {
        if (index < 0 || index >= Limit) throw new ArgumentOutOfRangeException(nameof(index));
        if (index < Position) return index;
        if (IsDeletion) return index < EndExclusive ? null : index - Count;
        return index >= Limit - Count ? null : index + Count;
    }

    public CellAddress? Map(CellAddress address)
    {
        var index = MapIndex(Rows ? address.Row : address.Column);
        return index is null ? null : Rows ? address with { Row = index.Value } : address with { Column = index.Value };
    }

    /// <summary>Contracts a partially deleted range; an entirely deleted or overflowing range has no result.</summary>
    public CellRange? Map(CellRange range)
    {
        var first = Rows ? range.Top : range.Left;
        var last = Rows ? range.Bottom : range.Right;
        if (IsDeletion)
        {
            if (first >= Position && first < EndExclusive) first = EndExclusive;
            if (last >= Position && last < EndExclusive) last = Position - 1;
            if (first > last) return null;
        }
        var low = MapIndex(first);
        var high = MapIndex(last);
        if (low is null || high is null) return null;
        // Retain endpoint orientation, including reversed ranges such as B9:A1.
        if (Rows)
            return new(range.Start with { Row = range.Start.Row <= range.End.Row ? low.Value : high.Value },
                range.End with { Row = range.Start.Row <= range.End.Row ? high.Value : low.Value });
        return new(range.Start with { Column = range.Start.Column <= range.End.Column ? low.Value : high.Value },
            range.End with { Column = range.Start.Column <= range.End.Column ? high.Value : low.Value });
    }

    public int MapBoundary(int frozenCount)
    {
        if (!IsDeletion) return Position < frozenCount ? Math.Min(Limit - 1, frozenCount + Count) : frozenCount;
        return frozenCount - Math.Clamp(frozenCount - Position, 0, Count);
    }
}
