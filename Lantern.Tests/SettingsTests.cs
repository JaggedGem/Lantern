using Lantern.Models;
using Lantern.Settings;
using Lantern.Transfers;
using Xunit;

namespace Lantern.Tests;

public sealed class SettingsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Lantern-settings-" + Guid.NewGuid());

    [Fact]
    public void Settings_PersistNameFolderPortDiscoveryAndTrust()
    {
        var store = new SettingsStore(_directory);
        var settings = new AppSettings { DeviceName = "Office", ReceiveFolder = _directory, Port = 55000, DiscoveryEnabled = false, TrustedCertificates = new() { ["peer"] = new string('A', 64) } };
        store.Save(settings);
        var loaded = new SettingsStore(_directory).Load();
        Assert.Equal(settings.DeviceName, loaded.DeviceName); Assert.Equal(settings.ReceiveFolder, loaded.ReceiveFolder);
        Assert.Equal(settings.Port, loaded.Port); Assert.False(loaded.DiscoveryEnabled);
        Assert.Equal(settings.TrustedCertificates, loaded.TrustedCertificates);
    }

    [Fact]
    public void InvalidSettings_DoNotReplaceSavedSettings()
    {
        var store = new SettingsStore(_directory); store.Save(new AppSettings());
        Assert.Throws<ArgumentException>(() => store.Save(new AppSettings { Port = 0 }));
        Assert.Equal(52846, store.Load().Port);
        Assert.Throws<ArgumentException>(() => store.Save(new AppSettings { DeviceName = "bad\nname" }));
        Assert.Throws<ArgumentException>(() => store.Save(new AppSettings { ReceiveFolder = "relative" }));
    }

    [Fact]
    public void History_PersistsOnlyFinishedAndKeepsLast100()
    {
        var store = new TransferHistoryStore(_directory);
        var items = Enumerable.Range(0, 120).Select(i => new TransferInfo(Guid.NewGuid(), "Peer", $"file{i}", TransferDirection.Send,
            TransferState.Completed, 12, 12, DateTimeOffset.UtcNow)).ToArray();
        store.Save(items.Append(items[0] with { Id = Guid.NewGuid(), State = TransferState.Sending }));
        var loaded = store.Load();
        Assert.Equal(100, loaded.Count); Assert.Equal("file20", loaded[0].Files); Assert.Equal("file119", loaded[^1].Files);
        store.Save([]); Assert.Empty(store.Load());
    }

    [Fact]
    public void Certificate_IsStableAcrossRestarts()
    {
        using var first = TransferCertificate.LoadOrCreate(_directory);
        using var second = TransferCertificate.LoadOrCreate(_directory);
        Assert.Equal(TransferCertificate.Fingerprint(first), TransferCertificate.Fingerprint(second));
        Assert.True(second.HasPrivateKey);
    }

    [Fact]
    public void CorruptedIdentity_IsReportedInsteadOfSilentlyChangingDevice()
    {
        Directory.CreateDirectory(_directory); File.WriteAllText(Path.Combine(_directory, "device-id.json"), "{\"deviceId\":\"00000000-0000-0000-0000-000000000000\"}");
        Assert.Throws<InvalidDataException>(() => LocalDeviceIdentityProvider.LoadOrCreateDeviceId(_directory));
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
