using System.Globalization;
using GridSpace.Core;
using GridSpace.Formulas;

namespace GridSpace.Controls;

/// <summary>Embeddable immediate-preview chart inspector. Hosts commit changes through their own transaction boundary.</summary>
public sealed class ChartEditorControl : UserControl
{
    private readonly StackPanel _root = new() { Spacing = 10, Padding = new Thickness(12) };
    private ChartSpec _document = new();
    private ChartData? _data;
    private string[] _sheets = [];
    private string _host = "";
    private bool _loading;
    public Func<ChartSpec, bool>? CommitChanges { get; set; }
    public event Action? CloseRequested;

    public ChartEditorControl()
    {
        Content = new ScrollViewer { Content = _root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        AutomationProperties.SetAutomationId(this, "ChartInspector");
    }

    public void Bind(ChartSpec document, string hostSheet, IEnumerable<string> sheets, ChartData? data)
    {
        _document = document.CloneDocument(); _host = hostSheet; _sheets = sheets.ToArray(); _data = data;
        Build();
    }

    private void Build()
    {
        _loading = true;
        try
        {
            _root.Children.Clear();
            var header = new Grid(); header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(OfficeTheme.Label("Format Chart", 19));
            var close = new OfficeButton("×", () => CloseRequested?.Invoke()) { Width = 28, FontSize = 18 };
            Grid.SetColumn(close, 1); header.Children.Add(close); _root.Children.Add(header);
            var help = OfficeTheme.Label("Drag the chart or its eight handles. Double-click its title to edit.", 11, "#666666");
            help.TextWrapping = TextWrapping.Wrap; _root.Children.Add(help);
            Text("Chart title", _document.Title, value => Change(c => c with { Title = value }));
            Choice("Chart type", _document.Kind, value => Change(c => c with { Kind = value }));
            Choice("Chart grouping", _document.Grouping, value => Change(c => c with { Grouping = value }));
            Choice("Chart legend", _document.Legend, value => Change(c => c with { Legend = value }));
            Section("Data source");
            var sheet = OfficeForm.Choice("Chart source sheet", _sheets.Select(s => new OfficeChoice<string>(s, s)), _document.SourceSheet ?? _host);
            sheet.SelectionChanged += (_, _) => Change(c => c with { SourceSheet = OfficeForm.Value<string>(sheet), SourceUnavailable = false, PivotTableId = null });
            _root.Children.Add(OfficeForm.Field("Worksheet", sheet));
            Text("Chart source range", _document.Range, value => Change(c => c with { Range = value, SourceUnavailable = false, PivotTableId = null, Series = [], Categories = null }));
            Check("Series in rows", _document.SeriesInRows, value => Change(c => c with { SeriesInRows = value, Series = [], Categories = null }));
            Check("First row/column has headers", _document.HasHeaders, value => Change(c => c with { HasHeaders = value }));
            Check("Plot hidden cells", _document.PlotHiddenCells, value => Change(c => c with { PlotHiddenCells = value }));
            Text("Chart categories", _document.Categories ?? "", value => Change(c => c with { Categories = string.IsNullOrWhiteSpace(value) ? null : value }));
            Section("Series");
            if (_document.Series.Count == 0)
            {
                foreach (var series in _data?.Series ?? []) _root.Children.Add(OfficeTheme.Label(series.Name + "  ·  " + series.ValuesRange, 11));
                _root.Children.Add(new OfficeButton("Customize series", () =>
                {
                    if (_data is null) return;
                    if (Change(c => c with { Categories = _data.CategoriesRange, Series = _data.Series.Select((s, i) =>
                        new ChartSeries { Name = s.Name, Values = s.ValuesRange, Color = ChartDataResolver.Palette[i % ChartDataResolver.Palette.Length] }).ToList() })) Build();
                }));
            }
            else
            {
                for (var i = 0; i < _document.Series.Count; i++)
                {
                    var index = i; var series = _document.Series[i];
                    var details = new StackPanel { Spacing = 6, Visibility = i == 0 ? Visibility.Visible : Visibility.Collapsed };
                    _root.Children.Add(new OfficeButton("▸ " + (i + 1) + ". " + series.Name, () => details.Visibility = details.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible)
                        { HorizontalContentAlignment = HorizontalAlignment.Left });
                    var previousRoot = _target; _target = details;
                    Text("Series " + i + " name", series.Name, value => Series(index, s => s with { Name = value }));
                    Text("Series " + i + " values", series.Values, value => Series(index, s => s with { Values = value }));
                    Text("Series " + i + " color", series.Color, value => Series(index, s => s with { Color = value }));
                    Check("Series " + i + " visible", series.Visible, value => Series(index, s => s with { Visible = value }));
                    if (_document.Kind == ChartKind.Combo)
                    {
                        var kind = OfficeForm.Choice("Series " + i + " type", new[] { ChartKind.Column, ChartKind.Line, ChartKind.Area }.Select(k => new OfficeChoice<ChartKind>(k, k.ToString())), series.Kind ?? ChartKind.Column);
                        kind.SelectionChanged += (_, _) => Series(index, s => s with { Kind = OfficeForm.Value<ChartKind>(kind) }); details.Children.Add(kind);
                        Check("Series " + i + " secondary axis", series.SecondaryAxis, value => Series(index, s => s with { SecondaryAxis = value }));
                    }
                    var commands = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                    commands.Children.Add(new OfficeButton("↑", () => MoveSeries(index, -1)));
                    commands.Children.Add(new OfficeButton("↓", () => MoveSeries(index, 1)));
                    commands.Children.Add(new OfficeButton("Remove", () => { if (Change(c => c with { Series = c.Series.Where((_, n) => n != index).ToList() })) Build(); }));
                    details.Children.Add(commands); _target = previousRoot;
                    _root.Children.Add(details);
                }
                _root.Children.Add(new OfficeButton("Add series", () =>
                {
                    if (Change(c => c with { Series = c.Series.Append(new ChartSeries { Name = "Series " + (c.Series.Count + 1), Color = ChartDataResolver.Palette[c.Series.Count % ChartDataResolver.Palette.Length] }).ToList() })) Build();
                }));
                _root.Children.Add(new OfficeButton("Use automatic series", () => { if (Change(c => c with { Series = [], Categories = null })) Build(); }));
            }
            Section("Chart elements");
            Check("Chart data labels", _document.ShowDataLabels, value => Change(c => c with { ShowDataLabels = value }));
            Check("Chart markers", _document.ShowMarkers, value => Change(c => c with { ShowMarkers = value }));
            Check("Chart gridlines", _document.ShowGridLines, value => Change(c => c with { ShowGridLines = value }));
            Text("Category axis title", _document.CategoryAxisTitle, value => Change(c => c with { CategoryAxisTitle = value }));
            Text("Value axis title", _document.ValueAxisTitle, value => Change(c => c with { ValueAxisTitle = value }));
            Text("Chart number format", _document.ValueFormat, value => Change(c => c with { ValueFormat = value }));
            Text("Axis minimum", _document.Minimum?.ToString(CultureInfo.InvariantCulture) ?? "", value => Change(c => c with { Minimum = OptionalNumber(value) }));
            Text("Axis maximum", _document.Maximum?.ToString(CultureInfo.InvariantCulture) ?? "", value => Change(c => c with { Maximum = OptionalNumber(value) }));
            Text("Gap width percent", _document.GapWidth.ToString(CultureInfo.InvariantCulture), value => Change(c => c with { GapWidth = (int)Number(value) }));
            Text("Doughnut hole percent", _document.HoleSize.ToString(CultureInfo.InvariantCulture), value => Change(c => c with { HoleSize = (int)Number(value) }));
            Section("Size and appearance");
            Text("Chart width", _document.Width.ToString("0.##", CultureInfo.InvariantCulture), value => Change(c => c with { Width = Number(value) }));
            Text("Chart height", _document.Height.ToString("0.##", CultureInfo.InvariantCulture), value => Change(c => c with { Height = Number(value) }));
            Text("Chart background", _document.Background, value => Change(c => c with { Background = value }));
            Text("Chart foreground", _document.Foreground, value => Change(c => c with { Foreground = value }));
        }
        finally { _loading = false; }
    }

    private StackPanel? _target;
    private StackPanel Target => _target ?? _root;
    private void Section(string title) => _root.Children.Add(new Border { BorderBrush = OfficeTheme.Brush("#DDDDDD"), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 10, 0, 0), Child = OfficeTheme.Label(title, 13, "#107C41") });
    private void Text(string name, string initial, Func<string, bool> commit)
    {
        var field = OfficeTheme.Field(name); field.Text = initial; var last = initial;
        void Apply()
        {
            if (_loading || field.Text == last) return;
            try { if (commit(field.Text)) last = field.Text; else field.Text = last; }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or FormatException or OverflowException)
            { field.Text = last; ToolTipService.SetToolTip(field, error.Message); }
        }
        field.LostFocus += (_, _) => Apply();
        field.KeyDown += (_, e) => { if (e.Key == VirtualKey.Enter) { Apply(); e.Handled = true; } };
        Target.Children.Add(OfficeForm.Field(name, field));
    }
    private void Choice<T>(string name, T initial, Func<T, bool> commit) where T : struct, Enum
    {
        var field = OfficeForm.EnumChoice(name, initial); var committedIndex = field.SelectedIndex; var reverting = false;
        field.SelectionChanged += (_, _) =>
        {
            if (_loading || reverting) return;
            if (commit(OfficeForm.Value<T>(field))) committedIndex = field.SelectedIndex;
            else { reverting = true; try { field.SelectedIndex = committedIndex; } finally { reverting = false; } }
        };
        Target.Children.Add(OfficeForm.Field(name, field));
    }
    private void Check(string name, bool initial, Func<bool, bool> commit)
    {
        var box = new CheckBox { Content = name, IsChecked = initial, FontSize = 12, MinHeight = 24 };
        AutomationProperties.SetAutomationId(box, name.Replace(" ", ""));
        box.Checked += (_, _) => { if (!_loading) commit(true); }; box.Unchecked += (_, _) => { if (!_loading) commit(false); };
        Target.Children.Add(box);
    }
    private bool Change(Func<ChartSpec, ChartSpec> edit)
    {
        if (_loading) return false;
        try
        {
            var next = edit(_document.CloneDocument()); next.Validate();
            if (CommitChanges?.Invoke(next) != true) return false;
            _document = next; return true;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or FormatException)
        {
            ToolTipService.SetToolTip(this, error.Message);
            return false;
        }
    }
    private bool Series(int index, Func<ChartSeries, ChartSeries> edit) => Change(c =>
    {
        var items = c.Series.ToList(); if (index >= items.Count) return c; items[index] = edit(items[index]); return c with { Series = items };
    });
    private void MoveSeries(int index, int delta)
    {
        var next = index + delta;
        if (next < 0 || next >= _document.Series.Count) return;
        if (Change(c => { var items = c.Series.ToList(); (items[index], items[next]) = (items[next], items[index]); return c with { Series = items }; })) Build();
    }
    private static double Number(string text) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value)
        ? value : throw new ArgumentException("Enter a finite number.");
    private static double? OptionalNumber(string text) => string.IsNullOrWhiteSpace(text) ? null : Number(text);
}
