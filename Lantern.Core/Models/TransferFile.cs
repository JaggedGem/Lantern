namespace Lantern.Models;

public sealed record TransferFile(string FileName, long FileSize, string FilePath, string? FileHash);
