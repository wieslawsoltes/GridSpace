namespace GridSpace.Controls;

/// <summary>Reusable, data-driven Office-density ribbon built from library-owned button and icon controls.</summary>
public sealed class RibbonControl : UserControl
{
    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal, Spacing = 2 };
    private readonly StackPanel _groups = new() { Orientation = Orientation.Horizontal };
    private readonly Dictionary<string, OfficeButton> _tabButtons = [];
    private IReadOnlyList<RibbonTab> _items = [];
    private string _selected = "Home";
    public string SelectedTab => _selected;
    public event Action<string>? CommandRequested;
    public event Action<string>? TabChanged;

    public RibbonControl()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        var root = new Grid(); root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) }); root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(108) });
        var tabsScroll = new ScrollViewer { Content = _tabs, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Enabled, Background = OfficeTheme.Brush("#F6F6F6") };
        root.Children.Add(tabsScroll);
        var groupsScroll = new ScrollViewer { Content = _groups, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Enabled, Background = OfficeTheme.Brush("#FFFFFF") };
        var border = new Border { Child = groupsScroll, BorderBrush = OfficeTheme.Brush("#D9D9D9"), BorderThickness = new Thickness(0, 0, 0, 1) }; Grid.SetRow(border, 1); root.Children.Add(border); Content = root;
        AutomationProperties.SetName(this, "Ribbon");
    }
    public void SetTabs(IReadOnlyList<RibbonTab> tabs)
    {
        _items = tabs; _tabs.Children.Clear(); _tabButtons.Clear();
        var file = new OfficeButton("File", () => CommandRequested?.Invoke("file")) { Width = 52, Height = 32, Background = OfficeTheme.Brush("#F6F6F6") }; _tabs.Children.Add(file);
        foreach (var tab in tabs)
        {
            var button = new OfficeButton(tab.Name, () => SelectTab(tab.Name)) { Height = 32, Padding = new Thickness(14, 3, 14, 3), Background = OfficeTheme.Brush("#F6F6F6"), CornerRadius = new CornerRadius(0) };
            AutomationProperties.SetAutomationId(button, "RibbonTab" + tab.Name.Replace(" ", ""));
            _tabButtons.Add(tab.Name, button); _tabs.Children.Add(button);
        }
        SelectTab(tabs.Any(t => t.Name == _selected) ? _selected : tabs.FirstOrDefault()?.Name ?? "");
    }
    public void SelectTab(string name)
    {
        var tab = _items.FirstOrDefault(t => t.Name == name); if (tab is null) return;
        _selected = name; _groups.Children.Clear();
        foreach (var pair in _tabButtons)
        {
            pair.Value.Foreground = OfficeTheme.Brush(pair.Key == name ? "#107C41" : "#242424");
            pair.Value.BorderBrush = OfficeTheme.Brush("#107C41"); pair.Value.BorderThickness = new Thickness(0, 0, 0, pair.Key == name ? 3 : 0);
        }
        foreach (var group in tab.Groups)
        {
            var outer = new Grid { Margin = new Thickness(5, 6, 5, 3) };
            outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(77) }); outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(18) });
            var commands = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
            StackPanel? small = null;
            foreach (var command in group.Commands)
            {
                if (command.Large) { commands.Children.Add(CreateButton(command)); small = null; }
                else
                {
                    if (small is null || small.Children.Count == 3) { small = new StackPanel { Spacing = 1 }; commands.Children.Add(small); }
                    small.Children.Add(CreateButton(command));
                }
            }
            outer.Children.Add(commands);
            var caption = OfficeTheme.Label(group.Name, 10, "#666666"); caption.HorizontalAlignment = HorizontalAlignment.Center; Grid.SetRow(caption, 1); outer.Children.Add(caption);
            _groups.Children.Add(outer);
            _groups.Children.Add(new Border { Width = 1, Height = 83, Margin = new Thickness(2, 8, 2, 8), Background = OfficeTheme.Brush("#E1E1E1") });
        }
        TabChanged?.Invoke(name);
    }
    private OfficeButton CreateButton(RibbonCommand command)
    {
        var content = new StackPanel { Orientation = command.Large ? Orientation.Vertical : Orientation.Horizontal, Spacing = command.Large ? 3 : 6, HorizontalAlignment = HorizontalAlignment.Center };
        content.Children.Add(new OfficeIcon { Kind = command.Icon, Width = command.Large ? 30 : 17, Height = command.Large ? 30 : 17, HorizontalAlignment = HorizontalAlignment.Center });
        var label = OfficeTheme.Label(command.Label, 11); label.TextAlignment = TextAlignment.Center; label.TextWrapping = TextWrapping.Wrap; content.Children.Add(label);
        var button = new OfficeButton { Content = content, Height = command.Large ? 74 : 24, MinWidth = command.Large ? 57 : 28, Padding = new Thickness(command.Large ? 6 : 4, 2, command.Large ? 6 : 4, 2), IsEnabled = command.Enabled, HorizontalContentAlignment = command.Large ? HorizontalAlignment.Center : HorizontalAlignment.Left };
        AutomationProperties.SetName(button, command.Label); AutomationProperties.SetAutomationId(button, "Command-" + command.Id);
        ToolTipService.SetToolTip(button, command.Label + (command.Shortcut is null ? "" : " (" + command.Shortcut + ")") + (command.Enabled ? "" : " — not implemented"));
        button.Click += (_, _) => CommandRequested?.Invoke(command.Id); return button;
    }
}
