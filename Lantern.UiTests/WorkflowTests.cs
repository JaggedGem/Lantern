using System.Net;
using System.Net.Sockets;
using Lantern.Models;
using Lantern.Settings;
using Lantern.Transfers;
using Lantern.UI;
using Xunit;

namespace Lantern.UiTests;

public sealed class WorkflowTests
{
    [Fact]
    public Task SettingsSave_RestartsServicesAndPersistsEdits() => RunAsync(async (form, directory) =>
    {
        var tabs = Find<TabControl>(form, "Main navigation");
        Assert.Equal(4, tabs.TabPages.Count);
        Assert.False(Find<Button>(form, "Send files").Enabled);
        tabs.SelectedIndex = 3;
        var name = Find<TextBox>(form, "This device's name"); name.Text = "Renamed PC";
        var port = Find<NumericUpDown>(form, "TCP listening port"); port.Value = FreePort();
        Find<Button>(form, "Save settings").PerformClick();
        await UntilAsync(() => new SettingsStore(directory).Load().DeviceName == "Renamed PC" && Find<Label>(form, "Network status").Text.StartsWith("Ready"));
        Assert.Equal("Renamed PC", new SettingsStore(directory).Load().DeviceName);
        Assert.Contains("Renamed PC", Find<Label>(form, "Local device identity").Text);
    });

    [Fact]
    public Task SendWorkflow_VerifiesPeerTransfersFilesAndUpdatesHistory() => RunAsync(async (form, directory) =>
    {
        var receive = Path.Combine(directory, "peer-receive");
        using var certificate = TransferCertificate.LoadOrCreate(Path.Combine(directory, "peer-identity"));
        await using var peer = new FileSender(new LocalDevice(Guid.NewGuid(), "Peer PC", FreePort()), receive, certificate);
        peer.TransferRequested += (_, request) => request.AcceptTransfer();
        await peer.StartAsync();
        var device = new Device(Guid.NewGuid(), "Peer PC", IPAddress.Loopback, peer.ListeningPort!.Value); device.UpdateStatus(DeviceStatus.Online);
        form.RegisterManualDevice(device);
        var path = Path.Combine(directory, "hello.txt"); await File.WriteAllTextAsync(path, "UI transfer test");
        form.AddFiles([path, path]);
        Assert.Single(Find<ListView>(form, "Files to send").Items.Cast<ListViewItem>());
        Find<TabControl>(form, "Main navigation").SelectedIndex = 1;
        var send = Find<Button>(form, "Send files"); Assert.True(send.Enabled); send.PerformClick();
        await UntilAsync(() => form.OwnedForms.OfType<DecisionDialog>().Any());
        var verification = form.OwnedForms.OfType<DecisionDialog>().Single();
        Assert.Contains(Ui.Fingerprint(peer.Fingerprint), Find<TextBox>(verification, "Request details").Text);
        Find<Button>(verification, "Trust and send").PerformClick();
        var activity = Find<ListView>(form, "Transfer history and progress");
        await UntilAsync(() => activity.Items.Cast<ListViewItem>().Any(i => i.SubItems[3].Text == "Completed"));
        Assert.Equal("UI transfer test", await File.ReadAllTextAsync(Directory.GetFiles(receive, "hello.txt", SearchOption.AllDirectories).Single()));
        Assert.Empty(Find<ListView>(form, "Files to send").Items.Cast<ListViewItem>());
        Assert.Equal(peer.Fingerprint, new SettingsStore(directory).Load().TrustedCertificates[device.Id.ToString("D")]);
        Assert.Single(new TransferHistoryStore(directory).Load());
    });

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
            var directory = Path.Combine(Path.GetTempPath(), "Lantern-ui-" + Guid.NewGuid());
            Exception? error = null;
            try
            {
                Application.SetHighDpiMode(HighDpiMode.PerMonitorV2); Application.EnableVisualStyles();
                new SettingsStore(directory).Save(new AppSettings { DeviceName = "Test PC", ReceiveFolder = Path.Combine(directory, "receive"), Port = FreePort(), DiscoveryEnabled = false });
                using var form = new MainWindow(directory);
                form.Shown += async (_, _) =>
                {
                    try
                    {
                        await UntilAsync(() => Find<Label>(form, "Network status").Text.StartsWith("Ready"));
                        await scenario(form, directory);
                    }
                    catch (Exception ex) { error = ex; }
                    finally { form.Close(); }
                };
                Application.Run(form);
            }
            catch (Exception ex) { error = ex; }
            finally
            {
                try { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
                catch (IOException) { }
                if (error is null) completion.TrySetResult(); else completion.TrySetException(error);
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(45));
    }

    private static T Find<T>(Control root, string accessibleName) where T : Control =>
        Descendants(root).OfType<T>().Single(control => control.AccessibleName == accessibleName);
    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls) { yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
    private static int FreePort() { using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); return ((IPEndPoint)listener.LocalEndpoint).Port; }
}
