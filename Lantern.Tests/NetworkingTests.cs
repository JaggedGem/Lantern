using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Lantern.Networking;
using Lantern.Networking.Protocol;
using Xunit;

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




