using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using GridSpace.Core;
using GridSpace.Formulas;

namespace GridSpace.IO;

public static partial class XlsxWorkbook
{
    private const string PivotExtension = "{B4678A94-3F8A-4ACF-B16B-1D697FE4DE41}";

    private static void WritePivots(ZipArchive zip, Worksheet sheet, int sheetNumber, XElement worksheet,
        XElement worksheetRelationships, XElement workbookCaches, XElement workbookRelationships,
        Action<string, string> type, ref int nextCache)
    {
        if (sheet.PivotTables.Count == 0) return;
        for (var i = 0; i < sheet.PivotTables.Count; i++)
        {
            var spec = sheet.PivotTables[i]; spec.Validate();
            if (spec.Cache is null || spec.OutputRange is null)
                throw new InvalidOperationException("Refresh " + spec.Name + " before exporting its PivotTable cache.");
            var source = PivotEngine.FromCache(spec.Cache);
            var report = PivotEngine.Build(source, spec);
            var cacheId = nextCache++;
            var cacheFile = "pivotCacheDefinition" + (cacheId + 1) + ".xml";
            var recordsFile = "pivotCacheRecords" + (cacheId + 1) + ".xml";
            var tableFile = $"pivotTable{sheetNumber}_{i + 1}.xml";
            var cachePath = "xl/pivotCache/" + cacheFile;
            var tablePath = "xl/pivotTables/" + tableFile;
            var shared = Enumerable.Range(0, source.Headers.Length)
                .Select(field => source.Rows.Select(row => row[field]).Distinct().ToList()).ToArray();
            // The dictionary uses the complete typed value, rather than a formatted-string key.
            var indexes = shared.Select(values => values.Select((value, index) => (value, index)).ToDictionary(p => p.value, p => p.index)).ToArray();
            var fields = E("cacheFields", new XAttribute("count", source.Headers.Length));
            for (var field = 0; field < source.Headers.Length; field++)
            {
                var values = shared[field];
                fields.Add(E("cacheField", new XAttribute("name", source.Headers[field]),
                    E("sharedItems", new XAttribute("count", values.Count),
                        new XAttribute("containsBlank", values.Any(v => v.Kind == ValueKind.Blank) ? 1 : 0),
                        new XAttribute("containsString", values.Any(v => v.Kind == ValueKind.Text) ? 1 : 0),
                        new XAttribute("containsNumber", values.Any(v => v.Kind == ValueKind.Number) ? 1 : 0),
                        new XAttribute("containsNonDate", 1),
                        new XAttribute("containsMixedTypes", values.Select(v => v.Kind).Distinct().Count() > 1 ? 1 : 0),
                        values.Select(PivotValueElement))));
            }
            var definition = E("pivotCacheDefinition", new XAttribute(XNamespace.Xmlns + "r", R), new XAttribute(R + "id", "records"),
                new XAttribute("refreshOnLoad", spec.NeedsLayoutRefresh ? 1 : 0), new XAttribute("saveData", 1),
                new XAttribute("recordCount", source.Rows.Count), new XAttribute("createdVersion", 6), new XAttribute("refreshedVersion", 6),
                E("cacheSource", new XAttribute("type", "worksheet"),
                    E("worksheetSource", new XAttribute("ref", spec.SourceRange), new XAttribute("sheet", spec.SourceSheet))), fields);
            var records = E("pivotCacheRecords", new XAttribute("count", source.Rows.Count),
                source.Rows.Select(row => E("r", row.Select((value, field) => E("x", new XAttribute("v", indexes[field][value]))))));
            WritePart(zip, cachePath, definition);
            WritePart(zip, "xl/pivotCache/" + recordsFile, records);
            WritePart(zip, "xl/pivotCache/_rels/" + cacheFile + ".rels", new XElement(P + "Relationships", Relationship("records", "pivotCacheRecords", recordsFile)));
            WritePart(zip, tablePath, WritePivotDefinition(spec, cacheId, source, report, shared));
            WritePart(zip, "xl/pivotTables/_rels/" + tableFile + ".rels", new XElement(P + "Relationships", Relationship("cache", "pivotCacheDefinition", "../pivotCache/" + cacheFile)));
            type(cachePath, "application/vnd.openxmlformats-officedocument.spreadsheetml.pivotCacheDefinition+xml");
            type("xl/pivotCache/" + recordsFile, "application/vnd.openxmlformats-officedocument.spreadsheetml.pivotCacheRecords+xml");
            type(tablePath, "application/vnd.openxmlformats-officedocument.spreadsheetml.pivotTable+xml");
            var cacheRelation = "pivotCache" + (cacheId + 1);
            workbookCaches.Add(E("pivotCache", new XAttribute("cacheId", cacheId), new XAttribute(R + "id", cacheRelation)));
            workbookRelationships.Add(Relationship(cacheRelation, "pivotCacheDefinition", "pivotCache/" + cacheFile));
            var tableRelation = "pivot" + (i + 1);
            worksheetRelationships.Add(Relationship(tableRelation, "pivotTable", "../pivotTables/" + tableFile));
        }
    }

