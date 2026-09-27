using System.Text;
using System.Text.Json;
using GridSpace.Editing;
using GridSpace.Controls;
using GridSpace.Formulas;
using GridSpace.Workbench;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace GridSpace.App;

/// <summary>Opt-in read-only model and hit-target snapshots. Tests still send actual keyboard and pointer events.</summary>
internal sealed class BrowserDiagnostics : IDisposable
{
    private readonly SpreadsheetSession _session;
    private readonly SpreadsheetWorkbench _workbench;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private ConditionalFormattingEngine? _conditional;
    private bool _disposed;

    public BrowserDiagnostics(SpreadsheetSession session, SpreadsheetWorkbench workbench)
    {
        _session = session;
        _workbench = workbench;
        session.Changed += Changed;
        workbench.Surface.ViewChanged += Publish;
        _timer.Tick += Tick;
        _timer.Start();
        Publish();
    }

    private void Changed(object? sender, SessionChangedEventArgs args) => Publish();
    private void Tick(object? sender, object args) => Publish();

    private void Publish()
    {
        if (_disposed) return;
        if (_conditional?.Calculation != _session.Calculation) _conditional = new(_session.Calculation);
        var value = _session.Calculation.Evaluate(_session.Sheet, _session.ActiveCell);
        var effective = _conditional.Evaluate(_session.Sheet, _session.ActiveCell, value);
        using var memory = new MemoryStream();
        using (var json = new Utf8JsonWriter(memory))
        {
            json.WriteStartObject();
            json.WriteBoolean("ready", true);
            json.WriteString("sheet", _session.Sheet.Name);
            json.WriteNumber("sheets", _session.Book.Sheets.Count);
            json.WriteString("selection", _session.Selection.ToString());
            json.WriteString("active", _session.ActiveCell.ToString());
            json.WriteString("input", _session.Sheet.Get(_session.ActiveCell).Input);
            json.WriteString("value", value.ToString());
            json.WriteNumber("cells", _session.Sheet.Cells.Count);
            json.WriteBoolean("bold", _session.SelectedStyle.Bold);
            json.WriteString("effectiveFill", effective.Style.Background);
            json.WriteBoolean("effectiveBold", effective.Style.Bold);
            json.WriteBoolean("dataBar", effective.DataBar is not null);
            json.WriteNumber("conditionalRules", _session.Sheet.ConditionalFormats.Count);
            json.WriteNumber("filterColumns", _session.Sheet.Filters.Count);
            json.WriteNumber("filteredRows", _session.Sheet.FilteredRows.Count);
            json.WriteNumber("manuallyHiddenRows", _session.Sheet.HiddenRows.Count);
            json.WriteNumber("sortLevels", _session.Sheet.SortLevels.Count);
            json.WriteBoolean("canUndo", _session.CanUndo);
            json.WriteBoolean("canRedo", _session.CanRedo);
            json.WriteBoolean("dirty", _session.IsDirty);
            json.WriteNumber("frozenRows", _session.Sheet.FrozenRows);
            json.WriteNumber("frozenColumns", _session.Sheet.FrozenColumns);
            json.WriteBoolean("gridlines", _session.Sheet.ShowGridLines);
            json.WriteNumber("zoom", _workbench.Surface.Viewport.Zoom);
            json.WriteNumber("scrollX", _workbench.Surface.Viewport.ScrollX);
            json.WriteNumber("scrollY", _workbench.Surface.Viewport.ScrollY);
            json.WriteNumber("fontFamilies", _workbench.Surface.Renderer.Fonts.RegisteredFamilyCount);
            json.WriteNumber("width", _workbench.Surface.ActualWidth);
            json.WriteNumber("height", _workbench.Surface.ActualHeight);
            json.WriteString("status", _workbench.CurrentStatus);
            json.WriteBoolean("overlayOpen", _workbench.DataToolOverlay is not null);
            json.WriteStartObject("controls");
            foreach (var (name, element) in Controls())
            {
                try
                {
                    var point = element.TransformToVisual(_workbench).TransformPoint(new Point(0, 0));
                    if (!double.IsFinite(point.X) || !double.IsFinite(point.Y)) continue;
                    json.WriteStartObject(name);
                    json.WriteNumber("x", point.X); json.WriteNumber("y", point.Y);
                    json.WriteNumber("width", element.ActualWidth); json.WriteNumber("height", element.ActualHeight);
                    if (element is TextBox text) json.WriteString("text", text.Text);
                    if (element is OfficeChoiceBox choice)
                    {
                        json.WriteNumber("selectedIndex", choice.SelectedIndex);
                        json.WriteNumber("previewIndex", choice.PreviewIndex);
                        json.WriteBoolean("expanded", choice.IsDropDownOpen);
                    }
                    else if (element is ComboBox combo) json.WriteNumber("selectedIndex", combo.SelectedIndex);
                    json.WriteEndObject();
                }
                catch (InvalidOperationException) { /* An opening/closing popup may not yet share a visual root. */ }
            }
            json.WriteEndObject();
            json.WriteEndObject();
        }
        BrowserFiles.PublishDiagnostics(Encoding.UTF8.GetString(memory.ToArray()));
    }

    private Dictionary<string, FrameworkElement> Controls()
    {
        var result = new Dictionary<string, FrameworkElement>(StringComparer.Ordinal);
        var budget = 5000;
        void Visit(DependencyObject node, int depth)
        {
            if (--budget < 0 || depth > 64) return;
            if (node is FrameworkElement element)
            {
                if (element.Visibility != Visibility.Visible) return;
                var name = AutomationProperties.GetAutomationId(element);
                if (string.IsNullOrEmpty(name)) name = element.Name;
                if (!string.IsNullOrEmpty(name) && element.ActualWidth > 0 && element.ActualHeight > 0)
                    result[name] = element;
            }
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) Visit(VisualTreeHelper.GetChild(node, i), depth + 1);
        }
        Visit(_workbench, 0);
        if (_workbench.DataToolOverlay is { } overlay) Visit(overlay, 0);
        return result;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop(); _timer.Tick -= Tick;
        _session.Changed -= Changed;
        _workbench.Surface.ViewChanged -= Publish;
    }
}
