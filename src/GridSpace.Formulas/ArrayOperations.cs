namespace GridSpace.Formulas;

/// <summary>Bounded rectangular array operations. No worksheet or host dependencies.</summary>
internal static class ArrayOperations
{
    internal static readonly HashSet<string> FunctionNames = new(StringComparer.Ordinal)
    { "SEQUENCE", "FILTER", "SORT", "SORTBY", "UNIQUE", "TRANSPOSE", "TAKE", "DROP", "HSTACK", "VSTACK", "CHOOSECOLS", "CHOOSEROWS" };

    internal static CalcValue Map(CalcValue value, Func<CalcValue, CalcValue> map) => value.Kind == ValueKind.Array
        ? CalcValue.Array(value.Items!.Select(map).ToArray(), value.Columns) : map(value);

    internal static CalcValue Zip(CalcValue left, CalcValue right, Func<CalcValue, CalcValue, CalcValue> map)
    {
        if (left.Kind != ValueKind.Array && right.Kind != ValueKind.Array) return map(left, right);
        var rows = Math.Max(left.Rows, right.Rows); var columns = Math.Max(left.Width, right.Width);
        return Create(rows, columns, (r, c) => map(Broadcast(left, r, c), Broadcast(right, r, c)));
    }

    internal static CalcValue Broadcast(CalcValue value, int row, int column) => value.Element(value.Rows == 1 ? 0 : row, value.Width == 1 ? 0 : column);

    internal static CalcValue Create(int rows, int columns, Func<int, int, CalcValue> factory)
    {
        if (rows <= 0 || columns <= 0) return CalcValue.Error("#CALC!");
        if ((long)rows * columns > CalculationEngine.MaximumArrayCells) return CalcValue.Error("#LIMIT!");
        var items = new CalcValue[rows * columns];
        for (var r = 0; r < rows; r++)
            for (var c = 0; c < columns; c++) items[r * columns + c] = factory(r, c);
        return CalcValue.Array(items, columns);
    }

    internal static CalcValue If(CalcValue condition, CalcValue whenTrue, CalcValue whenFalse)
    {
        var rows = Math.Max(condition.Rows, Math.Max(whenTrue.Rows, whenFalse.Rows));
        var columns = Math.Max(condition.Width, Math.Max(whenTrue.Width, whenFalse.Width));
        return Create(rows, columns, (r, c) =>
        {
            var test = Broadcast(condition, r, c);
            return test.IsError ? test : Broadcast(test.Truth ? whenTrue : whenFalse, r, c);
        });
    }

