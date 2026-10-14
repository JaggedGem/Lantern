using Lantern.Configuration;
using Lantern.Diagnostics;
using Lantern.Models;
using Lantern.Networking.Discovery;
using Lantern.Security;
using Lantern.Transfers;

namespace Lantern.Application;

/// <summary>Headless application composition with persisted preferences, trust, history and diagnostics.</summary>
public sealed class LanternApplication : IAsyncDisposable
{
    private readonly DiscoveryOptions _discoveryOptions;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly PeerIdentity _identity;
    private readonly FileStream _instanceLock;
    private DeviceDiscovery? _discovery;
    private bool _running;
    private bool _disposed;

    public static string DefaultDataDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Lantern"
        );

    public static LanternApplication Open(string? dataDirectory = null)
    {
        var directory = Path.GetFullPath(dataDirectory ?? DefaultDataDirectory);
        var settings = new SettingsStore(Path.Combine(directory, "settings.json")).Load();
        return new(
            settings.DeviceName,
            settings.TcpPort,
            identityPath: Path.Combine(directory, "device-id.json"),
            settings: settings
        );
    }

    public LanternApplication(
        string deviceName,
        int tcpPort = 0,
        TransferOptions? transferOptions = null,
        DiscoveryOptions? discoveryOptions = null,
        string? identityPath = null,
        LanternSettings? settings = null
    )
    {
        Settings =
            settings
            ?? new LanternSettings
            {
                DeviceName = deviceName,
                TcpPort = tcpPort,
                MaximumConcurrentTransfers = transferOptions?.MaximumConcurrentTransfers ?? 2,
                MaximumFilesPerTransfer = transferOptions?.MaximumFilesPerTransfer ?? 1024,
                MaximumTransferBytes =
                    transferOptions?.MaximumTransferBytes ?? 100L * 1024 * 1024 * 1024,
                DecisionTimeout = transferOptions?.RequestTimeout ?? TimeSpan.FromMinutes(2),
                NetworkTimeout = transferOptions?.InactivityTimeout ?? TimeSpan.FromSeconds(30),
                Discovery = discoveryOptions ?? new(),
            };
        Settings.Validate();
        _discoveryOptions = discoveryOptions ?? Settings.Discovery;
        identityPath = Path.GetFullPath(
            identityPath ?? Path.Combine(DefaultDataDirectory, "device-id.json")
        );
        var directory = Path.GetDirectoryName(identityPath)!;
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(
                    directory,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                );
        }
        _instanceLock = new(
            Path.Combine(directory, "instance.lock"),
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None
        );
        try
        {
            DeviceId = LocalDeviceIdentityProvider.LoadOrCreateDeviceId(identityPath);
            _ = new LocalDevice(DeviceId, Settings.DeviceName, 1);
            SettingsStore = new(Path.Combine(directory, "settings.json"));
            Trust = new(Path.Combine(directory, "trusted-peers.json"));
            History = new(Path.Combine(directory, "history.json"));
            Log = new(Path.Combine(directory, "logs", "lantern.jsonl"));
            _identity = PeerIdentity.LoadOrCreate(
                Path.Combine(directory, "identity.pfx"),
                DeviceId
            );
            try
            {
                Transfers = new(
                    DeviceId,
                    Settings.TcpPort,
                    transferOptions ?? Settings.ToTransferOptions(),
                    new(_identity, Trust)
                );
                Transfers.TransferChanged += OnTransferChanged;
                Transfers.Error += (_, error) => ReportError("transfer", error);
            }
            catch
            {
                _identity.Dispose();
                throw;
            }
        }
        catch
        {
            _instanceLock.Dispose();
            throw;
        }
    }

    public Guid DeviceId { get; }
    public string Fingerprint => _identity.Fingerprint;
    public LanternSettings Settings { get; }
    public SettingsStore SettingsStore { get; }
    public PeerTrustStore Trust { get; }
    public TransferHistory History { get; }
    public DiagnosticLog Log { get; }
    public TransferManager Transfers { get; }
    public Task DiscoveryCompletion => _discovery?.Completion ?? Task.CompletedTask;
    public LocalDevice? LocalDevice { get; private set; }
    public event EventHandler? DevicesChanged;
    public event EventHandler<Exception>? DiscoveryError;
    public event EventHandler<Exception>? Error;
    public event EventHandler<TransferChangedEventArgs>? NotificationRequested;

    public IReadOnlyList<Device> GetDevices() =>
        _discovery?.GetDiscoveredDevices() ?? Array.Empty<Device>();

    public ApplicationDiagnostics GetDiagnostics() =>
        new(
            DeviceId,
            Transfers.ListeningPort,
            _discovery?.IsRunning == true,
            GetDevices().Count,
            Transfers.GetTransfers().Count(t => !TransferHistory.IsTerminal(t.Status)),
            History.GetEntries().Count,
            Fingerprint,
            _identity.Certificate.NotAfter.ToUniversalTime(),
            Log.LogPath
        );

    public async Task StartAsync(CancellationToken token = default)
    {
        await _lifecycle.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_running)
                throw new InvalidOperationException("The application is already running.");
            await Transfers.StartAsync(token).ConfigureAwait(false);
            try
            {
                LocalDevice = new(DeviceId, Settings.DeviceName, Transfers.ListeningPort!.Value);
                if (Settings.DiscoveryEnabled)
                {
                    var discovery = new DeviceDiscovery(LocalDevice, _discoveryOptions);
                    discovery.DeviceDiscovered += (_, _) => NotifyDevicesChanged();
                    discovery.DeviceUpdated += (_, _) => NotifyDevicesChanged();
                    discovery.DeviceStatusChanged += (_, _) => NotifyDevicesChanged();
                    discovery.DeviceRemoved += (_, _) => NotifyDevicesChanged();
                    discovery.Error += (_, error) => ReportError("discovery", error);
                    try
                    {
                        await discovery.StartAsync(token).ConfigureAwait(false);
                    }
                    catch
                    {
                        await discovery.DisposeAsync().ConfigureAwait(false);
                        throw;
                    }
                    _discovery = discovery;
                }
                _running = true;
                WriteLog("application", "Started.");
            }
            catch
            {
                await Transfers.StopAsync().ConfigureAwait(false);
                LocalDevice = null;
                throw;
            }
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async Task<Transfer> SendFilesAsync(
        Guid destinationId,
        IEnumerable<string> sourcePaths,
        CancellationToken token = default
    )
    {
        ArgumentNullException.ThrowIfNull(sourcePaths);
        var paths = sourcePaths.Take(Settings.MaximumFilesPerTransfer + 1).ToArray();
        if (paths.Length < 1 || paths.Length > Settings.MaximumFilesPerTransfer)
            throw new ArgumentException("Invalid file count.", nameof(sourcePaths));
        var peer =
            GetDevices()
                .SingleOrDefault(device =>
                    device.Id == destinationId && device.Status == DeviceStatus.Online
                )
            ?? throw new InvalidOperationException(
                "The selected installation is not currently online."
            );
        var files = await TransferSelection
            .FromPathsAsync(paths, Settings.ToTransferOptions(), token)
            .ConfigureAwait(false);
        return await Transfers.SendAsync(peer, files, token).ConfigureAwait(false);
    }

    private void OnTransferChanged(object? sender, TransferChangedEventArgs args)
    {
        if (!TransferHistory.IsTerminal(args.Transfer.Status))
            return;
        try
        {
            History.Record(args.Transfer, args.DestinationDirectory);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            ReportError("history", error);
        }
        WriteLog(
            "transfer",
            $"{args.Transfer.Id:D}: {args.Transfer.Status}, {args.Transfer.TransferredBytes}/{args.Transfer.TotalBytes} bytes."
        );
        if (!Settings.ShowNotifications || NotificationRequested is not { } handlers)
            return;
        foreach (EventHandler<TransferChangedEventArgs> handler in handlers.GetInvocationList())
            try
            {
                handler(this, args);
            }
            catch (Exception error)
            {
                ReportError("notification", error);
            }
    }

    private void NotifyDevicesChanged()
    {
        if (DevicesChanged is not { } handlers)
            return;
        foreach (EventHandler handler in handlers.GetInvocationList())
            try
            {
                handler(this, EventArgs.Empty);
            }
            catch (Exception error)
            {
                ReportError("device-observer", error);
            }
    }

    private void WriteLog(string component, string message)
    {
        try
        {
            Log.Write(component, message);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.TraceError(error.ToString());
        }
    }

    private void ReportError(string component, Exception error)
    {
        WriteLog(component, error.ToString());
        if (component == "discovery" && DiscoveryError is { } discoveryHandlers)
            RaiseError(discoveryHandlers, error);
        if (Error is { } handlers)
            RaiseError(handlers, error);
    }

    private void RaiseError(EventHandler<Exception> handlers, Exception error)
    {
        foreach (EventHandler<Exception> handler in handlers.GetInvocationList())
            try
            {
                handler(this, error);
            }
            catch (Exception observer)
            {
                System.Diagnostics.Trace.TraceError(observer.ToString());
            }
    }

    public async Task StopAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    private async Task StopCoreAsync()
    {
        try
        {
            if (_discovery != null)
                await _discovery.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _discovery = null;
            LocalDevice = null;
            await Transfers.StopAsync().ConfigureAwait(false);
            _running = false;
            WriteLog("application", "Stopped.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
                return;
            _disposed = true;
            try
            {
                await StopCoreAsync().ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    await Transfers.DisposeAsync().ConfigureAwait(false);
                }
                finally
                {
                    _identity.Dispose();
                    _instanceLock.Dispose();
                }
            }
        }
        finally
        {
            _lifecycle.Release();
        }
    }
}
