using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Lantern.Networking;
using Lantern.Networking.Protocol;
using Xunit;
using Lantern.Models;
using Lantern.Networking.Discovery;

namespace Lantern.Tests;

public sealed class NetworkingTests
{
    private const int ProtocolVersion = 1;

    [Fact]
    public void ProtocolSerializer_RoundTripsHelloMessage()
    {
        var serializer = new ProtocolSerializer();
        var message = Message.CreateHello(new HelloPayload("Lantern.Tests", ProtocolVersion));

        var bytes = serializer.Serialize(message);

        var roundTrip = serializer.Deserialize(bytes);

        Assert.Equal(message.Type, roundTrip.Type);
        Assert.Equal(message.Id, roundTrip.Id);
        var payload = Assert.IsType<HelloPayload>(roundTrip.Payload);
        Assert.Equal("Lantern.Tests", payload.ApplicationName);
        Assert.Equal(ProtocolVersion, payload.ProtocolVersion);
    }

    [Fact]
    public void ProtocolSerializer_RoundTripsErrorMessage()
    {
        var serializer = new ProtocolSerializer();
        var message = Message.CreateError(new ErrorPayload("E_PROTOCOL", "Invalid protocol data."));

        var bytes = serializer.Serialize(message);

        var roundTrip = serializer.Deserialize(bytes);

        Assert.Equal(message.Type, roundTrip.Type);
        Assert.Equal(message.Id, roundTrip.Id);
        var payload = Assert.IsType<ErrorPayload>(roundTrip.Payload);
        Assert.Equal("E_PROTOCOL", payload.Code);
        Assert.Equal("Invalid protocol data.", payload.Description);
    }

    [Fact]
    public void ProtocolSerializer_RejectsMalformedData()
    {
        var serializer = new ProtocolSerializer();

        Assert.Throws<JsonException>(() => serializer.Deserialize("not-json"u8.ToArray()));
        Assert.Throws<JsonException>(() => serializer.Deserialize("{\"type\":1,\"id\":\"00000000-0000-0000-0000-000000000001\"}"u8.ToArray()));
    }

    [Fact]
    public async Task FramedMessageChannel_SendsAndReceivesSingleMessage()
    {
        var serializer = new ProtocolSerializer();
        var message = Message.CreateHello(new HelloPayload("Lantern.Tests", ProtocolVersion));
        using var stream = new MemoryStream();
        using var sender = new FramedMessageChannel(stream, serializer);

        await sender.SendAsync(message);

        stream.Position = 0;
        using var receiver = new FramedMessageChannel(stream, serializer);
        var received = await receiver.ReceiveAsync();

        Assert.Equal(message.Type, received.Type);
        Assert.Equal(message.Id, received.Id);
        var payload = Assert.IsType<HelloPayload>(received.Payload);
        Assert.Equal("Lantern.Tests", payload.ApplicationName);
        Assert.Equal(ProtocolVersion, payload.ProtocolVersion);
    }

    [Fact]
    public async Task FramedMessageChannel_ReceivesMultipleMessagesSequentially()
    {
        var serializer = new ProtocolSerializer();
        var first = Message.CreateHello(new HelloPayload("First", ProtocolVersion));
        var second = Message.CreateError(new ErrorPayload("E_TWO", "Second message."));
        using var stream = new MemoryStream();
        using var sender = new FramedMessageChannel(stream, serializer);

        await sender.SendAsync(first);
        await sender.SendAsync(second);

        stream.Position = 0;
        using var receiver = new FramedMessageChannel(stream, serializer);

        var receivedFirst = await receiver.ReceiveAsync();
        var receivedSecond = await receiver.ReceiveAsync();

        Assert.Equal(first, receivedFirst);
        Assert.Equal(second, receivedSecond);
    }

    [Fact]
    public async Task FramedMessageChannel_ReceivesWhenStreamDeliversPartialChunks()
    {
        var serializer = new ProtocolSerializer();
        var message = Message.CreateHello(new HelloPayload("Chunked", ProtocolVersion));
        var framedBytes = CreateFramedBytes(message, serializer);
        using var stream = new ChunkedMemoryStream(framedBytes, 2);
        using var receiver = new FramedMessageChannel(stream, serializer);

        var received = await receiver.ReceiveAsync();

        Assert.Equal(message, received);
    }

    [Fact]
    public async Task FramedMessageChannel_RejectsOversizedLengthPrefix()
    {
        var serializer = new ProtocolSerializer();
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, 1_048_577);
        using var stream = new MemoryStream(bytes, writable: true);
        using var receiver = new FramedMessageChannel(stream, serializer);

