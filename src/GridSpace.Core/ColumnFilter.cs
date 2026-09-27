namespace GridSpace.Core;

public enum FilterOperator { Equal, NotEqual, GreaterThan, GreaterThanOrEqual, LessThan, LessThanOrEqual, Contains, DoesNotContain, BeginsWith, EndsWith, Blank, NotBlank }

public sealed record FilterCondition(FilterOperator Operator, string Value = "");

/// <summary>Columns combine with AND. A column contains either an explicit value set or up to two joined predicates.</summary>
public sealed record ColumnFilter
{
    public int Column { get; init; }
    public string[]? Values { get; init; }
    public bool IncludeBlank { get; init; }
    public FilterCondition? First { get; init; }
    public FilterCondition? Second { get; init; }
    public bool And { get; init; } = true;

    public void Validate()
    {
        if (Column < 0 || Column >= CellAddress.MaxColumns) throw new InvalidDataException("Invalid filter column.");
        if (Values is { Length: > 10000 } || Values?.Any(v => v is null || v.Length > 32767) == true)
            throw new InvalidDataException("A value filter supports at most 10,000 valid values.");
        if (Values is not null && (First is not null || Second is not null))
            throw new InvalidDataException("Choose a value filter or custom conditions, not both.");
        foreach (var condition in new[] { First, Second })
            if (condition is not null && (!Enum.IsDefined(condition.Operator) || condition.Value is null || condition.Value.Length > 32767))
                throw new InvalidDataException("Invalid filter condition.");
        if (Second is not null && First is null) throw new InvalidDataException("A second filter condition needs a first condition.");
    }
}