    internal static CalcValue Call(string name, CalcValue[] args)
    {
        if (args.Any(a => a.IsError)) return args.First(a => a.IsError);
        CalcValue V(int i) => i < args.Length ? args[i] : CalcValue.Blank;
        double Number(int i, double fallback = 0) => i >= args.Length || V(i).Kind == ValueKind.Blank ? fallback
            : V(i).TryNumber(out var value) ? value : throw new FormatException("Expected a scalar number.");
        int Integer(int i, int fallback = 0)
        {
            var value = Number(i, fallback);
            if (value < int.MinValue || value > int.MaxValue) throw new FormatException("Array index is out of bounds.");
            return (int)value;
        }
        bool Flag(int i) => i < args.Length && (V(i).Kind == ValueKind.Boolean ? V(i).Truth : Number(i) != 0);
        void Arity(int minimum, int maximum)
        { if (args.Length < minimum || args.Length > maximum) throw new FormatException("Incorrect array-function argument count."); }
        var source = V(0);
        switch (name)
        {
            case "SEQUENCE":
                Arity(1, 4);
                var rows = Integer(0, 1); var columns = Integer(1, 1); var start = Number(2, 1); var step = Number(3, 1);
                if (rows <= 0 || columns <= 0) return CalcValue.Error("#VALUE!");
                return Create(rows, columns, (r, c) => CalcValue.Num(start + ((long)r * columns + c) * step));
            case "TRANSPOSE":
                Arity(1, 1); return Create(source.Width, source.Rows, (r, c) => source.Element(c, r));
            case "FILTER":
                Arity(2, 3);
                var include = V(1);
                var vertical = include.Width == 1 && include.Rows == source.Rows;
                if (!vertical && !(include.Rows == 1 && include.Width == source.Width)) return CalcValue.Error("#VALUE!");
                var count = vertical ? source.Rows : source.Width; var selected = new List<int>();
                for (var i = 0; i < count; i++)
                {
                    var criterion = vertical ? include.Element(i, 0) : include.Element(0, i);
                    if (criterion.IsError) return criterion;
                    if (!criterion.TryNumber(out var match)) return CalcValue.Error("#VALUE!");
                    if (match != 0) selected.Add(i);
                }
                if (selected.Count == 0) return args.Length == 3 ? V(2) : CalcValue.Error("#CALC!");
                return vertical ? Create(selected.Count, source.Width, (r, c) => source.Element(selected[r], c))
                    : Create(source.Rows, selected.Count, (r, c) => source.Element(r, selected[c]));
            case "SORT":
                Arity(1, 4);
                var byColumn = Flag(3); var sortIndex = Integer(1, 1) - 1; var direction = Integer(2, 1);
                if (direction is not (1 or -1) || sortIndex < 0 || sortIndex >= (byColumn ? source.Rows : source.Width)) return CalcValue.Error("#VALUE!");
                var sorted = Enumerable.Range(0, byColumn ? source.Width : source.Rows).ToArray();
                System.Array.Sort(sorted, (a, b) =>
                {
                    var comparison = CompareSort(byColumn ? source.Element(sortIndex, a) : source.Element(a, sortIndex), byColumn ? source.Element(sortIndex, b) : source.Element(b, sortIndex), direction);
                    return comparison == 0 ? a.CompareTo(b) : comparison;
                });
                return Create(source.Rows, source.Width, (r, c) => source.Element(byColumn ? r : sorted[r], byColumn ? sorted[c] : c));
            case "SORTBY":
                Arity(2, 129);
                var keys = new List<(CalcValue Value, int Direction)>();
                var horizontal = V(1).Rows == 1 && V(1).Width == source.Width && source.Width > 1;
                for (var i = 1; i < args.Length; i += 2)
                {
                    var key = V(i); var order = Integer(i + 1, 1);
                    if (order is not (1 or -1) || (horizontal ? key.Rows != 1 || key.Width != source.Width : key.Width != 1 || key.Rows != source.Rows)) return CalcValue.Error("#VALUE!");
                    keys.Add((key, order));
                }
                var ordered = Enumerable.Range(0, horizontal ? source.Width : source.Rows).ToArray();
                System.Array.Sort(ordered, (a, b) =>
                {
                    foreach (var key in keys)
                    {
                        var comparison = CompareSort(horizontal ? key.Value.Element(0, a) : key.Value.Element(a, 0), horizontal ? key.Value.Element(0, b) : key.Value.Element(b, 0), key.Direction);
                        if (comparison != 0) return comparison;
                    }
                    return a.CompareTo(b);
                });
                return Create(source.Rows, source.Width, (r, c) => source.Element(horizontal ? r : ordered[r], horizontal ? ordered[c] : c));
            case "UNIQUE":
                Arity(1, 3);
                var across = Flag(1); var exactlyOnce = Flag(2);
                var equality = new VectorComparer(source, across);
                var groups = new Dictionary<int, (int First, int Count)>(equality);
                for (var i = 0; i < (across ? source.Width : source.Rows); i++)
                    groups[i] = groups.TryGetValue(i, out var found) ? (found.First, found.Count + 1) : (i, 1);
                var unique = groups.Values.Where(v => !exactlyOnce || v.Count == 1).Select(v => v.First).Order().ToArray();
                return across ? Create(source.Rows, unique.Length, (r, c) => source.Element(r, unique[c]))
                    : Create(unique.Length, source.Width, (r, c) => source.Element(unique[r], c));
            case "TAKE": case "DROP":
                Arity(2, 3);
                var rowCount = Integer(1); var colCount = Integer(2, name == "TAKE" ? source.Width : 0);
                if (rowCount == 0 && V(1).Kind != ValueKind.Blank || args.Length == 3 && colCount == 0 && V(2).Kind != ValueKind.Blank) return CalcValue.Error("#CALC!");
                (int Offset, int Count) Slice(int extent, int requested, bool omitted)
                {
                    if (omitted) return (0, extent);
                    var amount = (int)Math.Min(extent, Math.Abs((long)requested));
                    return name == "TAKE" ? (requested < 0 ? extent - amount : 0, amount)
                        : (requested < 0 ? 0 : amount, extent - amount);
                }
                var rowSlice = Slice(source.Rows, rowCount, V(1).Kind == ValueKind.Blank);
                var colSlice = Slice(source.Width, colCount, args.Length < 3 || V(2).Kind == ValueKind.Blank);
                return Create(rowSlice.Count, colSlice.Count, (r, c) => source.Element(r + rowSlice.Offset, c + colSlice.Offset));
            case "CHOOSECOLS": case "CHOOSEROWS":
                Arity(2, 255);
                var chooseRows = name == "CHOOSEROWS"; var extent = chooseRows ? source.Rows : source.Width;
                var indexes = args.Skip(1).SelectMany(a => a.Flatten()).Select(v =>
                {
                    if (!v.TryNumber(out var n) || n < -extent || n > extent || (int)n == 0) throw new FormatException("Array index is outside the source.");
                    return n < 0 ? extent + (int)n : (int)n - 1;
                }).ToArray();
                return chooseRows ? Create(indexes.Length, source.Width, (r, c) => source.Element(indexes[r], c))
                    : Create(source.Rows, indexes.Length, (r, c) => source.Element(r, indexes[c]));
            case "HSTACK": case "VSTACK":
                Arity(1, 255);
                var verticalStack = name == "VSTACK";
                var total = args.Sum(a => (long)(verticalStack ? a.Rows : a.Width));
                var other = args.Max(a => verticalStack ? a.Width : a.Rows);
                if (total * other > CalculationEngine.MaximumArrayCells) return CalcValue.Error("#LIMIT!");
                var resultRows = verticalStack ? (int)total : other; var resultColumns = verticalStack ? other : (int)total;
                var output = Enumerable.Repeat(CalcValue.Error("#N/A"), resultRows * resultColumns).ToArray();
                var offset = 0;
                foreach (var block in args)
                {
                    for (var r = 0; r < block.Rows; r++)
                        for (var c = 0; c < block.Width; c++) output[(r + (verticalStack ? offset : 0)) * resultColumns + c + (verticalStack ? 0 : offset)] = block.Element(r, c);
                    offset += verticalStack ? block.Rows : block.Width;
                }
                return CalcValue.Array(output, resultColumns);
            default: return CalcValue.Error("#NAME?");
        }
    }

