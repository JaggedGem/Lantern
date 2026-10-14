namespace Lantern.Transfers;

public sealed record TransferOptions
{
    /// <summary>For isolated protocol tests only. Applications must configure TransferSecurity.</summary>
    public bool AllowUnencryptedConnections { get; init; }
    public const int ChunkSizeBytes = 64 * 1024;
    public const int MaximumFileCount = 1024;
    public int MaximumFilesPerTransfer { get; init; } = MaximumFileCount;
    public long MaximumTransferBytes { get; init; } = 100L * 1024 * 1024 * 1024;
    public int MaximumConcurrentTransfers { get; init; } = 2;
    public int MaximumRetainedTransfers { get; init; } = 256;
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromMinutes(2);
    public TimeSpan InactivityTimeout { get; init; } = TimeSpan.FromSeconds(30);

    internal void Validate()
    {
        if (
            MaximumConcurrentTransfers is < 1 or > 32
            || MaximumRetainedTransfers < MaximumConcurrentTransfers
            || MaximumFilesPerTransfer is < 1 or > MaximumFileCount
            || MaximumRetainedTransfers > 10000
            || MaximumTransferBytes < 0
        )
            throw new ArgumentOutOfRangeException(nameof(MaximumConcurrentTransfers));
        if (
            RequestTimeout <= TimeSpan.Zero
            || InactivityTimeout <= TimeSpan.Zero
            || RequestTimeout.TotalMilliseconds > uint.MaxValue - 1
            || InactivityTimeout.TotalMilliseconds > uint.MaxValue - 1
        )
            throw new ArgumentOutOfRangeException(nameof(RequestTimeout));
    }
}
