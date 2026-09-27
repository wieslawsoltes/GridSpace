using GridSpace.Core;

namespace GridSpace.Layout;

/// <summary>Shared DIP geometry for rasterization, input and editor overlays. Scroll offsets are unscaled sheet units.</summary>
public sealed class GridViewport
{
    public const double RowHeaderWidth = 48;
    public const double ColumnHeaderHeight = 26;
    private double _zoom = 1;
    public AxisLayout Columns { get; private set; } = new(CellAddress.MaxColumns, 88, new Dictionary<int, double>(), new HashSet<int>());
    public AxisLayout Rows { get; private set; } = new(CellAddress.MaxRows, 24, new Dictionary<int, double>(), new HashSet<int>());
    public double Width { get; set; }
    public double Height { get; set; }
    public double ScrollX { get; private set; }
    public double ScrollY { get; private set; }
    public int FrozenColumns { get; private set; }
    public int FrozenRows { get; private set; }
    public double Zoom { get => _zoom; set { _zoom = double.IsFinite(value) ? Math.Clamp(value, .25, 4) : 1; ScrollTo(ScrollX, ScrollY); } }
    public double FrozenWidth => Columns.Position(FrozenColumns) * Zoom;
    public double FrozenHeight => Rows.Position(FrozenRows) * Zoom;
    public double MaximumScrollX => Math.Max(0, Columns.Extent - Math.Max(0, Width - RowHeaderWidth) / Zoom);
    public double MaximumScrollY => Math.Max(0, Rows.Extent - Math.Max(0, Height - ColumnHeaderHeight) / Zoom);

    public void Refresh(Worksheet sheet)
    {
        Columns = new(CellAddress.MaxColumns, 88, sheet.ColumnWidths, sheet.HiddenColumns);
        Rows = new(CellAddress.MaxRows, 24, sheet.RowHeights, sheet.HiddenRows);
        FrozenColumns = Math.Clamp(sheet.FrozenColumns, 0, CellAddress.MaxColumns - 1);
        FrozenRows = Math.Clamp(sheet.FrozenRows, 0, CellAddress.MaxRows - 1);
        ScrollTo(ScrollX, ScrollY);
    }
    public void ScrollTo(double x, double y)
    {
        ScrollX = double.IsFinite(x) ? Math.Clamp(x, 0, MaximumScrollX) : 0;
        ScrollY = double.IsFinite(y) ? Math.Clamp(y, 0, MaximumScrollY) : 0;
    }
    public GridRect CellBounds(CellAddress address)
    {
        var x = RowHeaderWidth + (Columns.Position(address.Column) - (address.Column < FrozenColumns ? 0 : ScrollX)) * Zoom;
        var y = ColumnHeaderHeight + (Rows.Position(address.Row) - (address.Row < FrozenRows ? 0 : ScrollY)) * Zoom;
        return new(x, y, Columns.Size(address.Column) * Zoom, Rows.Size(address.Row) * Zoom);
    }
    public GridRect RangeBounds(CellRange range, GridPane pane) => new(
        pane.OriginX + Columns.Position(range.Left) * Zoom,
        pane.OriginY + Rows.Position(range.Top) * Zoom,
        (Columns.Position(range.Right + 1) - Columns.Position(range.Left)) * Zoom,
        (Rows.Position(range.Bottom + 1) - Rows.Position(range.Top)) * Zoom);

    public IReadOnlyList<GridPane> Panes()
    {
        var result = new List<GridPane>(4);
        var xSplit = Math.Min(Math.Max(RowHeaderWidth, Width), RowHeaderWidth + FrozenWidth);
        var ySplit = Math.Min(Math.Max(ColumnHeaderHeight, Height), ColumnHeaderHeight + FrozenHeight);
        Add(xSplit, ySplit, Width - xSplit, Height - ySplit, false, false);
        Add(RowHeaderWidth, ySplit, xSplit - RowHeaderWidth, Height - ySplit, true, false);
        Add(xSplit, ColumnHeaderHeight, Width - xSplit, ySplit - ColumnHeaderHeight, false, true);
        Add(RowHeaderWidth, ColumnHeaderHeight, xSplit - RowHeaderWidth, ySplit - ColumnHeaderHeight, true, true);
        return result;
        void Add(double x, double y, double width, double height, bool frozenX, bool frozenY)
        {
            if (width <= 0 || height <= 0) return;
            result.Add(new(new(x, y, width, height), RowHeaderWidth - (frozenX ? 0 : ScrollX * Zoom), ColumnHeaderHeight - (frozenY ? 0 : ScrollY * Zoom),
                frozenX ? 0 : FrozenColumns, frozenX ? FrozenColumns : CellAddress.MaxColumns,
                frozenY ? 0 : FrozenRows, frozenY ? FrozenRows : CellAddress.MaxRows));
        }
    }
    public GridHit HitTest(double x, double y)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return new(GridHitKind.None, -1, -1);
        if (x < RowHeaderWidth && y < ColumnHeaderHeight) return new(GridHitKind.Corner, 0, 0);
        var column = Columns.IndexAt(Math.Max(0, x - RowHeaderWidth) / Zoom + (x < RowHeaderWidth + FrozenWidth ? 0 : ScrollX));
        var row = Rows.IndexAt(Math.Max(0, y - ColumnHeaderHeight) / Zoom + (y < ColumnHeaderHeight + FrozenHeight ? 0 : ScrollY));
        return new(y < ColumnHeaderHeight ? GridHitKind.ColumnHeader : x < RowHeaderWidth ? GridHitKind.RowHeader : GridHitKind.Cell, row, column);
    }
    public void EnsureVisible(CellAddress address)
    {
        var left = Columns.Position(address.Column);
        var top = Rows.Position(address.Row);
        var x = ScrollX;
        var y = ScrollY;
        if (address.Column >= FrozenColumns)
        {
            if (left < x + Columns.Position(FrozenColumns)) x = left - Columns.Position(FrozenColumns);
            if (left + Columns.Size(address.Column) > x + (Width - RowHeaderWidth) / Zoom) x = left + Columns.Size(address.Column) - (Width - RowHeaderWidth) / Zoom;
        }
        if (address.Row >= FrozenRows)
        {
            if (top < y + Rows.Position(FrozenRows)) y = top - Rows.Position(FrozenRows);
            if (top + Rows.Size(address.Row) > y + (Height - ColumnHeaderHeight) / Zoom) y = top + Rows.Size(address.Row) - (Height - ColumnHeaderHeight) / Zoom;
        }
        ScrollTo(x, y);
    }
}
