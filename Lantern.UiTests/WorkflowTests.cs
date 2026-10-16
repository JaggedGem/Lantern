using System.Net;
using Lantern.Application;
using Lantern.Configuration;
using Lantern.Models;
using Lantern.Transfers;
using Lantern.UI;
using Xunit;

namespace Lantern.UiTests;

public sealed class WorkflowTests
{
    [Fact]
    public Task SettingsSave_RestartsServicesAndPersistsEdits() => RunAsync(async (form, directory) =>
    {
        var tabs = Find<TabControl>(form, "Main navigation"); Assert.Equal(4, tabs.TabPages.Count);
        Assert.False(Find<Button>(form, "Send files").Enabled); tabs.SelectedIndex = 3;
        Find<TextBox>(form, "This device's name").Text = "Renamed PC";
        Find<ComboBox>(form, "Theme").SelectedItem = "Dark";
        Find<Button>(form, "Save settings").PerformClick();
        await UntilAsync(() => form.Backend?.Settings.DeviceName == "Renamed PC" && Find<Label>(form, "Network status").Text.StartsWith("Ready"));
        var settings = new SettingsStore(Path.Combine(directory, "settings.json")).Load();
        Assert.Equal("Renamed PC", settings.DeviceName); Assert.Equal("Dark", settings.Theme);
        Assert.Contains("Renamed PC", Find<Label>(form, "Local device identity").Text);
        Assert.True(form.BackColor.GetBrightness() < 0.5);
    });

