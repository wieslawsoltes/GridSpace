namespace GridSpace.Core;

/// <summary>An absolute worksheet column and direction in a stable multi-level row sort.</summary>
public sealed record SortLevel(int Column, bool Descending = false);
