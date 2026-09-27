namespace Lantern.Models;

public class TransferFile
{
    public string FileName { get; }
    public long FileSize { get; }
    public string FilePath { get; }
    public string? FileHash { get; }

    public TransferFile(string fileName, long fileSize, string filePath, string? fileHash){
        FileName = fileName;
        FileSize = fileSize;
        FilePath = filePath;
        FileHash = fileHash;
    }
}