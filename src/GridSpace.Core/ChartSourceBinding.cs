namespace GridSpace.Core;

/// <summary>A transient editable chart reference. It is not a worksheet selection or persisted document.</summary>
public enum ChartSourcePart { DataRange, Categories, SeriesValues, Title, CategoryAxisTitle, ValueAxisTitle, SeriesName }
public enum ChartSourceHandle { Move, Start, End }

/// <summary>SeriesIndex is -1 for a data/category range; Horizontal preserves a one-cell vector's axis.</summary>
public sealed record ChartSourceBinding(ChartSourcePart Part, int SeriesIndex, CellRange Range, bool Horizontal)
{
    public bool IsSingleCell => Part is ChartSourcePart.Title or ChartSourcePart.CategoryAxisTitle or ChartSourcePart.ValueAxisTitle or ChartSourcePart.SeriesName;
    public bool IsVector => Part != ChartSourcePart.DataRange && !IsSingleCell;
}