    private static XElement PivotValueElement(CalcValue value) => value.Kind switch
    {
        ValueKind.Number => E("n", new XAttribute("v", F(value.Number))),
        ValueKind.Boolean => E("b", new XAttribute("v", value.Truth ? 1 : 0)),
        ValueKind.Error => E("e", new XAttribute("v", value.Text)),
        ValueKind.Blank => E("m"),
        _ => E("s", new XAttribute("v", value.ToString()))
    };

    private static XElement WritePivotDefinition(PivotTableSpec spec, int cacheId, PivotSource source,
        PivotReport report, List<CalcValue>[] shared)
    {
        var root = E("pivotTableDefinition", new XAttribute("name", spec.Name), new XAttribute("cacheId", cacheId),
            new XAttribute("dataCaption", "Values"), new XAttribute("rowGrandTotals", spec.RowGrandTotals ? 1 : 0),
            new XAttribute("colGrandTotals", spec.ColumnGrandTotals ? 1 : 0), new XAttribute("compact", 0),
            new XAttribute("compactData", 0), new XAttribute("outline", 0), new XAttribute("outlineData", 0),
            new XAttribute("gridDropZones", 1), new XAttribute("dataOnRows", 0), new XAttribute("updatedVersion", 6),
            new XAttribute("minRefreshableVersion", 3), new XAttribute("createdVersion", 6),
            E("location", new XAttribute("ref", spec.OutputRange!), new XAttribute("firstHeaderRow", 0),
                new XAttribute("firstDataRow", 1), new XAttribute("firstDataCol", report.LabelColumns)));
        var fields = E("pivotFields", new XAttribute("count", source.Headers.Length));
        for (var field = 0; field < source.Headers.Length; field++)
        {
            var axis = spec.Rows.Contains(field) ? "axisRow" : spec.Columns.Contains(field) ? "axisCol" : spec.Filters.Any(f => f.Field == field) ? "axisPage" : null;
            var filter = spec.Filters.FirstOrDefault(f => f.Field == field);
            var selected = filter?.Values.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var definition = E("pivotField", axis is null ? null : new XAttribute("axis", axis),
                new XAttribute("dataField", spec.Values.Any(v => v.Field == field) ? 1 : 0),
                new XAttribute("defaultSubtotal", 0), new XAttribute("showAll", 0), new XAttribute("compact", 0), new XAttribute("outline", 0),
                new XAttribute("sortType", spec.SortAscending ? "ascending" : "descending"),
                filter is not null ? new XAttribute("multipleItemSelectionAllowed", 1) : null);
            if (axis is not null)
                definition.Add(E("items", new XAttribute("count", shared[field].Count), shared[field].Select((value, index) =>
                    E("item", new XAttribute("x", index), selected is not null && !selected.Contains(value.ToString()) ? new XAttribute("h", 1) : null))));
            fields.Add(definition);
        }
        root.Add(fields);
        // Report keys are case-insensitive typed vectors; build their cache-item map once.
        // Repeated linear FindIndex scans made high-cardinality exports quadratic.
        var keyIndexes = shared.Select(values =>
        {
            var map = new Dictionary<PivotKey, int>();
            for (var index = 0; index < values.Count; index++) map.TryAdd(new PivotKey([values[index]]), index);
            return map;
        }).ToArray();
        int Index(int field, CalcValue value) => keyIndexes[field].GetValueOrDefault(new PivotKey([value]), -1);
        XElement Items(string name, IReadOnlyList<PivotKey> keys, IReadOnlyList<int> dimensions, bool grand, bool dataAxis)
        {
            var result = E(name);
            foreach (var key in keys)
                for (var valueIndex = 0; valueIndex < (dataAxis ? spec.Values.Count : 1); valueIndex++)
                {
                    var item = E("i", dataAxis && valueIndex > 0 ? new XAttribute("i", valueIndex) : null);
                    for (var d = 0; d < dimensions.Count; d++)
                        item.Add(E("x", new XAttribute("v", key.Items.Count > d ? Math.Max(0, Index(dimensions[d], key.Items[d])) : 0)));
                    if (dataAxis && spec.Values.Count > 1) item.Add(E("x", new XAttribute("v", valueIndex)));
                    if (!item.HasElements) item.Add(E("x"));
                    result.Add(item);
                }
            if (grand)
                for (var valueIndex = 0; valueIndex < (dataAxis ? spec.Values.Count : 1); valueIndex++)
                    result.Add(E("i", new XAttribute("t", "grand"), dataAxis && valueIndex > 0 ? new XAttribute("i", valueIndex) : null, E("x")));
            result.Add(new XAttribute("count", result.Elements().Count()));
            return result;
        }
        if (spec.Rows.Count > 0)
        {
            root.Add(E("rowFields", new XAttribute("count", spec.Rows.Count), spec.Rows.Select(f => E("field", new XAttribute("x", f)))));
            root.Add(Items("rowItems", report.RowKeys, spec.Rows, spec.ColumnGrandTotals, false));
        }
        if (spec.Columns.Count > 0 || spec.Values.Count > 1)
        {
            var columns = spec.Columns.Select(f => E("field", new XAttribute("x", f))).ToList();
            if (spec.Values.Count > 1) columns.Add(E("field", new XAttribute("x", -2)));
            root.Add(E("colFields", new XAttribute("count", columns.Count), columns));
            root.Add(Items("colItems", report.ColumnKeys, spec.Columns, spec.RowGrandTotals && spec.Columns.Count > 0, true));
        }
        if (spec.Filters.Count > 0)
            root.Add(E("pageFields", new XAttribute("count", spec.Filters.Count), spec.Filters.Select(f => E("pageField", new XAttribute("fld", f.Field), new XAttribute("hier", -1)))));
        root.Add(E("dataFields", new XAttribute("count", spec.Values.Count), spec.Values.Select(v =>
            E("dataField", new XAttribute("name", PivotEngine.ValueCaption(source.Headers, v)), new XAttribute("fld", v.Field),
                new XAttribute("subtotal", AggregateName(v.Aggregate)), new XAttribute("showDataAs", v.ShowAs switch
                { PivotShowAs.PercentOfRow => "percentOfRow", PivotShowAs.PercentOfColumn => "percentOfCol", PivotShowAs.PercentOfGrandTotal => "percentOfTotal", _ => "normal" }),
                new XAttribute("numFmtId", v.ShowAs == PivotShowAs.Normal ? 4 : 10)))));
        root.Add(E("pivotTableStyleInfo", new XAttribute("name", "PivotStyleMedium9"), new XAttribute("showRowHeaders", 1),
            new XAttribute("showColHeaders", 1), new XAttribute("showRowStripes", 1), new XAttribute("showColStripes", 0), new XAttribute("showLastColumn", 1)));
        root.Add(E("extLst", E("ext", new XAttribute("uri", PivotExtension),
            new XElement(Analytics + "pivot", JsonSerializer.Serialize(spec with { Cache = null })))));
        return root;
    }

