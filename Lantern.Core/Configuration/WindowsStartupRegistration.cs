using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Lantern.Configuration;

/// <summary>Per-user startup registration, invoked only when the user changes the corresponding preference.</summary>
[SupportedOSPlatform("windows")]
public static class WindowsStartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static void SetEnabled(bool enabled, string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        var path = Path.GetFullPath(executablePath);
        if (path.Contains('"') || (enabled && !File.Exists(path)))
            throw new ArgumentException(
                "A valid executable path is required.",
                nameof(executablePath)
            );
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (enabled)
            key.SetValue("Lantern", "\"" + path + "\"", RegistryValueKind.String);
        else
            key.DeleteValue("Lantern", throwOnMissingValue: false);
    }
}
