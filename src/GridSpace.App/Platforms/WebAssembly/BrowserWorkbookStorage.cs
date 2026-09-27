using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using GridSpace.IO;

namespace GridSpace.App;

internal sealed class BrowserWorkbookStorage : IWorkbookStorage
{
    public async Task<string?> ReadRecoveryAsync() => await BrowserFiles.Load();
    public async Task WriteRecoveryAsync(string json) => await BrowserFiles.Save(json);
    public async Task<OpenedFile?> OpenAsync()
    {
        var result = await BrowserFiles.Open(); if (string.IsNullOrEmpty(result)) return null;
        using var document = JsonDocument.Parse(result); var root = document.RootElement;
        return new(root.GetProperty("name").GetString()!, Convert.FromBase64String(root.GetProperty("base64").GetString()!));
    }
    public async Task SaveAsync(string name, byte[] bytes, string contentType) => await BrowserFiles.Download(name, Convert.ToBase64String(bytes), contentType);
}
internal static partial class BrowserFiles
{
    [JSImport("globalThis.gridSpaceStorage.load")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    internal static partial Task<string> Load();
    [JSImport("globalThis.gridSpaceStorage.save")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    internal static partial Task<string> Save(string document);
    [JSImport("globalThis.gridSpaceStorage.open")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    internal static partial Task<string> Open();
    [JSImport("globalThis.gridSpaceStorage.download")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    internal static partial Task<string> Download(string name, string base64, string contentType);
    [JSImport("globalThis.gridSpaceStorage.isTestMode")]
    internal static partial bool IsTestMode();
    [JSImport("globalThis.gridSpaceStorage.publishDiagnostics")]
    internal static partial void PublishDiagnostics(string json);
}
