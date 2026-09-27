namespace GridSpace.Core;

public readonly record struct CellRange(CellAddress Start, CellAddress End)
{
    public int Top => Math.Min(Start.Row, End.Row);
    public int Bottom => Math.Max(Start.Row, End.Row);
    public int Left => Math.Min(Start.Column, End.Column);
    public int Right => Math.Max(Start.Column, End.Column);
    public long Count => (long)(Bottom - Top + 1) * (Right - Left + 1);
    public bool Contains(CellAddress a) => a.Row >= Top && a.Row <= Bottom && a.Column >= Left && a.Column <= Right;
    public bool Intersects(CellRange other) => Top <= other.Bottom && Bottom >= other.Top && Left <= other.Right && Right >= other.Left;
    public CellRange Normalized => new(new(Top, Left), new(Bottom, Right));
    public override string ToString() => Start == End ? Start.ToString() : $"{new CellAddress(Top, Left)}:{new CellAddress(Bottom, Right)}";
    public static CellRange Parse(string text)
    {
        var parts = text.Split(':');
        if (parts.Length is < 1 or > 2) throw new FormatException("Use an A1 address or A1:B10 range.");
        var first = CellAddress.Parse(parts[0]);
        return new(first, parts.Length == 2 ? CellAddress.Parse(parts[1]) : first);
    }
    public IEnumerable<CellAddress> Cells(int limit = 100_000)
    {
        if (Count > limit) throw new InvalidOperationException($"This operation is limited to {limit:N0} cells.");
        for (var r = Top; r <= Bottom; r++) for (var c = Left; c <= Right; c++) yield return new(r, c);
    }
}
