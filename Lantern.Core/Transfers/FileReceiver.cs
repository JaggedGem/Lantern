using Lantern.Models;
using Lantern.Networking;
using Lantern.Networking.Protocol;

namespace Lantern.Transfers;

internal sealed class FileReceiver
{
    // A complete transfer is published as one new directory. Existing user files are never overwritten.
    public async Task<string> ReceiveAsync(Connection connection, Transfer transfer, string destinationDirectory,
        TimeSpan timeout, Action<long> progress, CancellationToken token)
    {
        var root = Path.GetFullPath(destinationDirectory);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("Choose an existing destination directory.");
        RejectReparseAncestors(root);
        var final = Path.Combine(root, "Lantern-" + transfer.Id.ToString("N"));
        if (Directory.Exists(final) || File.Exists(final)) throw new IOException("This transfer destination already exists.");
        var stage = Path.Combine(root, ".lantern-" + Guid.NewGuid().ToString("N") + ".partial");
        Directory.CreateDirectory(stage);
        try
        {
            long total = 0;
            foreach (var metadata in transfer.Files)
            {
                token.ThrowIfCancellationRequested();
                var path = ResolvePath(stage, metadata.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                RejectReparseAncestors(Path.GetDirectoryName(path)!);
                await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    TransferOptions.ChunkSizeBytes, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    long offset = 0;
                    while (offset < metadata.SizeBytes)
                    {
                        var chunk = await TransferMessages.ReceiveAsync<FileChunkPayload>(connection, timeout, token).ConfigureAwait(false);
                        if (chunk.TransferId != transfer.Id || chunk.FileId != metadata.Id || chunk.Offset != offset
                            || chunk.Data == null || chunk.Data.Length is < 1 or > TransferOptions.ChunkSizeBytes
                            || chunk.Data.Length > metadata.SizeBytes - offset)
                            throw new InvalidDataException("Invalid file chunk identity, offset or size.");
                        await output.WriteAsync(chunk.Data, token).ConfigureAwait(false);
                        offset += chunk.Data.Length;
                        total += chunk.Data.Length;
                        progress(total);
                    }
                    var end = await TransferMessages.ReceiveAsync<FileCompletePayload>(connection, timeout, token).ConfigureAwait(false);
                    if (end.TransferId != transfer.Id || end.FileId != metadata.Id)
                        throw new InvalidDataException("The file completion marker does not match the manifest.");
                    await output.FlushAsync(token).ConfigureAwait(false);
                    if (output.Length != metadata.SizeBytes) throw new InvalidDataException("Received file length does not match metadata.");
                }
            }
            var complete = await TransferMessages.ReceiveAsync<TransferCompletePayload>(connection, timeout, token).ConfigureAwait(false);
            if (complete.TransferId != transfer.Id) throw new InvalidDataException("Invalid transfer completion marker.");
            token.ThrowIfCancellationRequested();
            RejectReparseAncestors(root);
            RejectReparseAncestors(stage);
            Directory.Move(stage, final);
            return final;
        }
        catch
        {
            try { if (Directory.Exists(stage)) Directory.Delete(stage, recursive: true); }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            { System.Diagnostics.Trace.TraceError($"Could not remove incomplete transfer directory {stage}: {cleanup}"); }
            throw;
        }
    }

    internal static string ResolvePath(string root, string relativePath)
    {
        // Apply the domain's Windows rules even if this receiver runs on Linux in tests.
        _ = new TransferFile(Guid.NewGuid(), relativePath, 0);
        var path = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException("The destination escapes the selected directory.");
        return path;
    }

    internal static void RejectReparseAncestors(string path)
    {
        for (var directory = new DirectoryInfo(path); directory != null; directory = directory.Parent)
        {
            if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Receive paths cannot contain symbolic links or reparse points.");
        }
    }
}
