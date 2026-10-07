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

/// <summary>
/// Manages LAN device discovery using UDP broadcast.
/// Automatically discovers peer application instances and tracks their availability.
/// </summary>
public sealed class DeviceDiscovery : IDisposable, IAsyncDisposable
{
    private const int DiscoveryUdpPort = 52845;
    private const int HeartbeatIntervalMs = 3000;
    private const int OfflineTimeoutMs = 15000;
    private const int ExpirationCheckIntervalMs = 5000;

    private readonly LocalDevice _localDevice;
    private readonly ReaderWriterLockSlim _devicesLock = new();
    private readonly Dictionary<Guid, Device> _devices = new();
    private UdpClient? _udpClient;
    private CancellationTokenSource? _shutdownSource;
    private Task? _receiveLoopTask;
    private Task? _heartbeatLoopTask;
    private Task? _expirationCheckLoopTask;
    private int _isDisposed;
    private bool _isRunning;

    /// <summary>
    /// Fired when a new device is discovered.
    /// </summary>
    public event EventHandler<DeviceDiscoveredEventArgs>? DeviceDiscovered;

    /// <summary>
    /// Fired when a device's status changes (e.g., Online -> Offline).
    /// </summary>
    public event EventHandler<DeviceStatusChangedEventArgs>? DeviceStatusChanged;

    /// <summary>
    /// Fired when a device is removed (e.g., after offline timeout).
    /// </summary>
    public event EventHandler<DeviceRemovedEventArgs>? DeviceRemoved;

    public DeviceDiscovery(LocalDevice localDevice)
    {
        _localDevice = localDevice ?? throw new ArgumentNullException(nameof(localDevice));
    }

    /// <summary>
    /// Gets whether discovery is currently running.
    /// </summary>
    public bool IsRunning
    {
        get
        {
            _devicesLock.EnterReadLock();
            try
            {
                return _isRunning;
            }
            finally
            {
                _devicesLock.ExitReadLock();
            }
        }
    }

    /// <summary>
    /// Gets a snapshot of currently discovered devices (excluding the local device).
    /// </summary>
    public IReadOnlyList<Device> GetDiscoveredDevices()
    {
        _devicesLock.EnterReadLock();
        try
        {
            return _devices.Values.ToList();
        }
        finally
        {
            _devicesLock.ExitReadLock();
        }
    }

    /// <summary>
    /// Starts the discovery service.
    /// Begins listening for announcements and sending periodic heartbeats.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _devicesLock.EnterWriteLock();
        try
        {
            if (_isRunning)
            {
                throw new InvalidOperationException("Device discovery is already running.");
            }

            if (Volatile.Read(ref _isDisposed) != 0)
            {
                throw new ObjectDisposedException(nameof(DeviceDiscovery));
            }

            var shutdownSource = new CancellationTokenSource();
            UdpClient? udpClient = null;

            try
            {
                udpClient = new UdpClient(new IPEndPoint(IPAddress.Any, DiscoveryUdpPort))
                {
                    EnableBroadcast = true,
                    MulticastLoopback = false
                };

                _udpClient = udpClient;
                _shutdownSource = shutdownSource;
                _isRunning = true;

                // Start background tasks
                _receiveLoopTask = ReceiveLoopAsync(_udpClient, shutdownSource.Token);
                _heartbeatLoopTask = HeartbeatLoopAsync(_udpClient, shutdownSource.Token);
                _expirationCheckLoopTask = ExpirationCheckLoopAsync(shutdownSource.Token);
            }
            catch
            {
                shutdownSource?.Dispose();
                udpClient?.Dispose();
                _isRunning = false;
                throw;
            }
        }
        finally
        {
            _devicesLock.ExitWriteLock();
        }

