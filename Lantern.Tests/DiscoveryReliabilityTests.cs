using System.Net;
using System.Reflection;
using System.Text.Json;
using Lantern.Models;
using Lantern.Networking.Discovery;
using Xunit;

namespace Lantern.Tests;

public sealed class DiscoveryReliabilityTests
{
    private static void Announce(DeviceDiscovery discovery, Guid id, int type = 1, string name = "Peer", int port = 5000)
        => Process(discovery, JsonSerializer.SerializeToUtf8Bytes(new { v = 1, t = type, id, n = name, p = port }));
    private static void Process(DeviceDiscovery discovery, byte[] data)
        => typeof(DeviceDiscovery).GetMethod("ProcessDiscoveryPacket", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(discovery, [data, new IPEndPoint(IPAddress.Parse("192.168.1.20"), 12345)]);

    [Fact]
    public void DiscoveryKeepsOfflineIdentityAndResurrectsItWithoutFalsifyingLastSeen()
    {
        var clock = new ManualClock();
        using var discovery = new DeviceDiscovery(new LocalDevice(Guid.NewGuid(), "local", 5001), timeProvider: clock);
        var id = Guid.NewGuid();
        int additions = 0, updates = 0;
        discovery.DeviceDiscovered += (_, _) => additions++;
        discovery.DeviceStatusChanged += (_, _) => updates++;
        Announce(discovery, id);
        var seen = discovery.GetDiscoveredDevices()[0].LastSeen;
        clock.Advance(TimeSpan.FromSeconds(16));
        typeof(DeviceDiscovery).GetMethod("CheckPresence", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(discovery, null);
        Assert.Single(discovery.GetDiscoveredDevices());
        Assert.Equal(DeviceStatus.Offline, discovery.GetDiscoveredDevices()[0].Status);
        Assert.Equal(seen, discovery.GetDiscoveredDevices()[0].LastSeen);
        Announce(discovery, id);
        Assert.Equal(DeviceStatus.Online, discovery.GetDiscoveredDevices()[0].Status);
        Assert.Equal(1, additions);
        Assert.Equal(2, updates);
    }

    [Fact]
    public void SnapshotMutationAndReentrantHandlersDoNotChangeInternalDevices()
    {
        using var discovery = new DeviceDiscovery(new LocalDevice(Guid.NewGuid(), "local", 5001));
        discovery.DeviceDiscovered += (_, args) =>
        {
            Assert.Single(discovery.GetDiscoveredDevices());
            args.Device.UpdatePort(6000);
        };
        Announce(discovery, Guid.NewGuid());
        var snapshot = discovery.GetDiscoveredDevices()[0];
        Assert.Equal(5000, snapshot.Port);
        snapshot.UpdatePort(7000);
        Assert.Equal(5000, discovery.GetDiscoveredDevices()[0].Port);
    }

    [Fact]
    public void MaterialChangesNotifyButQuietHeartbeatsDoNot()
    {
        using var discovery = new DeviceDiscovery(new LocalDevice(Guid.NewGuid(), "local", 5001));
        var id = Guid.NewGuid();
        int updates = 0;
        discovery.DeviceUpdated += (_, _) => updates++;
        Announce(discovery, id);
        Announce(discovery, id);
        Announce(discovery, id, name: "Renamed", port: 6000);
        Assert.Equal(1, updates);
    }

    [Fact]
    public void RejectsIncompleteEmptyAndUnknownAnnouncements()
    {
        using var discovery = new DeviceDiscovery(new LocalDevice(Guid.NewGuid(), "local", 5001));
        Announce(discovery, Guid.Empty);
        Announce(discovery, Guid.NewGuid(), type: 999);
        Announce(discovery, Guid.NewGuid(), name: new string('a', 129));
        Process(discovery, JsonSerializer.SerializeToUtf8Bytes(new { n = "Peer", p = 5000 }));
        Assert.Empty(discovery.GetDiscoveredDevices());
    }

    [Fact]
    public void IdentityDoesNotReplaceInvalidOrUnwritableStorageSilently()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lantern-identity-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "id.json");
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(new { deviceId = Guid.Empty }));
            Assert.Throws<InvalidDataException>(() => LocalDeviceIdentityProvider.LoadOrCreateDeviceId(path));
            File.Delete(path);
            Directory.CreateDirectory(path);
            Assert.ThrowsAny<IOException>(() => LocalDeviceIdentityProvider.LoadOrCreateDeviceId(path));
        }
        finally { Directory.Delete(directory, true); }
    }

    private sealed class ManualClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(_ticks);
        public void Advance(TimeSpan elapsed) => _ticks += elapsed.Ticks;
    }
}
