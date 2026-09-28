namespace GridSpace.Core;

/// <summary>A bounded-journal entry. Sheet identity remains stable for cell-only edits.</summary>
public readonly record struct CellMutation(long Revision, Worksheet Sheet, CellAddress Address, bool InputChanged);
