using GridSpace.IO;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace GridSpace.App;

internal sealed class DesktopWorkbookStorage : IWorkbookStorage
{
    private static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GridSpace");
    private static string RecoveryPath => Path.Combine(DirectoryPath, "recovery.gridspace");
    public async Task<string?> ReadRecoveryAsync() => File.Exists(RecoveryPath) ? await File.ReadAllTextAsync(RecoveryPath) : null;
    public async Task WriteRecoveryAsync(string json)
    {
        Directory.CreateDirectory(DirectoryPath); var temporary = RecoveryPath + ".tmp";
        await File.WriteAllTextAsync(temporary, json); File.Move(temporary, RecoveryPath, true);
    }
    public async Task<OpenedFile?> OpenAsync()
    {
        var picker = new FileOpenPicker();
        foreach (var extension in new[] { ".gridspace", ".json", ".xlsx", ".csv", ".tsv", ".txt" }) picker.FileTypeFilter.Add(extension);
        var file = await picker.PickSingleFileAsync(); if (file is null) return null;
        if ((await file.GetBasicPropertiesAsync()).Size > WorkbookFiles.MaximumFileBytes) throw new InvalidDataException("The workbook exceeds the 32 MB import limit.");
        using var stream = await file.OpenStreamForReadAsync(); using var memory = new MemoryStream(); await stream.CopyToAsync(memory); return new(file.Name, memory.ToArray());
    }
    public async Task SaveAsync(string name, byte[] bytes, string contentType)
    {
        var picker = new FileSavePicker { SuggestedFileName = Path.GetFileNameWithoutExtension(name) }; picker.FileTypeChoices.Add(contentType, [Path.GetExtension(name)]);
        var file = await picker.PickSaveFileAsync(); if (file is null) throw new OperationCanceledException("Save was cancelled.");
        await FileIO.WriteBytesAsync(file, bytes);
    }
}
