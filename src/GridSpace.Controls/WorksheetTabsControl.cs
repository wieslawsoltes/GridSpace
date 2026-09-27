using GridSpace.Core;

namespace GridSpace.Controls;

public sealed class WorksheetTabsControl : UserControl
{
    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal, Spacing = 1 };
    private string _signature = "";
    private int _selected;
    public event Action<int>? SheetSelected;
    public event Action<int>? SheetContextRequested;
    public event Action? AddRequested;
    public WorksheetTabsControl()
    {
        Height = 32; HorizontalContentAlignment = HorizontalAlignment.Stretch;
        var root = new Grid { Background = OfficeTheme.Brush("#F5F5F5") };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) }); root.ColumnDefinitions.Add(new ColumnDefinition()); root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
        var arrows = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        arrows.Children.Add(new OfficeButton("‹", () => SheetSelected?.Invoke(Math.Max(0, _selected - 1))) { FontSize = 22, Width = 28 });
        arrows.Children.Add(new OfficeButton("›", () => SheetSelected?.Invoke(_selected + 1)) { FontSize = 22, Width = 28 }); root.Children.Add(arrows);
        var scroll = new ScrollViewer { Content = _tabs, HorizontalScrollMode = ScrollMode.Enabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetColumn(scroll, 1); root.Children.Add(scroll);
        var add = new OfficeButton("+", () => AddRequested?.Invoke()) { FontSize = 20, Width = 36 }; AutomationProperties.SetName(add, "New sheet"); AutomationProperties.SetAutomationId(add, "NewSheet"); Grid.SetColumn(add, 2); root.Children.Add(add); Content = root;
    }
    public void Update(Workbook book)
    {
        _selected = book.ActiveSheetIndex;
        var signature = string.Join("\u001f", book.Sheets.Select(s => s.Name + s.TabColor)) + _selected;
        if (_signature == signature) return; _signature = signature; _tabs.Children.Clear();
        for (var i = 0; i < book.Sheets.Count; i++)
        {
            var index = i; var sheet = book.Sheets[i];
            var button = new OfficeButton(sheet.Name, () => SheetSelected?.Invoke(index)) { Height = 31, Padding = new Thickness(18, 3, 18, 3), CornerRadius = new CornerRadius(0), Background = OfficeTheme.Brush(index == _selected ? "#FFFFFF" : "#F5F5F5"), Foreground = OfficeTheme.Brush(index == _selected ? "#107C41" : "#555555"), BorderBrush = OfficeTheme.Brush(sheet.TabColor), BorderThickness = new Thickness(0, 0, 0, index == _selected ? 3 : 0) };
            AutomationProperties.SetAutomationId(button, "Sheet-" + index); AutomationProperties.SetName(button, sheet.Name + " worksheet");
            button.RightTapped += (_, e) => { SheetContextRequested?.Invoke(index); e.Handled = true; };
            button.DoubleTapped += (_, e) => { SheetContextRequested?.Invoke(index); e.Handled = true; };
            _tabs.Children.Add(button);
        }
    }
}
