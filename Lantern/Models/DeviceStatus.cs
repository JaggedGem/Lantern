namespace Lantern.Models;

/// <summary>
/// Represents the current availability status of a discovered device.
/// This status indicates whether the device is visible on the LAN.
/// Note: This is separate from transfer status.
/// </summary>
public enum DeviceStatus
{
    /// <summary>
    /// Device status is unknown (initial state).
    /// </summary>
    Unknown,

    /// <summary>
    /// Device is online and reachable on the LAN.
    /// </summary>
    Online,

    /// <summary>
    /// Device is offline (has not been seen for the configured timeout).
    /// </summary>
    Offline,

    /// <summary>
    /// Device was previously online but is currently unavailable.
    /// </summary>
    Unavailable
}