    private static string AggregateName(PivotAggregate kind) => kind switch
    {
        PivotAggregate.Count => "count", PivotAggregate.CountNumbers => "countNums", PivotAggregate.Average => "average",
        PivotAggregate.Min => "min", PivotAggregate.Max => "max", PivotAggregate.Product => "product",
        PivotAggregate.StandardDeviation => "stdDev", PivotAggregate.PopulationStandardDeviation => "stdDevp",
        PivotAggregate.Variance => "var", PivotAggregate.PopulationVariance => "varp", _ => "sum"
    };

    private static void ReadPivots(ZipArchive zip, string sheetPath, XElement worksheet, Worksheet sheet,
        Dictionary<int, string> caches, List<string> warnings)
    {
        var parts = Relationships(zip, sheetPath, "pivotTable").Values.ToArray();
        if (parts.Length > 32) throw new InvalidDataException("A sheet has more than 32 PivotTables.");
        foreach (var path in parts)
        {
            try
            {
                var definition = Xml(zip, path).Root!;
                if (!caches.TryGetValue(Int(definition.Attribute("cacheId"), -1), out var cachePath))
                    throw new InvalidDataException("Unresolved PivotTable cache.");
                var cache = Xml(zip, cachePath).Root!;
                var cacheSource = cache.Element(S + "cacheSource");
                var sourceElement = cacheSource?.Element(S + "worksheetSource");
                var cacheFields = cache.Element(S + "cacheFields")?.Elements(S + "cacheField").ToArray() ?? [];
                if ((string?)cacheSource?.Attribute("type") != "worksheet" || sourceElement?.Attribute("ref") is null
                    || sourceElement.Attribute("sheet") is null || cacheFields.Length is < 1 or > 256
                    || cacheFields.Any(f => f.Element(S + "fieldGroup") is not null || f.Attribute("formula") is not null)
                    || Flag(definition.Attribute("dataOnRows")))
                    throw new NotSupportedException("Grouped/calculated, external/data-model, named-table or values-on-rows PivotTables are not supported.");
                var sourceRange = CellRange.Parse((string)sourceElement.Attribute("ref")!);
                if (sourceRange.Count > 200_000 || sourceRange.Right - sourceRange.Left + 1 != cacheFields.Length)
                    throw new InvalidDataException("Oversized or inconsistent PivotTable source.");
                var shared = cacheFields.Select(f => f.Element(S + "sharedItems")?.Elements().Select(ReadPivotValue).ToArray() ?? []).ToArray();
                var recordRel = (string?)cache.Attribute(R + "id");
                if (recordRel is null || !Relationships(zip, cachePath).TryGetValue(recordRel, out var recordPath))
                    throw new NotSupportedException("A PivotTable without saved records must be refreshed in its originating application first.");
                var records = new List<CalcValue[]>();
                foreach (var row in Xml(zip, recordPath).Root!.Elements(S + "r"))
                {
                    if ((long)(records.Count + 2) * cacheFields.Length > 200_000) throw new InvalidDataException("PivotTable records exceed the source budget.");
                    var values = row.Elements().ToArray();
                    if (values.Length != cacheFields.Length) throw new InvalidDataException("PivotTable cache record width is inconsistent.");
                    records.Add(values.Select((value, field) =>
                    {
                        if (value.Name != S + "x") return ReadPivotValue(value);
                        var index = Int(value.Attribute("v"), -1);
                        if (index < 0 || index >= shared[field].Length) throw new InvalidDataException("Invalid PivotTable shared-item index.");
                        return shared[field][index];
                    }).ToArray());
                }
                var source = new PivotSource(cacheFields.Select(f => (string?)f.Attribute("name") ?? "").ToArray(), records);
                var output = CellRange.Parse((string?)definition.Element(S + "location")?.Attribute("ref") ?? "A1");
                var native = definition.Element(S + "extLst")?.Elements(S + "ext").FirstOrDefault(e => (string?)e.Attribute("uri") == PivotExtension)?.Element(Analytics + "pivot");
                PivotTableSpec? spec = null;
                if (native is not null)
                {
                    if (native.Value.Length > 512 * 1024) throw new InvalidDataException("PivotTable metadata is oversized.");
                    spec = JsonSerializer.Deserialize<PivotTableSpec>(native.Value);
                }
                if (spec is null)
                {
                    var fields = definition.Element(S + "pivotFields")?.Elements(S + "pivotField").ToArray() ?? [];
                    if (fields.Length != cacheFields.Length) throw new InvalidDataException("PivotTable and cache field counts disagree.");
                    spec = new()
                    {
                        Name = (string?)definition.Attribute("name") ?? "PivotTable" + (sheet.PivotTables.Count + 1),
                        Rows = definition.Element(S + "rowFields")?.Elements(S + "field").Select(f => Int(f.Attribute("x"), -1)).ToList() ?? [],
                        Columns = definition.Element(S + "colFields")?.Elements(S + "field").Select(f => Int(f.Attribute("x"), -1)).Where(f => f != -2).ToList() ?? [],
                        RowGrandTotals = definition.Attribute("rowGrandTotals") is null || Flag(definition.Attribute("rowGrandTotals")),
                        ColumnGrandTotals = definition.Attribute("colGrandTotals") is null || Flag(definition.Attribute("colGrandTotals")),
                        NeedsLayoutRefresh = true
                    };
                    foreach (var value in definition.Element(S + "dataFields")?.Elements(S + "dataField") ?? [])
                    {
                        var name = (string?)value.Attribute("subtotal") ?? "sum";
                        var aggregate = Enum.GetValues<PivotAggregate>().FirstOrDefault(a => AggregateName(a) == name);
                        if (AggregateName(aggregate) != name) throw new NotSupportedException("Unsupported PivotTable aggregation: " + name);
                        var showAs = (string?)value.Attribute("showDataAs") ?? "normal";
                        if (showAs is not ("normal" or "percentOfRow" or "percentOfCol" or "percentOfTotal"))
                            throw new NotSupportedException("Unsupported PivotTable Show Values As calculation.");
                        spec.Values.Add(new()
                        {
                            Field = Int(value.Attribute("fld"), -1), Caption = (string?)value.Attribute("name") ?? "", Aggregate = aggregate,
                            ShowAs = showAs switch { "percentOfRow" => PivotShowAs.PercentOfRow, "percentOfCol" => PivotShowAs.PercentOfColumn, "percentOfTotal" => PivotShowAs.PercentOfGrandTotal, _ => PivotShowAs.Normal }
                        });
                    }
                    foreach (var filter in definition.Element(S + "pageFields")?.Elements(S + "pageField") ?? [])
                    {
                        var field = Int(filter.Attribute("fld"), -1);
                        if (field < 0 || field >= fields.Length) throw new InvalidDataException("Invalid report-filter field.");
                        var items = fields[field].Element(S + "items")?.Elements(S + "item").ToArray() ?? [];
                        var selectedItem = Int(filter.Attribute("item"), -1);
                        var chosen = items.Where((item, index) => selectedItem >= 0 ? index == selectedItem : !Flag(item.Attribute("h")))
                            .Select(item => Int(item.Attribute("x"), -1)).Where(i => i >= 0 && i < shared[field].Length).Select(i => shared[field][i].ToString()).ToArray();
                        spec.Filters.Add(new() { Field = field, Values = chosen });
                    }
                    warnings.Add("PivotTable " + spec.Name + " retains its saved cells. Refresh converts its layout to GridSpace's tabular report; subtotals and advanced layout options are not reproduced.");
                }
                spec.SourceSheet = (string)sourceElement.Attribute("sheet")!;
                spec.SourceRange = sourceRange.ToString(); spec.Destination = output.Normalized.Start.ToString(); spec.OutputRange = output.ToString();
                spec.FieldNames = source.Headers; spec.Cache = PivotEngine.ToCache(source); spec.Validate();
                if (sheet.PivotTables.Any(p => p.Id == spec.Id || p.OutputRange is { } range && CellRange.Parse(range).Intersects(output))
                    || sheet.Merges.Any(m => m.Intersects(output)))
                    throw new NotSupportedException("Overlapping or merged PivotTable outputs cannot be safely edited.");
                if (native is not null)
                {
                    var report = PivotEngine.FromCache(spec);
                    if (report.RowCount != output.Bottom - output.Top + 1 || report.ColumnCount != output.Right - output.Left + 1)
                        spec.NeedsLayoutRefresh = true;
                }
                sheet.PivotTables.Add(spec);
            }
            catch (Exception error) when (error is InvalidDataException or ArgumentException or FormatException or InvalidOperationException or NotSupportedException or JsonException)
            {
                warnings.Add("PivotTable definition was not imported; its saved worksheet cells remain: " + error.Message);
            }
        }
    }

    private static CalcValue ReadPivotValue(XElement value) => value.Name.LocalName switch
    {
        "n" => double.TryParse((string?)value.Attribute("v"), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var number) ? CalcValue.Num(number) : throw new InvalidDataException("Invalid pivot numeric value."),
        "s" => CalcValue.Str((string?)value.Attribute("v") ?? ""),
        "b" => CalcValue.Bool(Flag(value.Attribute("v"))),
        "e" => CalcValue.Error((string?)value.Attribute("v") ?? "#VALUE!"),
        "m" => CalcValue.Blank,
        "d" => throw new NotSupportedException("Typed date cache items require date-group semantics that are not implemented."),
        _ => throw new NotSupportedException("Unsupported pivot cache item: " + value.Name.LocalName)
    };
}
