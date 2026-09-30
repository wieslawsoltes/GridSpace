using System.Collections.Immutable;

namespace GridSpace.Core;

/// <summary>Immutable source values at the last refresh. Literal inputs retain text, errors and booleans without a formula dependency.</summary>
public sealed record PivotCacheSnapshot
{
    public ImmutableArray<string> Headers { get; init; } = [];
    public ImmutableArray<ImmutableArray<string>> Rows { get; init; } = [];
    [System.Text.Json.Serialization.JsonIgnore]
    public long EstimatedBytes => 128L + Headers.Sum(s => 24L + s.Length * 2L) +
        Rows.Sum(r => 24L + r.Sum(s => 24L + s.Length * 2L));

    public void Validate()
    {
        if (Headers.IsDefault || Rows.IsDefault || Headers.Length is < 1 or > 256 ||
            (long)(Rows.Length + 1) * Headers.Length > 200_000 ||
            Headers.Any(s => string.IsNullOrWhiteSpace(s) || s.Length > 32767) || Headers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != Headers.Length ||
            Rows.Any(r => r.IsDefault || r.Length != Headers.Length || r.Any(s => s is null || s.Length > 32767)))
            throw new ArgumentException("Invalid or oversized PivotTable cache.");
    }
}
