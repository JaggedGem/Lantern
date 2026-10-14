namespace Lantern.Networking.Discovery;

public sealed record DiscoveryOptions
{
    public int UdpPort { get; init; } = 52845;
    public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromSeconds(3);
    public TimeSpan OfflineTimeout { get; init; } = TimeSpan.FromSeconds(15);
    public TimeSpan ExpirationInterval { get; init; } = TimeSpan.FromSeconds(5);
    public int MaximumDevices { get; init; } = 256;

    internal void Validate()
    {
        if (UdpPort is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(UdpPort));
        if (HeartbeatInterval <= TimeSpan.Zero || OfflineTimeout <= HeartbeatInterval || ExpirationInterval <= TimeSpan.Zero)
            throw new ArgumentException("Discovery timing must be positive and tolerate multiple heartbeats.");
        if (MaximumDevices < 1) throw new ArgumentOutOfRangeException(nameof(MaximumDevices));
    }
}
