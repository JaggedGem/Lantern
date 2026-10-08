using System.Text.Json;

namespace Lantern.Models;

/// <summary>Single-writer, atomic identity storage. Storage failures are surfaced to the owner.</summary>
internal sealed class LocalDeviceIdentityProvider
{
    public static Guid LoadOrCreateDeviceId(string? storagePath = null)
    {
        var path = Path.GetFullPath(storagePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lantern", "device-id.json"));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // An exclusive companion lock also coordinates different application processes.
        using var ownership = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (File.Exists(path))
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("deviceId", out var element)
                || element.ValueKind != JsonValueKind.String || !element.TryGetGuid(out var id) || id == Guid.Empty)
                throw new InvalidDataException("The persisted device identity is invalid; repair it explicitly rather than silently replacing it.");
            return id;
        }
        var newId = Guid.NewGuid();
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(new { deviceId = newId }));
            File.Move(temporary, path, overwrite: false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return newId;
    }
}
