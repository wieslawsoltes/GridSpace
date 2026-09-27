using System.Collections;

namespace GridSpace.Controls;

/// <summary>
/// Office-density choice control with a virtualized popup and explicit keyboard ownership.
/// Navigation previews a choice; Enter commits, Escape cancels, and Tab commits then advances focus.
/// </summary>
public sealed class OfficeChoiceBox : UserControl
{
    private sealed class ChoiceButton(OfficeChoiceBox owner) : OfficeButton
    {
        protected override void OnKeyDown(KeyRoutedEventArgs e)
        {
            if (!owner.HandleKey(e, false)) base.OnKeyDown(e);
        }
        protected override void OnKeyUp(KeyRoutedEventArgs e)
        {
            if (e.Key is VirtualKey.Enter or VirtualKey.Space) e.Handled = true;
            else base.OnKeyUp(e);
        }
    }

    private sealed class ChoiceList(OfficeChoiceBox owner) : ListView
    {
        protected override void OnKeyDown(KeyRoutedEventArgs e)
        {
            if (!owner.HandleKey(e, true)) base.OnKeyDown(e);
        }
        protected override void OnKeyUp(KeyRoutedEventArgs e)
        {
            if (e.Key is VirtualKey.Enter or VirtualKey.Space or VirtualKey.Escape) e.Handled = true;
            else base.OnKeyUp(e);
        }
    }

    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IEnumerable), typeof(OfficeChoiceBox),
        new PropertyMetadata(null, (sender, _) => ((OfficeChoiceBox)sender).ItemsChanged()));
    public static readonly DependencyProperty SelectedIndexProperty = DependencyProperty.Register(
        nameof(SelectedIndex), typeof(int), typeof(OfficeChoiceBox),
        new PropertyMetadata(-1, (sender, _) => ((OfficeChoiceBox)sender).IndexChanged()));
    public static readonly DependencyProperty SelectedItemProperty = DependencyProperty.Register(
        nameof(SelectedItem), typeof(object), typeof(OfficeChoiceBox),
        new PropertyMetadata(null, (sender, _) => ((OfficeChoiceBox)sender).ItemChanged()));

    private readonly ChoiceButton _button;
    private readonly TextBlock _label = OfficeTheme.Label("", 13);
    private readonly ChoiceList _list;
    private readonly Flyout _popup;
    private object[] _items = [];
    private bool _synchronizing;
    private bool _restoreFocus;
    private FocusNavigationDirection? _nextFocus;

    public IEnumerable? ItemsSource { get => (IEnumerable?)GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public int SelectedIndex { get => (int)GetValue(SelectedIndexProperty); set => SetValue(SelectedIndexProperty, value); }
    public object? SelectedItem { get => GetValue(SelectedItemProperty); set => SetValue(SelectedItemProperty, value); }
    public bool IsDropDownOpen { get; private set; }
    public int ItemCount => _items.Length;
    public int PreviewIndex => IsDropDownOpen ? _list.SelectedIndex : SelectedIndex;
    public event EventHandler? SelectionChanged;

    public OfficeChoiceBox()
    {
        IsTabStop = false;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        _button = new ChoiceButton(this)
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = OfficeTheme.Brush("#FFFFFF"), BorderBrush = OfficeTheme.Brush("#B8B8B8"),
            BorderThickness = new Thickness(1), MinHeight = 30, Padding = new Thickness(8, 4, 8, 4)
        };
        _label.TextTrimming = TextTrimming.CharacterEllipsis;
        var header = new Grid { ColumnSpacing = 8 };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        header.Children.Add(_label);
        var chevron = new OfficeIcon { Kind = OfficeIconKind.ChevronDown, Width = 12, Height = 12 };
        Grid.SetColumn(chevron, 1); header.Children.Add(chevron);
        _button.Content = header;
        _button.Click += (_, _) => { if (IsDropDownOpen) Close(false); else Open(); };
        Content = _button;

        _list = new ChoiceList(this)
        {
            SelectionMode = ListViewSelectionMode.Single, IsItemClickEnabled = true,
            FontFamily = OfficeTheme.Font, FontSize = 13,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        var itemStyle = new Style(typeof(ListViewItem));
        itemStyle.Setters.Add(new Setter(MinHeightProperty, 30d));
        itemStyle.Setters.Add(new Setter(PaddingProperty, new Thickness(8, 4, 8, 4)));
        itemStyle.Setters.Add(new Setter(HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        _list.ItemContainerStyle = itemStyle;
        _list.ItemClick += (_, args) =>
        {
            var index = Array.IndexOf(_items, args.ClickedItem);
            if (index >= 0) { _list.SelectedIndex = index; Close(true); }
        };
        _popup = new Flyout { Content = _list };
        _popup.Opened += (_, _) =>
        {
            _list.Focus(FocusState.Programmatic);
            if (_list.SelectedItem is { } item) _list.ScrollIntoView(item);
        };
        _popup.Closed += (_, _) =>
        {
            IsDropDownOpen = false;
            if (_restoreFocus && IsLoaded)
            {
                _button.Focus(FocusState.Programmatic);
                if (_nextFocus is { } direction) FocusManager.TryMoveFocus(direction);
            }
            _restoreFocus = false; _nextFocus = null;
        };
        Unloaded += (_, _) => { _restoreFocus = false; _nextFocus = null; _popup.Hide(); };
    }

    private void ItemsChanged()
    {
        var selected = SelectedItem;
        _items = ItemsSource?.Cast<object>().ToArray() ?? [];
        _list.ItemsSource = _items;
        var index = Array.IndexOf(_items, selected);
        SetSelection(index >= 0 ? index : _items.Length == 0 ? -1 : 0);
    }

    private void IndexChanged()
    {
        if (!_synchronizing) SetSelection(Math.Clamp(SelectedIndex, -1, _items.Length - 1));
    }

    private void ItemChanged()
    {
        if (!_synchronizing) SetSelection(Array.IndexOf(_items, SelectedItem));
    }

    private void SetSelection(int index)
    {
        var item = index < 0 ? null : _items[index];
        _synchronizing = true;
        try
        {
            SetValue(SelectedIndexProperty, index);
            SetValue(SelectedItemProperty, item);
            _label.Text = item?.ToString() ?? "";
            AutomationProperties.SetName(_button, _label.Text);
        }
        finally { _synchronizing = false; }
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Open()
    {
        if (IsDropDownOpen || !IsEnabled || _items.Length == 0 || XamlRoot is null) return;
        _list.ItemsSource = _items;
        _list.SelectedIndex = Math.Max(0, SelectedIndex);
        _list.Width = Math.Max(140, Math.Min(Math.Max(ActualWidth, 200), XamlRoot.Size.Width - 64));
        _list.Height = Math.Max(30, Math.Min(Math.Min(270, _items.Length * 30d), XamlRoot.Size.Height - 100));
        IsDropDownOpen = true; _restoreFocus = false; _nextFocus = null;
        try { _popup.ShowAt(_button); }
        catch { IsDropDownOpen = false; throw; }
    }

    public void Close(bool commit)
    {
        if (!IsDropDownOpen) return;
        var selected = _list.SelectedIndex;
        _restoreFocus = true;
        _popup.Hide();
        if (commit && selected >= 0 && selected != SelectedIndex) SetSelection(selected);
    }

    private bool HandleKey(KeyRoutedEventArgs e, bool popup)
    {
        var shift = Down(VirtualKey.Shift);
        var alt = Down(VirtualKey.Menu);
        var index = popup ? _list.SelectedIndex : SelectedIndex;
        switch (e.Key)
        {
            case VirtualKey.Enter:
            case VirtualKey.Space:
                if (IsDropDownOpen) Close(true); else Open();
                break;
            case VirtualKey.Escape when IsDropDownOpen: Close(false); break;
            case VirtualKey.Tab when IsDropDownOpen:
                _nextFocus = shift ? FocusNavigationDirection.Previous : FocusNavigationDirection.Next;
                Close(true); break;
            case VirtualKey.Down when alt: Open(); break;
            case VirtualKey.Up when alt && IsDropDownOpen: Close(true); break;
            case VirtualKey.F4:
                if (IsDropDownOpen) Close(true); else Open();
                break;
            case VirtualKey.Home: Navigate(0, popup); break;
            case VirtualKey.End: Navigate(_items.Length - 1, popup); break;
            case VirtualKey.Down: Navigate(index + 1, popup); break;
            case VirtualKey.Up: Navigate(index - 1, popup); break;
            case VirtualKey.PageDown: Navigate(index + 8, popup); break;
            case VirtualKey.PageUp: Navigate(index - 8, popup); break;
            default:
                if (Down(VirtualKey.Control) || alt || (int)e.Key < 65 || (int)e.Key > 90) return false;
                var prefix = ((char)e.Key).ToString();
                for (var i = 1; i <= _items.Length; i++)
                {
                    var candidate = (Math.Max(-1, index) + i) % _items.Length;
                    if (_items[candidate].ToString()?.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == true)
                    { Navigate(candidate, popup); break; }
                }
                break;
        }
        e.Handled = true;
        return true;
    }

    private void Navigate(int index, bool popup)
    {
        if (_items.Length == 0) return;
        index = Math.Clamp(index, 0, _items.Length - 1);
        if (popup)
        {
            _list.SelectedIndex = index;
            _list.ScrollIntoView(_items[index]);
        }
        else if (SelectedIndex != index) SetSelection(index);
    }

    private static bool Down(VirtualKey key) =>
        (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
}
