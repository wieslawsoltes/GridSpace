namespace GridSpace.Controls;

public sealed class FormulaBarControl : UserControl
{
    private readonly TextBox _address = OfficeTheme.Field("Name box", 92);
    private readonly TextBox _formula = OfficeTheme.Field("Formula bar");
    private bool _updating, _finishing;
    private string _original = "";
    public bool IsEditing { get; private set; }
    public Func<string, bool>? CommitInput { get; set; }
    public Func<string, bool>? Navigate { get; set; }
    public event Action? EditingStarted;
    public event Action? FocusGridRequested;
    public event Action? FunctionRequested;
    public FormulaBarControl()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch; Height = 36;
        var root = new Grid { Padding = new Thickness(5, 3, 5, 3), Background = OfficeTheme.Brush("#F6F6F6"), ColumnSpacing = 3 };
        foreach (var width in new[] { new GridLength(96), new GridLength(1), new GridLength(28), new GridLength(28), new GridLength(30), new GridLength(1, GridUnitType.Star) }) root.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        root.Children.Add(_address);
        Add(new Border { Background = OfficeTheme.Brush("#CCCCCC"), Margin = new Thickness(0, 4, 0, 4) }, 1);
        Add(Icon(OfficeIconKind.Close, "Cancel formula", () => { Cancel(); FocusGridRequested?.Invoke(); }), 2);
        Add(Icon(OfficeIconKind.Check, "Enter formula", () => { if (Commit()) FocusGridRequested?.Invoke(); }), 3);
        Add(new OfficeButton("fx", () => FunctionRequested?.Invoke()) { FontSize = 16 }, 4);
        Add(_formula, 5); Content = root;
        _address.KeyDown += (_, e) => { if (e.Key == VirtualKey.Enter) { if (Navigate?.Invoke(_address.Text) == true) FocusGridRequested?.Invoke(); e.Handled = true; } };
        _formula.GotFocus += (_, _) => { if (_updating || IsEditing) return; _original = _formula.Text; IsEditing = true; EditingStarted?.Invoke(); };
        _formula.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Enter) { if (Commit()) FocusGridRequested?.Invoke(); e.Handled = true; }
            else if (e.Key == VirtualKey.Escape) { Cancel(); FocusGridRequested?.Invoke(); e.Handled = true; }
        };
        _formula.LostFocus += (_, _) => { if (!_finishing) Commit(); };
        void Add(UIElement element, int column) { Grid.SetColumn(element, column); root.Children.Add(element); }
    }
    private static OfficeButton Icon(OfficeIconKind kind, string name, Action action)
    {
        var button = new OfficeButton { Content = new OfficeIcon { Kind = kind, Width = 16, Height = 16 }, Padding = new Thickness(3) };
        AutomationProperties.SetName(button, name); button.Click += (_, _) => action(); return button;
    }
    public void Update(string address, string input)
    {
        _updating = true;
        try { if (_address.FocusState == FocusState.Unfocused) _address.Text = address; if (!IsEditing) _formula.Text = input; }
        finally { _updating = false; }
    }
    public bool Commit()
    {
        if (!IsEditing || _finishing) return true;
        _finishing = true;
        try
        {
            if (_formula.Text != _original && CommitInput?.Invoke(_formula.Text) != true) return false;
            IsEditing = false; return true;
        }
        finally { _finishing = false; }
    }
    public void Cancel() { _finishing = true; _formula.Text = _original; IsEditing = false; _finishing = false; }
    public void FocusFormula() { _formula.Focus(FocusState.Programmatic); _formula.SelectionStart = _formula.Text.Length; }
}
