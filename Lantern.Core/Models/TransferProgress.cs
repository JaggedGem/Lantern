namespace Lantern.Models;

/// <summary>Average throughput and ETA derived from handled bytes; no filesystem polling.</summary>
public sealed record TransferProgress(
    double Fraction,
    double BytesPerSecond,
    TimeSpan? EstimatedRemaining
)
{
    public static TransferProgress Calculate(Transfer transfer, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(transfer);
        var fraction =
            transfer.TotalBytes == 0
                ? (transfer.Status == TransferStatus.Completed ? 1 : 0)
                : (double)transfer.TransferredBytes / transfer.TotalBytes;
        var elapsed = transfer.StartedAt.HasValue
            ? ((transfer.FinishedAt ?? now) - transfer.StartedAt.Value).TotalSeconds
            : 0;
        var speed = elapsed > 0 ? transfer.TransferredBytes / elapsed : 0;
        var remainingSeconds =
            speed > 0
                ? (transfer.TotalBytes - transfer.TransferredBytes) / speed
                : double.PositiveInfinity;
        TimeSpan? remaining =
            remainingSeconds <= TimeSpan.MaxValue.TotalSeconds
                ? TimeSpan.FromSeconds(remainingSeconds)
                : null;
        return new(fraction, speed, remaining);
    }
}
