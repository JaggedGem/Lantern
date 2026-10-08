using System.Net;
using System.Security.Cryptography;
using Lantern.Models;
using Lantern.Networking;
using Lantern.Networking.Protocol;
using Lantern.Transfers;
using Xunit;

namespace Lantern.Tests;

public sealed class FileTransferIntegrationTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lantern-transfer-" + Guid.NewGuid());
    private readonly Guid _senderId = Guid.NewGuid();
    private readonly Guid _receiverId = Guid.NewGuid();
    private TransferManager _sender = null!;
    private TransferManager _receiver = null!;
    private string Destination => Path.Combine(_root, "received");
    private Device Peer => new(_receiverId, "receiver", IPAddress.Loopback, _receiver.ListeningPort!.Value);

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Destination);
        Directory.CreateDirectory(Path.Combine(_root, "source"));
        _sender = new TransferManager(_senderId);
        _receiver = new TransferManager(_receiverId);
        await _sender.StartAsync();
        await _receiver.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _sender.DisposeAsync();
        await _receiver.DisposeAsync();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private async Task<TransferSourceFile> SourceAsync(string name, int length, string? relative = null)
    {
        var path = Path.Combine(_root, "source", name);
        var bytes = new byte[length];
        new Random(42).NextBytes(bytes);
        await File.WriteAllBytesAsync(path, bytes);
        return TransferSourceFile.FromPath(path, relative);
    }

    private static Task<Transfer> TerminalTask(TransferManager manager)
    {
        var completion = new TaskCompletionSource<Transfer>(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.TransferChanged += (_, args) =>
        {
            if (args.Transfer.Status is TransferStatus.Completed or TransferStatus.Failed or TransferStatus.Rejected or TransferStatus.Cancelled)
                completion.TrySetResult(args.Transfer);
        };
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(20));
    }

    [Fact]
    public async Task TransfersMultipleFilesEmptyFilesAndNestedHierarchyWithReceiverAcknowledgment()
    {
        _receiver.IncomingTransferRequested += (_, args) => args.Accept(Destination);
        var files = new[] { await SourceAsync("photo.bin", 200_000, "Trip/day-1/photo.bin"),
            await SourceAsync("empty.txt", 0, "Trip/empty.txt"), await SourceAsync("notes.txt", 100) };
        var received = TerminalTask(_receiver);
        var sent = await _sender.SendAsync(Peer, files).WaitAsync(TimeSpan.FromSeconds(20));
        var incoming = await received;
        Assert.Equal(TransferStatus.Completed, sent.Status);
        Assert.Equal(TransferStatus.Completed, incoming.Status);
        Assert.Equal(sent.Id, incoming.Id);
        Assert.Equal(sent.TotalBytes, sent.TransferredBytes);
        Assert.Equal(sent.TotalBytes, incoming.TransferredBytes);
        foreach (var source in files)
        {
            var path = Path.Combine(Destination, "Lantern-" + sent.Id.ToString("N"), source.Metadata.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.Equal(await File.ReadAllBytesAsync(source.SourcePath), await File.ReadAllBytesAsync(path));
        }
        Assert.Single(Directory.GetDirectories(Destination));
        Assert.Empty(Directory.GetDirectories(Destination, "*.partial"));
    }

    [Fact]
    public async Task StreamsAFileManyTimesLargerThanTheChunkSize()
    {
        _receiver.IncomingTransferRequested += (_, args) => args.Accept(Destination);
        var path = Path.Combine(_root, "source", "large.bin");
        await using (var source = new FileStream(path, FileMode.CreateNew, FileAccess.Write)) source.SetLength(16 * 1024 * 1024);
        var selected = TransferSourceFile.FromPath(path);
        var observed = new List<long>();
        _sender.TransferChanged += (_, args) => observed.Add(args.Transfer.TransferredBytes);
        var sent = await _sender.SendAsync(Peer, [selected]).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(TransferStatus.Completed, sent.Status);
        var output = Path.Combine(Destination, "Lantern-" + sent.Id.ToString("N"), "large.bin");
        await using var original = File.OpenRead(path);
        await using var copy = File.OpenRead(output);
        Assert.Equal(await SHA256.HashDataAsync(original), await SHA256.HashDataAsync(copy));
        Assert.Contains(TransferOptions.ChunkSizeBytes, observed);
        Assert.Equal(observed.Order(), observed);
    }

    [Fact]
    public async Task DefaultRecipientPolicyRejectsWithoutWritingAnyFiles()
    {
        var result = await _sender.SendAsync(Peer, [await SourceAsync("file", 100)]).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(TransferStatus.Rejected, result.Status);
        Assert.Equal(0, result.TransferredBytes);
        Assert.Empty(Directory.GetFileSystemEntries(Destination));
    }

    [Fact]
    public async Task SenderCancellationStopsBytesAndReceiverCleansPartialFiles()
    {
        _receiver.IncomingTransferRequested += (_, args) => args.Accept(Destination);
        var received = TerminalTask(_receiver);
        _sender.TransferChanged += (_, args) =>
        {
            if (args.Transfer.Status == TransferStatus.Transferring && args.Transfer.TransferredBytes > 0)
                _sender.Cancel(args.Transfer.Id);
        };
        var sent = await _sender.SendAsync(Peer, [await SourceAsync("file", 2_000_000)]).WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(TransferStatus.Cancelled, sent.Status);
        Assert.InRange(sent.TransferredBytes, 1, sent.TotalBytes - 1);
        Assert.Equal(TransferStatus.Failed, (await received).Status);
        Assert.Empty(Directory.GetFileSystemEntries(Destination));
    }

    [Fact]
    public async Task ReceiverCancellationCleansPartialFilesAndSenderDoesNotReportSuccess()
    {
        _receiver.IncomingTransferRequested += (_, args) => args.Accept(Destination);
        var received = TerminalTask(_receiver);
        _receiver.TransferChanged += (_, args) =>
        {
            if (args.Transfer.Status == TransferStatus.Transferring && args.Transfer.TransferredBytes > 0)
                _receiver.Cancel(args.Transfer.Id);
        };
        var sent = await _sender.SendAsync(Peer, [await SourceAsync("file", 2_000_000)]).WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(TransferStatus.Failed, sent.Status);
        Assert.Equal(TransferStatus.Cancelled, (await received).Status);
        Assert.Empty(Directory.GetFileSystemEntries(Destination));
    }

    [Fact]
    public async Task ChangedSourceFailsAndIncompleteDestinationIsRemoved()
    {
        _receiver.IncomingTransferRequested += (_, args) => args.Accept(Destination);
        var selected = await SourceAsync("file", 100);
        await File.AppendAllTextAsync(selected.SourcePath, "changed");
        var received = TerminalTask(_receiver);
        var sent = await _sender.SendAsync(Peer, [selected]).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(TransferStatus.Failed, sent.Status);
        Assert.Equal(TransferFailureKind.FileSystem, sent.Error!.Kind);
        Assert.Equal(TransferStatus.Failed, (await received).Status);
        Assert.Empty(Directory.GetFileSystemEntries(Destination));
    }

    [Fact]
    public async Task StoppingWhileAwaitingAcceptanceDrainsWorkersAndInvalidatesLateDecisions()
    {
        var request = new TaskCompletionSource<IncomingTransferRequestEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        _receiver.IncomingTransferRequested += (_, args) => request.SetResult(args);
        var sending = _sender.SendAsync(Peer, [await SourceAsync("file", 100)]);
        var pending = await request.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await _receiver.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(pending.Accept(Destination));
        Assert.Equal(TransferStatus.Cancelled, _receiver.GetTransfers().Single().Status);
        Assert.Equal(TransferStatus.Failed, (await sending.WaitAsync(TimeSpan.FromSeconds(5))).Status);
        Assert.Empty(Directory.GetFileSystemEntries(Destination));
    }

    [Fact]
    public async Task RejectsUnsafeWireManifestBeforeRequestIsPresented()
    {
        var errors = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        _receiver.Error += (_, error) => errors.TrySetResult(error);
        var prompted = false;
        _receiver.IncomingTransferRequested += (_, args) => { prompted = true; args.Accept(Destination); };
        await using var connection = await OpenPeerAsync();
        await SendAsync(connection, new TransferRequestPayload(Guid.NewGuid(), _senderId, _receiverId,
            [new TransferFileMetadata(Guid.NewGuid(), "../escape.txt", 1)]));
        Assert.IsAssignableFrom<ArgumentException>(await errors.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(prompted);
        Assert.Empty(_receiver.GetTransfers());
        Assert.Empty(Directory.GetFileSystemEntries(Destination));
    }

    [Theory]
    [InlineData("offset")]
    [InlineData("file-id")]
    [InlineData("transfer-id")]
    [InlineData("oversize")]
    [InlineData("empty")]
    [InlineData("unexpected-marker")]
    public async Task InvalidFileChunksFailWithoutPublishingAnyFiles(string defect)
    {
        _receiver.IncomingTransferRequested += (_, args) => args.Accept(Destination);
        var result = TerminalTask(_receiver);
        await using var connection = await OpenPeerAsync();
        var transferId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        await SendAsync(connection, new TransferRequestPayload(transferId, _senderId, _receiverId,
            [new TransferFileMetadata(fileId, "file.bin", 10)]));
        Assert.IsType<TransferAcceptedPayload>((await connection.ReceiveAsync()).Payload);
        MessagePayload payload = defect == "unexpected-marker" ? new FileCompletePayload(transferId, fileId)
            : new FileChunkPayload(defect == "transfer-id" ? Guid.NewGuid() : transferId,
                defect == "file-id" ? Guid.NewGuid() : fileId, defect == "offset" ? 1 : 0,
                new byte[defect == "oversize" ? TransferOptions.ChunkSizeBytes + 1 : defect == "empty" ? 0 : 10]);
        await SendAsync(connection, payload);
        var failed = await result;
        Assert.Equal(TransferStatus.Failed, failed.Status);
        Assert.Equal(TransferFailureKind.Protocol, failed.Error!.Kind);
        Assert.Empty(Directory.GetFileSystemEntries(Destination));
    }

    [Fact]
    public async Task MissingCompletionMarkerIsNotSuccessfulAndDoesNotOverwriteExistingFiles()
    {
        await File.WriteAllTextAsync(Path.Combine(Destination, "keep.txt"), "keep");
        _receiver.IncomingTransferRequested += (_, args) => args.Accept(Destination);
        var result = TerminalTask(_receiver);
        var transferId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        await using (var connection = await OpenPeerAsync())
        {
            await SendAsync(connection, new TransferRequestPayload(transferId, _senderId, _receiverId,
                [new TransferFileMetadata(fileId, "file.bin", 10)]));
            await connection.ReceiveAsync();
            await SendAsync(connection, new FileChunkPayload(transferId, fileId, 0, new byte[10]));
            // Disconnect at the declared size, without file/transfer completion markers.
        }
        Assert.Equal(TransferStatus.Failed, (await result).Status);
        Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(Destination, "keep.txt")));
        Assert.Single(Directory.GetFileSystemEntries(Destination));
    }

    [Fact]
    public async Task ConnectsOnlyToTheRequestedInstallation()
    {
        var wrongPeer = new Device(Guid.NewGuid(), "wrong ID", IPAddress.Loopback, _receiver.ListeningPort!.Value);
        var result = await _sender.SendAsync(wrongPeer, [await SourceAsync("file", 100)]).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(TransferStatus.Failed, result.Status);
        Assert.Equal(TransferFailureKind.Protocol, result.Error!.Kind);
        Assert.Empty(Directory.GetFileSystemEntries(Destination));
    }

    [Fact]
    public async Task AcceptanceTimeoutRejectsAndLateAcceptCannotStartWriting()
    {
        await _receiver.DisposeAsync();
        _receiver = new TransferManager(_receiverId, options: new TransferOptions { RequestTimeout = TimeSpan.FromMilliseconds(100) });
        await _receiver.StartAsync();
        IncomingTransferRequestEventArgs? request = null;
        _receiver.IncomingTransferRequested += (_, args) => request = args;
        var result = await _sender.SendAsync(Peer, [await SourceAsync("file", 100)]).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(TransferStatus.Rejected, result.Status);
        Assert.NotNull(request);
        Assert.False(request.Accept(Destination));
        Assert.Empty(Directory.GetFileSystemEntries(Destination));
    }

    [Fact]
    public async Task StalledSenderTimesOutAndPartialDirectoryIsRemoved()
    {
        await _receiver.DisposeAsync();
        _receiver = new TransferManager(_receiverId, options: new TransferOptions { InactivityTimeout = TimeSpan.FromMilliseconds(200) });
        await _receiver.StartAsync();
        _receiver.IncomingTransferRequested += (_, args) => args.Accept(Destination);
        var failed = TerminalTask(_receiver);
        await using var connection = await OpenPeerAsync();
        var id = Guid.NewGuid();
        await SendAsync(connection, new TransferRequestPayload(id, _senderId, _receiverId,
            [new TransferFileMetadata(Guid.NewGuid(), "file", 100)]));
        Assert.IsType<TransferAcceptedPayload>((await connection.ReceiveAsync()).Payload);
        var result = await failed;
        Assert.Equal(TransferStatus.Failed, result.Status);
        Assert.Equal(TransferFailureKind.Connection, result.Error!.Kind);
        Assert.Empty(Directory.GetFileSystemEntries(Destination));
    }

    [Fact]
    public async Task ExistingTransferDirectoryIsNeverOverwritten()
    {
        _receiver.IncomingTransferRequested += (_, args) => args.Accept(Destination);
        var id = Guid.NewGuid();
        var existing = Path.Combine(Destination, "Lantern-" + id.ToString("N"));
        Directory.CreateDirectory(existing);
        await File.WriteAllTextAsync(Path.Combine(existing, "keep.txt"), "keep");
        var failed = TerminalTask(_receiver);
        await using var connection = await OpenPeerAsync();
        await SendAsync(connection, new TransferRequestPayload(id, _senderId, _receiverId,
            [new TransferFileMetadata(Guid.NewGuid(), "file", 100)]));
        Assert.Equal(TransferStatus.Failed, (await failed).Status);
        Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(existing, "keep.txt")));
        Assert.Single(Directory.GetFileSystemEntries(Destination));
    }

    [Fact]
    public async Task WrongAcknowledgmentCannotCompleteOutgoingTransferEvenAtFullByteCount()
    {
        await using var server = new NetworkServer(0);
        var accepted = new TaskCompletionSource<Connection>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.ConnectionAccepted += (_, args) => accepted.TrySetResult(args.Connection);
        await server.StartAsync();
        var peer = new Device(_receiverId, "test peer", IPAddress.Loopback, server.ListeningPort!.Value);
        var send = _sender.SendAsync(peer, [await SourceAsync("file", 100)]);
        await using var connection = await accepted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await connection.ReceiveAsync();
        await connection.SendAsync(Message.CreateHello(new HelloPayload("test", ProtocolLimits.Version, _receiverId)));
        var request = Assert.IsType<TransferRequestPayload>((await connection.ReceiveAsync()).Payload);
        await SendAsync(connection, new TransferAcceptedPayload(request.TransferId));
        while ((await connection.ReceiveAsync()).Payload is not TransferCompletePayload) { }
        await SendAsync(connection, new TransferAcknowledgedPayload(Guid.NewGuid()));
        var result = await send.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(result.TotalBytes, result.TransferredBytes);
        Assert.Equal(TransferStatus.Failed, result.Status);
        Assert.Equal(TransferFailureKind.Protocol, result.Error!.Kind);
    }

    [Fact]
    public async Task FailureInLaterFileDoesNotPublishEarlierFiles()
    {
        _receiver.IncomingTransferRequested += (_, args) => args.Accept(Destination);
        var first = await SourceAsync("first", 100);
        var second = await SourceAsync("second", 100);
        File.Delete(second.SourcePath);
        var failed = TerminalTask(_receiver);
        var result = await _sender.SendAsync(Peer, [first, second]).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(TransferStatus.Failed, result.Status);
        Assert.Equal(100, result.TransferredBytes);
        Assert.Equal(TransferStatus.Failed, (await failed).Status);
        Assert.Empty(Directory.GetFileSystemEntries(Destination));
    }

    [Fact]
    public async Task PreCancelledRequestDoesNotAllocateATransferOrCreateFiles()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var source = await SourceAsync("file", 100);
        Assert.ThrowsAny<OperationCanceledException>(() => { _ = _sender.SendAsync(Peer, [source], cancellation.Token); });
        Assert.Empty(_sender.GetTransfers());
        Assert.Empty(Directory.GetFileSystemEntries(Destination));
    }

    private async Task<Connection> OpenPeerAsync()
    {
        var connection = await new NetworkClient().ConnectAsync(IPAddress.Loopback, _receiver.ListeningPort!.Value);
        await connection.SendAsync(Message.CreateHello(new HelloPayload("test", ProtocolLimits.Version, _senderId)));
        var hello = Assert.IsType<HelloPayload>((await connection.ReceiveAsync()).Payload);
        Assert.Equal(_receiverId, hello.DeviceId);
        return connection;
    }

    private static ValueTask SendAsync(Connection connection, MessagePayload payload)
        => connection.SendAsync(new Message(MessagePayloadKindHelper.GetMessageType(payload), MessageId.NewId(), payload));
}
