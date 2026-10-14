using System.Buffers.Binary;
using System.Text.Json;

namespace Lantern.Transfers;

internal sealed record OfferedFile(string Name, long Size, string Hash);
internal sealed record TransferOffer(int Version, Guid Id, string Sender, OfferedFile[] Files);
internal sealed record TransferReply(bool Accepted, string? Error = null);

internal static class TransferWire
{
    public const int ChunkSize = 64 * 1024;
    public const int MaximumFiles = 100;
    public const long MaximumBytes = 1L << 40;
    private const int MaximumHeaderBytes = 64 * 1024;

    public static async Task WriteAsync<T>(Stream stream, T value, CancellationToken token)
    {
        var data = JsonSerializer.SerializeToUtf8Bytes(value);
        if (data.Length > MaximumHeaderBytes) throw new InvalidDataException("Transfer metadata is too large.");
        var prefix = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(prefix, data.Length);
        await stream.WriteAsync(prefix, token).ConfigureAwait(false);
        await stream.WriteAsync(data, token).ConfigureAwait(false);
    }

    public static async Task<T> ReadAsync<T>(Stream stream, CancellationToken token)
    {
        var prefix = new byte[4];
        await stream.ReadExactlyAsync(prefix, token).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32BigEndian(prefix);
        if (length is < 1 or > MaximumHeaderBytes) throw new InvalidDataException("Invalid transfer metadata size.");
        var bytes = new byte[length];
        await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        return JsonSerializer.Deserialize<T>(bytes) ?? throw new InvalidDataException("Missing transfer metadata.");
    }

    public static void Validate(TransferOffer offer)
    {
        if (offer.Version != 1 || offer.Id == Guid.Empty || string.IsNullOrWhiteSpace(offer.Sender) || offer.Sender.Length > 80 || offer.Sender.Any(char.IsControl))
            throw new InvalidDataException("Unsupported or invalid transfer offer.");
        if (offer.Files is null || offer.Files.Length is < 1 or > MaximumFiles)
            throw new InvalidDataException($"Select between 1 and {MaximumFiles} files.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var file in offer.Files)
        {
            if (file is null) throw new InvalidDataException("Invalid file metadata.");
            ValidateFileName(file.Name);
            if (!names.Add(file.Name)) throw new InvalidDataException("A transfer cannot contain duplicate file names.");
            if (file.Size < 0 || file.Size > MaximumBytes - total) throw new InvalidDataException("The transfer size is invalid or exceeds 1 TB.");
            total += file.Size;
            if (file.Hash is null || file.Hash.Length != 64 || !file.Hash.All(Uri.IsHexDigit)) throw new InvalidDataException("Invalid file checksum.");
        }
    }

    internal static void ValidateFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 180 || name is "." or ".." || name.EndsWith('.') || name.EndsWith(' ') ||
            name.Any(c => char.IsControl(c) || "<>:\"/\\|?*".Contains(c)))
            throw new InvalidDataException("A file name is unsafe or too long.");
        var stem = name.Split('.')[0].TrimEnd(' ');
        if (new[] { "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$" }.Contains(stem, StringComparer.OrdinalIgnoreCase) ||
            (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && (char.IsDigit(stem[3]) || "¹²³".Contains(stem[3]))))
            throw new InvalidDataException("A file name is reserved by Windows.");
    }
}
