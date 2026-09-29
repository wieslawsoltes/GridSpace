using System.Collections.Immutable;
using GridSpace.Core;
using GridSpace.Editing;
using GridSpace.Formulas;
using Xunit;

namespace GridSpace.Tests;

public sealed class PivotLayoutCacheTests
{
    private static SpreadsheetSession Create()
    {
        var session = ChartAndPivotTests.Sales();
        session.AddSheet("Report");
        session.SetPivotTable(ChartAndPivotTests.Definition());
        return session;
    }

    [Fact]
    public void FieldLayoutUsesLastRefreshSnapshotAndExplicitRefreshReadsNewValues()
    {
        var session = Create();
        var original = session.Sheet.PivotTables.Single();
        var cache = original.Cache;
        session.Book.FindSheet("Sales")!.Set("C2", "1010");
        var report = session.ReconfigurePivotTable(original with { Columns = [] });
        Assert.Same(cache, session.Sheet.PivotTables.Single().Cache);
        Assert.Equal(60, report[1, 1].Number);
        Assert.Equal(200, report[report.RowCount - 1, report.ColumnCount - 1].Number);
        Assert.StartsWith("Layout ", session.UndoName);
        session.RefreshPivotTable(original.Id);
        Assert.NotSame(cache, session.Sheet.PivotTables.Single().Cache);
        var refreshed = PivotEngine.FromCache(session.Sheet.PivotTables.Single());
        Assert.Equal(1200, refreshed[refreshed.RowCount - 1, refreshed.ColumnCount - 1].Number);
    }

    [Fact]
    public void LayoutDoesNotEvaluateScalarSourceFormulasOrTrustInjectedCache()
    {
        var session = Create();
        var original = session.Sheet.PivotTables.Single();
        session.Book.FindSheet("Sales")!.Set("C2", "=SUM(C3:C6)+9999");
        var evaluations = session.Calculation.EvaluatedCellCount;
        var forged = original.Cache! with { Rows = [] };
        var report = session.ReconfigurePivotTable(original with
        {
            Cache = forged, Values = [new PivotValueField { Field = 2, Aggregate = PivotAggregate.Average }]
        });
        Assert.Equal(evaluations, session.Calculation.EvaluatedCellCount);
        Assert.Same(original.Cache, session.Sheet.PivotTables.Single().Cache);
        Assert.Equal(40, report[report.RowCount - 1, report.ColumnCount - 1].Number);
    }

    [Fact]
    public void CapturePolicyChangesExplicitlyRecaptureAndUndoRestoresBothEpochs()
    {
        var session = Create();
        var original = session.Sheet.PivotTables.Single();
        session.Book.FindSheet("Sales")!.HiddenRows.Add(1);
        session.Book.Touch();
        var report = session.ReconfigurePivotTable(original with { IncludeHiddenRows = false });
        Assert.Equal(190, report[report.RowCount - 1, report.ColumnCount - 1].Number);
        var replacement = session.Sheet.PivotTables.Single();
        Assert.NotSame(original.Cache, replacement.Cache);
        session.Undo();
        Assert.Same(original.Cache, session.Sheet.PivotTables.Single().Cache);
        Assert.True(session.Sheet.PivotTables.Single().IncludeHiddenRows);
        session.Redo();
        Assert.Same(replacement.Cache, session.Sheet.PivotTables.Single().Cache);
        Assert.False(session.Sheet.PivotTables.Single().IncludeHiddenRows);
    }

    [Fact]
    public void FailedLayoutExpansionIsAtomicAndRetainsSnapshot()
    {
        var session = Create();
        var original = session.Sheet.PivotTables.Single();
        session.SetInput("Keep", CellAddress.Parse("E2"));
        var before = session.Book.ToJson();
        Assert.Throws<InvalidOperationException>(() => session.ReconfigurePivotTable(original with
        {
            Values = [new() { Field = 2 }, new() { Field = 3 }]
        }));
        Assert.Equal(before, session.Book.ToJson());
        Assert.Same(original.Cache, session.Sheet.PivotTables.Single().Cache);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImportedOrMissingCacheRequiresExplicitRefresh(bool missing)
    {
        var session = Create();
        var original = session.Sheet.PivotTables.Single();
        session.Sheet.PivotTables[0] = original with
        {
            Cache = missing ? null : original.Cache, NeedsLayoutRefresh = !missing
        };
        var before = session.Book.ToJson();
        Assert.Throws<InvalidOperationException>(() => session.ReconfigurePivotTable(original with { Columns = [] }));
        Assert.Equal(before, session.Book.ToJson());
        session.RefreshPivotTable(original.Id);
        session.ReconfigurePivotTable(session.Sheet.PivotTables.Single() with { Columns = [] });
        Assert.NotNull(session.Sheet.PivotTables.Single().Cache);
    }

    [Fact]
    public void CachedFieldListsAreReadOnlyAndRespectSnapshotEpoch()
    {
        var session = Create();
        var original = session.Sheet.PivotTables.Single();
        var values = PivotFieldValues.Get(original.Cache!, 0);
        Assert.Equal(new[] { "East", "West" }, values);
        Assert.Same(values, PivotFieldValues.Get(original.Cache!, 0));
        Assert.Throws<NotSupportedException>(() => ((IList<string>)values)[0] = "Mutated");
        session.Book.FindSheet("Sales")!.Set("A2", "North");
        Assert.Same(values, PivotFieldValues.Get(original.Cache!, 0));
        session.RefreshPivotTable(original.Id);
        Assert.Equal(new[] { "East", "North", "West" }, PivotFieldValues.Get(session.Sheet.PivotTables.Single().Cache!, 0));
    }

    [Fact]
    public void FieldCatalogRejectsTooManyValuesInsteadOfTruncating()
    {
        var cache = new PivotCacheSnapshot
        {
            Headers = ["Key"], Rows = Enumerable.Range(0, 10_001)
                .Select(i => ImmutableArray.Create(i.ToString(System.Globalization.CultureInfo.InvariantCulture))).ToImmutableArray()
        };
        Assert.Throws<InvalidOperationException>(() => PivotFieldValues.Get(cache, 0));
    }

    [Fact]
    public void CachedFiltersStillSupportLiteralTextBlankBooleanAndErrorValues()
    {
        var cache = new PivotCacheSnapshot
        {
            Headers = ["Key"], Rows = [["'001"], ["1"], ["TRUE"], ["#N/A"], [""], ["'alpha"], ["'ALPHA"]]
        };
        var values = PivotFieldValues.Get(cache, 0);
        Assert.Equal(6, values.Count);
        Assert.Contains("001", values); Assert.Contains("1", values); Assert.Contains("TRUE", values);
        Assert.Contains("#N/A", values); Assert.Contains("", values);
        Assert.Throws<ArgumentOutOfRangeException>(() => PivotFieldValues.Get(cache, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => PivotFieldValues.Get(cache, 1));
    }
}
