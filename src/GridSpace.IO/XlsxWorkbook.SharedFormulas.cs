using System.Globalization;
using System.Xml.Linq;
using GridSpace.Core;
using GridSpace.Formulas;

namespace GridSpace.IO;

public static partial class XlsxWorkbook
{
    private sealed record SharedFormulaMaster(CellAddress Anchor, CellRange Range, string Expression);

    /// <summary>
    /// Expands only explicitly shared cells, in two passes so XML row order is irrelevant.
    /// The workbook-wide character budget prevents a short ZIP from expanding into gigabytes of formula strings.
    /// </summary>
    private static void NormalizeWorksheetFormulas(XElement worksheet, ICollection<string> warnings, ref long remainingSharedCharacters)
    {
        var data = worksheet.Element(S + "sheetData");
        if (data is null) return;
        var masters = new Dictionary<uint, SharedFormulaMaster>();
        var ambiguous = new HashSet<uint>();
        var notes = new HashSet<string>(StringComparer.Ordinal);
        var cells = data.Elements(S + "row").Elements(S + "c");

        foreach (var cell in cells)
        {
            var formula = cell.Element(S + "f");
            if ((string?)formula?.Attribute("t") != "shared" || formula.Attribute("ref") is null) continue;
            if (!SharedIndex(formula, out var index)) continue;
            if (!CellAddress.TryParse((string?)cell.Attribute("r"), out var address)
                || !CellRange.TryParse((string?)formula.Attribute("ref"), out var range)
                || !range.Contains(address) || string.IsNullOrEmpty(formula.Value))
            {
                ambiguous.Add(index);
                continue;
            }
            if (!masters.TryAdd(index, new(address, range, formula.Value))) ambiguous.Add(index);
        }

        foreach (var cell in cells)
        {
            var formula = cell.Element(S + "f");
            if (formula is null) continue;
            var type = (string?)formula.Attribute("t");
            if (type == "dataTable")
            {
                UseCachedValue(cell, formula, "#N/A", notes);
                notes.Add("What-if data-table formulas are not recalculated; their cached values were imported.");
                continue;
            }
            if (type != "shared") continue;
            if (!SharedIndex(formula, out var index) || ambiguous.Contains(index)
                || !masters.TryGetValue(index, out var master)
                || !CellAddress.TryParse((string?)cell.Attribute("r"), out var address)
                || !master.Range.Contains(address))
            {
                UseCachedValue(cell, formula, "#REF!", notes);
                notes.Add("An unresolved, ambiguous or out-of-range shared formula was imported as its cached value. Keep the original workbook.");
                continue;
            }

            // A non-master shared cell's own expression is ignored by OOXML: the group's master wins.
            if (master.Expression.Length + 1 > remainingSharedCharacters)
                throw new InvalidDataException("Expanded shared formulas exceed the workbook-wide 32 MB character limit.");
            var expanded = FormulaReferences.Translate("=" + master.Expression,
                address.Row - master.Anchor.Row, address.Column - master.Anchor.Column);
            remainingSharedCharacters -= expanded.Length;
            if (remainingSharedCharacters < 0)
                throw new InvalidDataException("Expanded shared formulas exceed the workbook-wide 32 MB character limit.");
            if (expanded.Length > 32767) throw new InvalidDataException("An expanded cell formula exceeds 32,767 characters.");
            formula.Value = expanded[1..];
            formula.Attribute("t")?.Remove();
            formula.Attribute("si")?.Remove();
            formula.Attribute("ref")?.Remove();
        }
        foreach (var note in notes) warnings.Add(note);
    }

    private static bool SharedIndex(XElement formula, out uint index) =>
        uint.TryParse((string?)formula.Attribute("si"), NumberStyles.None, CultureInfo.InvariantCulture, out index);

    private static void UseCachedValue(XElement cell, XElement formula, string missingError, ISet<string> notes)
    {
        formula.Remove();
        // Keep c/@t intact: cached text such as "0012", booleans and errors must not be coerced to numbers.
        if (cell.Element(S + "v") is not null || cell.Element(S + "is") is not null) return;
        cell.SetAttributeValue("t", "e");
        cell.Add(new XElement(S + "v", missingError));
        notes.Add("An unsupported or malformed formula had no cached result; an explicit error was imported instead of a silent blank.");
    }
}
