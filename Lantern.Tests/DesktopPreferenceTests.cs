using Lantern.Configuration;
using Xunit;

namespace Lantern.Tests;

public sealed class DesktopPreferenceTests
{
    [Fact]
    public void OlderPreferences_DefaultToSystemTheme()
    {
        var settings = System.Text.Json.JsonSerializer.Deserialize<LanternSettings>("{\"DeviceName\":\"Office\"}")!;
        Assert.Equal("System", settings.Theme); settings.Validate();
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    [InlineData("System")]
    public void DesktopPreferences_SurviveRestart(string theme)
    {
        var directory = Path.Combine(Path.GetTempPath(), "Lantern-desktop-" + Guid.NewGuid());
        try
        {
            var store = new SettingsStore(Path.Combine(directory, "settings.json"));
            store.Save(new LanternSettings { Theme = theme, MinimizeToTray = true, ShowNotifications = false, StartWithWindows = true });
            var restored = store.Load(); Assert.Equal(theme, restored.Theme);
            Assert.True(restored.MinimizeToTray); Assert.True(restored.StartWithWindows); Assert.False(restored.ShowNotifications);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void UnsupportedTheme_IsReportedInsteadOfOverwritingPreferences()
    {
        Assert.Throws<ArgumentException>(() => new LanternSettings { Theme = "Unknown" }.Validate());
    }
}
