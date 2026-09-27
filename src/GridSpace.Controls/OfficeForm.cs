using System.Text.RegularExpressions;

namespace GridSpace.Controls;

public sealed record OfficeChoice<T>(T Value, string Label);

/// <summary>Consistent named input primitives for reusable spreadsheet editors.</summary>
public static class OfficeForm
{
    public static ComboBox Choice<T>(string name, IEnumerable<OfficeChoice<T>> choices, T selected, double width = double.NaN)
    {
        var items = choices.ToArray();
        var result = new ComboBox
        {
            ItemsSource = items, DisplayMemberPath = nameof(OfficeChoice<T>.Label),
            SelectedItem = items.FirstOrDefault(i => EqualityComparer<T>.Default.Equals(i.Value, selected)) ?? items.FirstOrDefault(),
            FontFamily = OfficeTheme.Font, FontSize = 13, MinHeight = 30, Width = width,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        AutomationProperties.SetName(result, name); AutomationProperties.SetAutomationId(result, name.Replace(" ", ""));
        return result;
    }

    public static ComboBox EnumChoice<T>(string name, T selected, double width = double.NaN) where T : struct, Enum =>
        Choice(name, Enum.GetValues<T>().Select(value => new OfficeChoice<T>(value, Regex.Replace(value.ToString(), "([a-z])([A-Z])", "$1 $2"))), selected, width);

    public static T Value<T>(ComboBox choice) => choice.SelectedItem is OfficeChoice<T> selected ? selected.Value : throw new InvalidOperationException("Choose a value.");

    public static StackPanel Field(string label, UIElement input)
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(OfficeTheme.Label(label)); panel.Children.Add(input);
        return panel;
    }
}
