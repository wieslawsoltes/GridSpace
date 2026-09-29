using System.IO.Compression;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using GridSpace.Core;
using GridSpace.Editing;
using GridSpace.Formulas;
using GridSpace.IO;
using Xunit;

namespace GridSpace.Tests;

public sealed class AnalyticsInterchangeTests
{
    private static void Validate(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var file = SpreadsheetDocument.Open(stream, false);
        var errors = new OpenXmlValidator(FileFormatVersions.Office2019).Validate(file)
            .Select(e => $"{e.Part?.Uri} {e.Path?.XPath}: {e.Description}").ToArray();
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors));
    }

    [Theory]
    [InlineData(ChartKind.Column)] [InlineData(ChartKind.Bar)] [InlineData(ChartKind.Line)]
    [InlineData(ChartKind.Area)] [InlineData(ChartKind.Scatter)] [InlineData(ChartKind.Pie)]
    [InlineData(ChartKind.Doughnut)] [InlineData(ChartKind.Radar)] [InlineData(ChartKind.Combo)]
    public void AllChartKindsProduceValidMultiseriesOoxmlAndRoundtrip(ChartKind kind)
    {
        var s = ChartAndPivotTests.Sales();
        var chart = new ChartSpec
        {
            Kind = kind, Range = "B1:D6", Title = "Annual results", SourceSheet = "Sales",
            Width = 710, Height = 390, OffsetX = 25.5, OffsetY = 8.2,
            ShowDataLabels = true, CategoryAxisTitle = "Product", ValueAxisTitle = "Amount",
            Series = [new() { Name = "Sales", Values = "C2:C6", Color = "#4472C4" },
                      new() { Name = "Units", Values = "D2:D6", Color = "#ED7D31", SecondaryAxis = kind == ChartKind.Combo }]
        };
        s.AddChart(chart);
        var bytes = XlsxWorkbook.Write(s.Book); Validate(bytes);
        var loaded = XlsxWorkbook.Read(bytes);
        var saved = Assert.Single(loaded.Workbook.ActiveSheet.Charts);
        Assert.Equal(chart.Id, saved.Id); Assert.Equal(kind, saved.Kind);
        Assert.Equal(710, saved.Width); Assert.Equal(390, saved.Height); Assert.Equal(25.5, saved.OffsetX, 3);
        Assert.Equal(chart.Series, saved.Series);
        using var zip = new ZipArchive(new MemoryStream(bytes));
        Assert.Equal(zip.Entries.Count, zip.Entries.Select(e => e.FullName).Distinct().Count());
        XNamespace c = "http://schemas.openxmlformats.org/drawingml/2006/chart";
        using var part = zip.GetEntry("xl/charts/chart1_1.xml")!.Open();
        Assert.Equal(kind == ChartKind.Pie ? 1 : 2, XDocument.Load(part).Descendants(c + "ser").Count());
    }

    [Theory]
    [InlineData(ChartGrouping.Clustered)] [InlineData(ChartGrouping.Stacked)] [InlineData(ChartGrouping.PercentStacked)]
    public void ChartGroupingExportsValidSchema(ChartGrouping grouping)
    {
        var s = ChartAndPivotTests.Sales(); s.AddChart(new ChartSpec { Range = "B1:D6", Grouping = grouping });
        Validate(XlsxWorkbook.Write(s.Book));
    }

    [Theory]
    [InlineData(PivotAggregate.Sum)] [InlineData(PivotAggregate.Count)] [InlineData(PivotAggregate.CountNumbers)]
    [InlineData(PivotAggregate.Average)] [InlineData(PivotAggregate.Min)] [InlineData(PivotAggregate.Max)]
    [InlineData(PivotAggregate.Product)] [InlineData(PivotAggregate.StandardDeviation)]
    [InlineData(PivotAggregate.PopulationStandardDeviation)] [InlineData(PivotAggregate.Variance)] [InlineData(PivotAggregate.PopulationVariance)]
    public void PivotDefinitionsAndCachesAreIndependentSchemaValidAndRefreshable(PivotAggregate aggregate)
    {
        var s = ChartAndPivotTests.Sales(); s.AddSheet("Report");
        var spec = ChartAndPivotTests.Definition() with { Values = [new() { Field = 2, Aggregate = aggregate }] };
        s.SetPivotTable(spec); s.AddPivotChart(spec.Id);
        var bytes = XlsxWorkbook.Write(s.Book); Validate(bytes);
        var loaded = XlsxWorkbook.Read(bytes);
        Assert.DoesNotContain(loaded.Warnings, text => text.Contains("definition was not imported"));
        var report = loaded.Workbook.FindSheet("Report")!;
        var pivot = Assert.Single(report.PivotTables);
        Assert.Equal(spec.Id, pivot.Id); Assert.Equal(aggregate, pivot.Values[0].Aggregate);
        Assert.Equal(5, pivot.Cache!.Rows.Length);
        Assert.False(pivot.NeedsLayoutRefresh);
        Assert.Single(report.Charts);
        var session = new SpreadsheetSession(loaded.Workbook);
        session.SwitchSheet(1);
        session.RefreshPivotTable(spec.Id);
        Assert.Equal(pivot.OutputRange, session.Sheet.PivotTables[0].OutputRange);
    }

    [Fact]
    public void MultipleValuesReportFilterAndPercentTotalsPreserveStandardCacheRecords()
    {
        var s = ChartAndPivotTests.Sales(); s.AddSheet("Report");
        var spec = ChartAndPivotTests.Definition() with
        {
            Columns = [], Filters = [new() { Field = 1, Values = ["A"] }],
            Values = [new() { Field = 2, Caption = "Total sales" },
                new() { Field = 3, Caption = "Share of units", ShowAs = PivotShowAs.PercentOfGrandTotal }]
        };
        s.SetPivotTable(spec); var bytes = XlsxWorkbook.Write(s.Book); Validate(bytes);
        var loaded = XlsxWorkbook.Read(bytes);
        var pivot = Assert.Single(loaded.Workbook.ActiveSheet.PivotTables);
        Assert.Equal(5, pivot.Cache!.Rows.Length); // cache contains all records, not only the filtered report
        Assert.Equal(3, PivotEngine.FromCache(pivot).Source.Rows.Count);
        Assert.Equal(PivotShowAs.PercentOfGrandTotal, pivot.Values[1].ShowAs);
    }

    [Fact]
    public void OrdinaryPivotMetadataCanBeReadWithoutGridSpaceExtensions()
    {
        var s = ChartAndPivotTests.Sales(); s.AddSheet("Report");
        s.SetPivotTable(ChartAndPivotTests.Definition());
        var bytes = WithoutNativeExtensions(XlsxWorkbook.Write(s.Book));
        Validate(bytes);
        var loaded = XlsxWorkbook.Read(bytes);
        var pivot = Assert.Single(loaded.Workbook.ActiveSheet.PivotTables);
        Assert.True(pivot.NeedsLayoutRefresh); Assert.Equal(new[] { 0 }, pivot.Rows); Assert.Equal(new[] { 1 }, pivot.Columns);
        var session = new SpreadsheetSession(loaded.Workbook);
        Assert.Throws<InvalidOperationException>(() => session.DrillDownPivot(CellAddress.Parse("B2")));
        session.RefreshPivotTable(pivot.Id);
        Assert.False(session.Sheet.PivotTables[0].NeedsLayoutRefresh);
        Assert.Equal(200, session.Calculation.Evaluate(session.Sheet, "D4").Number);
    }

    [Fact]
    public void StandardChartBindingsSurviveWithoutNativeExtensions()
    {
        var s = ChartAndPivotTests.Sales();
        s.AddChart(new ChartSpec { Range = "B1:D6", Kind = ChartKind.Combo,
            Series = [new() { Name = "Revenue", Values = "C2:C6" }, new() { Name = "Volume", Values = "D2:D6", Kind = ChartKind.Line, SecondaryAxis = true }] });
        var loaded = XlsxWorkbook.Read(WithoutNativeExtensions(XlsxWorkbook.Write(s.Book)));
        var chart = Assert.Single(loaded.Workbook.ActiveSheet.Charts);
        Assert.Equal(ChartKind.Combo, chart.Kind); Assert.Equal(2, chart.Series.Count);
        Assert.Equal("C2:C6", chart.Series[0].Values);
        Assert.True(chart.Series[1].SecondaryAxis);
        Assert.Equal(5, ChartDataResolver.Resolve(loaded.Workbook, loaded.Workbook.ActiveSheet, chart, new(loaded.Workbook)).Categories.Length);
    }

    [Fact]
    public void PivotSnapshotDoesNotRefreshImplicitlyDuringSave()
    {
        var s = ChartAndPivotTests.Sales(); s.AddSheet("Report"); var spec = ChartAndPivotTests.Definition();
        s.SetPivotTable(spec); s.Book.FindSheet("Sales")!.Set("C2", "999");
        var loaded = XlsxWorkbook.Read(XlsxWorkbook.Write(s.Book)).Workbook;
        Assert.Equal(200, new CalculationEngine(loaded).Evaluate(loaded.ActiveSheet, "D4").Number);
        Assert.Equal("10", loaded.ActiveSheet.PivotTables[0].Cache!.Rows[0][2]);
    }

    private static byte[] WithoutNativeExtensions(byte[] bytes)
    {
        using var source = new ZipArchive(new MemoryStream(bytes)); using var output = new MemoryStream();
        using (var target = new ZipArchive(output, ZipArchiveMode.Create, true))
            foreach (var entry in source.Entries)
            {
                using var input = entry.Open(); using var destination = target.CreateEntry(entry.FullName).Open();
                if (entry.FullName.StartsWith("xl/charts/") && entry.FullName.EndsWith(".xml")
                    || entry.FullName.StartsWith("xl/pivotTables/") && entry.FullName.EndsWith(".xml"))
                {
                    var xml = XDocument.Load(input);
                    xml.Descendants().Where(e => e.Name.LocalName == "extLst").ToArray().ToList().ForEach(e => e.Remove());
                    xml.Save(destination);
                }
                else input.CopyTo(destination);
            }
        return output.ToArray();
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void MultipleDimensionsValuesAndFilteredEmptyReportsValidate(bool empty)
    {
        var session = ChartAndPivotTests.Sales(); session.AddSheet("Report");
        var spec = ChartAndPivotTests.Definition() with
        {
            Rows = [0, 1], Columns = [],
            Values = [new() { Field = 2 }, new() { Field = 3, Aggregate = PivotAggregate.Average, ShowAs = PivotShowAs.PercentOfGrandTotal }]
        };
        if (empty) spec = spec with { Rows = [0], Filters = [new() { Field = 1, Values = [] }] };
        session.SetPivotTable(spec); session.AddPivotChart(spec.Id);
        Validate(XlsxWorkbook.Write(session.Book));
    }

    [Fact]
    public void AnalyticsSampleContainsLiveReportAndValidChartParts()
    {
        var book = AnalyticsSampleWorkbook.Create();
        var report = book.FindSheet("Pivot analysis")!;
        Assert.Single(report.PivotTables); Assert.Equal(2, report.Charts.Count);
        var bytes = XlsxWorkbook.Write(book); Validate(bytes);
        var loaded = XlsxWorkbook.Read(bytes);
        Assert.Single(loaded.Workbook.FindSheet("Pivot analysis")!.PivotTables);
        Assert.Equal(2, loaded.Workbook.FindSheet("Pivot analysis")!.Charts.Count);
        var session = new SpreadsheetSession(loaded.Workbook);
        var id = session.Sheet.PivotTables[0].Id;
        var before = session.Sheet.Get("C6").Input;
        session.Book.FindSheet("Sales")!.Set("D2", "999999");
        session.RefreshPivotTable(id);
        Assert.NotEqual(before, session.Sheet.Get("C6").Input);
    }
}
