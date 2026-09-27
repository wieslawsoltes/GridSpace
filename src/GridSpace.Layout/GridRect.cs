namespace GridSpace.Layout;

public readonly record struct GridRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public bool Contains(double x, double y) => x >= X && y >= Y && x < Right && y < Bottom;
    public bool Intersects(GridRect other) => X < other.Right && Right > other.X && Y < other.Bottom && Bottom > other.Y;
}

public readonly record struct GridPane(GridRect Clip, double OriginX, double OriginY, int FirstColumn, int LastColumn, int FirstRow, int LastRow);
public enum GridHitKind { None, Cell, ColumnHeader, RowHeader, Corner }
public readonly record struct GridHit(GridHitKind Kind, int Row, int Column);
