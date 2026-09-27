using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace GridSpace.IO;

/// <summary>Bounded Office Open XML transitional workbook interchange. Never executes macros or follows external relationships.</summary>
public static partial class XlsxWorkbook
{
    private static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace P = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace T = "http://schemas.openxmlformats.org/package/2006/content-types";
    private const string RelBase = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
    private static string F(double value) => value.ToString("G17", CultureInfo.InvariantCulture);
    private static double Number(XAttribute? attribute, double fallback = 0) => double.TryParse(attribute?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n) ? n : fallback;
    private static int Int(XAttribute? attribute, int fallback = 0) => int.TryParse(attribute?.Value, out var n) ? n : fallback;
    private static XElement E(string name, params object?[] children) => new(S + name, children);

    private static XDocument Xml(ZipArchive zip, string path)
    {
        var entry = zip.GetEntry(path) ?? throw new InvalidDataException("Missing workbook part: " + path);
        if (entry.Length > 16 * 1024 * 1024) throw new InvalidDataException("An XML part exceeds 16 MB.");
        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 16 * 1024 * 1024 });
        return XDocument.Load(reader);
    }

    private static void WritePart(ZipArchive zip, string path, XElement root)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = XmlWriter.Create(stream, new() { Encoding = new UTF8Encoding(false), Indent = false });
        new XDocument(new XDeclaration("1.0", "utf-8", "yes"), root).Save(writer);
    }

    private static Dictionary<string, string> Relationships(ZipArchive zip, string part)
    {
        var slash = part.LastIndexOf('/'); var folder = slash < 0 ? "" : part[..(slash + 1)]; var file = part[(slash + 1)..];
        var path = folder + "_rels/" + file + ".rels";
        if (zip.GetEntry(path) is null) return [];
        var result = new Dictionary<string, string>();
        foreach (var rel in Xml(zip, path).Root!.Elements(P + "Relationship"))
        {
            if ((string?)rel.Attribute("TargetMode") == "External") continue;
            var target = (string?)rel.Attribute("Target") ?? "";
            var normalized = new Uri(new Uri("https://workbook.invalid/" + part), target).AbsolutePath.TrimStart('/');
            if (!normalized.StartsWith("xl/", StringComparison.Ordinal)) continue;
            result[(string?)rel.Attribute("Id") ?? ""] = Uri.UnescapeDataString(normalized);
        }
        return result;
    }

    private static XElement Relationship(string id, string type, string target) => new(P + "Relationship", new XAttribute("Id", id), new XAttribute("Type", RelBase + type), new XAttribute("Target", target));
}
