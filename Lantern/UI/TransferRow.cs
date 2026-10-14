using Lantern.Models;
using Lantern.Configuration;
using Lantern.Transfers;

namespace Lantern.UI;

/// <summary>Presentation state; the backend's immutable transfer remains the source of truth.</summary>
internal sealed record TransferRow(Guid Id, Guid PeerId, string Peer, string Files, int FileCount,
    TransferDirection Direction, TransferStatus State, long BytesTransferred, long TotalBytes,
    DateTimeOffset StartedAt, string? Error = null, string? SavedFolder = null,
    string? CurrentFile = null, double Speed = 0, TimeSpan? Remaining = null)
{
    public bool IsFinished => State is TransferStatus.Completed or TransferStatus.Cancelled or TransferStatus.Failed or TransferStatus.Rejected;
    public int Percent => TotalBytes == 0 ? (State == TransferStatus.Completed ? 100 : 0) : (int)Math.Clamp(BytesTransferred * 100d / TotalBytes, 0, 100);
    public static TransferRow From(TransferChangedEventArgs args, string peer)
    {
        var transfer = args.Transfer;
        var progress = TransferProgress.Calculate(transfer, DateTimeOffset.UtcNow);
        return new(transfer.Id, transfer.Direction == TransferDirection.Sending ? transfer.DestinationDeviceId : transfer.SourceDeviceId,
            peer, string.Join(", ", transfer.Files.Select(f => f.RelativePath)), transfer.Files.Count, transfer.Direction, transfer.Status,
            transfer.TransferredBytes, transfer.TotalBytes, transfer.CreatedAt, DescribeError(transfer.Error), args.DestinationDirectory,
            transfer.Files.FirstOrDefault(f => f.Id == args.CurrentFileId)?.RelativePath, progress.BytesPerSecond, progress.EstimatedRemaining);
    }
    private static string? DescribeError(TransferError? error) => error is null ? null : error.Description + (error.Kind switch
    {
        TransferFailureKind.Authentication => "\r\nVerify and pair both PCs' current device IDs and fingerprints, then try a new transfer.",
        TransferFailureKind.Connection => "\r\nCheck that both PCs are online and that the private-network firewall allows Lantern.",
        TransferFailureKind.FileSystem => "\r\nCheck the selected files, destination permissions and available disk space.",
        TransferFailureKind.Integrity => "\r\nThe incomplete receive batch was not published. Check the source files before retrying.",
        _ => ""
    });
    public static TransferRow From(TransferHistoryEntry entry, string peer) => new(entry.Id,
        entry.Direction == TransferDirection.Sending ? entry.DestinationDeviceId : entry.SourceDeviceId, peer,
        "Previous session", 0, entry.Direction, entry.Status, entry.TransferredBytes, entry.TotalBytes, entry.CreatedAt,
        entry.FailureKind?.ToString(), entry.DestinationDirectory);
}
