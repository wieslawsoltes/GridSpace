namespace GridSpace.Controls;

public static class OfficeTheme
{
    public static FontFamily Font { get; set; } = new("Arial");
    public static SolidColorBrush Brush(string hex)
    {
        var value = Convert.ToUInt32(hex.TrimStart('#'), 16);
        return new(Windows.UI.Color.FromArgb(255, (byte)(value >> 16), (byte)(value >> 8), (byte)value));
    }
    public static TextBlock Label(string text, double size = 12, string color = "#242424") => new() { Text = text, FontSize = size, FontFamily = Font, Foreground = Brush(color), VerticalAlignment = VerticalAlignment.Center };
    public static TextBox Field(string name, double width = double.NaN)
    {
        var field = new TextBox { FontSize = 13, FontFamily = Font, MinHeight = 28, Padding = new Thickness(6, 3, 6, 3), BorderThickness = new Thickness(1), BorderBrush = Brush("#B8B8B8"), Background = Brush("#FFFFFF"), Foreground = Brush("#242424"), Width = width };
        AutomationProperties.SetName(field, name); AutomationProperties.SetAutomationId(field, name.Replace(" ", "")); return field;
    }
}
