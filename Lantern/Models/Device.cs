using System.Net;

namespace Lantern.Models;

/// <summary>
/// Represents a discovered peer application instance on the LAN.
/// A Device is not a TCP connection; it is a known peer that can be contacted.
/// </summary>
public class Device
{
    /// <summary>
    /// Unique identifier for this device/application instance.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// Human-readable device name.
    /// </summary>
    public string Name { get; private set; }

    /// <summary>
    /// IP address of the device.
    /// </summary>
    public IPAddress IpAddress { get; private set; }

    /// <summary>
    /// TCP listening port of the device.
    /// </summary>
    public int Port { get; private set; }

    /// <summary>
    /// Timestamp when this device was last observed.
    /// </summary>
    public DateTimeOffset LastSeen { get; private set; }

    /// <summary>
    /// Current availability status of the device.
    /// </summary>
    public DeviceStatus Status { get; private set; }

    public Device(Guid id, string name, IPAddress ipAddress, int port)
    {
        ArgumentNullException.ThrowIfNull(ipAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), port, "The port must be between 1 and 65535.");
        }

        Id = id;
        Name = name;
        IpAddress = ipAddress;
        Port = port;
        LastSeen = DateTimeOffset.UtcNow;
        Status = DeviceStatus.Unknown;
    }

    /// <summary>
    /// Updates the device's name.
    /// </summary>
    public void UpdateName(string newName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);
        Name = newName;
        LastSeen = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Updates the device's TCP port.
    /// </summary>
    public void UpdatePort(int newPort)
    {
        if (newPort is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(newPort), newPort, "The port must be between 1 and 65535.");
        }

        Port = newPort;
        LastSeen = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Updates the device's IP address.
    /// </summary>
    public void UpdateIpAddress(IPAddress newIpAddress)
    {
        ArgumentNullException.ThrowIfNull(newIpAddress);
        IpAddress = newIpAddress;
        LastSeen = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Updates the device's status and refreshes LastSeen.
    /// </summary>
    public void UpdateStatus(DeviceStatus newStatus)
    {
        Status = newStatus;
        LastSeen = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Refreshes the LastSeen timestamp without changing status.
    /// Used for heartbeat updates.
    /// </summary>
    public void RefreshLastSeen()
    {
        LastSeen = DateTimeOffset.UtcNow;
    }
}