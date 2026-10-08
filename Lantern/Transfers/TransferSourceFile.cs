using Lantern.Models;

namespace Lantern.Transfers;

/// <summary>Local-only selection. SourcePath is never serialized in the protocol.</summary>
public sealed class TransferSourceFile
{
    public TransferSourceFile(TransferFile metadata, string sourcePath)
    {
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        SourcePath = Path.GetFullPath(sourcePath);
    }
    public TransferFile Metadata { get; }
    public string SourcePath { get; }

    public static TransferSourceFile FromPath(string sourcePath, string? relativePath = null)
    {
        var file = new FileInfo(Path.GetFullPath(sourcePath));
        if (!file.Exists) throw new FileNotFoundException("The selected file no longer exists.", file.FullName);
        if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Symbolic links and reparse points cannot be selected as source files.");
        return new TransferSourceFile(new TransferFile(Guid.NewGuid(), relativePath ?? file.Name, file.Length,
            new DateTimeOffset(file.LastWriteTimeUtc)), file.FullName);
    }
}
