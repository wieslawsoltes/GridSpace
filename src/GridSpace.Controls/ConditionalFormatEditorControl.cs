using System.Globalization;
using GridSpace.Core;

namespace GridSpace.Controls;

/// <summary>Reusable conditional-rule editor. Unset differential properties retain lower-priority or base formatting.</summary>
public sealed class ConditionalFormatEditorControl : UserControl
{
    private readonly ConditionalFormatRule _original;
    private readonly TextBox _range = OfficeTheme.Field("Conditional range");
    private readonly OfficeChoiceBox _kind;
    private readonly OfficeChoiceBox _comparison;
    private readonly TextBox _operand = OfficeTheme.Field("Conditional operand");
    private readonly TextBox _operand2 = OfficeTheme.Field("Conditional second operand");
    private readonly TextBox _rank = OfficeTheme.Field("Conditional rank");
    private readonly TextBox _background = OfficeTheme.Field("Conditional fill color");
    private readonly TextBox _foreground = OfficeTheme.Field("Conditional font color");
    private readonly TextBox _number = OfficeTheme.Field("Conditional number format");
    private readonly TextBox _low = OfficeTheme.Field("Conditional low color");
    private readonly TextBox _middle = OfficeTheme.Field("Conditional middle color");
    private readonly TextBox _high = OfficeTheme.Field("Conditional high color");
    private readonly CheckBox _percent = new() { Content = "Percent of numeric values" };
    private readonly CheckBox _stop = new() { Content = "Stop If True" };
    private readonly CheckBox _three = new() { Content = "Three-color scale (median midpoint)" };
    private readonly CheckBox _show = new() { Content = "Show cell values" };
    private readonly CheckBox _bold = new() { Content = "Bold", IsThreeState = true };
    private readonly CheckBox _italic = new() { Content = "Italic", IsThreeState = true };
    private readonly CheckBox _underline = new() { Content = "Underline", IsThreeState = true };

