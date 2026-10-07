using System.Text.Json;

namespace Lantern.Models;

/// <summary>
/// Manages persistent storage of the local device ID.
/// Ensures the same device ID is used across application restarts.
/// </summary>
internal sealed class LocalDeviceIdentityProvider
{
    private static readonly string DeviceIdFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Lantern",
        "device-id.json"
    );

    /// <summary>
    /// Loads the persisted device ID, or generates and persists a new one if missing.
    /// </summary>
    public static Guid LoadOrCreateDeviceId()
    {
        try
        {
            if (File.Exists(DeviceIdFilePath))
            {
                var json = File.ReadAllText(DeviceIdFilePath);
                var document = JsonDocument.Parse(json);
                if (document.RootElement.TryGetProperty("deviceId", out var idElement) &&
                    idElement.TryGetGuid(out var id))
                {
                    return id;
                }
            }
        }
        catch
        {
            // If reading fails, generate a new ID
        }

        // Generate and persist a new ID
        var newId = Guid.NewGuid();
        PersistDeviceId(newId);
        return newId;
    }

    private static void PersistDeviceId(Guid deviceId)
    {
        try
        {
            var directory = Path.GetDirectoryName(DeviceIdFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(new { deviceId });
            File.WriteAllText(DeviceIdFilePath, json);
        }
        catch
        {
            // Silently fail - if we can't persist, we'll just generate a new ID next time
            // This is acceptable for a local application
        }
    }
}

