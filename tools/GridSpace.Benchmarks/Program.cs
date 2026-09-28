using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using GridSpace.Core;
using GridSpace.Editing;

// Portable, dependency-free microbenchmark. The same harness also runs against 0.2.0 sources.
// Times are observations, not CI gates; counters/identity assertions belong in regression tests.
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
var rows = args.Length > 0 ? int.Parse(args[0], CultureInfo.InvariantCulture) : 10_000;
if (rows is < 100 or > 50_000) throw new ArgumentOutOfRangeException(nameof(rows));
var book = new Workbook();
for (var r = 0; r < rows; r++)
{
    book.ActiveSheet.Set(new CellAddress(r, 0), new Cell { Input = (r + 1).ToString(CultureInfo.InvariantCulture) });
    book.ActiveSheet.Set(new CellAddress(r, 1), new Cell { Input = $"=A{r + 1}*2" });
}
var session = new SpreadsheetSession(book);
var target = new CellAddress(rows / 2, 0);
var dependent = new CellAddress(rows / 2, 1);
var observations = new List<object>();
int input = rows;
WarmFormulas();
Observe("edit-one-and-recalculate-dependent", 30, () =>
{
    session.SetInput((++input).ToString(CultureInfo.InvariantCulture), target);
    var result = session.Calculation.Evaluate(session.Sheet, dependent);
    if (result.Number != input * 2) throw new InvalidOperationException("Incorrect recalculation.");
});
Observe("edit-one-and-undo", 20, () =>
{
    var before = session.Sheet.Get(target).Input;
    session.SetInput((++input).ToString(CultureInfo.InvariantCulture), target);
    session.Undo();
    if (session.Sheet.Get(target).Input != before) throw new InvalidOperationException("Incorrect undo.");
});
session.Select("A1:B100");
Observe("unchanged-selection-summary", 100, () => { _ = session.SelectionSummary(); });
WarmFormulas();
var counter = session.Calculation.GetType().GetProperty("EvaluatedCellCount");
var beforeEvaluations = counter?.GetValue(session.Calculation);
session.SetInput((++input).ToString(CultureInfo.InvariantCulture), target);
WarmFormulas();
var evaluations = counter is null ? (long?)null : (long)counter.GetValue(session.Calculation)! - (long)beforeEvaluations!;
var history = session.GetType().GetProperty("RetainedHistoryBytes")?.GetValue(session);
Console.WriteLine(JsonSerializer.Serialize(new
{
    schema = 1,
    framework = RuntimeInformation.FrameworkDescription,
    os = RuntimeInformation.OSDescription,
    architecture = RuntimeInformation.ProcessArchitecture.ToString(),
    logicalProcessors = Environment.ProcessorCount,
    rows, storedCells = rows * 2,
    warmIndependentFormulaEvaluationsAfterOneEdit = evaluations,
    estimatedRetainedHistoryBytes = history,
    observations
}, new JsonSerializerOptions { WriteIndented = true }));

void WarmFormulas()
{
    for (var r = 0; r < rows; r++) _ = session.Calculation.Evaluate(session.Sheet, new CellAddress(r, 1));
}
void Observe(string scenario, int iterations, Action action)
{
    for (var i = 0; i < 5; i++) action();
    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    var samples = new double[iterations];
    var allocated = GC.GetAllocatedBytesForCurrentThread();
    var collections = GC.CollectionCount(0);
    for (var i = 0; i < iterations; i++)
    {
        var start = Stopwatch.GetTimestamp(); action();
        samples[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }
    allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
    Array.Sort(samples);
    observations.Add(new { scenario, iterations, medianMilliseconds = samples[iterations / 2], p95Milliseconds = samples[(int)Math.Ceiling(iterations * .95) - 1], allocatedBytesPerOperation = allocated / iterations, generation0Collections = GC.CollectionCount(0) - collections });
}
