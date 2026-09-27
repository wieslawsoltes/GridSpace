namespace GridSpace.Controls;

/// <summary>Office-density button with a library-owned template, keyboard focus and automation semantics.</summary>
public class OfficeButton : Button
{
    public OfficeButton()
    {
        DefaultStyleKey = typeof(OfficeButton); FontFamily = OfficeTheme.Font;
        FontSize = 12; Padding = new Thickness(7, 4, 7, 4); MinWidth = 24; MinHeight = 24;
        Background = OfficeTheme.Brush("#FFFFFF"); Foreground = OfficeTheme.Brush("#242424");
        BorderBrush = OfficeTheme.Brush("#FFFFFF"); BorderThickness = new Thickness(0); CornerRadius = new CornerRadius(3);
    }
    public OfficeButton(string text, Action clicked) : this() { Content = text; AutomationProperties.SetName(this, text); Click += (_, _) => clicked(); }
}
