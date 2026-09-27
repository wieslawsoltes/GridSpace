using GridSpace.Core;

namespace GridSpace.Editing;

public sealed record ClipboardBlock(string SheetName, CellRange Source, IReadOnlyList<Cell> Cells, string Text);
