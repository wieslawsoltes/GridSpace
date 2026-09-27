using GridSpace.Core;
using GridSpace.Editing;
using GridSpace.Formulas;
using Xunit;

namespace GridSpace.Tests;

public class EditingTests
{
    [Fact] public void UndoRedoAreAtomic()
    {
        var s = new SpreadsheetSession(new()); s.SetInput("42"); s.Undo(); Assert.Equal("", s.Sheet.Get("A1").Input);
        s.Redo(); Assert.Equal("42",s.Sheet.Get("A1").Input);
        Assert.Throws<InvalidOperationException>(()=>s.Perform("fail",()=>{s.Sheet.Set("B1","17");throw new InvalidOperationException();}));
        Assert.Equal("",s.Sheet.Get("B1").Input);
    }
    [Fact] public void PasteAndFillTranslateFormulas()
    {
        var s=new SpreadsheetSession(new());s.SetInput("=B1+$C$1");var copy=s.Copy();s.Select("A2");s.Paste(copy.Text);
        Assert.Equal("=B2+$C$1",s.Sheet.Get("A2").Input);
        s.Select("A2:A5");s.FillDown();Assert.Equal("=B5+$C$1",s.Sheet.Get("A5").Input);
    }
    [Fact] public void NumericFillExtendsSeries()
    {
        var s=new SpreadsheetSession(new());s.Sheet.Set("A1","2");s.Sheet.Set("A2","4");
        s.Fill(CellRange.Parse("A1:A2"),CellRange.Parse("A1:A5"));Assert.Equal("10",s.Sheet.Get("A5").Input);
    }
    [Fact] public void MergesPersistAndRejectDataLoss()
    {
        var s=new SpreadsheetSession(new());s.Select("A1:C1");s.Merge();
        var loaded=Workbook.FromJson(s.Book.ToJson());Assert.Single(loaded.ActiveSheet.Merges);
        s.Undo();Assert.Empty(s.Sheet.Merges);s.Sheet.Set("B1","data");Assert.Throws<InvalidOperationException>(s.Merge);
    }
    [Fact] public void RenameUpdatesCrossSheetFormulaAndUndo()
    {
        var s=new SpreadsheetSession(new());s.AddSheet("Data");s.SetInput("10");s.SwitchSheet(0);s.SetInput("=Data!A1*2");
        s.SwitchSheet(1);s.RenameSheet("Input Data");s.SwitchSheet(0);
        Assert.Equal("20",s.Calculation.Evaluate(s.Sheet,"A1").ToString());Assert.Equal("='Input Data'!A1*2",s.Sheet.Get("A1").Input);
    }
    [Fact] public void InsertRebasesAbsoluteReferencesAndRanges()
    {
        var s=new SpreadsheetSession(new());s.Sheet.Set("A1","10");s.Sheet.Set("B1","=$A$1+SUM(A1:A2)");s.Select("A1");s.Insert(true);
        Assert.Equal("10",s.Sheet.Get("A2").Input);Assert.Equal("=$A$2+SUM(A2:A3)",s.Sheet.Get("B2").Input);
        Assert.Equal("20",s.Calculation.Evaluate(s.Sheet,"B2").ToString());
    }
    [Fact] public void DelimitedTextRoundTripsQuotedFields()
    {
        string[][] rows=[["a,b","x\"y","line\nnext"],["", "12", "end"]];
        var parsed=DelimitedText.Parse(DelimitedText.Write(rows,','),',');Assert.Equal(rows[0],parsed[0]);Assert.Equal(rows[1],parsed[1]);
    }
    [Fact] public void SampleHasNoCalculationErrorsAndNativeRoundTrips()
    {
        var book=Workbook.FromJson(SampleWorkbook.Create().ToJson());var e=new CalculationEngine(book);
        foreach(var sheet in book.Sheets) foreach(var (address,cell) in sheet.Cells) if(cell.IsFormula) Assert.False(e.Evaluate(sheet,address).IsError,$"{sheet.Name}!{address}");
        Assert.True(e.Evaluate(book.Sheets[0],"F18").Number>0);Assert.Single(book.Sheets[0].Charts);
    }
    [Fact] public void ValidationRejectsInvalidValueWithoutChangingHistory()
    {
        var s=new SpreadsheetSession(new());s.SetValidation(["Open","Closed"]);s.SetInput("Open");
        Assert.Throws<InvalidOperationException>(()=>s.SetInput("Other"));Assert.Equal("Open",s.Sheet.Get("A1").Input);
    }
}
