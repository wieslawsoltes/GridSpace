using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using GridSpace.Core;
using GridSpace.Editing;
using GridSpace.Formulas;
using GridSpace.IO;
using Xunit;

namespace GridSpace.Tests;

public class InterchangeTests
{
    [Fact] public void XlsxRoundTripsValuesFormulasStylesMergesAndCharts()
    {
        var book=SampleWorkbook.Create();var bytes=XlsxWorkbook.Write(book);var loaded=XlsxWorkbook.Read(bytes).Workbook;
        Assert.Equal(3,loaded.Sheets.Count);Assert.Equal(book.Sheets[0].Get("F6").Input,loaded.Sheets[0].Get("F6").Input);
        Assert.Equal(book.Sheets[0].Get("B5").Style.Background,loaded.Sheets[0].Get("B5").Style.Background);
        Assert.Equal(book.Sheets[0].Merges,loaded.Sheets[0].Merges);Assert.Single(loaded.Sheets[0].Charts);
        Assert.Equal(new CalculationEngine(book).Evaluate(book.Sheets[0],"F18"),new CalculationEngine(loaded).Evaluate(loaded.Sheets[0],"F18"));
    }
    [Fact] public void XlsxPackageHasValidXmlAndRelationships()
    {
        using var stream=new MemoryStream(XlsxWorkbook.Write(SampleWorkbook.Create()));using var zip=new ZipArchive(stream);
        foreach(var entry in zip.Entries.Where(e=>e.FullName.EndsWith(".xml")||e.FullName.EndsWith(".rels"))) { using var file=entry.Open();Assert.NotNull(XDocument.Load(file).Root); }
        Assert.NotNull(zip.GetEntry("[Content_Types].xml"));Assert.NotNull(zip.GetEntry("xl/charts/chart1_1.xml"));
    }
    [Fact] public void CsvImportTreatsFormulaTextAsLiteral()
    {
        var result=WorkbookFiles.Import("data.csv",Encoding.UTF8.GetBytes("Name,Value\r\nExample,=1+2"));
        var value=new CalculationEngine(result.Workbook).Evaluate(result.Workbook.ActiveSheet,"B2");Assert.Equal(ValueKind.Text,value.Kind);Assert.Equal("=1+2",value.ToString());
    }
    [Fact] public void CsvExportEscapesFormulaInjection()
    {
        var book=new Workbook();book.ActiveSheet.Set("A1","'=1+2");var text=Encoding.UTF8.GetString(WorkbookFiles.Csv(book));Assert.Equal("'=1+2",text);
    }
    [Fact] public void RejectsMalformedArchives() => Assert.ThrowsAny<Exception>(()=>XlsxWorkbook.Read(Encoding.UTF8.GetBytes("not a zip")));
    [Fact] public void ValidationAndFreezeRoundTrip()
    {
        var book=new Workbook();book.ActiveSheet.FrozenRows=2;book.ActiveSheet.FrozenColumns=1;book.ActiveSheet.ValidationLists["A1:A5"]=["Open","Closed"];
        var loaded=XlsxWorkbook.Read(XlsxWorkbook.Write(book)).Workbook;Assert.Equal(2,loaded.ActiveSheet.FrozenRows);Assert.Equal(["Open","Closed"],loaded.ActiveSheet.ValidationLists["A1:A5"]);
    }
}
