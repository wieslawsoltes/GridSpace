using GridSpace.Core;

namespace GridSpace.Formulas;

/// <summary>A derived result, not stored cell data. Only Anchor owns an editable formula.</summary>
public sealed record SpillInfo(CellAddress Anchor, CellRange Range, CalcValue Values);