    public ConditionalFormatEditorControl(ConditionalFormatRule rule)
    {
        _original = rule; _range.Text = rule.Range; _operand.Text = rule.Operand; _operand2.Text = rule.Operand2;
        _kind = OfficeForm.EnumChoice("Conditional kind", rule.Kind);
        _comparison = OfficeForm.EnumChoice("Conditional comparison", rule.Comparison);
        _rank.Text = rule.Rank.ToString(CultureInfo.InvariantCulture); _percent.IsChecked = rule.Percent;
        _stop.IsChecked = rule.StopIfTrue; _three.IsChecked = rule.ThreeColorScale; _show.IsChecked = rule.ShowValue;
        _background.Text = rule.Style.Background ?? ""; _foreground.Text = rule.Style.Foreground ?? ""; _number.Text = rule.Style.NumberFormat ?? "";
        _low.Text = rule.LowColor; _middle.Text = rule.MiddleColor; _high.Text = rule.HighColor;
        _bold.IsChecked = rule.Style.Bold; _italic.IsChecked = rule.Style.Italic; _underline.IsChecked = rule.Style.Underline;
        AutomationProperties.SetAutomationId(_stop, "ConditionalStopIfTrue");
        var root = new StackPanel { Spacing = 10, MinWidth = 310, MaxWidth = 460 };
        root.Children.Add(OfficeForm.Field("Applies to", _range)); root.Children.Add(OfficeForm.Field("Rule type", _kind));
        var comparison = OfficeForm.Field("Format cells where the value is", _comparison); root.Children.Add(comparison);
        var operand = OfficeForm.Field("Value, text or formula (relative to upper-left cell)", _operand); root.Children.Add(operand);
        var operand2 = OfficeForm.Field("Second value or formula", _operand2); root.Children.Add(operand2);
        var rank = new StackPanel { Spacing = 4 }; rank.Children.Add(OfficeForm.Field("Rank", _rank)); rank.Children.Add(_percent); root.Children.Add(rank);
        var visual = new StackPanel { Spacing = 6 };
        visual.Children.Add(OfficeForm.Field("Minimum / negative color", _low));
        var middle = OfficeForm.Field("Midpoint color", _middle); visual.Children.Add(middle);
        visual.Children.Add(OfficeForm.Field("Maximum / positive color", _high)); visual.Children.Add(_three); visual.Children.Add(_show); root.Children.Add(visual);
        var style = new StackPanel { Spacing = 6 };
        style.Children.Add(OfficeForm.Field("Fill color (#RRGGBB, empty = unchanged)", _background));
        style.Children.Add(OfficeForm.Field("Font color (#RRGGBB, empty = unchanged)", _foreground));
        style.Children.Add(OfficeForm.Field("Number format (empty = unchanged)", _number));
        var flags = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 }; flags.Children.Add(_bold); flags.Children.Add(_italic); flags.Children.Add(_underline); style.Children.Add(flags);
        style.Children.Add(OfficeTheme.Label("Indeterminate checkboxes leave that property unchanged.", 11, "#666666"));
        style.Children.Add(_stop); root.Children.Add(style);
        void Update()
        {
            var kind = OfficeForm.Value<ConditionalFormatKind>(_kind);
            var isVisual = kind is ConditionalFormatKind.ColorScale or ConditionalFormatKind.DataBar;
            comparison.Visibility = kind == ConditionalFormatKind.CellValue ? Visibility.Visible : Visibility.Collapsed;
            operand.Visibility = kind is ConditionalFormatKind.CellValue or ConditionalFormatKind.Expression or ConditionalFormatKind.ContainsText ? Visibility.Visible : Visibility.Collapsed;
            operand2.Visibility = kind == ConditionalFormatKind.CellValue && OfficeForm.Value<CellComparison>(_comparison) is CellComparison.Between or CellComparison.NotBetween ? Visibility.Visible : Visibility.Collapsed;
            rank.Visibility = kind is ConditionalFormatKind.Top or ConditionalFormatKind.Bottom ? Visibility.Visible : Visibility.Collapsed;
            visual.Visibility = isVisual ? Visibility.Visible : Visibility.Collapsed;
            middle.Visibility = kind == ConditionalFormatKind.ColorScale && _three.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            _three.Visibility = kind == ConditionalFormatKind.ColorScale ? Visibility.Visible : Visibility.Collapsed;
            _show.Visibility = kind == ConditionalFormatKind.DataBar ? Visibility.Visible : Visibility.Collapsed;
            style.Visibility = isVisual ? Visibility.Collapsed : Visibility.Visible;
        }
        _kind.SelectionChanged += (_, _) => Update(); _comparison.SelectionChanged += (_, _) => Update();
        _three.Checked += (_, _) => Update(); _three.Unchecked += (_, _) => Update();
        Content = new ScrollViewer { Content = root, MaxHeight = 455, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; Update();
    }
    public ConditionalFormatRule BuildRule()
    {
        if (!int.TryParse(_rank.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var rank)) throw new InvalidOperationException("Enter an integer rank.");
        var result = _original with
        {
            Range = CellRange.Parse(_range.Text).ToString(), Kind = OfficeForm.Value<ConditionalFormatKind>(_kind),
            Comparison = OfficeForm.Value<CellComparison>(_comparison), Operand = _operand.Text, Operand2 = _operand2.Text,
            Rank = rank, Percent = _percent.IsChecked == true, StopIfTrue = _stop.IsChecked == true,
            LowColor = _low.Text, MiddleColor = _middle.Text, HighColor = _high.Text,
            ThreeColorScale = _three.IsChecked == true, ShowValue = _show.IsChecked == true,
            Style = new DifferentialStyle
            {
                Background = string.IsNullOrWhiteSpace(_background.Text) ? null : _background.Text,
                Foreground = string.IsNullOrWhiteSpace(_foreground.Text) ? null : _foreground.Text,
                NumberFormat = string.IsNullOrWhiteSpace(_number.Text) ? null : _number.Text,
                Bold = _bold.IsChecked, Italic = _italic.IsChecked, Underline = _underline.IsChecked
            }
        };
        result.Validate(); return result;
    }
}
