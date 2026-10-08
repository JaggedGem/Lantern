using System.Buffers.Binary;
using System.Net;
using System.Text.Json;
using Lantern.Networking;
using Lantern.Networking.Protocol;
using Xunit;

namespace Lantern.Tests;

public sealed class NetworkingReliabilityTests
{
    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"type\":2147483648,\"id\":\"00000000-0000-0000-0000-000000000001\",\"payload\":{}}")]
    [InlineData("{\"type\":1,\"type\":2,\"id\":\"00000000-0000-0000-0000-000000000001\",\"payload\":{}}")]
    public void InvalidEnvelopesAreProtocolErrors(string json)
        => Assert.Throws<JsonException>(() => new ProtocolSerializer().Deserialize(System.Text.Encoding.UTF8.GetBytes(json)));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(ProtocolLimits.MaximumMessageSizeBytes + 1)]
    public async Task InvalidLengthsTerminateChannel(int length)
    {
        var prefix = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(prefix, length);
        using var stream = new MemoryStream(prefix, true);
        using var channel = new FramedMessageChannel(stream, new ProtocolSerializer());
        await Assert.ThrowsAsync<InvalidDataException>(() => channel.ReceiveAsync().AsTask());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => channel.ReceiveAsync().AsTask());
    }

    [Fact]
    public async Task PartialCancellationTerminatesFrameRatherThanRetryingResidualBytes()
    {
        using var cancellation = new CancellationTokenSource();
        using var stream = new PartialCancellationStream(cancellation);
        using var channel = new FramedMessageChannel(stream, new ProtocolSerializer());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => channel.ReceiveAsync(cancellation.Token).AsTask());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => channel.ReceiveAsync().AsTask());
    }

    [Fact]
    public async Task PreCancelledOperationLeavesChannelUsable()
    {
        using var stream = new MemoryStream();
        using var channel = new FramedMessageChannel(stream, new ProtocolSerializer());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var hello = Message.CreateHello(new HelloPayload("test", ProtocolLimits.Version));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => channel.SendAsync(hello, cancellation.Token).AsTask());
        await channel.SendAsync(hello);
        stream.Position = 0;
        Assert.Equal(hello, await channel.ReceiveAsync());
    }

    [Fact]
    public async Task ConcurrentSendsPreserveEveryFrame()
    {
        using var stream = new MemoryStream();
        using var channel = new FramedMessageChannel(stream, new ProtocolSerializer());
        var messages = Enumerable.Range(0, 64).Select(i => Message.CreateHello(new HelloPayload($"peer{i}", 1))).ToArray();
        await Task.WhenAll(messages.Select(message => channel.SendAsync(message).AsTask()));
        stream.Position = 0;
        var ids = new HashSet<MessageId>();
        for (var i = 0; i < messages.Length; i++) ids.Add((await channel.ReceiveAsync()).Id);
        Assert.Equal(messages.Select(message => message.Id).ToHashSet(), ids);
    }

    [Fact]
    public async Task DisposalUnblocksActiveAndQueuedReads()
    {
        await using var server = new NetworkServer(0);
        var accepted = new TaskCompletionSource<Connection>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.ConnectionAccepted += (_, args) => accepted.SetResult(args.Connection);
        await server.StartAsync();
        await using var client = await new NetworkClient().ConnectAsync(IPAddress.Loopback, server.ListeningPort!.Value);
        await using var remote = await accepted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var reads = Enumerable.Range(0, 8).Select(_ => client.ReceiveAsync().AsTask()).ToArray();
        client.Dispose();
        foreach (var task in reads)
        {
            var exception = await Record.ExceptionAsync(async () => await task.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.NotNull(exception);
            Assert.IsNotType<TimeoutException>(exception);
            if (exception is ObjectDisposedException disposed) Assert.NotEqual("System.Threading.SemaphoreSlim", disposed.ObjectName);
        }
    }

    [Fact]
    public async Task ServerCannotRestartAfterDisposal()
    {
        var server = new NetworkServer(0);
        await server.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => server.StartAsync());
    }

    [Fact]
    public async Task ImmediateStopAndRestartAlwaysDrainTheAcceptLoop()
    {
        await using var server = new NetworkServer(0);
        for (var attempt = 0; attempt < 10; attempt++)
        {
            await server.StartAsync();
            await server.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(server.IsRunning);
            Assert.Null(server.ListeningPort);
        }
    }

    [Fact]
    public async Task ConnectionWithoutOwnerIsClosedAndServerKeepsAccepting()
    {
        await using var server = new NetworkServer(0);
        await server.StartAsync();
        await using var client = await new NetworkClient().ConnectAsync(IPAddress.Loopback, server.ListeningPort!.Value);
        await Assert.ThrowsAsync<EndOfStreamException>(() => client.ReceiveAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(server.IsRunning);
    }

    [Fact]
    public async Task SynchronousServerDisposalDoesNotCaptureTheCallersSynchronizationContext()
    {
        var context = new CountingContext();
        await Task.Run(() =>
        {
            var previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                using var server = new NetworkServer(0);
                server.StartAsync().GetAwaiter().GetResult();
            }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
        }).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, context.Posts);
    }

    private sealed class CountingContext : SynchronizationContext
    {
        private int _posts;
        public int Posts => _posts;
        public override void Post(SendOrPostCallback callback, object? state)
        {
            Interlocked.Increment(ref _posts);
            ThreadPool.QueueUserWorkItem(_ => callback(state));
        }
    }

    private sealed class PartialCancellationStream(CancellationTokenSource source) : MemoryStream(new byte[8], true)
    {
        private int _reads;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (++_reads == 1) return base.ReadAsync(buffer[..2], cancellationToken);
            source.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return base.ReadAsync(buffer, cancellationToken);
        }
    }
}
