using GridSpace.Core;

namespace GridSpace.Editing;

internal sealed record CellPatch(CellAddress Address, Cell Before, Cell After);
