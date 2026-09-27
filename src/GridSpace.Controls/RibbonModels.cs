namespace GridSpace.Controls;

public sealed record RibbonCommand(string Id, string Label, OfficeIconKind Icon, bool Large = false, string? Shortcut = null, bool Enabled = true);
public sealed record RibbonGroup(string Name, IReadOnlyList<RibbonCommand> Commands);
public sealed record RibbonTab(string Name, IReadOnlyList<RibbonGroup> Groups);
