using Lantern.Networking;
using Lantern.Networking.Protocol;

namespace Lantern.Transfers;

internal sealed class FileSender
{
    public async Task SendAsync(Connection connection, Guid transferId, IReadOnlyList<TransferSourceFile> files,
        Action<long> progress, CancellationToken token)
    {
        var buffer = new byte[TransferOptions.ChunkSizeBytes];
        long total = 0;
        foreach (var file in files)
        {
            if ((File.GetAttributes(file.SourcePath) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Source reparse points are not supported.");
            await using var source = new FileStream(file.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                buffer.Length, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (source.Length != file.Metadata.SizeBytes) throw new IOException("The selected file size changed before sending.");
            long offset = 0;
            while (offset < file.Metadata.SizeBytes)
            {
                var count = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, file.Metadata.SizeBytes - offset)), token).ConfigureAwait(false);
                if (count == 0) throw new EndOfStreamException("The selected file was truncated while sending.");
                await TransferMessages.SendAsync(connection, new FileChunkPayload(transferId, file.Metadata.Id, offset,
                    buffer.AsSpan(0, count).ToArray()), token).ConfigureAwait(false);
                offset += count;
                total += count;
                progress(total);
            }
            if (await source.ReadAsync(buffer.AsMemory(0, 1), token).ConfigureAwait(false) != 0)
                throw new IOException("The selected file grew while sending.");
            await TransferMessages.SendAsync(connection, new FileCompletePayload(transferId, file.Metadata.Id), token).ConfigureAwait(false);
        }
        await TransferMessages.SendAsync(connection, new TransferCompletePayload(transferId), token).ConfigureAwait(false);
    }
}
