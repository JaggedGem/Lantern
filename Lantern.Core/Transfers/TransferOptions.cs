namespace Lantern.Transfers;

public sealed record TransferOptions
{
    public const int ChunkSizeBytes = 64 * 1024;
    public const int MaximumFileCount = 1024;
    public int MaximumConcurrentTransfers { get; init; } = 2;
    public int MaximumRetainedTransfers { get; init; } = 256;
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromMinutes(2);
    public TimeSpan InactivityTimeout { get; init; } = TimeSpan.FromSeconds(30);

    internal void Validate()
    {
        if (MaximumConcurrentTransfers < 1 || MaximumRetainedTransfers < MaximumConcurrentTransfers)
            throw new ArgumentOutOfRangeException(nameof(MaximumConcurrentTransfers));
        if (RequestTimeout <= TimeSpan.Zero || InactivityTimeout <= TimeSpan.Zero
            || RequestTimeout.TotalMilliseconds > uint.MaxValue - 1 || InactivityTimeout.TotalMilliseconds > uint.MaxValue - 1)
            throw new ArgumentOutOfRangeException(nameof(RequestTimeout));
    }
}
