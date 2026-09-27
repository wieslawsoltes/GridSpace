using GridSpace.Core;
using GridSpace.Editing;

namespace GridSpace.Controls;

/// <summary>Reusable ordered sort-level editor with header and case-sensitivity settings.</summary>
public sealed class SortEditorControl : UserControl
{
    private sealed record LevelRow(Grid View, OfficeChoiceBox Column, OfficeChoiceBox Direction);
    private readonly List<LevelRow> _rows = [];
    private readonly StackPanel _levels = new() { Spacing = 6 };
    private readonly OfficeChoice<int>[] _columns;
    private readonly CheckBox _header = new() { Content = "My data has headers", IsChecked = true };
    private readonly CheckBox _case = new() { Content = "Case sensitive" };
    public CellRange Range { get; }
    public bool HasHeader => _header.IsChecked == true;
    public bool CaseSensitive => _case.IsChecked == true;

    public SortEditorControl(SpreadsheetSession session, CellRange range)
    {
        Range = range.Normalized;
        _columns = Enumerable.Range(range.Left, range.Right - range.Left + 1).Select(c => new OfficeChoice<int>(c,
            CellAddress.ColumnName(c) + " · " + session.Calculation.Evaluate(session.Sheet, new CellAddress(range.Top, c)))).ToArray();
        _header.IsChecked = session.Sheet.SortHasHeader; _case.IsChecked = session.Sheet.SortCaseSensitive;
        AutomationProperties.SetAutomationId(_header, "SortHasHeaders"); AutomationProperties.SetAutomationId(_case, "SortCaseSensitive");
        var root = new StackPanel { Spacing = 12, MinWidth = 320 };
        root.Children.Add(OfficeTheme.Label("Range: " + range, 13)); root.Children.Add(_header);
        var add = new OfficeButton("Add Level", () => AddLevel(new SortLevel(_columns.FirstOrDefault(c => _rows.All(r => OfficeForm.Value<int>(r.Column) != c.Value))?.Value ?? range.Left)));
        AutomationProperties.SetAutomationId(add, "SortAddLevel"); root.Children.Add(add);
        root.Children.Add(new ScrollViewer { Content = _levels, MaxHeight = 300, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        root.Children.Add(_case);
        var existing = session.Sheet.SortLevels.Where(s => s.Column >= range.Left && s.Column <= range.Right).ToArray();
        if (existing.Length == 0) AddLevel(new SortLevel(Math.Clamp(session.ActiveCell.Column, range.Left, range.Right)));
        else foreach (var level in existing) AddLevel(level);
        Content = root;
    }

    private void AddLevel(SortLevel level)
    {
        if (_rows.Count >= 64) return;
        var column = OfficeForm.Choice("Sort column", _columns, level.Column, 205);
        var direction = OfficeForm.Choice("Sort order", new[] { new OfficeChoice<bool>(false, "A to Z / Smallest to Largest"), new OfficeChoice<bool>(true, "Z to A / Largest to Smallest") }, level.Descending, 215);
        var view = new Grid { ColumnSpacing = 5 };
        foreach (var width in new[] { 205d, 215, 28, 28, 28 }) view.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });
        var row = new LevelRow(view, column, direction); _rows.Add(row);
        var up = new OfficeButton("↑", () => Move(row, -1)); var down = new OfficeButton("↓", () => Move(row, 1));
        var remove = new OfficeButton("×", () => { if (_rows.Count > 1) { _rows.Remove(row); Rebuild(); } });
        view.Children.Add(column); Grid.SetColumn(direction, 1); view.Children.Add(direction);
        Grid.SetColumn(up, 2); view.Children.Add(up); Grid.SetColumn(down, 3); view.Children.Add(down); Grid.SetColumn(remove, 4); view.Children.Add(remove);
        Rebuild();
    }
    private void Move(LevelRow row, int offset)
    {
        var index = _rows.IndexOf(row); var target = index + offset;
        if (target < 0 || target >= _rows.Count) return;
        (_rows[index], _rows[target]) = (_rows[target], _rows[index]); Rebuild();
    }
    private void Rebuild()
    {
        _levels.Children.Clear();
        for (var i = 0; i < _rows.Count; i++)
        {
            AutomationProperties.SetAutomationId(_rows[i].Column, "SortColumn" + i);
            AutomationProperties.SetAutomationId(_rows[i].Direction, "SortDirection" + i);
            _levels.Children.Add(_rows[i].View);
        }
    }
    public IReadOnlyList<SortLevel> BuildLevels()
    {
        var result = _rows.Select(r => new SortLevel(OfficeForm.Value<int>(r.Column), OfficeForm.Value<bool>(r.Direction))).ToArray();
        if (result.Select(r => r.Column).Distinct().Count() != result.Length) throw new InvalidOperationException("Each sort level must use a different column.");
        return result;
    }
}
