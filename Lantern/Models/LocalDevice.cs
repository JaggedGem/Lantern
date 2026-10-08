namespace Lantern.Models;

/// <summary>
/// Represents the identity and configuration of the local application instance.
/// This is used for discovery advertisement.
/// </summary>
public class LocalDevice
{
    /// <summary>
    /// Unique persistent identifier for this application instance.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// Human-readable name for this device.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// TCP listening port for this application instance.
    /// </summary>
    public int Port { get; }

    public LocalDevice(Guid id, string name, int port)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), port, "The port must be between 1 and 65535.");
        }

        if (id == Guid.Empty) throw new ArgumentException("The device ID cannot be empty.", nameof(id));
        if (name.Length > 128 || name.Any(char.IsControl)) throw new ArgumentException("Device names must be at most 128 characters.", nameof(name));
        Id = id;
        Name = name;
        Port = port;
    }
}