    private static int CompareSort(CalcValue a, CalcValue b, int direction)
    {
        if (a.Kind == ValueKind.Blank) return b.Kind == ValueKind.Blank ? 0 : 1;
        if (b.Kind == ValueKind.Blank) return -1;
        return Compare(a, b) * direction;
    }

    private static int Compare(CalcValue a, CalcValue b)
    {
        var rank = a.Kind.CompareTo(b.Kind);
        if (rank != 0) return rank;
        return a.Kind is ValueKind.Number or ValueKind.Boolean ? a.Number.CompareTo(b.Number)
            : StringComparer.OrdinalIgnoreCase.Compare(a.ToString(), b.ToString());
    }

    private sealed class VectorComparer(CalcValue data, bool columns) : IEqualityComparer<int>
    {
        public bool Equals(int x, int y)
        {
            for (var i = 0; i < (columns ? data.Rows : data.Width); i++)
                if (Compare(columns ? data.Element(i, x) : data.Element(x, i), columns ? data.Element(i, y) : data.Element(y, i)) != 0) return false;
            return true;
        }
        public int GetHashCode(int value)
        {
            var hash = new HashCode();
            for (var i = 0; i < (columns ? data.Rows : data.Width); i++)
            {
                var cell = columns ? data.Element(i, value) : data.Element(value, i);
                hash.Add(cell.Kind);
                if (cell.Kind is ValueKind.Number or ValueKind.Boolean) hash.Add(cell.Number);
                else hash.Add(cell.ToString(), StringComparer.OrdinalIgnoreCase);
            }
            return hash.ToHashCode();
        }
    }
}
