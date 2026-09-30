using System.Xml.Linq;
using GridSpace.Core;
using GridSpace.Formulas;

namespace GridSpace.IO;

public static partial class XlsxWorkbook
{
    private static void WritePivotFieldHierarchy(PivotTableSpec spec, int field, List<CalcValue> values, XElement definition)
    {
        var level = spec.Rows.IndexOf(field);
        if (level < 0 || level >= spec.Rows.Count - 1 || definition.Element(S + "items") is not { } items) return;
        // A shared field-item flag cannot express different states for the same
        // member under two parents. Only top-level collapse is emitted as sd;
        // rowItems below and the native extension retain the full typed paths.
        if (level == 0)
        {
            var collapsed = spec.CollapsedRows.Where(p => p.Fields.Length == 1).Select(PivotHierarchy.Key).ToHashSet();
            foreach (var item in items.Elements(S + "item"))
            {
                var index = Int(item.Attribute("x"), -1);
                if (index >= 0 && index < values.Count && collapsed.Contains(new PivotKey([values[index]]))) item.SetAttributeValue("sd", 0);
            }
        }
        if (spec.Subtotals != PivotSubtotals.None) items.Add(E("item", new XAttribute("t", "default")));
        items.SetAttributeValue("count", items.Elements().Count());
    }

    private static XElement WritePivotRowItems(PivotReport report, Func<int, CalcValue, int> index)
    {
        var result = E("rowItems", new XAttribute("count", report.RowBands.Count));
        foreach (var band in report.RowBands)
        {
            var type = band.Kind == PivotRowKind.GrandTotal ? "grand" : band.Kind == PivotRowKind.Subtotal ? "default" : null;
            var row = E("i", type is null ? null : new XAttribute("t", type));
            for (var level = 0; level < band.Key.Items.Count; level++)
                row.Add(E("x", new XAttribute("v", Math.Max(0, index(report.Definition.Rows[level], band.Key.Items[level])))));
            if (!row.HasElements) row.Add(E("x"));
            result.Add(row);
        }
        return result;
    }

    private static void ReadPivotHierarchy(XElement root, XElement[] fields, CalcValue[][] shared, PivotSource source, PivotTableSpec spec)
    {
        spec.Layout = Flag(root.Attribute("compact")) ? PivotLayout.Compact : Flag(root.Attribute("outline")) ? PivotLayout.Outline : PivotLayout.Tabular;
        spec.RepeatRowLabels = false;
        if (spec.Rows.Any(f => f < 0 || f >= fields.Length)) throw new InvalidDataException("Invalid PivotTable row field.");
        var parents = spec.Rows.Take(Math.Max(0, spec.Rows.Count - 1)).Select(f => fields[f]).ToArray();
        var subtotals = parents.Where(f => f.Attribute("defaultSubtotal") is null || Flag(f.Attribute("defaultSubtotal"))).ToArray();
        spec.Subtotals = subtotals.Length == 0 ? PivotSubtotals.None
            : subtotals.Any(f => f.Attribute("subtotalTop") is null || Flag(f.Attribute("subtotalTop"))) ? PivotSubtotals.Top : PivotSubtotals.Bottom;
        for (var level = 0; level < spec.Rows.Count - 1; level++)
        {
            var field = spec.Rows[level];
            var collapsed = fields[field].Element(S + "items")?.Elements(S + "item")
                .Where(e => e.Attribute("sd") is not null && !Flag(e.Attribute("sd")))
                .Select(e => Int(e.Attribute("x"), -1)).Where(i => i >= 0 && i < shared[field].Length)
                .Select(i => new PivotKey([shared[field][i]])).ToHashSet() ?? [];
            if (collapsed.Count == 0) continue;
            var prefixes = new HashSet<PivotKey>();
            foreach (var row in source.Rows)
            {
                if (!collapsed.Contains(new PivotKey([row[field]]))) continue;
                var key = new PivotKey(spec.Rows.Take(level + 1).Select(f => row[f]));
                if (!prefixes.Add(key)) continue;
                spec.CollapsedRows.Add(PivotHierarchy.Path(spec, key));
                if (spec.CollapsedRows.Count > 10_000) throw new InvalidDataException("Too many collapsed PivotTable groups.");
            }
        }
    }
}
