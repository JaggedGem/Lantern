using System.Text.Json;

namespace Lantern.Models;

/// <summary>Persists installation identity separately from editable application settings.</summary>
public static class LocalDeviceIdentityProvider
{
    public static Guid LoadOrCreateDeviceId(string? directory = null)
    {
        directory ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lantern");
        var path = Path.Combine(directory, "device-id.json");
        if (File.Exists(path))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.TryGetProperty("deviceId", out var element) && element.TryGetGuid(out var existing) && existing != Guid.Empty)
                return existing;
            throw new InvalidDataException("The saved device identity is invalid. Restore or remove device-id.json to create a new identity.");
        }
        var id = Guid.NewGuid();
        Directory.CreateDirectory(directory);
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(output, new { deviceId = id });
        return id;
    }
}
