using System.Net;
using System.Security.Cryptography;
using Lantern.Application;
using Lantern.Configuration;
using Lantern.Models;
using Lantern.Networking;
using Lantern.Networking.Protocol;
using Lantern.Security;
using Lantern.Transfers;
using Xunit;

namespace Lantern.Tests;

public sealed class SecureTransferIntegrationTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "lantern-tls-" + Guid.NewGuid().ToString("N")
    );
    private LanternApplication _sender = null!;
    private LanternApplication _receiver = null!;
    private string Destination => Path.Combine(_root, "received");
    private string SenderData => Path.Combine(_root, "sender");
    private string ReceiverData => Path.Combine(_root, "receiver");
    private Device Peer =>
        new(
            _receiver.DeviceId,
            "receiver",
            IPAddress.Loopback,
            _receiver.Transfers.ListeningPort!.Value
        );
    private LanternSettings Settings =>
        new()
        {
            TcpPort = 0,
            DiscoveryEnabled = false,
            DownloadDirectory = Destination,
            NetworkTimeout = TimeSpan.FromSeconds(3),
        };

    private LanternApplication Create(string directory) =>
        new("test", identityPath: Path.Combine(directory, "device-id.json"), settings: Settings);

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Destination);
        _sender = Create(SenderData);
        _receiver = Create(ReceiverData);
        _sender.Trust.TrustPeer(_receiver.DeviceId, _receiver.Fingerprint);
        _receiver.Trust.TrustPeer(_sender.DeviceId, _sender.Fingerprint);
        await _sender.StartAsync();
        await _receiver.StartAsync();
    }

    private async Task<TransferSourceFile> SourceAsync(string name, int size)
    {
        var path = Path.Combine(_root, name);
        await File.WriteAllBytesAsync(path, RandomNumberGenerator.GetBytes(size));
        return TransferSourceFile.FromPath(path);
    }

    [Fact]
    public async Task MutuallyPinnedPeersTransferFiles_AndPersistHistoryAndIdentity()
    {
        _receiver.Transfers.IncomingTransferRequested += (_, request) =>
            request.Accept(Destination);
        var file = await SourceAsync("content.bin", 250_003);
        var empty = await SourceAsync("empty.txt", 0);
        int notifications = 0;
        _sender.NotificationRequested += (_, _) => notifications++;
        var result = await _sender.Transfers.SendAsync(Peer, [file, empty]);
        Assert.Equal(TransferStatus.Completed, result.Status);
        var received = Path.Combine(Destination, "Lantern-" + result.Id.ToString("N"));
        Assert.Equal(
            await File.ReadAllBytesAsync(file.SourcePath),
            await File.ReadAllBytesAsync(Path.Combine(received, "content.bin"))
        );
        Assert.Equal(0, new FileInfo(Path.Combine(received, "empty.txt")).Length);
        Assert.Equal(TransferStatus.Completed, Assert.Single(_sender.History.GetEntries()).Status);
        Assert.Equal(received, Assert.Single(_receiver.History.GetEntries()).DestinationDirectory);
        Assert.Equal(1, notifications);
        var id = _sender.DeviceId;
        var fingerprint = _sender.Fingerprint;
        await _sender.DisposeAsync();
        _sender = Create(SenderData);
        Assert.Equal(id, _sender.DeviceId);
        Assert.Equal(fingerprint, _sender.Fingerprint);
        Assert.Equal(result.Id, Assert.Single(_sender.History.GetEntries()).Id);
        Assert.Single(_sender.Trust.GetTrustedPeers());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ChangedOrUnknownPeerCertificateCannotReachConsent(bool wrongPin)
    {
        if (wrongPin)
            _sender.Trust.TrustPeer(_receiver.DeviceId, new string('0', 64));
        else
            _receiver.Trust.RemovePeer(_sender.DeviceId);
        bool offered = false;
        _receiver.Transfers.IncomingTransferRequested += (_, _) => offered = true;
        var result = await _sender.Transfers.SendAsync(
            Peer,
            [await SourceAsync("private.txt", 10)]
        );
        Assert.Equal(TransferStatus.Failed, result.Status);
        Assert.False(offered);
        Assert.Empty(Directory.GetFileSystemEntries(Destination));
    }

    [Fact]
    public async Task AuthenticatedCertificateCannotClaimAnotherInstallationId()
    {
        var error = new TaskCompletionSource<Exception>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        _receiver.Transfers.Error += (_, ex) => error.TrySetResult(ex);
        bool offered = false;
        _receiver.Transfers.IncomingTransferRequested += (_, _) => offered = true;
        using var identity = PeerIdentity.LoadOrCreate(
            Path.Combine(SenderData, "identity.pfx"),
            _sender.DeviceId
        );
        using var connection = await new NetworkClient().ConnectAsync(
            IPAddress.Loopback,
            Peer.Port
        );
        await new TransferSecurity(identity, _sender.Trust).AuthenticateAsync(
            connection,
            false,
            _receiver.DeviceId,
            CancellationToken.None
        );
        await connection.SendAsync(
            Message.CreateHello(new HelloPayload("Lantern", ProtocolLimits.Version, Guid.NewGuid()))
        );
        Assert.IsType<InvalidDataException>(await error.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(offered);
    }

    [Fact]
    public async Task PlaintextTrafficIsRejectedByTheApplication()
    {
        var error = new TaskCompletionSource<Exception>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        _receiver.Transfers.Error += (_, ex) => error.TrySetResult(ex);
        using var connection = await new NetworkClient().ConnectAsync(
            IPAddress.Loopback,
            Peer.Port
        );
        await connection.SendAsync(
            Message.CreateHello(
                new HelloPayload("Lantern", ProtocolLimits.Version, _sender.DeviceId)
            )
        );
        await error.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(_receiver.Transfers.GetTransfers());
    }

    [Fact]
    public void TransferManagerRequiresSecurityUnlessExplicitlyOptedOutForTests() =>
        Assert.Throws<ArgumentException>(() => new TransferManager(Guid.NewGuid()));

    [Fact]
    public void InstallationLockPreventsConcurrentWriters() =>
        Assert.Throws<IOException>(() => Create(SenderData));

    [Fact]
    public async Task DiagnosticsDescribeLiveState_AndSettingsSurviveReopen()
    {
        var diagnostics = _sender.GetDiagnostics();
        Assert.Equal(_sender.DeviceId, diagnostics.DeviceId);
        Assert.Equal(_sender.Transfers.ListeningPort, diagnostics.ListeningPort);
        Assert.False(diagnostics.DiscoveryRunning);
        _sender.SettingsStore.Save(Settings with { DeviceName = "Saved name" });
        await _sender.DisposeAsync();
        _sender = LanternApplication.Open(SenderData);
        Assert.Equal("Saved name", _sender.Settings.DeviceName);
        Assert.Equal(_sender.Fingerprint, _sender.GetDiagnostics().CertificateFingerprint);
    }

    public async Task DisposeAsync()
    {
        await _sender.DisposeAsync();
        await _receiver.DisposeAsync();
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
