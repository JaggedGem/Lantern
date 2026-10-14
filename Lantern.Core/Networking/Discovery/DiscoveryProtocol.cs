using System.Text.Json.Serialization;

namespace Lantern.Networking.Discovery;

/// <summary>
/// Discovery protocol constants and message types.
/// </summary>
internal static class DiscoveryProtocolConstants
{
    /// <summary>
    /// Discovery protocol version for future compatibility.
    /// </summary>
    public const int ProtocolVersion = 1;

    /// <summary>
    /// Maximum size for a discovery packet.
    /// Discovery packets should be very small; keep this conservative.
    /// </summary>
    public const int MaxDiscoveryPacketSize = 4096;
}

/// <summary>
/// Types of discovery messages.
/// </summary>
internal enum DiscoveryMessageType
{
    /// <summary>
    /// Announcement/heartbeat of a device's presence.
    /// </summary>
    Announcement = 1,

    /// <summary>
    /// Response to a discovery query.
    /// </summary>
    Response = 2
}

/// <summary>
/// A discovery protocol message (JSON serialized).
/// This is separate from TCP Message used in the protocol layer.
/// </summary>
internal sealed record DiscoveryMessage
{
    /// <summary>
    /// Protocol version for future compatibility.
    /// </summary>
    [JsonPropertyName("v")]
    public int Version { get; init; } = DiscoveryProtocolConstants.ProtocolVersion;

    /// <summary>
    /// Type of discovery message.
    /// </summary>
    [JsonPropertyName("t")]
    public DiscoveryMessageType Type { get; init; }

    /// <summary>
    /// Unique identifier of the announcing device.
    /// </summary>
    [JsonPropertyName("id")]
    public Guid DeviceId { get; init; }

    /// <summary>
    /// Human-readable name of the device.
    /// </summary>
    [JsonPropertyName("n")]
    public string DeviceName { get; init; } = string.Empty;

    /// <summary>
    /// TCP listening port of the device.
    /// </summary>
    [JsonPropertyName("p")]
    public int TcpPort { get; init; }
}



