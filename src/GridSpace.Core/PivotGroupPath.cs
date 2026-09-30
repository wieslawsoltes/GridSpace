using System.Collections.Immutable;

namespace GridSpace.Core;

/// <summary>Source-relative field identities and typed input literals, not ambiguous display labels.</summary>
public sealed record PivotGroupPath
{
    public ImmutableArray<int> Fields { get; init; } = [];
    public ImmutableArray<string> Values { get; init; } = [];

    public bool IsCompatible(IReadOnlyList<int> rows)
    {
        if (Fields.IsDefaultOrEmpty || Fields.Length >= rows.Count) return false;
        for (var i = 0; i < Fields.Length; i++) if (Fields[i] != rows[i]) return false;
        return true;
    }
}