        await Assert.ThrowsAsync<InvalidDataException>(() => receiver.ReceiveAsync().AsTask());
    }

    [Fact]
    public async Task NetworkServer_StartsStopsAndReleasesPort()
    {
        await using var server = new NetworkServer(0);

        await server.StartAsync();

        Assert.True(server.IsRunning);
        Assert.True(server.ListeningPort is > 0);

        var listeningPort = server.ListeningPort!.Value;
        await server.StopAsync();

        Assert.False(server.IsRunning);
        Assert.Null(server.ListeningPort);

        using var probeListener = new TcpListener(IPAddress.Loopback, listeningPort);
        probeListener.Start();
        probeListener.Stop();
    }

    [Fact]
    public async Task NetworkServer_RepeatedStartStopWorks()
    {
        await using var server = new NetworkServer(0);

        await server.StartAsync();
        var firstPort = server.ListeningPort!.Value;
        await server.StopAsync();

        await server.StartAsync();
        var secondPort = server.ListeningPort!.Value;
        await server.StopAsync();

        Assert.True(firstPort > 0);
        Assert.True(secondPort > 0);
    }

    [Fact]
    public async Task NetworkServer_RejectsDuplicateStart()
    {
        await using var server = new NetworkServer(0);

        await server.StartAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => server.StartAsync());
        await server.StopAsync();
    }

    [Fact]
    public async Task NetworkClient_ConnectsAndReportsFailureAndCancellation()
    {
        await using var server = new NetworkServer(0);
        await server.StartAsync();
        var port = server.ListeningPort!.Value;
        await server.StopAsync();

        var client = new NetworkClient();

        await Assert.ThrowsAsync<SocketException>(() => client.ConnectAsync(IPAddress.Loopback, port));

        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ConnectAsync(IPAddress.Loopback, port, cancellationSource.Token));
    }

    [Fact]
    public async Task Connection_CanExchangeHelloAndHandleClosure()
    {
        await using var server = new NetworkServer(0);
        var acceptedConnectionSource = new TaskCompletionSource<Connection>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.ConnectionAccepted += (_, e) => acceptedConnectionSource.TrySetResult(e.Connection);

        await server.StartAsync();
        var port = server.ListeningPort!.Value;

        var client = new NetworkClient();
        await using var clientConnection = await client.ConnectAsync(IPAddress.Loopback, port);
        await using var serverConnection = await acceptedConnectionSource.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var clientHello = Message.CreateHello(new HelloPayload("Client", ProtocolVersion));
        var serverHello = Message.CreateHello(new HelloPayload("Server", ProtocolVersion));

        await clientConnection.SendAsync(clientHello);
        var receivedByServer = await serverConnection.ReceiveAsync();
        Assert.Equal(clientHello.Type, receivedByServer.Type);
        Assert.Equal(clientHello.Id, receivedByServer.Id);
        var serverPayload = Assert.IsType<HelloPayload>(receivedByServer.Payload);
        Assert.Equal("Client", serverPayload.ApplicationName);

        await serverConnection.SendAsync(serverHello);
        var receivedByClient = await clientConnection.ReceiveAsync();
        Assert.Equal(serverHello.Type, receivedByClient.Type);
        Assert.Equal(serverHello.Id, receivedByClient.Id);
        var clientPayload = Assert.IsType<HelloPayload>(receivedByClient.Payload);
        Assert.Equal("Server", clientPayload.ApplicationName);

        serverConnection.Close();
        await Assert.ThrowsAsync<EndOfStreamException>(() => clientConnection.ReceiveAsync().AsTask());

        await server.StopAsync();
    }

    private static byte[] CreateFramedBytes(Message message, ProtocolSerializer serializer)
    {
        var payload = serializer.Serialize(message);
        var bytes = new byte[sizeof(int) + payload.Length];
        BinaryPrimitives.WriteInt32BigEndian(bytes, payload.Length);
        payload.CopyTo(bytes.AsSpan(sizeof(int)));
        return bytes;
    }

    private sealed class ChunkedMemoryStream : MemoryStream
    {
        private readonly int _maximumChunkSize;

        public ChunkedMemoryStream(byte[] buffer, int maximumChunkSize)
            : base(buffer, 0, buffer.Length, writable: true, publiclyVisible: true)
        {
            _maximumChunkSize = maximumChunkSize;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var chunkSize = Math.Min(buffer.Length, _maximumChunkSize);
            return base.ReadAsync(buffer.Slice(0, chunkSize), cancellationToken);
        }
    }
}

