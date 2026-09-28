using System.IO.Compression;
using System.Xml.Linq;
using GridSpace.Core;
using GridSpace.Formulas;

namespace GridSpace.IO;

public static partial class XlsxWorkbook
{
    private static readonly XNamespace DynamicArrayNamespace = "http://schemas.microsoft.com/office/spreadsheetml/2017/dynamicarray";

    private static XElement DynamicArrayMetadata()
    {
        var type = E("metadataType", new XAttribute("name", "XLDAPR"), new XAttribute("minSupportedVersion", 120000));
        foreach (var attribute in new[] { "copy", "pasteAll", "pasteValues", "merge", "splitFirst", "rowColShift", "clearFormats", "clearComments", "assign", "coerce", "cellMeta" }) type.Add(new XAttribute(attribute, 1));
        return E("metadata", new XAttribute(XNamespace.Xmlns + "xda", DynamicArrayNamespace),
            E("metadataTypes", new XAttribute("count", 1), type),
            E("futureMetadata", new XAttribute("name", "XLDAPR"), new XAttribute("count", 1),
                E("bk", E("extLst", E("ext", new XAttribute("uri", "{bdbb8cdc-fa1e-496e-a857-3c3f30c029c3}"),
                    new XElement(DynamicArrayNamespace + "dynamicArrayProperties", new XAttribute("fDynamic", 1), new XAttribute("fCollapsed", 0)))))),
            E("cellMetadata", new XAttribute("count", 1), E("bk", E("rc", new XAttribute("t", 1), new XAttribute("v", 0)))));
    }

    private static HashSet<int> ReadDynamicMetadata(ZipArchive zip, Dictionary<string, string> relationships)
    {
        var result = new HashSet<int>();
        if (zip.GetEntry("xl/_rels/workbook.xml.rels") is null) return result;
        var relation = Xml(zip, "xl/_rels/workbook.xml.rels").Root!.Elements(P + "Relationship")
            .FirstOrDefault(r => (string?)r.Attribute("Type") == RelBase + "sheetMetadata" && (string?)r.Attribute("TargetMode") != "External");
        if (relation is null || !relationships.TryGetValue((string?)relation.Attribute("Id") ?? "", out var path)) return result;
        var metadata = Xml(zip, path).Root!;
        var types = metadata.Element(S + "metadataTypes")?.Elements(S + "metadataType").ToArray() ?? [];
        var futures = metadata.Elements(S + "futureMetadata").FirstOrDefault(f => (string?)f.Attribute("name") == "XLDAPR")?.Elements(S + "bk").ToArray() ?? [];
        var records = metadata.Element(S + "cellMetadata")?.Elements(S + "bk").ToArray() ?? [];
        for (var i = 0; i < records.Length; i++)
            foreach (var record in records[i].Elements(S + "rc"))
            {
                var type = Int(record.Attribute("t"), 0) - 1; var value = Int(record.Attribute("v"), -1);
                if (type < 0 || type >= types.Length || (string?)types[type].Attribute("name") != "XLDAPR" || value < 0 || value >= futures.Length) continue;
                if (futures[value].Descendants(DynamicArrayNamespace + "dynamicArrayProperties").Any(p => Flag(p.Attribute("fDynamic")))) result.Add(i + 1);
            }
        return result;
    }

    private static HashSet<CellAddress> NormalizeDynamicArrays(XElement root, HashSet<int> metadata, ICollection<string> warnings, ref long remainingCells)
    {
        var followers = new HashSet<CellAddress>();
        var cells = root.Element(S + "sheetData")?.Elements(S + "row").Elements(S + "c").ToArray() ?? [];
        var formulas = cells.Where(c => c.Element(S + "f") is not null).ToArray();
        var plans = new List<(XElement Formula, CellAddress Anchor, CellRange Range)>();
        foreach (var cell in formulas)
        {
            var formula = cell.Element(S + "f")!;
            if ((string?)formula.Attribute("t") != "array") continue;
            if (!metadata.Contains(Int(cell.Attribute("cm"))))
            {
                var notes = new HashSet<string>(); UseCachedValue(cell, formula, "#N/A", notes);
                warnings.Add("A legacy fixed-size array was imported as typed cached values; CSE recalculation is not implemented.");
                foreach (var note in notes) warnings.Add(note);
                continue;
            }
            var anchor = CellAddress.Parse((string?)cell.Attribute("r") ?? "");
            if (!CellRange.TryParse((string?)formula.Attribute("ref"), out var range) || range.Top != anchor.Row || range.Left != anchor.Column || range.Count > 100_000 || !FormulaNotation.IsSupported(formula.Value))
            {
                var notes = new HashSet<string>(); UseCachedValue(cell, formula, "#N/A", notes);
                warnings.Add("An unsupported or malformed dynamic array was imported as typed cached values, not recalculated.");
                foreach (var note in notes) warnings.Add(note);
                continue;
            }
            remainingCells -= range.Count;
            if (remainingCells < 0) throw new InvalidDataException("Dynamic-array result ranges exceed the workbook-wide 200,000-cell import limit.");
            plans.Add((formula, anchor, range));
        }
        var formulaAddresses = formulas.Select(c => CellAddress.Parse((string?)c.Attribute("r") ?? "")).ToHashSet();
        var owners = new Dictionary<CellAddress, int>();
        var conflicts = new HashSet<int>();
        for (var index = 0; index < plans.Count; index++)
            foreach (var address in plans[index].Range.Cells())
            {
                if (address != plans[index].Anchor && formulaAddresses.Contains(address)) conflicts.Add(index);
                if (!owners.TryAdd(address, index)) { conflicts.Add(index); conflicts.Add(owners[address]); }
            }
        foreach (var index in conflicts) warnings.Add("Overlapping array ownership was not trusted; cached cells were retained and may block the formula.");
        for (var index = 0; index < plans.Count; index++)
        {
            if (conflicts.Contains(index)) continue;
            var plan = plans[index];
            plan.Formula.Value = FormulaNotation.ToNative(plan.Formula.Value)[1..];
            plan.Formula.Attribute("t")?.Remove(); plan.Formula.Attribute("ref")?.Remove();
        }
        foreach (var cell in cells)
            if (CellAddress.TryParse((string?)cell.Attribute("r"), out var address) && owners.TryGetValue(address, out var owner)
                && !conflicts.Contains(owner) && address != plans[owner].Anchor) followers.Add(address);
        return followers;
    }
}
