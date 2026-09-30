using System.Globalization;
using GridSpace.Core;

namespace GridSpace.Controls;

public sealed partial class ChartEditorControl
{
    private readonly Dictionary<string, Action<string>> _textSynchronizers = new(StringComparer.Ordinal);
    public string BoundChartId => _document.Id;
    public int VisualBuildCount { get; private set; }
    public int GeometrySyncCount { get; private set; }

    /// <summary>
    /// Synchronizes placement and size without rebuilding controls, resolving data,
    /// or replacing a focused text draft. Invoke on the control's owning UI thread.
    /// </summary>
    public void SynchronizeGeometry(ChartSpec document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Id != _document.Id)
            throw new InvalidOperationException("Geometry belongs to a different chart.");
        var next = _document with
        {
            Row = document.Row, Column = document.Column,
            OffsetX = document.OffsetX, OffsetY = document.OffsetY,
            Width = document.Width, Height = document.Height
        };
        next.Validate();
        var wasLoading = _loading;
        _loading = true;
        try
        {
            _document = next;
            Refresh("Chart width", next.Width);
            Refresh("Chart height", next.Height);
            GeometrySyncCount++;
        }
        finally { _loading = wasLoading; }

        void Refresh(string name, double value)
        {
            if (_textSynchronizers.TryGetValue(name, out var synchronize))
                synchronize(value.ToString("0.##", CultureInfo.InvariantCulture));
        }
    }

    private static OfficeButton ActionButton(string id, string label, Action action)
    {
        var button = new OfficeButton(label, action);
        AutomationProperties.SetAutomationId(button, id);
        return button;
    }
}
