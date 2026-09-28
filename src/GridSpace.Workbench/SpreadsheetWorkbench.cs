namespace GridSpace.Workbench;

/// <summary>A reusable local-first spreadsheet shell. Hosts provide storage; the workbench owns UI subscriptions and recovery scheduling.</summary>
public sealed partial class SpreadsheetWorkbench : UserControl, IDisposable
{
    private readonly IWorkbookStorage _storage;
    private readonly RibbonControl _ribbon = new();
    private readonly FormulaBarControl _formula = new();
    private readonly WorksheetTabsControl _tabs = new();
    private readonly SpreadsheetStatusBar _statusBar = new();
    private readonly TextBlock _title = OfficeTheme.Label("", 13, "#FFFFFF");
    private readonly TextBlock _saved = OfficeTheme.Label("Local workbook", 11, "#E1F0E6");
    private readonly DispatcherTimer _recoveryTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private CellAddress _formulaAddress;
    private string _status = "Ready";
    private bool _statusError, _executing, _writingRecovery, _disposed;
    private long _revision, _savedRevision;
    public SpreadsheetSession Session { get; }
    public SpreadsheetGrid Surface { get; } = new();
    public string CurrentStatus => _status;

    public SpreadsheetWorkbench(SpreadsheetSession session, IWorkbookStorage storage)
    {
        Session = session; _storage = storage; Surface.Session = session;
        HorizontalContentAlignment = HorizontalAlignment.Stretch; VerticalContentAlignment = VerticalAlignment.Stretch; Background = OfficeTheme.Brush("#FFFFFF");
        var root = new Grid();
        foreach (var height in new[] { new GridLength(46), new GridLength(140), new GridLength(36), new GridLength(1, GridUnitType.Star), new GridLength(32), new GridLength(27) }) root.RowDefinitions.Add(new RowDefinition { Height = height });
        Add(TitleBar(), 0); Add(_ribbon, 1); Add(_formula, 2); Add(Surface, 3); Add(_tabs, 4); Add(_statusBar, 5); Content = root;
        _ribbon.SetTabs(WorkbookRibbon.Create()); _ribbon.CommandRequested += RunCommand;
        _formula.EditingStarted += () => _formulaAddress = Session.ActiveCell;
        _formula.CommitInput = text => Try(() => Session.SetInput(text, _formulaAddress));
        _formula.Navigate = address => Try(() =>
        {
            if (!Surface.CommitEdit()) return;
            if (Session.Book.Names.TryGetValue(address, out var range))
            {
                var bang = range.LastIndexOf('!');
                if (bang >= 0)
                {
                    var name = range[..bang].Trim('='); if (name.StartsWith('\'')) name = name[1..^1].Replace("''", "'");
                    var target = Session.Book.FindSheet(name);
                    if (target is not null) Session.SwitchSheet(Session.Book.Sheets.IndexOf(target)); range = range[(bang + 1)..];
                }
                Session.Select(range);
            }
            else Session.Select(address);
            Surface.RevealSelection();
        });
        _formula.FocusGridRequested += Surface.FocusGrid; _formula.FunctionRequested += () => RunCommand("function");
        Surface.CommandRequested += RunCommand; Surface.Error += message => ShowStatus(message, true); Surface.ViewChanged += UpdateStatus;
        Surface.ContextRequested += ShowCellMenu;
        _tabs.SheetSelected += index => { if (_formula.Commit() && Surface.CommitEdit()) { Session.SwitchSheet(index); Surface.FocusGrid(); } };
        _tabs.AddRequested += () => RunCommand("add-sheet");
        _tabs.SheetContextRequested += index => { if (_formula.Commit() && Surface.CommitEdit()) { Session.SwitchSheet(index); RunCommand("sheet-menu"); } };
        _statusBar.ZoomRequested += Surface.SetZoom;
        Session.Changed += Changed; _recoveryTimer.Tick += RecoveryTick;
        InitializeDataTools();
        Loaded += (_, _) => { Update(); Surface.FocusGrid(); };
        Update();
        void Add(UIElement element, int row) { Grid.SetRow(element, row); root.Children.Add(element); }
    }
    private UIElement TitleBar()
    {
        var bar = new Grid { Background = OfficeTheme.Brush("#107C41"), Padding = new Thickness(12, 0, 14, 0), ColumnSpacing = 14 };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); bar.ColumnDefinitions.Add(new ColumnDefinition()); bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var quick = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
        var brand = OfficeTheme.Label("GridSpace", 17, "#FFFFFF"); brand.Margin = new Thickness(0, 0, 15, 0); quick.Children.Add(brand);
        foreach (var item in new[] { ("save", "Save", OfficeIconKind.Save), ("undo", "Undo", OfficeIconKind.Undo), ("redo", "Redo", OfficeIconKind.Redo) })
        {
            var button = new OfficeButton { Content = new OfficeIcon { Kind = item.Item3, Ink = "#FFFFFF", Width = 18, Height = 18 }, Background = OfficeTheme.Brush("#107C41"), Width = 30, Height = 30 };
            AutomationProperties.SetName(button, item.Item2); AutomationProperties.SetAutomationId(button, "Quick-" + item.Item1);
            ToolTipService.SetToolTip(button, item.Item2); button.Click += (_, _) => RunCommand(item.Item1); quick.Children.Add(button);
        }
        bar.Children.Add(quick);
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 13, VerticalAlignment = VerticalAlignment.Center }; title.Children.Add(_title); title.Children.Add(_saved); Grid.SetColumn(title, 1); bar.Children.Add(title);
        var search = new OfficeButton("Search commands", () => RunCommand("command-search")) { Background = OfficeTheme.Brush("#DDEEE4"), Width = 205, Height = 28, Foreground = OfficeTheme.Brush("#28583B") }; Grid.SetColumn(search, 2); bar.Children.Add(search);
        return bar;
    }
    private void Changed(object? sender, SessionChangedEventArgs args)
    {
        if (args.DocumentChanged) { _revision++; _recoveryTimer.Stop(); _recoveryTimer.Start(); }
        if (!args.DocumentChanged && args.Reason != "Selection") _status = args.Reason;
        Update();
    }
    private void Update()
    {
        _title.Text = Session.Book.Title + (Session.IsDirty ? " •" : ""); _title.TextTrimming = TextTrimming.CharacterEllipsis;
        _formula.Update(Session.Selection.ToString(), Session.FormulaInput);
        _formula.SetReadOnly(Session.IsSpillFollower);
        _tabs.Update(Session.Book); UpdateStatus();
    }
    private void UpdateStatus() => _statusBar.Update(_status, Session.SelectionSummary(), Surface.Viewport.Zoom, _statusError);
    public void ShowStatus(string message, bool error = false) { _status = message; _statusError = error; UpdateStatus(); }
    private bool Try(Action action)
    {
        try { action(); return true; }
        catch (Exception error) { ShowStatus(error.Message, true); return false; }
    }
    private async void RecoveryTick(object? sender, object args)
    {
        _recoveryTimer.Stop(); if (_writingRecovery || _disposed || _savedRevision == _revision) return;
        _writingRecovery = true; var revision = _revision;
        try { await _storage.WriteRecoveryAsync(Session.Book.ToJson()); _savedRevision = revision; _saved.Text = "Recovery saved · local"; }
        catch (Exception error) { _saved.Text = "Recovery unavailable"; ShowStatus("Recovery could not be saved: " + error.Message, true); }
        finally { _writingRecovery = false; if (!_disposed && revision != _revision) _recoveryTimer.Start(); }
    }
    private async void RunCommand(string id) => await ExecuteAsync(id);
    public async Task ExecuteAsync(string id)
    {
        if (_executing || _disposed) return; _executing = true;
        try
        {
            if (!_formula.Commit() || !Surface.CommitEdit()) return;
            if (!await ExecuteDataToolAsync(id)) await ExecuteCoreAsync(id);
            Update();
        }
        catch (Exception error) { ShowStatus(error.Message, true); }
        finally { _executing = false; }
    }
    private void ShowCellMenu(Windows.Foundation.Point point)
    {
        var menu = new MenuFlyout();
        foreach (var item in new[] { ("cut", "Cut"), ("copy", "Copy"), ("paste", "Paste"), ("paste-values", "Paste Values"), ("clear", "Clear Contents"), ("format-cells", "Format Cells…"), ("conditional-format", "Conditional Formatting…"), ("insert-row", "Insert Rows"), ("insert-column", "Insert Columns"), ("delete-row", "Delete Rows"), ("delete-column", "Delete Columns"), ("note", "New Note…") })
        {
            var command = new MenuFlyoutItem { Text = item.Item2 }; command.Click += (_, _) => RunCommand(item.Item1); menu.Items.Add(command);
        }
        menu.ShowAt(Surface, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions { Position = point });
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; _recoveryTimer.Stop(); _recoveryTimer.Tick -= RecoveryTick;
        Session.Changed -= Changed; Surface.ViewChanged -= UpdateStatus; Surface.CommandRequested -= RunCommand; Surface.ContextRequested -= ShowCellMenu; Surface.FilterRequested -= FilterRequested; Surface.Dispose();
    }
}