    [Fact]
    public Task SendWorkflow_PairsPeerSendsNestedFolderAndUpdatesHistory() => RunAsync(async (form, directory) =>
    {
        var receive = Path.Combine(directory, "peer-receive"); Directory.CreateDirectory(receive);
        await using var peer = new LanternApplication("Peer PC", identityPath: Path.Combine(directory, "peer", "device-id.json"), settings: new LanternSettings { DeviceName = "Peer PC", TcpPort = 0, DiscoveryEnabled = false });
        peer.Transfers.IncomingTransferRequested += (_, request) => request.Accept(receive);
        await peer.StartAsync();
        var device = DeviceFor(peer); form.RegisterManualDevice(device);
        var backend = form.Backend!; peer.Trust.TrustPeer(backend.DeviceId, backend.Fingerprint);
        var folder = Path.Combine(directory, "Folder", "nested"); Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "hello.txt"), "UI transfer test");
        await form.AddFilesAsync([Path.GetDirectoryName(folder)!]);
        Assert.Single(Find<ListView>(form, "Files to send").Items.Cast<ListViewItem>());
        Find<TabControl>(form, "Main navigation").SelectedIndex = 1;
        var pairing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        form.BeginInvoke((Action)(async () =>
        {
            try
            {
                await UntilAsync(() => form.OwnedForms.OfType<PairingDialog>().Any());
                var dialog = form.OwnedForms.OfType<PairingDialog>().Single();
                Assert.Equal(peer.DeviceId.ToString("D"), Find<TextBox>(dialog, "Peer device ID").Text);
                Find<TextBox>(dialog, "Verified peer fingerprint").Text = peer.Fingerprint;
                Find<Button>(dialog, "Trust verified identity").PerformClick(); pairing.TrySetResult();
            }
            catch (Exception ex) { pairing.TrySetException(ex); }
        }));
        Find<Button>(form, "Pair recipient…").PerformClick(); await pairing.Task;
        await UntilAsync(() => backend.Trust.GetTrustedPeers().ContainsKey(peer.DeviceId));
        Find<Button>(form, "Send files").PerformClick();
        var activity = Find<ListView>(form, "Transfer history and progress");
        await UntilAsync(() => activity.Items.Cast<ListViewItem>().Any(i => i.SubItems[3].Text == "Completed"));
        Assert.Equal("UI transfer test", await File.ReadAllTextAsync(Directory.GetFiles(receive, "hello.txt", SearchOption.AllDirectories).Single()));
        Assert.Empty(Find<ListView>(form, "Files to send").Items.Cast<ListViewItem>());
        Assert.Equal(peer.Fingerprint, backend.Trust.GetTrustedPeers()[peer.DeviceId]);
        Assert.Single(backend.History.GetEntries());
    });

    [Fact]
    public Task IncomingWorkflow_AcceptsChosenDestinationAndRejectsNextRequest() => RunAsync(async (form, directory) =>
    {
        await using var peer = new LanternApplication("Sender PC", identityPath: Path.Combine(directory, "peer", "device-id.json"), settings: new LanternSettings { DeviceName = "Sender PC", TcpPort = 0, DiscoveryEnabled = false });
        await peer.StartAsync();
        var backend = form.Backend!;
        peer.Trust.TrustPeer(backend.DeviceId, backend.Fingerprint); backend.Trust.TrustPeer(peer.DeviceId, peer.Fingerprint);
        form.RegisterManualDevice(DeviceFor(peer));
        var source = Path.Combine(directory, "incoming.txt"); await File.WriteAllTextAsync(source, "expected incoming file");
        var files = await TransferSelection.FromPathsAsync([source], peer.Settings.ToTransferOptions());
        var task = peer.Transfers.SendAsync(DeviceFor(backend), files);
        await UntilAsync(() => form.OwnedForms.OfType<DecisionDialog>().Any());
        var dialog = form.OwnedForms.OfType<DecisionDialog>().Single();
        Assert.Contains("incoming.txt", Find<TextBox>(dialog, "Request details").Text);
        var destination = Path.Combine(directory, "chosen-destination"); Find<TextBox>(dialog, "Receive destination").Text = destination;
        Find<Button>(dialog, "Accept files").PerformClick();
        Assert.Equal(destination, dialog.DestinationDirectory);
        Assert.Equal(TransferStatus.Completed, (await task).Status);
        Assert.Equal("expected incoming file", await File.ReadAllTextAsync(Directory.GetFiles(destination, "incoming.txt", SearchOption.AllDirectories).Single()));
        var again = peer.Transfers.SendAsync(DeviceFor(backend), files);
        await UntilAsync(() => form.OwnedForms.OfType<DecisionDialog>().Any());
        Find<Button>(form.OwnedForms.OfType<DecisionDialog>().Single(), "Decline").PerformClick();
        Assert.Equal(TransferStatus.Rejected, (await again).Status);
    });

    private static Device DeviceFor(LanternApplication application)
    {
        var device = new Device(application.DeviceId, application.Settings.DeviceName, IPAddress.Loopback, application.LocalDevice!.Port);
        device.UpdateStatus(DeviceStatus.Online); return device;
    }
    private static async Task UntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!condition()) await Task.Delay(25, timeout.Token);
    }
    private static Task RunAsync(Func<MainWindow, string, Task> scenario)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "Lantern-ui-" + Guid.NewGuid()); Exception? error = null;
            try
            {
                System.Windows.Forms.Application.SetHighDpiMode(HighDpiMode.PerMonitorV2); System.Windows.Forms.Application.EnableVisualStyles();
                new SettingsStore(Path.Combine(directory, "settings.json")).Save(new LanternSettings { DeviceName = "Test PC", DownloadDirectory = Path.Combine(directory, "receive"), TcpPort = 0, DiscoveryEnabled = false, ShowNotifications = false });
                using var form = new MainWindow(directory);
                form.Shown += async (_, _) =>
                {
                    try { await UntilAsync(() => Find<Label>(form, "Network status").Text.StartsWith("Ready")); await scenario(form, directory); }
                    catch (Exception ex) { error = ex; }
                    finally
                    {
                        if (form.Backend is { } backend) foreach (var transfer in backend.Transfers.GetTransfers()) backend.Transfers.Cancel(transfer.Id);
                        foreach (var dialog in form.OwnedForms) dialog.Close();
                        await Task.Delay(100); form.RequestExit();
                    }
                };
                System.Windows.Forms.Application.Run(form);
            }
            catch (Exception ex) { error = ex; }
            finally
            {
                try { if (Directory.Exists(directory)) Directory.Delete(directory, true); } catch (IOException) { }
                if (error is null) completion.TrySetResult(); else completion.TrySetException(error);
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task.WaitAsync(TimeSpan.FromSeconds(45));
    }
    private static T Find<T>(Control root, string accessibleName) where T : Control => Descendants(root).OfType<T>().Single(control => control.AccessibleName == accessibleName);
    private static IEnumerable<Control> Descendants(Control root)
    { foreach (Control child in root.Controls) { yield return child; foreach (var nested in Descendants(child)) yield return nested; } }
}
