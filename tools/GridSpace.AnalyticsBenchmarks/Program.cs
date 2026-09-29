using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using GridSpace.Core;
using GridSpace.Editing;
using GridSpace.Formulas;
using GridSpace.IO;

// Fixed-shape observations, not speed claims or machine-sensitive CI assertions.
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
var count = args.Length == 0 ? 10_000 : int.Parse(args[0], CultureInfo.InvariantCulture);
if (count is < 100 or > 40_000) throw new ArgumentOutOfRangeException(nameof(count));
var book = new Workbook(); book.ActiveSheet.Name = "Facts";
string[] columns = ["Region", "Category", "Revenue", "Units"];
for (var c = 0; c < columns.Length; c++) book.ActiveSheet.Set(new CellAddress(0, c), new Cell { Input = columns[c] });
for (var r = 0; r < count; r++)
{
    string[] values = ["R" + (r % 50), "P" + ((r / 50) % 10), (r * .125 + 10).ToString("G17"), (r % 40 + 1).ToString()];
    for (var c = 0; c < values.Length; c++) book.ActiveSheet.Set(new CellAddress(r + 1, c), new Cell { Input = values[c] });
}
var session = new SpreadsheetSession(book);
var definition = new PivotTableSpec
{
    SourceSheet = "Facts", SourceRange = $"A1:D{count + 1}", Destination = "A1", Rows = [0], Columns = [1],
    Values = [new() { Field = 2 }, new() { Field = 3, Aggregate = PivotAggregate.Average }]
};
var snapshot = PivotEngine.Capture(book, definition, session.Calculation);
var results = new List<object>();
Observe("pivot-capture-and-aggregate", 12, 1, () =>
{
    var report = PivotEngine.Calculate(book, definition, session.Calculation);
    if (report.RowKeys.Count != 50) throw new InvalidOperationException("Bad grouping.");
});
Observe("pivot-aggregate-captured-values", 20, 1, () => _ = PivotEngine.Build(snapshot, definition));
session.AddSheet("Report"); session.SetPivotTable(definition);
Observe("pivot-layout-with-source-refresh", 12, 1, () => session.SetPivotTable(
    session.Sheet.PivotTables.Single() with { SortAscending = !session.Sheet.PivotTables.Single().SortAscending }));
Observe("pivot-layout-from-immutable-cache", 12, 1, () => session.ReconfigurePivotTable(
    session.Sheet.PivotTables.Single() with { SortAscending = !session.Sheet.PivotTables.Single().SortAscending }));
var fieldCache = session.Sheet.PivotTables.Single().Cache!;
Observe("cached-pivot-filter-catalog", 20, 100, () => _ = PivotFieldValues.Get(fieldCache, 0));
var chartId = session.AddPivotChart(definition.Id);
var chart = session.FindChart(chartId)!; var cache = new ChartDataCache();
_ = cache.Get(book, session.Sheet, chart, session.Calculation);
Observe("cached-pivot-chart-binding", 20, 100, () => _ = cache.Get(book, session.Sheet, chart, session.Calculation));
session.SwitchSheet(0);
var ordinary = new ChartSpec { Range = "B1:D100", SourceSheet = "Facts", Column = 6 };
session.AddChart(ordinary); _ = cache.Get(book, session.Sheet, ordinary, session.Calculation);
var offset = 0;
Observe("chart-update-and-cached-data", 20, 20, () =>
{
    session.UpdateChart(ordinary.Id, c => c with { OffsetX = ++offset });
    _ = cache.Get(book, session.Sheet, session.FindChart(ordinary.Id)!, session.Calculation);
});
Observe("xlsx-chart-and-pivot-cache-export", 5, 1, () => _ = XlsxWorkbook.Write(book));
Console.WriteLine(JsonSerializer.Serialize(new
{
    schema = 1, runtime = RuntimeInformation.FrameworkDescription, os = RuntimeInformation.OSDescription,
    rows = count, storedSourceCells = (count + 1) * 4, chartDataResolutions = cache.ResolveCount, observations = results
}, new JsonSerializerOptions { WriteIndented = true }));
void Observe(string name, int samples, int batch, Action action)
{
    for (var i = 0; i < 3; i++) action();
    var times = new List<double>(); var allocations = new List<long>();
    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    for (var i = 0; i < samples; i++)
    {
        var before = GC.GetAllocatedBytesForCurrentThread(); var clock = Stopwatch.GetTimestamp();
        for (var b = 0; b < batch; b++) action();
        times.Add(Stopwatch.GetElapsedTime(clock).TotalMilliseconds / batch);
        allocations.Add((GC.GetAllocatedBytesForCurrentThread() - before) / batch);
    }
    times.Sort(); allocations.Sort();
    results.Add(new { name, samples, operationsPerSample = batch, medianMs = times[times.Count / 2], medianAllocatedBytes = allocations[allocations.Count / 2] });
}
