using GridSpace.Core;

namespace GridSpace.Controls;

/// <summary>Virtualized value checklist and two-condition filter editor. Search never discards hidden checklist selections.</summary>
public sealed class FilterEditorControl : UserControl
{
    private sealed record ValueOption(string Value)
    {
        public override string ToString() => Value.Length == 0 ? "(Blanks)" : Value;
    }
    private readonly int _column;
    private readonly ValueOption[] _values;
    private readonly HashSet<string> _selected;
    private readonly ComboBox _mode;
    private readonly ListView _list = new() { SelectionMode = ListViewSelectionMode.Multiple, IsMultiSelectCheckBoxEnabled = true, Height = 205 };
    private readonly TextBox _search = OfficeTheme.Field("Filter search");
    private readonly ComboBox _first;
    private readonly ComboBox _second;
    private readonly ComboBox _join;
    private readonly TextBox _firstValue = OfficeTheme.Field("Filter first value");
    private readonly TextBox _secondValue = OfficeTheme.Field("Filter second value");
    private readonly CheckBox _useSecond = new() { Content = "Use a second condition" };
    private bool _updating;

    public FilterEditorControl(int column, IReadOnlyList<string> values, ColumnFilter? current = null)
    {
        if (values.Count > 10000) throw new InvalidOperationException("More than 10,000 distinct values: use a custom filter through the Data command.");
        _column = column;
        _values = values.Concat(current?.Values ?? []).Append("").Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).Select(v => new ValueOption(v)).ToArray();
        _selected = current?.Values is { } accepted ? accepted.ToHashSet(StringComparer.OrdinalIgnoreCase) : _values.Select(v => v.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (current?.Values is not null && current.IncludeBlank) _selected.Add("");
        _mode = OfficeForm.Choice("Filter mode", new[] { new OfficeChoice<int>(0, "Select values"), new OfficeChoice<int>(1, "Custom conditions") }, current?.First is null ? 0 : 1);
        _first = OfficeForm.EnumChoice("Filter first operator", current?.First?.Operator ?? FilterOperator.Contains);
        _second = OfficeForm.EnumChoice("Filter second operator", current?.Second?.Operator ?? FilterOperator.LessThan);
        _join = OfficeForm.Choice("Filter join", new[] { new OfficeChoice<bool>(true, "And — both conditions"), new OfficeChoice<bool>(false, "Or — either condition") }, current?.And ?? true);
        _firstValue.Text = current?.First?.Value ?? ""; _secondValue.Text = current?.Second?.Value ?? "";
        _useSecond.IsChecked = current?.Second is not null;
        AutomationProperties.SetAutomationId(_useSecond, "FilterUseSecond");
        _search.PlaceholderText = "Search values";
        var root = new StackPanel { Spacing = 8, MinWidth = 260, MaxWidth = 340 };
        root.Children.Add(_mode);
        var valuesPanel = new StackPanel { Spacing = 5 };
        valuesPanel.Children.Add(_search);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        buttons.Children.Add(new OfficeButton("Select visible", () => SelectVisible(true)));
        buttons.Children.Add(new OfficeButton("Clear visible", () => SelectVisible(false)));
        valuesPanel.Children.Add(buttons); valuesPanel.Children.Add(_list); root.Children.Add(valuesPanel);
        var custom = new StackPanel { Spacing = 8 };
        custom.Children.Add(OfficeForm.Field("Show rows where the value", _first)); custom.Children.Add(_firstValue); custom.Children.Add(_useSecond);
        var second = new StackPanel { Spacing = 8 }; second.Children.Add(_join); second.Children.Add(_second); second.Children.Add(_secondValue); custom.Children.Add(second); root.Children.Add(custom);
        void Update()
        {
            var isValues = OfficeForm.Value<int>(_mode) == 0;
            valuesPanel.Visibility = isValues ? Visibility.Visible : Visibility.Collapsed;
            custom.Visibility = isValues ? Visibility.Collapsed : Visibility.Visible;
            second.Visibility = _useSecond.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }
        _mode.SelectionChanged += (_, _) => Update(); _useSecond.Checked += (_, _) => Update(); _useSecond.Unchecked += (_, _) => Update();
        _search.TextChanged += (_, _) => RefreshValues();
        _list.SelectionChanged += (_, e) =>
        {
            if (_updating) return;
            foreach (var item in e.RemovedItems.OfType<ValueOption>()) _selected.Remove(item.Value);
            foreach (var item in e.AddedItems.OfType<ValueOption>()) _selected.Add(item.Value);
        };
        Content = root; Update(); RefreshValues();
    }

    private void RefreshValues()
    {
        _updating = true;
        try
        {
            var visible = _values.Where(v => v.ToString().Contains(_search.Text, StringComparison.OrdinalIgnoreCase)).ToArray();
            _list.ItemsSource = visible; _list.SelectedItems.Clear();
            foreach (var item in visible) if (_selected.Contains(item.Value)) _list.SelectedItems.Add(item);
        }
        finally { _updating = false; }
    }
    private void SelectVisible(bool select)
    {
        foreach (var item in _values.Where(v => v.ToString().Contains(_search.Text, StringComparison.OrdinalIgnoreCase)))
            if (select) _selected.Add(item.Value); else _selected.Remove(item.Value);
        RefreshValues();
    }
    public ColumnFilter BuildFilter()
    {
        ColumnFilter result;
        if (OfficeForm.Value<int>(_mode) == 0)
        {
            if (_selected.Count == 0) throw new InvalidOperationException("Select at least one value or use custom conditions.");
            result = new ColumnFilter { Column = _column, Values = _selected.Where(v => v.Length > 0).ToArray(), IncludeBlank = _selected.Contains("") };
        }
        else result = new ColumnFilter
        {
            Column = _column, First = new FilterCondition(OfficeForm.Value<FilterOperator>(_first), _firstValue.Text),
            Second = _useSecond.IsChecked == true ? new FilterCondition(OfficeForm.Value<FilterOperator>(_second), _secondValue.Text) : null,
            And = OfficeForm.Value<bool>(_join)
        };
        result.Validate(); return result;
    }
}