public sealed class DiscoveryTests
{
    [Fact]
    public void LocalDeviceIdentityProvider_GeneratesAndPersistsDeviceId()
    {
        // This test verifies that a device ID is generated and could be persisted
        var id1 = LocalDeviceIdentityProvider.LoadOrCreateDeviceId();
        var id2 = LocalDeviceIdentityProvider.LoadOrCreateDeviceId();

        // Both calls should return the same ID (persistence working)
        Assert.Equal(id1, id2);
        Assert.NotEqual(Guid.Empty, id1);
    }

    [Fact]
    public void LocalDevice_ConstructorValidatesInput()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new LocalDevice(Guid.NewGuid(), "Test", 0));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new LocalDevice(Guid.NewGuid(), "Test", 65536));

        Assert.Throws<ArgumentException>(() =>
            new LocalDevice(Guid.NewGuid(), "", 5000));
    }

    [Fact]
    public void Device_UpdatesMaintainsLastSeen()
    {
        var device = new Device(Guid.NewGuid(), "Test", IPAddress.Loopback, 5000);
        var initialLastSeen = device.LastSeen;

        // Allow small time delay
        System.Threading.Thread.Sleep(10);
        device.RefreshLastSeen();

        Assert.True(device.LastSeen > initialLastSeen);
    }

    [Fact]
    public void Device_UpdateNameRefreshesLastSeen()
    {
        var device = new Device(Guid.NewGuid(), "Test", IPAddress.Loopback, 5000);
        var initialLastSeen = device.LastSeen;

        System.Threading.Thread.Sleep(10);
        device.UpdateName("NewName");

        Assert.Equal("NewName", device.Name);
        Assert.True(device.LastSeen > initialLastSeen);
    }

    [Fact]
    public void Device_UpdateIpAddressRefreshesLastSeen()
    {
        var device = new Device(Guid.NewGuid(), "Test", IPAddress.Loopback, 5000);
        var initialLastSeen = device.LastSeen;

        System.Threading.Thread.Sleep(10);
        device.UpdateIpAddress(IPAddress.Parse("192.168.1.1"));

        Assert.Equal(IPAddress.Parse("192.168.1.1"), device.IpAddress);
        Assert.True(device.LastSeen > initialLastSeen);
    }

    [Fact]
    public void Device_UpdatePortRefreshesLastSeen()
    {
        var device = new Device(Guid.NewGuid(), "Test", IPAddress.Loopback, 5000);
        var initialLastSeen = device.LastSeen;

        System.Threading.Thread.Sleep(10);
        device.UpdatePort(6000);

        Assert.Equal(6000, device.Port);
        Assert.True(device.LastSeen > initialLastSeen);
    }

    [Fact]
    public void Device_UpdateStatusRefreshesLastSeen()
    {
        var device = new Device(Guid.NewGuid(), "Test", IPAddress.Loopback, 5000);
        var initialLastSeen = device.LastSeen;

        System.Threading.Thread.Sleep(10);
        device.UpdateStatus(DeviceStatus.Online);

        Assert.Equal(DeviceStatus.Online, device.Status);
        Assert.True(device.LastSeen > initialLastSeen);
    }

    [Fact]
    public void DiscoveryMessage_SerializesAndDeserializes()
    {
        var message = new DiscoveryMessage
        {
            Type = DiscoveryMessageType.Announcement,
            DeviceId = Guid.NewGuid(),
            DeviceName = "TestDevice",
            TcpPort = 5000,
            Version = DiscoveryProtocolConstants.ProtocolVersion
        };

        var json = JsonSerializer.Serialize(message);
        var deserialized = JsonSerializer.Deserialize<DiscoveryMessage>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(message.Type, deserialized.Type);
        Assert.Equal(message.DeviceId, deserialized.DeviceId);
        Assert.Equal(message.DeviceName, deserialized.DeviceName);
        Assert.Equal(message.TcpPort, deserialized.TcpPort);
        Assert.Equal(DiscoveryProtocolConstants.ProtocolVersion, deserialized.Version);
    }

    [Fact]
    public async Task DeviceDiscovery_StartsAndStops()
    {
        var localDevice = new LocalDevice(Guid.NewGuid(), "LocalTest", 5000);
        await using var discovery = new DeviceDiscovery(localDevice);

        Assert.False(discovery.IsRunning);

        await discovery.StartAsync();
        Assert.True(discovery.IsRunning);

        await discovery.StopAsync();
        Assert.False(discovery.IsRunning);
    }

    [Fact]
    public async Task DeviceDiscovery_RejectsDoubleStart()
    {
        var localDevice = new LocalDevice(Guid.NewGuid(), "LocalTest", 5000);
        await using var discovery = new DeviceDiscovery(localDevice);

        await discovery.StartAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => discovery.StartAsync());
        await discovery.StopAsync();
    }

    [Fact]
    public async Task DeviceDiscovery_RepeatedStartStopWorks()
    {
        var localDevice = new LocalDevice(Guid.NewGuid(), "LocalTest", 5000);
        await using var discovery = new DeviceDiscovery(localDevice);

        await discovery.StartAsync();
        Assert.True(discovery.IsRunning);
        await discovery.StopAsync();
        Assert.False(discovery.IsRunning);

        await discovery.StartAsync();
        Assert.True(discovery.IsRunning);
        await discovery.StopAsync();
        Assert.False(discovery.IsRunning);
    }

    [Fact]
    public async Task DeviceDiscovery_IgnoresLocalDevice()
    {
        var localDeviceId = Guid.NewGuid();
        var localDevice = new LocalDevice(localDeviceId, "LocalTest", 5000);

        using var discovery = new DeviceDiscovery(localDevice);

        var deviceAddedFired = false;
        discovery.DeviceDiscovered += (_, _) => { deviceAddedFired = true; };

        // Simulate receiving our own announcement
        var selfMessage = new DiscoveryMessage
        {
            Type = DiscoveryMessageType.Announcement,
            DeviceId = localDeviceId,
            DeviceName = "LocalTest",
            TcpPort = 5000,
            Version = DiscoveryProtocolConstants.ProtocolVersion
        };

        var json = JsonSerializer.Serialize(selfMessage);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);

        // This is a reflection-based test simulating internal processing
        // In a real scenario, this would be received via UDP
        var processMethod = typeof(DeviceDiscovery).GetMethod(
            "ProcessDiscoveryPacket",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        if (processMethod != null)
        {
            processMethod.Invoke(discovery, new object[] { bytes, new System.Net.IPEndPoint(IPAddress.Loopback, 12345) });
        }

        // Local device should not be added
        var devices = discovery.GetDiscoveredDevices();
        Assert.Empty(devices);
        Assert.False(deviceAddedFired);
    }

    [Fact]
    public async Task DeviceDiscovery_RejectsInvalidMessages()
    {
        var localDevice = new LocalDevice(Guid.NewGuid(), "LocalTest", 5000);
        using var discovery = new DeviceDiscovery(localDevice);

        var deviceAddedFired = false;
        discovery.DeviceDiscovered += (_, _) => { deviceAddedFired = true; };

        var processMethod = typeof(DeviceDiscovery).GetMethod(
            "ProcessDiscoveryPacket",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        if (processMethod != null)
        {
            // Test invalid JSON
            var invalidJson = System.Text.Encoding.UTF8.GetBytes("not-json");
            processMethod.Invoke(discovery, new object[] { invalidJson, new System.Net.IPEndPoint(IPAddress.Loopback, 12345) });

            // Test message without name
            var noNameMessage = new DiscoveryMessage
            {
                Type = DiscoveryMessageType.Announcement,
                DeviceId = Guid.NewGuid(),
                DeviceName = "",
                TcpPort = 5000,
                Version = DiscoveryProtocolConstants.ProtocolVersion
            };
            var noNameJson = JsonSerializer.Serialize(noNameMessage);
            var noNameBytes = System.Text.Encoding.UTF8.GetBytes(noNameJson);
            processMethod.Invoke(discovery, new object[] { noNameBytes, new System.Net.IPEndPoint(IPAddress.Loopback, 12345) });

            // Test invalid port
            var invalidPortMessage = new DiscoveryMessage
            {
                Type = DiscoveryMessageType.Announcement,
                DeviceId = Guid.NewGuid(),
                DeviceName = "Test",
                TcpPort = 70000,
                Version = DiscoveryProtocolConstants.ProtocolVersion
            };
            var invalidPortJson = JsonSerializer.Serialize(invalidPortMessage);
            var invalidPortBytes = System.Text.Encoding.UTF8.GetBytes(invalidPortJson);
            processMethod.Invoke(discovery, new object[] { invalidPortBytes, new System.Net.IPEndPoint(IPAddress.Loopback, 12345) });

            // Test unsupported version
            var wrongVersionMessage = new DiscoveryMessage
            {
                Type = DiscoveryMessageType.Announcement,
                DeviceId = Guid.NewGuid(),
                DeviceName = "Test",
                TcpPort = 5000,
                Version = 99
            };
            var wrongVersionJson = JsonSerializer.Serialize(wrongVersionMessage);
            var wrongVersionBytes = System.Text.Encoding.UTF8.GetBytes(wrongVersionJson);
            processMethod.Invoke(discovery, new object[] { wrongVersionBytes, new System.Net.IPEndPoint(IPAddress.Loopback, 12345) });
        }

        // No devices should have been added
        var devices = discovery.GetDiscoveredDevices();
        Assert.Empty(devices);
        Assert.False(deviceAddedFired);
    }

    [Fact]
    public async Task DeviceDiscovery_SupportsProcessingValidMessage()
    {
        var localDevice = new LocalDevice(Guid.NewGuid(), "LocalTest", 5000);
        using var discovery = new DeviceDiscovery(localDevice);

        var deviceDiscoveredEventFired = false;
        Device? discoveredDevice = null;
        discovery.DeviceDiscovered += (_, e) =>
        {
            deviceDiscoveredEventFired = true;
            discoveredDevice = e.Device;
        };

        var remoteDeviceId = Guid.NewGuid();
        var message = new DiscoveryMessage
        {
            Type = DiscoveryMessageType.Announcement,
            DeviceId = remoteDeviceId,
            DeviceName = "RemoteDevice",
            TcpPort = 6000,
            Version = DiscoveryProtocolConstants.ProtocolVersion
        };

        var json = JsonSerializer.Serialize(message);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);

        var processMethod = typeof(DeviceDiscovery).GetMethod(
            "ProcessDiscoveryPacket",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        if (processMethod != null)
        {
            processMethod.Invoke(discovery, new object[] { bytes, new System.Net.IPEndPoint(IPAddress.Parse("192.168.1.1"), 12345) });
        }

        var devices = discovery.GetDiscoveredDevices();
        Assert.Single(devices);
        Assert.True(deviceDiscoveredEventFired);
        Assert.NotNull(discoveredDevice);
        Assert.Equal(remoteDeviceId, discoveredDevice.Id);
        Assert.Equal("RemoteDevice", discoveredDevice.Name);
        Assert.Equal(6000, discoveredDevice.Port);
        Assert.Equal(IPAddress.Parse("192.168.1.1"), discoveredDevice.IpAddress);
        Assert.Equal(DeviceStatus.Online, discoveredDevice.Status);
    }

    [Fact]
    public async Task DeviceDiscovery_UpdatesExistingDeviceIpAddress()
    {
        var localDevice = new LocalDevice(Guid.NewGuid(), "LocalTest", 5000);
        using var discovery = new DeviceDiscovery(localDevice);

        var remoteDeviceId = Guid.NewGuid();

        // First announcement
        var message1 = new DiscoveryMessage
        {
            Type = DiscoveryMessageType.Announcement,
            DeviceId = remoteDeviceId,
            DeviceName = "RemoteDevice",
            TcpPort = 6000,
            Version = DiscoveryProtocolConstants.ProtocolVersion
        };

        var json1 = JsonSerializer.Serialize(message1);
        var bytes1 = System.Text.Encoding.UTF8.GetBytes(json1);

        var processMethod = typeof(DeviceDiscovery).GetMethod(
            "ProcessDiscoveryPacket",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        if (processMethod != null)
        {
            processMethod.Invoke(discovery, new object[] { bytes1, new System.Net.IPEndPoint(IPAddress.Parse("192.168.1.1"), 12345) });

            // Second announcement with different IP
            var message2 = new DiscoveryMessage
            {
                Type = DiscoveryMessageType.Announcement,
                DeviceId = remoteDeviceId,
                DeviceName = "RemoteDevice",
                TcpPort = 6000,
                Version = DiscoveryProtocolConstants.ProtocolVersion
            };

            var json2 = JsonSerializer.Serialize(message2);
            var bytes2 = System.Text.Encoding.UTF8.GetBytes(json2);
            processMethod.Invoke(discovery, new object[] { bytes2, new System.Net.IPEndPoint(IPAddress.Parse("192.168.1.99"), 12345) });
        }

        var devices = discovery.GetDiscoveredDevices();
        Assert.Single(devices);
        Assert.Equal(IPAddress.Parse("192.168.1.99"), devices[0].IpAddress);
    }

    [Fact]
    public void DeviceDiscovery_SupportsDisposal()
    {
        var localDevice = new LocalDevice(Guid.NewGuid(), "LocalTest", 5000);
        var discovery = new DeviceDiscovery(localDevice);

        // Should not throw
        discovery.Dispose();
        discovery.Dispose();
    }

    [Fact]
    public async Task DeviceDiscovery_SupportsAsyncDisposal()
    {
        var localDevice = new LocalDevice(Guid.NewGuid(), "LocalTest", 5000);
        var discovery = new DeviceDiscovery(localDevice);

        // Should not throw
        await discovery.DisposeAsync();
        await discovery.DisposeAsync();
    }
}