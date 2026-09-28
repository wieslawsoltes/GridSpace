using GridSpace.Core;
using GridSpace.Editing;
using GridSpace.Formulas;
using Xunit;

namespace GridSpace.Tests;

public sealed class IncrementalPerformanceTests
{
    [Fact]
    public void CellDeltaDoesNotReplaceDocumentOrInvalidateIndependentFormulas()
    {
        var book = new Workbook(); var sheet = book.ActiveSheet;
        for (var i = 0; i < 1000; i++) { sheet.Set(new(i, 0), new Cell { Input = "1" }); sheet.Set(new(i, 1), new Cell { Input = $"=A{i + 1}*2" }); }
        var session = new SpreadsheetSession(book); var engine = session.Calculation;
        for (var i = 0; i < 1000; i++) Assert.Equal("2", engine.Evaluate(sheet, new CellAddress(i, 1)).ToString());
        var evaluated = engine.EvaluatedCellCount; var structure = book.StructureRevision;
        session.SetInput("5", new(500, 0));
        Assert.Equal(structure, book.StructureRevision);
        for (var i = 0; i < 1000; i++) Assert.Equal(i == 500 ? "10" : "2", engine.Evaluate(sheet, new CellAddress(i, 1)).ToString());
        Assert.Equal(2, engine.EvaluatedCellCount - evaluated);
        Assert.InRange(session.RetainedHistoryBytes, 1, 1024);
        session.Undo(); Assert.Same(book, session.Book); Assert.Same(sheet, session.Sheet); Assert.Same(engine, session.Calculation);
        Assert.Equal("2", engine.Evaluate(sheet, "B501").ToString());
        session.Redo(); Assert.Equal("10", engine.Evaluate(sheet, "B501").ToString());
    }
    [Fact]
    public void DependencyChangesDetachPreviousPrecedents()
    {
        var book = new Workbook(); var sheet = book.ActiveSheet;
        sheet.Set("A1", "1"); sheet.Set("B1", "2"); sheet.Set("C1", "=A1"); sheet.Set("D1", "=C1+1");
        var engine = new CalculationEngine(book); Assert.Equal("2", engine.Evaluate(sheet, "D1").ToString());
        sheet.Set("C1", "=B1"); Assert.Equal("3", engine.Evaluate(sheet, "D1").ToString());
        var before = engine.EvaluatedCellCount; sheet.Set("A1", "50");
        Assert.Equal("3", engine.Evaluate(sheet, "D1").ToString()); Assert.Equal(before, engine.EvaluatedCellCount);
        sheet.Set("B1", "10"); Assert.Equal("11", engine.Evaluate(sheet, "D1").ToString());
    }
    [Fact]
    public void BlankPrecedentsAreTrackedButNotCached()
    {
        var book = new Workbook(); var sheet = book.ActiveSheet; sheet.Set("A1", "=Z10+1");
        var engine = new CalculationEngine(book); Assert.Equal("1", engine.Evaluate(sheet, "A1").ToString());
        for (var i = 100; i < 1000; i++) engine.Evaluate(sheet, new CellAddress(i, 0));
        Assert.Equal(1, engine.CachedCellCount);
        sheet.Set("Z10", "41"); Assert.Equal("42", engine.Evaluate(sheet, "A1").ToString());
    }
    [Fact]
    public void StyleOnlyDeltaRetainsCalculatedResults()
    {
        var session = new SpreadsheetSession(new Workbook()); session.SetInput("=1+2");
        Assert.Equal("3", session.Calculation.Evaluate(session.Sheet, "A1").ToString()); var before = session.Calculation.EvaluatedCellCount;
        session.ApplyStyle(s => s with { Bold = true });
        Assert.Equal("3", session.Calculation.Evaluate(session.Sheet, "A1").ToString());
        Assert.Equal(before, session.Calculation.EvaluatedCellCount);
    }
    [Fact]
    public void BulkCellChangesArePrevalidatedAndAtomic()
    {
        var session = new SpreadsheetSession(new Workbook()); session.SetInput("original");
        var revision = session.Book.Revision;
        Assert.Throws<ArgumentException>(() => session.ApplyCells("Bad paste", [new(new(0, 0), new Cell { Input = "changed" }), new(new(-1, 0), new Cell())]));
        Assert.Equal("original", session.Sheet.Get("A1").Input); Assert.Equal(revision, session.Book.Revision);
        Assert.Throws<InvalidOperationException>(() => session.ApplyStyle(_ => throw new InvalidOperationException()));
        Assert.Equal("original", session.Sheet.Get("A1").Input);
    }
    [Fact]
    public void DeltaHistoryInterleavesWithStructuralHistory()
    {
        var session = new SpreadsheetSession(new Workbook()); session.SetInput("10"); session.AddSheet("Second"); session.SetInput("20");
        session.Undo(); Assert.Equal("", session.Sheet.Get("A1").Input);
        session.Undo(); Assert.Single(session.Book.Sheets); Assert.Equal("10", session.Sheet.Get("A1").Input);
        session.Undo(); Assert.Equal("", session.Sheet.Get("A1").Input);
        session.Redo(); session.Redo(); session.Redo();
        Assert.Equal("20", session.Sheet.Get("A1").Input); Assert.Equal("10", session.Book.Sheets[0].Get("A1").Input);
    }
    [Fact]
    public void MissedJournalEntriesFallBackToFullInvalidation()
    {
        var book = new Workbook(); var sheet = book.ActiveSheet; sheet.Set("A1", "=B1+1");
        var engine = new CalculationEngine(book); Assert.Equal("1", engine.Evaluate(sheet, "A1").ToString());
        for (var i = 0; i < 9000; i++) sheet.Set("B1", i.ToString());
        Assert.Equal("9000", engine.Evaluate(sheet, "A1").ToString());
    }
    [Fact]
    public void SummaryIsCachedAcrossViewOnlyUpdates()
    {
        var session = new SpreadsheetSession(new Workbook()); session.SetInput("1"); session.SetInput("2", new(0, 1)); session.Select("A1:B1");
        var summary = session.SelectionSummary(); var before = session.Calculation.EvaluatedCellCount;
        for (var i = 0; i < 100; i++) { session.Notify("View"); Assert.Equal(summary, session.SelectionSummary()); }
        Assert.Equal(before, session.Calculation.EvaluatedCellCount);
    }
    [Fact]
    public void FillUsesDeltasAndRejectsPartialSpillOverwrites()
    {
        var session = new SpreadsheetSession(new Workbook());
        session.SetInput("=SEQUENCE(3)", new(0, 0));
        session.SetInput("99", new(1, 1));
        var book = session.Book;
        Assert.Throws<InvalidOperationException>(() => session.Fill(CellRange.Parse("B2"), CellRange.Parse("A2:B2"), false));
        Assert.Equal("2", session.Calculation.Evaluate(session.Sheet, "A2").ToString());
        session.Fill(CellRange.Parse("A2"), CellRange.Parse("A2:C2"), false);
        Assert.Equal("2", session.Sheet.Get("C2").Input);
        session.Undo(); Assert.Same(book, session.Book); Assert.Equal("99", session.Sheet.Get("B2").Input);
    }
    [Fact]
    public void DynamicSortRetainsBlankLastInBothDirections()
    {
        var book = new Workbook(); var sheet = book.ActiveSheet;
        sheet.Set("A1", "3"); sheet.Set("A3", "1"); sheet.Set("A4", "2");
        var engine = new CalculationEngine(book);
        Assert.Equal("3", engine.EvaluateFormula(sheet, "=SORT(A1:A4,1,-1)").Element(0, 0).ToString());
        Assert.Equal(ValueKind.Blank, engine.EvaluateFormula(sheet, "=SORT(A1:A4,1,-1)").Element(3, 0).Kind);
        Assert.Equal("1", engine.EvaluateFormula(sheet, "=SORTBY(A1:A4,A1:A4)").Element(0, 0).ToString());
        Assert.Equal(ValueKind.Blank, engine.EvaluateFormula(sheet, "=SORTBY(A1:A4,A1:A4)").Element(3, 0).Kind);
    }
}
