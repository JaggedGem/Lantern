using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Lantern.Models;

namespace Lantern.Networking.Discovery;

/// <summary>
/// Event args for device discovery events.
/// </summary>
public sealed class DeviceDiscoveredEventArgs : EventArgs
{
    public DeviceDiscoveredEventArgs(Device device)
    {
        Device = device ?? throw new ArgumentNullException(nameof(device));
    }

    public Device Device { get; }
}

/// <summary>
/// Event args for device status changes.
/// </summary>
public sealed class DeviceStatusChangedEventArgs : EventArgs
{
    public DeviceStatusChangedEventArgs(Device device, DeviceStatus oldStatus, DeviceStatus newStatus)
    {
        Device = device ?? throw new ArgumentNullException(nameof(device));
        OldStatus = oldStatus;
        NewStatus = newStatus;
    }

    public Device Device { get; }
    public DeviceStatus OldStatus { get; }
    public DeviceStatus NewStatus { get; }
}

/// <summary>
/// Event args for when a device is removed.
/// </summary>
public sealed class DeviceRemovedEventArgs : EventArgs
{
    public DeviceRemovedEventArgs(Guid deviceId)
    {
        DeviceId = deviceId;
    }

    public Guid DeviceId { get; }
}

/// <summary>IPv4 LAN discovery. Returned devices/events are detached snapshots.</summary>
public sealed class DeviceDiscovery : IDisposable, IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly LocalDevice _local;
    private readonly DiscoveryOptions _options;
    private readonly TimeProvider _clock;
    private readonly Dictionary<Guid, Device> _devices = new();
    private readonly Dictionary<Guid, long> _observed = new();
    private UdpClient? _socket;
    private CancellationTokenSource? _shutdown;
    private Task? _completion;
    private Task? _stop;
    private bool _disposed;

    public DeviceDiscovery(LocalDevice localDevice, DiscoveryOptions? options = null, TimeProvider? timeProvider = null)
    {
        _local = localDevice ?? throw new ArgumentNullException(nameof(localDevice));
        _options = options ?? new DiscoveryOptions();
        _options.Validate();
        _clock = timeProvider ?? TimeProvider.System;
    }

    public event EventHandler<DeviceDiscoveredEventArgs>? DeviceDiscovered;
    public event EventHandler<DeviceDiscoveredEventArgs>? DeviceUpdated;
    public event EventHandler<DeviceStatusChangedEventArgs>? DeviceStatusChanged;
    public event EventHandler<DeviceRemovedEventArgs>? DeviceRemoved;
    public event EventHandler<Exception>? Error;
    public bool IsRunning { get { lock (_sync) return _socket != null && _stop == null && _completion?.IsCompleted == false; } }
    public Task Completion { get { lock (_sync) return _completion ?? Task.CompletedTask; } }

    public IReadOnlyList<Device> GetDiscoveredDevices()
    {
        lock (_sync) return _devices.Values.Select(device => device.Snapshot()).ToList().AsReadOnly();
    }

    /// <summary>Startup token only; StopAsync controls and drains the running service.</summary>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_socket != null) throw new InvalidOperationException("Discovery is running or stopping.");
            var socket = new UdpClient(new IPEndPoint(IPAddress.Any, _options.UdpPort)) { EnableBroadcast = true };
            _socket = socket;
            _shutdown = new CancellationTokenSource();
            _stop = null;
            _completion = RunAsync(socket, _shutdown);
        }
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        lock (_sync)
        {
            if (_socket == null) return Task.CompletedTask;
            return _stop ??= StopCoreAsync(_socket, _shutdown!, _completion!);
        }
    }

    private async Task StopCoreAsync(UdpClient socket, CancellationTokenSource shutdown, Task completion)
    {
        await Task.Yield();
        try { shutdown.Cancel(); socket.Dispose(); await completion.ConfigureAwait(false); }
        finally
        {
            socket.Dispose();
            shutdown.Dispose();
            lock (_sync) { _socket = null; _shutdown = null; _completion = null; _stop = null; }
        }
    }

    private async Task RunAsync(UdpClient socket, CancellationTokenSource shutdown)
    {
        await Task.Yield();
        var tasks = new[] { ReceiveAsync(socket, shutdown.Token), AnnounceAsync(socket, shutdown.Token), ExpireAsync(shutdown.Token) };
        await Task.WhenAny(tasks).ConfigureAwait(false);
        shutdown.Cancel();
        socket.Dispose();
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task ReceiveAsync(UdpClient socket, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var packet = await socket.ReceiveAsync(token).ConfigureAwait(false);
                    ProcessDiscoveryPacket(packet.Buffer, packet.RemoteEndPoint);
                }
                catch (SocketException exception) when (!token.IsCancellationRequested)
                {
                    ReportError(exception);
                    await Task.Delay(TimeSpan.FromMilliseconds(250), _clock, token).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (token.IsCancellationRequested) { }
        catch (SocketException) when (token.IsCancellationRequested) { }
    }

    private async Task AnnounceAsync(UdpClient socket, CancellationToken token)
    {
        var message = new DiscoveryMessage { Version = DiscoveryProtocolConstants.ProtocolVersion,
            Type = DiscoveryMessageType.Announcement, DeviceId = _local.Id, DeviceName = _local.Name, TcpPort = _local.Port };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message);
        if (bytes.Length > DiscoveryProtocolConstants.MaxDiscoveryPacketSize)
            throw new InvalidOperationException("The local announcement exceeds the protocol limit.");
        try
        {
            while (true)
            {
                // Directed broadcasts route to each eligible IPv4 LAN instead of a single default adapter.
                foreach (var address in GetBroadcastAddresses())
                {
                    try { await socket.SendAsync(bytes, new IPEndPoint(address, _options.UdpPort), token).ConfigureAwait(false); }
                    catch (SocketException exception) when (!token.IsCancellationRequested) { ReportError(exception); }
                }
                await Task.Delay(_options.HeartbeatInterval, _clock, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (token.IsCancellationRequested) { }
        catch (SocketException) when (token.IsCancellationRequested) { }
    }

    private IEnumerable<IPAddress> GetBroadcastAddresses()
    {
        var addresses = new HashSet<IPAddress>();
        foreach (var adapter in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adapter.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up
                || adapter.NetworkInterfaceType is System.Net.NetworkInformation.NetworkInterfaceType.Loopback
                    or System.Net.NetworkInformation.NetworkInterfaceType.Tunnel) continue;
            try
            {
                foreach (var unicast in adapter.GetIPProperties().UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(unicast.Address)) continue;
                    var ip = unicast.Address.GetAddressBytes();
                    var mask = unicast.IPv4Mask.GetAddressBytes();
                    if (mask.All(value => value == 255)) continue;
                    addresses.Add(new IPAddress(ip.Zip(mask, (value, subnet) => (byte)(value | ~subnet)).ToArray()));
                }
            }
            catch (System.Net.NetworkInformation.NetworkInformationException exception) { ReportError(exception); }
        }
        return addresses;
    }

    private async Task ExpireAsync(CancellationToken token)
    {
        try
        {
            while (true)
            {
                await Task.Delay(_options.ExpirationInterval, _clock, token).ConfigureAwait(false);
                CheckPresence();
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private void ProcessDiscoveryPacket(byte[] data, IPEndPoint endpoint)
    {
        if (data.Length == 0 || data.Length > DiscoveryProtocolConstants.MaxDiscoveryPacketSize) return;
        DiscoveryMessage? message;
        try { message = JsonSerializer.Deserialize<DiscoveryMessage>(data); }
        catch (JsonException) { return; }
        if (message == null || message.Version != DiscoveryProtocolConstants.ProtocolVersion
            || message.Type != DiscoveryMessageType.Announcement || message.DeviceId == Guid.Empty
            || message.DeviceId == _local.Id || string.IsNullOrWhiteSpace(message.DeviceName)
            || message.DeviceName.Length > 128 || message.DeviceName.Any(char.IsControl)
            || message.TcpPort is < 1 or > 65535 || endpoint.Address.AddressFamily != AddressFamily.InterNetwork
            || IPAddress.IsLoopback(endpoint.Address) || endpoint.Address.Equals(IPAddress.Any)
            || endpoint.Address.Equals(IPAddress.Broadcast)) return;

        Device snapshot;
        bool added, changed;
        DeviceStatus previous;
        lock (_sync)
        {
            added = !_devices.TryGetValue(message.DeviceId, out var device);
            if (added && _devices.Count >= _options.MaximumDevices) return;
            device ??= new Device(message.DeviceId, message.DeviceName, endpoint.Address, message.TcpPort);
            previous = device.Status;
            changed = device.Name != message.DeviceName || !device.IpAddress.Equals(endpoint.Address) || device.Port != message.TcpPort;
            device.Observe(message.DeviceName, endpoint.Address, message.TcpPort, _clock.GetUtcNow());
            _devices[message.DeviceId] = device;
            _observed[message.DeviceId] = _clock.GetTimestamp();
            snapshot = device.Snapshot();
        }
        if (added) DeviceDiscovered?.Invoke(this, new DeviceDiscoveredEventArgs(snapshot));
        else
        {
            if (changed) DeviceUpdated?.Invoke(this, new DeviceDiscoveredEventArgs(snapshot.Snapshot()));
            if (previous != DeviceStatus.Online)
                DeviceStatusChanged?.Invoke(this, new DeviceStatusChangedEventArgs(snapshot, previous, DeviceStatus.Online));
        }
    }

    private void CheckPresence()
    {
        var changes = new List<(Device, DeviceStatus)>();
        lock (_sync)
        {
            var now = _clock.GetTimestamp();
            foreach (var device in _devices.Values)
            {
                if (device.Status == DeviceStatus.Online && _clock.GetElapsedTime(_observed[device.Id], now) > _options.OfflineTimeout)
                {
                    var previous = device.Status;
                    device.UpdateStatus(DeviceStatus.Offline);
                    changes.Add((device.Snapshot(), previous));
                }
            }
        }
        foreach (var (device, previous) in changes)
            DeviceStatusChanged?.Invoke(this, new DeviceStatusChangedEventArgs(device, previous, DeviceStatus.Offline));
    }

    /// <summary>Explicit pruning: heartbeat expiry alone never deletes an installation.</summary>
    public bool ForgetOfflineDevice(Guid id)
    {
        lock (_sync)
        {
            if (!_devices.TryGetValue(id, out var device) || device.Status != DeviceStatus.Offline) return false;
            _devices.Remove(id);
            _observed.Remove(id);
        }
        DeviceRemoved?.Invoke(this, new DeviceRemovedEventArgs(id));
        return true;
    }

    private void ReportError(Exception exception)
    {
        System.Diagnostics.Trace.TraceError(exception.ToString());
        Error?.Invoke(this, exception);
    }

    public async ValueTask DisposeAsync()
    {
        lock (_sync) _disposed = true;
        await StopAsync().ConfigureAwait(false);
    }
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
}