        // Send initial announcement
        await SendAnnouncementAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Stops the discovery service cleanly.
    /// </summary>
    public async Task StopAsync()
    {
        UdpClient? udpClient;
        CancellationTokenSource? shutdownSource;
        Task? receiveLoopTask;
        Task? heartbeatLoopTask;
        Task? expirationCheckLoopTask;

        _devicesLock.EnterWriteLock();
        try
        {
            if (!_isRunning)
            {
                return;
            }

            _isRunning = false;
            udpClient = _udpClient;
            shutdownSource = _shutdownSource;
            receiveLoopTask = _receiveLoopTask;
            heartbeatLoopTask = _heartbeatLoopTask;
            expirationCheckLoopTask = _expirationCheckLoopTask;

            _udpClient = null;
            _shutdownSource = null;
            _receiveLoopTask = null;
            _heartbeatLoopTask = null;
            _expirationCheckLoopTask = null;
        }
        finally
        {
            _devicesLock.ExitWriteLock();
        }

        try
        {
            shutdownSource?.Cancel();
            udpClient?.Dispose();

            if (receiveLoopTask is not null)
            {
                await receiveLoopTask.ConfigureAwait(false);
            }

            if (heartbeatLoopTask is not null)
            {
                await heartbeatLoopTask.ConfigureAwait(false);
            }

            if (expirationCheckLoopTask is not null)
            {
                await expirationCheckLoopTask.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (shutdownSource?.IsCancellationRequested == true)
        {
        }
        catch (ObjectDisposedException) when (shutdownSource?.IsCancellationRequested == true)
        {
        }
        finally
        {
            shutdownSource?.Dispose();
            udpClient?.Dispose();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return;
        }

        StopAsync().GetAwaiter().GetResult();
        _devicesLock.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return;
        }

        await StopAsync().ConfigureAwait(false);
        _devicesLock.Dispose();
    }

    private async Task SendAnnouncementAsync(CancellationToken cancellationToken)
    {
        UdpClient? udpClient;
        _devicesLock.EnterReadLock();
        try
        {
            if (!_isRunning)
            {
                return;
            }

            udpClient = _udpClient;
            if (udpClient is null)
            {
                return;
            }
        }
        finally
        {
            _devicesLock.ExitReadLock();
        }

        try
        {
            var message = new DiscoveryMessage
            {
                Type = DiscoveryMessageType.Announcement,
                DeviceId = _localDevice.Id,
                DeviceName = _localDevice.Name,
                TcpPort = _localDevice.Port,
                Version = DiscoveryProtocolConstants.ProtocolVersion
            };

            var json = JsonSerializer.Serialize(message);
            var bytes = System.Text.Encoding.UTF8.GetBytes(json);

            if (bytes.Length > DiscoveryProtocolConstants.MaxDiscoveryPacketSize)
            {
                return; // Silently drop oversized packet
            }

            var broadcastEndpoint = new IPEndPoint(IPAddress.Broadcast, DiscoveryUdpPort);
            await udpClient.SendAsync(bytes, broadcastEndpoint, cancellationToken).ConfigureAwait(false);
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            // Network errors are expected; silently continue
        }
    }

    private async Task ReceiveLoopAsync(UdpClient udpClient, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var result = await udpClient.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                    ProcessDiscoveryPacket(result.Buffer, result.RemoteEndPoint);
                }
                catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch
                {
                    // Network errors are expected; continue receiving
                }
            }
        }
        finally
        {
            _devicesLock.EnterWriteLock();
            try
            {
                _receiveLoopTask = null;
            }
            finally
            {
                _devicesLock.ExitWriteLock();
            }
        }
    }

    private void ProcessDiscoveryPacket(byte[] packetData, IPEndPoint remoteEndpoint)
    {
        try
        {
            if (packetData.Length > DiscoveryProtocolConstants.MaxDiscoveryPacketSize)
            {
                return; // Reject oversized packet
            }

            var json = System.Text.Encoding.UTF8.GetString(packetData);
            var message = JsonSerializer.Deserialize<DiscoveryMessage>(json);

            if (message is null)
            {
                return; // Invalid JSON
            }

            // Validate protocol version
            if (message.Version != DiscoveryProtocolConstants.ProtocolVersion)
            {
                return; // Unsupported version
            }

            // Ignore our own announcements
            if (message.DeviceId == _localDevice.Id)
            {
                return;
            }

            // Validate fields
            if (string.IsNullOrWhiteSpace(message.DeviceName))
            {
                return;
            }

            if (message.TcpPort is < 1 or > 65535)
            {
                return;
            }

            ProcessValidDiscoveryMessage(message, remoteEndpoint.Address);
        }
        catch (JsonException)
        {
            // Invalid JSON; silently ignore
        }
        catch (Exception)
        {
            // Unexpected error; log but don't crash
        }
    }

    private void ProcessValidDiscoveryMessage(DiscoveryMessage message, IPAddress senderIp)
    {
        _devicesLock.EnterUpgradeableReadLock();
        try
        {
            if (_devices.TryGetValue(message.DeviceId, out var existingDevice))
            {
                // Update existing device
                var ipChanged = !existingDevice.IpAddress.Equals(senderIp);
                var nameChanged = existingDevice.Name != message.DeviceName;
                var portChanged = existingDevice.Port != message.TcpPort;

                if (ipChanged)
                {
                    existingDevice.UpdateIpAddress(senderIp);
                }

                if (nameChanged)
                {
                    existingDevice.UpdateName(message.DeviceName);
                }

                if (portChanged)
                {
                    existingDevice.UpdatePort(message.TcpPort);
                }

                // Update status if needed
                var wasOffline = existingDevice.Status == DeviceStatus.Offline;
                if (wasOffline)
                {
                    var oldStatus = existingDevice.Status;
                    existingDevice.UpdateStatus(DeviceStatus.Online);
                    OnDeviceStatusChanged(existingDevice, oldStatus, DeviceStatus.Online);
                }
                else
                {
                    existingDevice.RefreshLastSeen();
                }
            }
            else
            {
                // New device discovered
                _devicesLock.EnterWriteLock();
                try
                {
                    var device = new Device(message.DeviceId, message.DeviceName, senderIp, message.TcpPort);
                    device.UpdateStatus(DeviceStatus.Online);
                    _devices[message.DeviceId] = device;
                    OnDeviceDiscovered(device);
                }
                finally
                {
                    _devicesLock.ExitWriteLock();
                }
            }
        }
        finally
        {
            _devicesLock.ExitUpgradeableReadLock();
        }
    }

    private async Task HeartbeatLoopAsync(UdpClient udpClient, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(HeartbeatIntervalMs, cancellationToken).ConfigureAwait(false);
                    await SendAnnouncementAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
        finally
        {
            _devicesLock.EnterWriteLock();
            try
            {
                _heartbeatLoopTask = null;
            }
            finally
            {
                _devicesLock.ExitWriteLock();
            }
        }
    }

    private async Task ExpirationCheckLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(ExpirationCheckIntervalMs, cancellationToken).ConfigureAwait(false);
                    CheckAndRemoveExpiredDevices();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
        finally
        {
            _devicesLock.EnterWriteLock();
            try
            {
                _expirationCheckLoopTask = null;
            }
            finally
            {
                _devicesLock.ExitWriteLock();
            }
        }
    }

    private void CheckAndRemoveExpiredDevices()
    {
        var now = DateTimeOffset.UtcNow;
        var offlineTimeout = TimeSpan.FromMilliseconds(OfflineTimeoutMs);

        _devicesLock.EnterUpgradeableReadLock();
        try
        {
            var expiredDeviceIds = _devices
                .Where(kvp => now - kvp.Value.LastSeen > offlineTimeout)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var deviceId in expiredDeviceIds)
            {
                _devicesLock.EnterWriteLock();
                try
                {
                    if (_devices.TryGetValue(deviceId, out var device))
                    {
                        var previousStatus = device.Status;
                        if (previousStatus != DeviceStatus.Offline)
                        {
                            device.UpdateStatus(DeviceStatus.Offline);
                            OnDeviceStatusChanged(device, previousStatus, DeviceStatus.Offline);
                        }

                        // Remove after marking offline (can be adjusted if needed)
                        _devices.Remove(deviceId);
                        OnDeviceRemoved(deviceId);
                    }
                }
                finally
                {
                    _devicesLock.ExitWriteLock();
                }
            }
        }
        finally
        {
            _devicesLock.ExitUpgradeableReadLock();
        }
    }

    private void OnDeviceDiscovered(Device device)
    {
        DeviceDiscovered?.Invoke(this, new DeviceDiscoveredEventArgs(device));
    }

    private void OnDeviceStatusChanged(Device device, DeviceStatus oldStatus, DeviceStatus newStatus)
    {
        DeviceStatusChanged?.Invoke(this, new DeviceStatusChangedEventArgs(device, oldStatus, newStatus));
    }

    private void OnDeviceRemoved(Guid deviceId)
    {
        DeviceRemoved?.Invoke(this, new DeviceRemovedEventArgs(deviceId));
    }
}



