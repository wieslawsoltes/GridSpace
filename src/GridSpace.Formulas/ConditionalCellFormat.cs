using GridSpace.Core;

namespace GridSpace.Formulas;

public readonly record struct DataBarVisual(double Axis, double Start, double End, string Color);
public sealed record ConditionalCellFormat(CellStyle Style, DataBarVisual? DataBar = null, bool HideValue = false);
