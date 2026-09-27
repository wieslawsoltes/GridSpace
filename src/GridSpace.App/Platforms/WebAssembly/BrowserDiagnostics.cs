using System.Text;
using System.Text.Json;
using GridSpace.Editing;
using GridSpace.Workbench;

namespace GridSpace.App;

/// <summary>Opt-in, read-only state for testing real UI interactions. No mutation hooks are exposed.</summary>
internal sealed class BrowserDiagnostics : IDisposable
{
    private readonly SpreadsheetSession _session;
    private readonly SpreadsheetWorkbench _workbench;

    public BrowserDiagnostics(SpreadsheetSession session, SpreadsheetWorkbench workbench)
    {
        _session = session;
        _workbench = workbench;
        session.Changed += Changed;
        workbench.Surface.ViewChanged += Publish;
        Publish();
    }

    private void Changed(object? sender, SessionChangedEventArgs args) => Publish();

    private void Publish()
    {
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
            json.WriteString("value", _session.Calculation.Evaluate(_session.Sheet, _session.ActiveCell).ToString());
            json.WriteNumber("cells", _session.Sheet.Cells.Count);
            json.WriteBoolean("bold", _session.SelectedStyle.Bold);
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
            json.WriteEndObject();
        }
        BrowserFiles.PublishDiagnostics(Encoding.UTF8.GetString(memory.ToArray()));
    }

    public void Dispose()
    {
        _session.Changed -= Changed;
        _workbench.Surface.ViewChanged -= Publish;
    }
}
