using System.Text.Json;

namespace Lantern.Diagnostics;

/// <summary>Small rotating JSON-lines log. Callers supply operational events, never keys or file contents.</summary>
public sealed class DiagnosticLog(string path)
{
    private const long MaximumBytes = 1024 * 1024;
    private readonly object _sync = new();
    public string LogPath { get; } = Path.GetFullPath(path);

    public void Write(string component, string message)
    {
        lock (_sync)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            if (File.Exists(LogPath) && new FileInfo(LogPath).Length >= MaximumBytes)
            {
                if (File.Exists(LogPath + ".1"))
                    File.Move(LogPath + ".1", LogPath + ".2", true);
                File.Move(LogPath, LogPath + ".1", true);
            }
            File.AppendAllText(
                LogPath,
                JsonSerializer.Serialize(
                    new
                    {
                        timestamp = DateTimeOffset.UtcNow,
                        component,
                        message,
                    }
                ) + Environment.NewLine
            );
        }
    }
}

public sealed record ApplicationDiagnostics(
    Guid DeviceId,
    int? ListeningPort,
    bool DiscoveryRunning,
    int KnownDevices,
    int ActiveTransfers,
    int HistoryEntries,
    string CertificateFingerprint,
    DateTime CertificateExpiresAt,
    string LogPath
);
