using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Lantern.Application;
using Lantern.Models;
using Lantern.Networking;
using Lantern.Networking.Discovery;
using Lantern.Networking.Protocol;
using Xunit;

namespace Lantern.Tests;

public sealed class ApplicationStartupTests
{
    private static int UnusedUdpPort()
    {
        using var socket = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
        return ((IPEndPoint)socket.Client.LocalEndPoint!).Port;
    }

    [Fact]
    public async Task CompositionAdvertisesTheLiveTcpPortAndKeepsIdentityAcrossRestarts()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lantern-startup-" + Guid.NewGuid());
        var identity = Path.Combine(directory, "id.json");
        var options = new DiscoveryOptions { UdpPort = UnusedUdpPort() };
        Guid firstId;
        try
        {
            await using (var application = new LanternApplication("test", discoveryOptions: options, identityPath: identity))
            {
                await application.StartAsync();
                firstId = application.LocalDevice!.Id;
                Assert.Equal(application.Transfers.ListeningPort, application.LocalDevice.Port);
                Assert.InRange(application.LocalDevice.Port, 1, 65535);
                await application.StopAsync();
                Assert.Null(application.Transfers.ListeningPort);
                await application.StartAsync();
                Assert.Equal(firstId, application.LocalDevice!.Id);
            }
            await using var restarted = new LanternApplication("test", discoveryOptions: options, identityPath: identity);
            await restarted.StartAsync();
            Assert.Equal(firstId, restarted.LocalDevice!.Id);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task DiscoveryBindFailureRollsBackTheTcpListener()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lantern-startup-" + Guid.NewGuid());
        using var occupied = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
        var port = ((IPEndPoint)occupied.Client.LocalEndPoint!).Port;
        try
        {
            await using var application = new LanternApplication("test", discoveryOptions: new DiscoveryOptions { UdpPort = port },
                identityPath: Path.Combine(directory, "id.json"));
            await Assert.ThrowsAsync<SocketException>(() => application.StartAsync());
            Assert.Null(application.LocalDevice);
            Assert.Null(application.Transfers.ListeningPort);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task RealUdpAnnouncementProvidesAConnectableTcpEndpoint()
    {
        await using var server = new NetworkServer(0);
        var accepted = new TaskCompletionSource<Connection>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.ConnectionAccepted += (_, args) => accepted.TrySetResult(args.Connection);
        await server.StartAsync();
        var options = new DiscoveryOptions { UdpPort = UnusedUdpPort() };
        await using var discovery = new DeviceDiscovery(new LocalDevice(Guid.NewGuid(), "local", 12345), options);
        var discovered = new TaskCompletionSource<Device>(TaskCreationOptions.RunContinuationsAsynchronously);
        discovery.DeviceDiscovered += (_, args) => discovered.TrySetResult(args.Device);
        await discovery.StartAsync();
        var address = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter => adapter.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
            .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses)
            .Select(unicast => unicast.Address)
            .First(ip => ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip));
        using var announcer = new UdpClient(new IPEndPoint(address, 0));
        var peerId = Guid.NewGuid();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { v = 1, t = 1, id = peerId, n = "peer", p = server.ListeningPort!.Value });
        await announcer.SendAsync(bytes, new IPEndPoint(address, options.UdpPort));
        var peer = await discovered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(peerId, peer.Id);
        Assert.Equal(address, peer.IpAddress);
        await using var client = await new NetworkClient().ConnectAsync(peer.IpAddress, peer.Port);
        await using var remote = await accepted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var hello = Message.CreateHello(new HelloPayload("test", 1));
        await client.SendAsync(hello);
        Assert.Equal(hello, await remote.ReceiveAsync());
    }
}
