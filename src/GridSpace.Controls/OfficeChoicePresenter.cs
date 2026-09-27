namespace GridSpace.Controls;

/// <summary>A fixed-row virtual popup: only visible choices own Uno elements, and selection is independent of native list focus.</summary>
internal sealed class OfficeChoicePresenter : UserControl
{
    private const double RowHeight = 32;
    private readonly Canvas _rows = new() { Background = OfficeTheme.Brush("#FFFFFF") };
    private readonly SheetScrollBar _scroll = new() { Orientation = Orientation.Vertical, Width = 14, IsTabStop = false };
    private IReadOnlyList<object> _items = Array.Empty<object>();
    private int _top;
    public int SelectedIndex { get; private set; } = -1;
    public event Action<int>? ItemInvoked;
    private int VisibleCount => Math.Max(1, (int)Math.Floor(Math.Max(RowHeight, _rows.ActualHeight) / RowHeight));

    public OfficeChoicePresenter()
    {
        IsTabStop = true;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition());
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        root.Children.Add(_rows); Grid.SetColumn(_scroll, 1); root.Children.Add(_scroll);
        Content = root;
        _rows.SizeChanged += (_, _) => { EnsureSelectedVisible(); Rebuild(); };
        _scroll.ValueChanged += value => { _top = Math.Clamp((int)Math.Round(value / RowHeight), 0, Math.Max(0, _items.Count - VisibleCount)); Rebuild(); };
        PointerWheelChanged += (_, args) =>
        {
            var delta = args.GetCurrentPoint(this).Properties.MouseWheelDelta;
            _top = Math.Clamp(_top - Math.Sign(delta) * 3, 0, Math.Max(0, _items.Count - VisibleCount));
            Rebuild(); args.Handled = true;
        };
    }

    public void SetItems(IReadOnlyList<object> items, int selected)
    {
        _items = items;
        SelectedIndex = items.Count == 0 ? -1 : Math.Clamp(selected, 0, items.Count - 1);
        _top = 0; EnsureSelectedVisible(); Rebuild();
    }

    public void Preview(int index)
    {
        if (_items.Count == 0) return;
        SelectedIndex = Math.Clamp(index, 0, _items.Count - 1);
        EnsureSelectedVisible(); Rebuild();
    }

    private void EnsureSelectedVisible()
    {
        if (SelectedIndex < _top) _top = Math.Max(0, SelectedIndex);
        if (SelectedIndex >= _top + VisibleCount) _top = SelectedIndex - VisibleCount + 1;
        _top = Math.Clamp(_top, 0, Math.Max(0, _items.Count - VisibleCount));
    }

    private void Rebuild()
    {
        _rows.Children.Clear();
        var count = Math.Min(_items.Count - _top, VisibleCount);
        for (var slot = 0; slot < count; slot++)
        {
            var index = _top + slot;
            var label = OfficeTheme.Label(_items[index].ToString() ?? "", 13);
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            var button = new OfficeButton
            {
                Content = label, IsTabStop = false, Height = RowHeight,
                Width = Math.Max(1, _rows.ActualWidth), Padding = new Thickness(8, 4, 8, 4),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Background = OfficeTheme.Brush(index == SelectedIndex ? "#DCEDE2" : "#FFFFFF"),
                BorderBrush = OfficeTheme.Brush("#107C41"),
                BorderThickness = new Thickness(index == SelectedIndex ? 1 : 0)
            };
            AutomationProperties.SetName(button, label.Text);
            button.Click += (_, _) => { SelectedIndex = index; ItemInvoked?.Invoke(index); };
            Canvas.SetTop(button, slot * RowHeight); _rows.Children.Add(button);
        }
        _scroll.SetRange(Math.Max(0, _items.Count - VisibleCount) * RowHeight, VisibleCount * RowHeight, _top * RowHeight);
    }
}
