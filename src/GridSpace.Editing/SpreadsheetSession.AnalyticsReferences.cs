using GridSpace.Core;

namespace GridSpace.Editing;

public sealed partial class SpreadsheetSession
{
    private void RejectPivotWrite(CellRange range)
    {
        if (Sheet.PivotTables.Any(p => p.OutputRange is { } text && CellRange.Parse(text).Intersects(range)))
            throw new InvalidOperationException("A PivotTable report cannot be partially overwritten. Edit its source or field layout instead.");
    }

    private void CheckAnalyticsAxisEdit(AxisEdit edit)
    {
        foreach (var pivot in Sheet.PivotTables)
        {
            if (pivot.OutputRange is not { } text) continue;
            var range = CellRange.Parse(text);
            var first = edit.Rows ? range.Top : range.Left;
            var last = edit.Rows ? range.Bottom : range.Right;
            if (edit.IsDeletion ? edit.Position <= last && edit.EndExclusive > first : edit.Position > first && edit.Position <= last)
                throw new InvalidOperationException("Rows or columns cannot be inserted or deleted through a PivotTable report.");
            if (edit.Map(range) is null) throw new InvalidOperationException("The edit would push a PivotTable outside the worksheet.");
        }
        foreach (var pivot in Book.Sheets.SelectMany(s => s.PivotTables))
        {
            if (!pivot.SourceSheet.Equals(Sheet.Name, StringComparison.OrdinalIgnoreCase)) continue;
            var old = CellRange.Parse(pivot.SourceRange);
            if (edit.Map(old) is not { } next || next.Top == next.Bottom || next.Count > 200_000 || next.Right - next.Left >= 256)
                throw new InvalidOperationException("The edit would remove or exceed a PivotTable source range.");
            if (edit.Rows && edit.IsDeletion && old.Top >= edit.Position && old.Top < edit.EndExclusive)
                throw new InvalidOperationException("Remove the PivotTable before deleting its source header.");
            if (!edit.Rows && pivot.Rows.Concat(pivot.Columns).Concat(pivot.Values.Select(v => v.Field)).Concat(pivot.Filters.Select(f => f.Field))
                .Any(field => edit.MapIndex(old.Left + field) is null))
                throw new InvalidOperationException("Remove references to a PivotTable field before deleting its source column.");
        }
    }

    private void TransformAnalytics(AxisEdit edit)
    {
        foreach (var host in Book.Sheets)
        {
            foreach (var chart in host.Charts)
            {
                if (host == Sheet)
                {
                    var anchor = edit.Map(new CellAddress(chart.Row, chart.Column));
                    if (anchor is null && !edit.IsDeletion) throw new InvalidOperationException("The edit would push a chart outside the worksheet.");
                    if (edit.Rows) chart.Row = anchor?.Row ?? edit.Position;
                    else chart.Column = anchor?.Column ?? edit.Position;
                }
                if (chart.SourceUnavailable || !(chart.SourceSheet ?? host.Name).Equals(Sheet.Name, StringComparison.OrdinalIgnoreCase)) continue;
                string Map(string range)
                {
                    if (edit.Map(CellRange.Parse(range)) is { } mapped) return mapped.ToString();
                    if (!edit.IsDeletion) throw new InvalidOperationException("The edit would push chart data outside the worksheet.");
                    chart.SourceUnavailable = true;
                    return range; // retain the broken binding without reconnecting to subsequently inserted data
                }
                chart.Range = Map(chart.Range);
                if (chart.Categories is not null) chart.Categories = Map(chart.Categories);
                chart.Series = chart.Series.Select(series => series with { Values = Map(series.Values) }).ToList();
            }
            foreach (var pivot in host.PivotTables)
            {
                if (host == Sheet)
                {
                    pivot.Destination = edit.Map(pivot.Anchor)!.Value.ToString();
                    if (pivot.OutputRange is { } output) pivot.OutputRange = edit.Map(CellRange.Parse(output))!.Value.ToString();
                    if (pivot.ChartRange is { } chart) pivot.ChartRange = edit.Map(CellRange.Parse(chart))!.Value.ToString();
                }
                if (!pivot.SourceSheet.Equals(Sheet.Name, StringComparison.OrdinalIgnoreCase)) continue;
                var old = CellRange.Parse(pivot.SourceRange);
                var next = edit.Map(old)!.Value;
                pivot.SourceRange = next.ToString();
                if (edit.Rows || next.Right - next.Left == old.Right - old.Left) continue;
                int Field(int index) => edit.MapIndex(old.Left + index)!.Value - next.Left;
                pivot.Rows = pivot.Rows.Select(Field).ToList(); pivot.Columns = pivot.Columns.Select(Field).ToList();
                pivot.Values = pivot.Values.Select(v => v with { Field = Field(v.Field) }).ToList();
                pivot.Filters = pivot.Filters.Select(f => f with { Field = Field(f.Field) }).ToList();
                var headers = Enumerable.Range(0, next.Right - next.Left + 1).Select(i => "New field " + (i + 1)).ToArray();
                for (var i = 0; i < pivot.FieldNames.Length; i++)
                    if (edit.MapIndex(old.Left + i) is { } mapped) headers[mapped - next.Left] = pivot.FieldNames[i];
                pivot.FieldNames = headers;
                pivot.Cache = null; // schema changed; old cached records cannot be interpreted with new field indexes
            }
        }
    }
}
