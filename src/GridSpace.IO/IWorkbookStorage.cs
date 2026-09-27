namespace GridSpace.IO;

public sealed record OpenedFile(string Name, byte[] Bytes);
public interface IWorkbookStorage
{
    Task<OpenedFile?> OpenAsync();
    Task SaveAsync(string name, byte[] bytes, string contentType);
    Task<string?> ReadRecoveryAsync();
    Task WriteRecoveryAsync(string json);
}
