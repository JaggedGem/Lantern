using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Lantern.Models;
using Lantern.Transfers;
using Xunit;

namespace Lantern.Tests;

public sealed class TransferFixture : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "Lantern-tests-" + Guid.NewGuid());
    public X509Certificate2 Certificate { get; }
    public TransferFixture() => Certificate = TransferCertificate.LoadOrCreate(Path.Combine(Root, "identity"));
    public void Dispose() { Certificate.Dispose(); Directory.Delete(Root, true); }
}

public sealed class TransferTests(TransferFixture fixture) : IClassFixture<TransferFixture>
{
    [Fact]
    public async Task AcceptedBatch_PreservesContentsIncludingEmptyFile_AndDoesNotOverwrite()
    {
        var root = NewDirectory();
        var source = Path.Combine(root, "source"); Directory.CreateDirectory(source);
        var receive = Path.Combine(root, "received"); Directory.CreateDirectory(receive);
        var bytes = RandomNumberGenerator.GetBytes(400_000);
        var path = Path.Combine(source, "report.bin"); await File.WriteAllBytesAsync(path, bytes);
        var empty = Path.Combine(source, "empty.txt"); await File.WriteAllBytesAsync(empty, []);
        await File.WriteAllTextAsync(Path.Combine(receive, "report.bin"), "existing");
        await using var receiver = CreateService(receive);
        await using var sender = CreateService(Path.Combine(root, "unused"));
        receiver.TransferRequested += (_, r) => r.AcceptTransfer();
        var received = Finished(receiver); var sent = Finished(sender);
        await receiver.StartAsync(); await sender.StartAsync();
        await sender.SendAsync(Peer(receiver), [path, empty], Verify).WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(TransferState.Completed, (await sent).State);
        var result = await received;
        Assert.Equal(TransferState.Completed, result.State);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(result.SavedFolder!, "report.bin")));
        Assert.Empty(await File.ReadAllBytesAsync(Path.Combine(result.SavedFolder!, "empty.txt")));
        Assert.Equal("existing", await File.ReadAllTextAsync(Path.Combine(receive, "report.bin")));
        Assert.Equal(bytes.Length, result.BytesTransferred);
        Assert.Equal(100, result.Percent);
        AssertNoPartial(receive);
    }

    [Fact]
    public async Task DeclinedOffer_WritesNothing_AndReportsRejectionToSender()
    {
        var root = NewDirectory(); var path = Path.Combine(root, "file.txt"); await File.WriteAllTextAsync(path, "hello");
        var receive = Path.Combine(root, "receive");
        await using var receiver = CreateService(receive); await using var sender = CreateService(root);
        receiver.TransferRequested += (_, r) => r.RejectTransfer();
        var received = Finished(receiver); var sent = Finished(sender);
        await receiver.StartAsync(); await sender.StartAsync();
        await sender.SendAsync(Peer(receiver), [path], Verify).WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(TransferState.Rejected, (await sent).State);
        Assert.Equal(TransferState.Rejected, (await received).State);
        Assert.False(Directory.Exists(receive));
    }

    [Fact]
    public async Task UntrustedCertificate_SendsNoOfferOrFileMetadata()
    {
        var root = NewDirectory(); var path = Path.Combine(root, "private.txt"); await File.WriteAllTextAsync(path, "private");
        await using var receiver = CreateService(Path.Combine(root, "receive")); await using var sender = CreateService(root);
        var requested = false; receiver.TransferRequested += (_, _) => requested = true;
        var sent = Finished(sender);
        await receiver.StartAsync(); await sender.StartAsync();
        await sender.SendAsync(Peer(receiver), [path], (fingerprint, _) =>
        { Assert.Equal(receiver.Fingerprint, fingerprint); return Task.FromResult(false); }).WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(TransferState.Rejected, (await sent).State);
        Assert.False(requested);
    }

    [Fact]
    public async Task CancelDuringReceive_DeletesStagingBatch()
    {
        var root = NewDirectory(); var path = Path.Combine(root, "large.bin");
        await File.WriteAllBytesAsync(path, RandomNumberGenerator.GetBytes(8 * 1024 * 1024));
        var receive = Path.Combine(root, "receive");
        await using var receiver = CreateService(receive); await using var sender = CreateService(root);
        receiver.TransferRequested += (_, r) => r.AcceptTransfer();
        receiver.TransferChanged += (_, t) => { if (t.State == TransferState.Receiving && t.BytesTransferred > 0) receiver.Cancel(t.Id); };
        var received = Finished(receiver);
        await receiver.StartAsync(); await sender.StartAsync();
        await sender.SendAsync(Peer(receiver), [path], Verify).WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(TransferState.Canceled, (await received).State);
        Assert.Empty(Directory.GetFileSystemEntries(receive));
    }

    [Fact]
    public async Task Shutdown_CancelsPendingApproval_AndReleasesPort()
    {
        var root = NewDirectory(); var path = Path.Combine(root, "file.txt"); await File.WriteAllTextAsync(path, "hello");
        await using var receiver = CreateService(Path.Combine(root, "receive")); await using var sender = CreateService(root);
        var request = new TaskCompletionSource<TransferRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
        receiver.TransferRequested += (_, r) => request.TrySetResult(r);
        var received = Finished(receiver);
        await receiver.StartAsync(); await sender.StartAsync(); var port = receiver.ListeningPort!.Value;
        var task = sender.SendAsync(Peer(receiver), [path], Verify);
        await request.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await receiver.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(TransferState.Canceled, (await received).State);
        using var probe = new TcpListener(IPAddress.Any, port); probe.Start();
    }

    [Fact]
    public async Task CorruptContent_IsRejectedWithoutPublishingFiles()
    {
        var root = NewDirectory(); var receive = Path.Combine(root, "receive");
        await using var receiver = CreateService(receive);
        receiver.TransferRequested += (_, r) => r.AcceptTransfer(); var received = Finished(receiver);
        await receiver.StartAsync();
        using var client = new TcpClient(); await client.ConnectAsync(IPAddress.Loopback, receiver.ListeningPort!.Value);
        using var stream = new SslStream(client.GetStream(), false, (_, cert, _, _) => cert is not null && TransferCertificate.Fingerprint(cert) == receiver.Fingerprint);
        await stream.AuthenticateAsClientAsync("Lantern");
        var offer = new TransferOffer(1, Guid.NewGuid(), "Test", [new("file.txt", 3, new string('0', 64))]);
        await TransferWire.WriteAsync(stream, offer, CancellationToken.None);
        Assert.True((await TransferWire.ReadAsync<TransferReply>(stream, CancellationToken.None)).Accepted);
        await stream.WriteAsync(new byte[] { 1, 2, 3 });
        Assert.Equal(TransferState.Failed, (await received).State);
        Assert.Empty(Directory.GetFileSystemEntries(receive));
    }

    [Fact]
    public async Task DisconnectMidFile_DeletesIncompleteBatch()
    {
        var root = NewDirectory(); var receive = Path.Combine(root, "receive");
        await using var receiver = CreateService(receive);
        receiver.TransferRequested += (_, r) => r.AcceptTransfer(); var received = Finished(receiver);
        await receiver.StartAsync();
        using (var client = new TcpClient())
        {
            await client.ConnectAsync(IPAddress.Loopback, receiver.ListeningPort!.Value);
            using var stream = new SslStream(client.GetStream(), false, (_, cert, _, _) => cert is not null && TransferCertificate.Fingerprint(cert) == receiver.Fingerprint);
            await stream.AuthenticateAsClientAsync("Lantern");
            var offer = new TransferOffer(1, Guid.NewGuid(), "Test", [new("file.txt", 10_000, new string('0', 64))]);
            await TransferWire.WriteAsync(stream, offer, CancellationToken.None);
            Assert.True((await TransferWire.ReadAsync<TransferReply>(stream, CancellationToken.None)).Accepted);
            await stream.WriteAsync(new byte[100]);
        }
        Assert.Equal(TransferState.Failed, (await received).State);
        Assert.Empty(Directory.GetFileSystemEntries(receive));
    }

    [Fact]
    public async Task DuplicateNames_FailBeforeOfferingFiles()
    {
        var root = NewDirectory(); Directory.CreateDirectory(Path.Combine(root, "other"));
        var first = Path.Combine(root, "same.txt"); var second = Path.Combine(root, "other", "same.txt");
        await File.WriteAllTextAsync(first, "1"); await File.WriteAllTextAsync(second, "2");
        await using var sender = CreateService(root); var finished = Finished(sender);
        await sender.StartAsync();
        await sender.SendAsync(Peer(sender), [first, second], Verify);
        Assert.Equal(TransferState.Failed, (await finished).State);
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("..\\escape.txt")]
    [InlineData("C:\\escape.txt")]
    [InlineData("file:stream")]
    [InlineData("CON.txt")]
    [InlineData("lpt1")]
    [InlineData("NUL")]
    [InlineData("COM¹.txt")]
    [InlineData("CON .txt")]
    [InlineData("file.")]
    [InlineData("file ")]
    [InlineData("line\nname")]
    [InlineData("")]
    public void UnsafeWindowsFileNames_AreRejected(string name) => Assert.Throws<InvalidDataException>(() => TransferWire.ValidateFileName(name));

    [Fact]
    public void InvalidOffers_AreRejected()
    {
        var file = new OfferedFile("ok.txt", 0, new string('0', 64));
        Assert.Throws<InvalidDataException>(() => TransferWire.Validate(new(2, Guid.NewGuid(), "peer", [file])));
        Assert.Throws<InvalidDataException>(() => TransferWire.Validate(new(1, Guid.Empty, "peer", [file])));
        Assert.Throws<InvalidDataException>(() => TransferWire.Validate(new(1, Guid.NewGuid(), "peer", [])));
        Assert.Throws<InvalidDataException>(() => TransferWire.Validate(new(1, Guid.NewGuid(), "peer", [file with { Size = -1 }])));
        Assert.Throws<InvalidDataException>(() => TransferWire.Validate(new(1, Guid.NewGuid(), "peer", [file with { Hash = "bad" }])));
        Assert.Throws<InvalidDataException>(() => TransferWire.Validate(new(1, Guid.NewGuid(), "peer", [file, file with { Name = "OK.TXT" }])));
    }

    private string NewDirectory() { var path = Path.Combine(fixture.Root, Guid.NewGuid().ToString()); Directory.CreateDirectory(path); return path; }
    private FileSender CreateService(string receive) => new(new LocalDevice(Guid.NewGuid(), "Test PC", FreePort()), receive, fixture.Certificate);
    private Task<bool> Verify(string fingerprint, CancellationToken _) => Task.FromResult(fingerprint == TransferCertificate.Fingerprint(fixture.Certificate));
    private static Device Peer(FileSender service) => new(Guid.NewGuid(), "Peer", IPAddress.Loopback, service.ListeningPort!.Value);
    private static int FreePort() { using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); return ((IPEndPoint)listener.LocalEndpoint).Port; }
    private static Task<TransferInfo> Finished(FileSender service)
    {
        var completion = new TaskCompletionSource<TransferInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.TransferChanged += (_, info) => { if (info.IsFinished) completion.TrySetResult(info); };
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(20));
    }
    private static void AssertNoPartial(string path) => Assert.DoesNotContain(Directory.GetDirectories(path), p => p.EndsWith(".partial"));
}
