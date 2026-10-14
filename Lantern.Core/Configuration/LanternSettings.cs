using Lantern.Networking.Discovery;
using Lantern.Transfers;

namespace Lantern.Configuration;

public sealed record LanternSettings
{
    public int SchemaVersion { get; init; } = 1;
    public string DeviceName { get; init; } = Environment.MachineName;
    public int TcpPort { get; init; } = 52846;
    public string DownloadDirectory { get; init; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads",
            "Lantern"
        );
    public int MaximumConcurrentTransfers { get; init; } = 2;
    public int MaximumFilesPerTransfer { get; init; } = 1024;
    public long MaximumTransferBytes { get; init; } = 100L * 1024 * 1024 * 1024;
    public TimeSpan DecisionTimeout { get; init; } = TimeSpan.FromMinutes(2);
    public TimeSpan NetworkTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public bool DiscoveryEnabled { get; init; } = true;
    public DiscoveryOptions Discovery { get; init; } = new();
    public bool ShowNotifications { get; init; } = true;
    public bool MinimizeToTray { get; init; }
    public bool StartWithWindows { get; init; }
    public string Theme { get; init; } = "System";

    public void Validate()
    {
        if (
            Theme is not ("System" or "Light" or "Dark")
            || SchemaVersion != 1
            || string.IsNullOrWhiteSpace(DeviceName)
            || DeviceName.Length > 128
            || DeviceName.Any(char.IsControl)
            || TcpPort is < 0 or > 65535
            || string.IsNullOrWhiteSpace(DownloadDirectory)
            || !Path.IsPathFullyQualified(DownloadDirectory)
            || Discovery is null
        )
            throw new ArgumentException("Invalid Lantern settings.");
        ToTransferOptions().Validate();
        Discovery.Validate();
    }

    public TransferOptions ToTransferOptions() =>
        new()
        {
            MaximumConcurrentTransfers = MaximumConcurrentTransfers,
            MaximumFilesPerTransfer = MaximumFilesPerTransfer,
            MaximumTransferBytes = MaximumTransferBytes,
            RequestTimeout = DecisionTimeout,
            InactivityTimeout = NetworkTimeout,
        };
}

public sealed class SettingsStore(string path)
{
    private readonly string _path = Path.GetFullPath(path);

    public LanternSettings Load()
    {
        var settings = File.Exists(_path)
            ? JsonFile.Read<LanternSettings>(_path)
                ?? throw new InvalidDataException("Invalid settings.")
            : new();
        settings.Validate();
        return settings;
    }

    public void Save(LanternSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        JsonFile.Write(_path, settings);
    }
}
