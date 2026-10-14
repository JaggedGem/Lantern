using Lantern.Models;
using Lantern.Networking.Discovery;
using Lantern.Transfers;

namespace Lantern.Application;

/// <summary>Headless application composition: persistent identity, live TCP endpoint, then LAN presence.</summary>
public sealed class LanternApplication : IAsyncDisposable
{
    private readonly string _name;
    private readonly Guid _deviceId;
    private readonly DiscoveryOptions? _discoveryOptions;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private DeviceDiscovery? _discovery;
    private bool _disposed;

    public LanternApplication(string deviceName, int tcpPort = 0, TransferOptions? transferOptions = null,
        DiscoveryOptions? discoveryOptions = null, string? identityPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceName);
        _name = deviceName;
        _deviceId = LocalDeviceIdentityProvider.LoadOrCreateDeviceId(identityPath);
        _ = new LocalDevice(_deviceId, _name, 1); // Validate advertisement before owning sockets.
        _discoveryOptions = discoveryOptions;
        Transfers = new TransferManager(_deviceId, tcpPort, transferOptions);
    }

    public TransferManager Transfers { get; }
    public Task DiscoveryCompletion => _discovery?.Completion ?? Task.CompletedTask;
    public LocalDevice? LocalDevice { get; private set; }
    public event EventHandler? DevicesChanged;
    public event EventHandler<Exception>? DiscoveryError;
    public IReadOnlyList<Device> GetDevices() => _discovery?.GetDiscoveredDevices() ?? Array.Empty<Device>();

    public async Task StartAsync(CancellationToken token = default)
    {
        await _lifecycle.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_discovery != null) throw new InvalidOperationException("The application is already running.");
            await Transfers.StartAsync(token).ConfigureAwait(false);
            try
            {
                LocalDevice = new LocalDevice(_deviceId, _name, Transfers.ListeningPort!.Value);
                var discovery = new DeviceDiscovery(LocalDevice, _discoveryOptions);
                discovery.DeviceDiscovered += (_, _) => DevicesChanged?.Invoke(this, EventArgs.Empty);
                discovery.DeviceUpdated += (_, _) => DevicesChanged?.Invoke(this, EventArgs.Empty);
                discovery.DeviceStatusChanged += (_, _) => DevicesChanged?.Invoke(this, EventArgs.Empty);
                discovery.DeviceRemoved += (_, _) => DevicesChanged?.Invoke(this, EventArgs.Empty);
                discovery.Error += (_, error) => DiscoveryError?.Invoke(this, error);
                try { await discovery.StartAsync(token).ConfigureAwait(false); }
                catch { await discovery.DisposeAsync().ConfigureAwait(false); throw; }
                _discovery = discovery;
            }
            catch { await Transfers.StopAsync().ConfigureAwait(false); LocalDevice = null; throw; }
        }
        finally { _lifecycle.Release(); }
    }

    public async Task<Transfer> SendFilesAsync(Guid destinationId, IEnumerable<string> sourcePaths, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(sourcePaths);
        var paths = sourcePaths.ToArray();
        var peer = GetDevices().SingleOrDefault(device => device.Id == destinationId && device.Status == DeviceStatus.Online)
            ?? throw new InvalidOperationException("The selected installation is not currently online.");
        var files = await Task.Run(() => paths.Select(path => TransferSourceFile.FromPath(path)).ToArray(), token).ConfigureAwait(false);
        return await Transfers.SendAsync(peer, files, token).ConfigureAwait(false);
    }

    public async Task StopAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            try { if (_discovery != null) await _discovery.DisposeAsync().ConfigureAwait(false); }
            finally { _discovery = null; LocalDevice = null; await Transfers.StopAsync().ConfigureAwait(false); }
        }
        finally { _lifecycle.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try { _disposed = true; }
        finally { _lifecycle.Release(); }
        try { await StopAsync().ConfigureAwait(false); }
        finally { await Transfers.DisposeAsync().ConfigureAwait(false); }
    }
}
