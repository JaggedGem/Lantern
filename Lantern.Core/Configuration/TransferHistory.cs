using Lantern.Models;

namespace Lantern.Configuration;

public sealed record TransferHistoryEntry(
    Guid Id,
    Guid SourceDeviceId,
    Guid DestinationDeviceId,
    TransferDirection Direction,
    TransferStatus Status,
    long TotalBytes,
    long TransferredBytes,
    DateTimeOffset CreatedAt,
    DateTimeOffset FinishedAt,
    TransferFailureKind? FailureKind,
    string? DestinationDirectory = null
);

/// <summary>Bounded terminal summaries only. No source paths, keys or file contents are persisted here.</summary>
public sealed class TransferHistory
{
    private const int MaximumEntries = 256;
    private readonly string _path;
    private readonly object _sync = new();
    private List<TransferHistoryEntry> _entries;

    public TransferHistory(string path)
    {
        _path = Path.GetFullPath(path);
        _entries = File.Exists(_path)
            ? JsonFile.Read<List<TransferHistoryEntry>>(_path)
                ?? throw new InvalidDataException("Invalid transfer history.")
            : [];
        if (
            _entries.Count > MaximumEntries
            || _entries.Any(e =>
                e is null
                || e.Id == Guid.Empty
                || !Enum.IsDefined(e.Direction)
                || !IsTerminal(e.Status)
                || e.TotalBytes < 0
                || e.TransferredBytes < 0
                || e.TransferredBytes > e.TotalBytes
                || e.FinishedAt < e.CreatedAt
            )
        )
            throw new InvalidDataException("Invalid transfer history.");
    }

    public IReadOnlyList<TransferHistoryEntry> GetEntries()
    {
        lock (_sync)
            return _entries.ToArray();
    }

    public void Clear()
    {
        lock (_sync)
        {
            JsonFile.Write(_path, Array.Empty<TransferHistoryEntry>());
            _entries = [];
        }
    }

    internal void Record(Transfer transfer, string? destinationDirectory = null)
    {
        if (!IsTerminal(transfer.Status))
            return;
        var entry = new TransferHistoryEntry(
            transfer.Id,
            transfer.SourceDeviceId,
            transfer.DestinationDeviceId,
            transfer.Direction,
            transfer.Status,
            transfer.TotalBytes,
            transfer.TransferredBytes,
            transfer.CreatedAt,
            transfer.FinishedAt!.Value,
            transfer.Error?.Kind,
            destinationDirectory
        );
        lock (_sync)
        {
            var updated = _entries
                .Where(e => e.Id != transfer.Id)
                .Append(entry)
                .TakeLast(MaximumEntries)
                .ToList();
            JsonFile.Write(_path, updated);
            _entries = updated;
        }
    }

    internal static bool IsTerminal(TransferStatus status) =>
        status
            is TransferStatus.Completed
                or TransferStatus.Failed
                or TransferStatus.Cancelled
                or TransferStatus.Rejected;
}
