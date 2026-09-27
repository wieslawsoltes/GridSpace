namespace GridSpace.Controls;

public sealed class SpreadsheetStatusBar : UserControl
{
    private readonly TextBlock _status = OfficeTheme.Label("Ready", 11);
    private readonly TextBlock _summary = OfficeTheme.Label("", 11);
    private readonly OfficeButton _zoom;
    private double _value = 1;
    public event Action<double>? ZoomRequested;
    public SpreadsheetStatusBar()
    {
        Height = 27; HorizontalContentAlignment = HorizontalAlignment.Stretch;
        var root = new Grid { Padding = new Thickness(10, 0, 5, 0), Background = OfficeTheme.Brush("#F3F3F3"), ColumnSpacing = 16 };
        root.ColumnDefinitions.Add(new ColumnDefinition()); root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _status.TextTrimming = TextTrimming.CharacterEllipsis; root.Children.Add(_status); Grid.SetColumn(_summary, 1); root.Children.Add(_summary);
        var zoom = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        zoom.Children.Add(new OfficeButton("−", () => ZoomRequested?.Invoke(Math.Max(.25, _value - .1))) { Width = 25 });
        _zoom = new OfficeButton("100%", () => ZoomRequested?.Invoke(1)) { Width = 48 }; AutomationProperties.SetName(_zoom, "Reset zoom"); zoom.Children.Add(_zoom);
        zoom.Children.Add(new OfficeButton("+", () => ZoomRequested?.Invoke(Math.Min(4, _value + .1))) { Width = 25 }); Grid.SetColumn(zoom, 2); root.Children.Add(zoom); Content = root;
        AutomationProperties.SetAutomationId(_status, "WorkbookStatus");
    }
    public void Update(string status, string summary, double zoom, bool error = false)
    {
        _status.Text = status; _status.Foreground = OfficeTheme.Brush(error ? "#A4262C" : "#444444"); _summary.Text = summary; _value = zoom; _zoom.Content = Math.Round(zoom * 100) + "%";
    }
}
