using System.Text.Json;

namespace Lantern.Settings;

public sealed record AppSettings
{
    public string DeviceName { get; init; } = Environment.MachineName;
    public string ReceiveFolder { get; init; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Lantern");
    public int Port { get; init; } = 52846;
    public bool DiscoveryEnabled { get; init; } = true;
    public Dictionary<string, string> TrustedCertificates { get; init; } = new();

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(DeviceName) || DeviceName.Length > 80 || DeviceName.Any(char.IsControl))
            throw new ArgumentException("Choose a device name of 1–80 characters without control characters.");
        if (Port is < 1024 or > 65535)
            throw new ArgumentException("Choose a TCP port between 1024 and 65535.");
        if (string.IsNullOrWhiteSpace(ReceiveFolder) || !Path.IsPathFullyQualified(ReceiveFolder))
            throw new ArgumentException("Choose an absolute path for received files.");
        if (TrustedCertificates is null)
            throw new ArgumentException("The trusted device list is invalid.");
    }
}

public sealed class SettingsStore(string? directory = null)
{
    public string DirectoryPath { get; } = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lantern");

    public AppSettings Load()
    {
        var path = Path.Combine(DirectoryPath, "settings.json");
        if (!File.Exists(path)) return new AppSettings();
        var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? throw new InvalidDataException("Settings are empty.");
        settings.Validate();
        return settings;
    }

    public void Save(AppSettings settings)
    {
        settings.Validate();
        Directory.CreateDirectory(DirectoryPath);
        var path = Path.Combine(DirectoryPath, "settings.json");
        var temporary = path + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
