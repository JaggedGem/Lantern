using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using Lantern.Models;
using Lantern.Networking.Discovery;
using Lantern.Settings;
using Lantern.Transfers;
using Lantern.UI;

namespace Lantern;

public partial class MainWindow : Form
{
    private readonly SettingsStore _settingsStore;
    private readonly TransferHistoryStore _historyStore;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<Guid, Device> _knownDevices = new();
    private readonly HashSet<Guid> _manualDevices = new();
    private readonly Dictionary<Guid, TransferInfo> _transfers = new();
    private readonly Dictionary<Guid, DecisionDialog> _incomingDialogs = new();
    private AppSettings _settings = new();
    private DeviceDiscovery? _discovery;
    private FileSender? _sender;
    private X509Certificate2? _certificate;
    private CancellationTokenSource? _serviceLifetime;
    private System.Windows.Forms.Timer? _refreshTimer;
    private Guid _deviceId;
    private bool _restarting, _closing, _canClose;

    public MainWindow(string? settingsDirectory = null)
    {
        _settingsStore = new SettingsStore(settingsDirectory);
        _historyStore = new TransferHistoryStore(_settingsStore.DirectoryPath);
        InitializeComponent();
        Shown += async (_, _) => await InitializeAsync();
        FormClosing += OnClosing;
        KeyDown += OnKeyDown;
        UpdateActions();
    }

    private async Task InitializeAsync()
    {
        try { _settings = _settingsStore.Load(); }
        catch (Exception ex) when (IsUserError(ex)) { ShowError("Settings could not be loaded. Defaults are shown; save settings to replace them.", ex); }
        try { foreach (var transfer in _historyStore.Load()) { _transfers[transfer.Id] = transfer; RenderTransfer(transfer); } }
        catch (Exception ex) when (IsUserError(ex)) { ShowError("Transfer history could not be loaded.", ex); }
        LoadSettingsFields();
        try
        {
            _deviceId = await Task.Run(() => LocalDeviceIdentityProvider.LoadOrCreateDeviceId());
            if (_closing) return;
            var certificate = await Task.Run(() => TransferCertificate.LoadOrCreate(_settingsStore.DirectoryPath));
            if (_closing) { certificate.Dispose(); return; }
            _certificate = certificate;
            _fingerprint.Text = Ui.Fingerprint(TransferCertificate.Fingerprint(_certificate));
            await RestartServicesAsync();
        }
        catch (Exception ex) when (IsUserError(ex)) { SetNetworkFailure(ex); }
        if (_closing) return;
        _refreshTimer = new System.Windows.Forms.Timer(components!) { Interval = 3000 };
        _refreshTimer.Tick += (_, _) => { RenderDevices(); UpdateActions(); };
        _refreshTimer.Start();
    }

    private async Task StopServicesAsync()
    {
        _serviceLifetime?.Cancel();
        if (_discovery is not null)
        {
            var discovery = _discovery; _discovery = null;
            await discovery.DisposeAsync();
        }
        if (_sender is not null)
        {
            var sender = _sender; _sender = null;
            await sender.DisposeAsync();
        }
        _serviceLifetime?.Dispose(); _serviceLifetime = null;
    }

    private async Task RestartServicesAsync()
    {
        if (_restarting || _closing) return;
        _restarting = true; _networkStatus.Text = "Restarting network services…"; _retryButton.Enabled = false; _saveSettingsButton.Enabled = false; UpdateActions();
        try
        {
            await StopServicesAsync();
            foreach (var id in _knownDevices.Keys.Where(id => !_manualDevices.Contains(id)).ToArray()) _knownDevices.Remove(id);
            _certificate ??= await Task.Run(() => TransferCertificate.LoadOrCreate(_settingsStore.DirectoryPath));
            _deviceId = _deviceId == Guid.Empty ? LocalDeviceIdentityProvider.LoadOrCreateDeviceId() : _deviceId;
            _fingerprint.Text = Ui.Fingerprint(TransferCertificate.Fingerprint(_certificate));
            _serviceLifetime = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            var local = new LocalDevice(_deviceId, _settings.DeviceName, _settings.Port);
            _sender = new FileSender(local, _settings.ReceiveFolder, _certificate);
            _sender.TransferChanged += OnTransferChanged;
            _sender.TransferRequested += OnTransferRequested;
            await _sender.StartAsync(_lifetime.Token);
            if (_settings.DiscoveryEnabled)
            {
                _discovery = new DeviceDiscovery(local);
                _discovery.DeviceDiscovered += (_, e) => Post(() => { _knownDevices[e.Device.Id] = e.Device; RenderDevices(); });
                _discovery.DeviceStatusChanged += (_, e) => Post(() => { _knownDevices[e.Device.Id] = e.Device; RenderDevices(); });
                _discovery.DeviceRemoved += (_, e) => Post(() => { if (!_manualDevices.Contains(e.DeviceId)) _knownDevices.Remove(e.DeviceId); RenderDevices(); });
                try { await _discovery.StartAsync(_lifetime.Token); }
                catch (Exception ex) when (IsUserError(ex))
                {
                    await _discovery.DisposeAsync(); _discovery = null;
                    _networkStatus.Text = $"Transfers ready on TCP {_settings.Port}. Discovery failed: {ex.Message}";
                    _retryButton.Visible = true; RenderDevices(); return;
                }
            }
            _identityLabel.Text = $"{_settings.DeviceName}\r\nTCP {_settings.Port}";
            _networkStatus.Text = $"Ready • TCP {_settings.Port} • {(_settings.DiscoveryEnabled ? "LAN discovery on" : "Discovery off — add devices by IP")}";
            _networkStatus.ForeColor = Ui.Muted; _retryButton.Visible = false;
            RenderDevices();
        }
        catch (Exception ex) when (IsUserError(ex))
        {
            await StopServicesAsync(); SetNetworkFailure(ex);
        }
        finally { _restarting = false; _retryButton.Enabled = true; _saveSettingsButton.Enabled = true; UpdateActions(); }
    }

    private void SetNetworkFailure(Exception ex)
    {
        _networkStatus.Text = $"Network unavailable: {ex.Message}";
        _networkStatus.ForeColor = Color.Firebrick; _retryButton.Visible = true;
    }

    private async Task RefreshDiscoveryAsync()
    {
        if (_restarting) return;
        if (_discovery is null) { await RestartServicesAsync(); return; }
        try
        {
            await _discovery.RefreshAsync(_lifetime.Token);
            RenderDevices();
            _deviceHint.Text = "Searching again. Peers announce themselves every 3 seconds.";
        }
        catch (Exception ex) when (IsUserError(ex)) { SetNetworkFailure(ex); }
    }

    private void RenderDevices()
    {
        if (_closing) return;
        var selected = _devices.SelectedItems.Count > 0 ? _devices.SelectedItems[0].Tag as Device : null;
        var recipientId = (_recipient.SelectedItem as Device)?.Id;
        var search = _search.Text.Trim();
        var devices = _knownDevices.Values.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        _devices.BeginUpdate(); _devices.Items.Clear();
        foreach (var device in devices.Where(d => d.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase) || d.IpAddress.ToString().Contains(search, StringComparison.OrdinalIgnoreCase)))
        {
            var manual = _manualDevices.Contains(device.Id);
            var item = new ListViewItem([device.Name, $"{device.IpAddress}:{device.Port}", manual ? "Manual • verify" : device.Status.ToString(),
                manual ? "Added by IP" : $"{Math.Max(0, (int)(DateTimeOffset.UtcNow - device.LastSeen).TotalSeconds)}s ago"]) { Tag = device };
            _devices.Items.Add(item); item.Selected = selected?.Id == device.Id;
        }
        _devices.EndUpdate();
        _recipient.BeginUpdate(); _recipient.Items.Clear();
        foreach (var device in devices.Where(d => d.Status == DeviceStatus.Online)) _recipient.Items.Add(device);
        _recipient.SelectedItem = _recipient.Items.Cast<Device>().FirstOrDefault(d => d.Id == recipientId);
        if (_recipient.SelectedIndex < 0 && _recipient.Items.Count == 1) _recipient.SelectedIndex = 0;
        _recipient.EndUpdate();
        _deviceHint.Text = devices.Length == 0 ? "No devices yet. Open Lantern on another PC or add a device by IP." :
            _devices.Items.Count == 0 ? "No devices match your search." : $"{devices.Length} device(s) available. Manual devices are checked when you connect.";
        UpdateActions();
    }

    private void ChooseDevice()
    {
        if (_devices.SelectedItems.Count == 0 || _devices.SelectedItems[0].Tag is not Device device) return;
        if (device.Status != DeviceStatus.Online) return;
        _recipient.SelectedItem = device; _tabs.SelectedIndex = 1;
        if (_files.Items.Count == 0) PickFiles();
    }

    private void AddManualDevice()
    {
        using var dialog = new ManualDeviceDialog();
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Device is not { } device) return;
        RegisterManualDevice(device);
    }

    internal void RegisterManualDevice(Device device)
    {
        _knownDevices[device.Id] = device; _manualDevices.Add(device.Id); RenderDevices(); _recipient.SelectedItem = device;
    }

    private void PickFiles()
    {
        using var picker = new OpenFileDialog { Title = "Choose files to send", Multiselect = true, CheckFileExists = true, Filter = "All files (*.*)|*.*" };
        if (picker.ShowDialog(this) == DialogResult.OK) { AddFiles(picker.FileNames); _tabs.SelectedIndex = 1; }
    }

    internal void AddFiles(IEnumerable<string> paths)
    {
        var errors = new List<string>();
        foreach (var path in paths)
        {
            try
            {
                var file = new FileInfo(path);
                if (!file.Exists) throw new IOException("Only existing files can be sent; folders are not supported.");
                if (_files.Items.Cast<ListViewItem>().Any(i => string.Equals((i.Tag as SelectedFile)?.Path, file.FullName, StringComparison.OrdinalIgnoreCase))) continue;
                if (_files.Items.Count >= 100) throw new IOException("A transfer supports up to 100 files.");
                if (_files.Items.Cast<ListViewItem>().Any(i => string.Equals(i.Text, file.Name, StringComparison.OrdinalIgnoreCase)))
                    throw new IOException("Files in one transfer must have different names.");
                var selected = new SelectedFile(file.FullName, file.Length);
                _files.Items.Add(new ListViewItem([file.Name, Ui.Size(file.Length), file.DirectoryName ?? ""]) { Tag = selected });
            }
            catch (Exception ex) when (IsUserError(ex)) { errors.Add($"{Path.GetFileName(path)}: {ex.Message}"); }
        }
        if (errors.Count > 0) MessageBox.Show(this, string.Join(Environment.NewLine, errors.Take(8)), "Some files could not be added", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        UpdateActions();
    }

    private void RemoveSelectedFile()
    { foreach (ListViewItem item in _files.SelectedItems) _files.Items.Remove(item); UpdateActions(); }

    private async Task SendFilesAsync()
    {
        if (_recipient.SelectedItem is not Device device || _sender is null || _restarting || _files.Items.Count == 0) return;
        var paths = _files.Items.Cast<ListViewItem>().Select(i => ((SelectedFile)i.Tag!).Path).ToArray();
        var sender = _sender;
        try
        {
            var task = sender.SendAsync(device, paths, (fingerprint, token) => OnUiAsync(() => VerifyDeviceAsync(device, fingerprint, token)), _lifetime.Token);
            _files.Items.Clear(); _tabs.SelectedIndex = 2; UpdateActions();
            await task;
        }
        catch (Exception ex) when (IsUserError(ex)) { ShowError("Could not start the transfer.", ex); }
    }

    private async Task<bool> VerifyDeviceAsync(Device device, string fingerprint, CancellationToken token)
    {
        if (_closing || token.IsCancellationRequested) return false;
        var key = device.Id.ToString("D");
        if (_settings.TrustedCertificates.TryGetValue(key, out var known) && string.Equals(known, fingerprint, StringComparison.Ordinal)) return true;
        var changed = known is not null;
        using var dialog = new DecisionDialog(changed ? "Device identity changed" : "Verify recipient",
            changed ? "This device has a different certificate. Verify the new fingerprint on the other PC before continuing." :
                "Compare this fingerprint with Settings on the receiving PC. Trust it only if every group matches.",
            $"Recipient: {device.Name}\r\nAddress: {device.IpAddress}:{device.Port}\r\n\r\nSHA-256 fingerprint:\r\n{Ui.Fingerprint(fingerprint)}\r\n\r\nChoosing Trust and send remembers this certificate for future transfers.", "Trust and send");
        if (!await dialog.AskAsync(this, token) || token.IsCancellationRequested) return false;
        var trust = new Dictionary<string, string>(_settings.TrustedCertificates) { [key] = fingerprint };
        var updated = _settings with { TrustedCertificates = trust };
        try { _settingsStore.Save(updated); _settings = updated; }
        catch (Exception ex) when (IsUserError(ex)) { ShowError("The trusted device could not be saved. Verify it again next time.", ex); }
        return true;
    }

    private void OnTransferRequested(object? sender, TransferRequest request)
    {
        Post(async () =>
        {
            if (_closing || _serviceLifetime is null) { request.RejectTransfer(); return; }
            var details = $"Sender: {request.SenderName} (self-reported)\r\nAddress: {request.Address}\r\nTotal: {Ui.Size(request.Files.Sum(f => f.FileSize))}\r\n\r\n" +
                string.Join("\r\n", request.Files.Select(f => $"{f.FileName} — {Ui.Size(f.FileSize)}")) + $"\r\n\r\nSave to: {_settings.ReceiveFolder}\r\nA separate folder is created for this batch. Existing files are never overwritten.";
            using var dialog = new DecisionDialog("Incoming files", "Accept only an expected transfer from someone you know. Check the sender and IP address.", details, "Accept files");
            _incomingDialogs[request.TransferId] = dialog;
            try
            {
                if (await dialog.AskAsync(this, _serviceLifetime.Token)) request.AcceptTransfer();
                else request.RejectTransfer();
            }
            finally { _incomingDialogs.Remove(request.TransferId); }
        });
    }

    private void OnTransferChanged(object? sender, TransferInfo transfer) => Post(() =>
    {
        _transfers[transfer.Id] = transfer; RenderTransfer(transfer);
        if (transfer.IsFinished)
        {
            if (_incomingDialogs.TryGetValue(transfer.Id, out var dialog)) dialog.Close();
            TrimHistory(); PersistHistory();
        }
        UpdateActions();
    });

    private void RenderTransfer(TransferInfo transfer)
    {
        var item = _activity.Items.Cast<ListViewItem>().FirstOrDefault(i => i.Name == transfer.Id.ToString());
        var values = new[] { transfer.Files, transfer.Peer, transfer.Direction.ToString(), StateLabel(transfer.State),
            $"{transfer.Percent}% • {Ui.Size(transfer.BytesTransferred)} / {Ui.Size(transfer.TotalBytes)}", transfer.StartedAt.LocalDateTime.ToString("g") };
        if (item is null)
        {
            item = new ListViewItem(values) { Name = transfer.Id.ToString(), Tag = transfer };
            _activity.Items.Insert(0, item);
        }
        else { for (var i = 0; i < values.Length; i++) item.SubItems[i].Text = values[i]; item.Tag = transfer; }
        item.ToolTipText = transfer.Error ?? transfer.Files;
        item.ForeColor = transfer.State == TransferState.Failed ? Color.Firebrick : transfer.State == TransferState.Completed ? Color.FromArgb(30, 117, 77) : Ui.Ink;
    }

    private static string StateLabel(TransferState state) => state == TransferState.AwaitingApproval ? "Awaiting approval" : state.ToString();
    private TransferInfo? SelectedTransfer => _activity.SelectedItems.Count > 0 ? _activity.SelectedItems[0].Tag as TransferInfo : null;
    private void CancelSelectedTransfer() { if (SelectedTransfer is { IsFinished: false } transfer) _sender?.Cancel(transfer.Id); }
    private void OpenSelectedFolder() { if (SelectedTransfer is { SavedFolder: { } folder }) OpenFolder(folder); }
    private void ShowTransferDetails()
    {
        if (SelectedTransfer is not { } transfer) return;
        MessageBox.Show(this, $"{transfer.Files}\r\n\r\n{transfer.Direction}: {transfer.Peer}\r\nStatus: {StateLabel(transfer.State)}\r\nProgress: {Ui.Size(transfer.BytesTransferred)} / {Ui.Size(transfer.TotalBytes)}\r\nStarted: {transfer.StartedAt.LocalDateTime:g}" +
            (transfer.Error is null ? "" : $"\r\n\r\n{transfer.Error}") + (transfer.SavedFolder is null ? "" : $"\r\n\r\nSaved to: {transfer.SavedFolder}"), "Transfer details", MessageBoxButtons.OK,
            transfer.State == TransferState.Failed ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
    }

    private void TrimHistory()
    {
        foreach (var transfer in _transfers.Values.Where(t => t.IsFinished).OrderByDescending(t => t.StartedAt).Skip(100).ToArray())
        { _transfers.Remove(transfer.Id); _activity.Items.RemoveByKey(transfer.Id.ToString()); }
    }
    private void PersistHistory()
    {
        try { _historyStore.Save(_transfers.Values.OrderBy(t => t.StartedAt)); }
        catch (Exception ex) when (IsUserError(ex)) { _activityHint.Text = $"History could not be saved: {ex.Message}"; }
    }
    private void ClearHistory()
    {
        foreach (var transfer in _transfers.Values.Where(t => t.IsFinished).ToArray())
        { _transfers.Remove(transfer.Id); _activity.Items.RemoveByKey(transfer.Id.ToString()); }
        PersistHistory(); UpdateActions();
    }

    private void UpdateActions()
    {
        _sendButton.Enabled = !_restarting && !_closing && _sender is not null && _recipient.SelectedItem is Device && _files.Items.Count > 0;
        _chooseDeviceButton.Enabled = _devices.SelectedItems.Count > 0 && _devices.SelectedItems[0].Tag is Device { Status: DeviceStatus.Online };
        _removeFileButton.Enabled = _files.SelectedItems.Count > 0;
        _cancelButton.Enabled = SelectedTransfer is { IsFinished: false };
        _openTransferButton.Enabled = SelectedTransfer is { IsFinished: true, SavedFolder: not null };
        var total = _files.Items.Cast<ListViewItem>().Sum(i => ((SelectedFile)i.Tag!).Size);
        _fileHint.Text = _files.Items.Count == 0 ? "No files selected. Add files or drop them here." : $"{_files.Items.Count} file(s) • {Ui.Size(total)} • Up to 100 files per transfer";
        _transferProgress.Value = SelectedTransfer?.Percent ?? 0;
        var active = _transfers.Values.Count(t => !t.IsFinished);
        _activityHint.Text = _transfers.Count == 0 ? "Your transfers will appear here." : $"{active} active • {_transfers.Values.Count(t => t.State == TransferState.Completed)} completed • Select a row to cancel or view details.";
    }

    private void LoadSettingsFields()
    {
        _deviceName.Text = _settings.DeviceName; _receiveFolder.Text = _settings.ReceiveFolder;
        _port.Value = _settings.Port; _discoveryEnabled.Checked = _settings.DiscoveryEnabled;
    }
    private void BrowseReceiveFolder()
    {
        using var picker = new FolderBrowserDialog { Description = "Choose where to save received files", UseDescriptionForTitle = true, SelectedPath = _receiveFolder.Text };
        if (picker.ShowDialog(this) == DialogResult.OK) _receiveFolder.Text = picker.SelectedPath;
    }
    private async Task SaveSettingsAsync()
    {
        if (_restarting) return;
        try
        {
            var updated = _settings with { DeviceName = _deviceName.Text.Trim(), ReceiveFolder = _receiveFolder.Text.Trim(), Port = (int)_port.Value, DiscoveryEnabled = _discoveryEnabled.Checked };
            updated.Validate();
            if (_transfers.Values.Any(t => !t.IsFinished) && MessageBox.Show(this, "Saving settings cancels active transfers and restarts network services. Continue?", "Apply settings", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            Directory.CreateDirectory(updated.ReceiveFolder);
            var probe = Path.Combine(updated.ReceiveFolder, $".lantern-write-test-{Guid.NewGuid():N}");
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
            _settingsStore.Save(updated); _settings = updated;
            await RestartServicesAsync();
            _settingsHint.Text = "Settings saved. Every incoming transfer still requires your approval.";
        }
        catch (Exception ex) when (IsUserError(ex)) { ShowError("Settings could not be saved.", ex); }
    }
    private void ForgetTrustedDevices()
    {
        if (MessageBox.Show(this, "Forget all trusted certificates? You will need to verify each recipient again before sending.", "Forget trusted devices", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        try { var updated = _settings with { TrustedCertificates = new() }; _settingsStore.Save(updated); _settings = updated; _settingsHint.Text = "Trusted devices cleared."; }
        catch (Exception ex) when (IsUserError(ex)) { ShowError("Could not clear trusted devices.", ex); }
    }
    private void CopyFingerprint()
    {
        try { if (_certificate is not null) Clipboard.SetText(TransferCertificate.Fingerprint(_certificate)); }
        catch (System.Runtime.InteropServices.ExternalException ex) { ShowError("The clipboard is unavailable.", ex); }
    }
    private void OpenFolder(string folder)
    {
        try { Directory.CreateDirectory(folder); Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true, Verb = "open" }); }
        catch (Exception ex) when (IsUserError(ex) || ex is System.ComponentModel.Win32Exception) { ShowError("Could not open the folder.", ex); }
    }

    private async void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (_canClose) return;
        e.Cancel = true;
        if (_closing) return;
        if (_transfers.Values.Any(t => !t.IsFinished) && MessageBox.Show(this, "Exit Lantern and cancel active transfers?", "Exit Lantern", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        _closing = true; _refreshTimer?.Stop(); _lifetime.Cancel(); Enabled = false;
        try { await StopServicesAsync(); }
        finally
        {
            foreach (var dialog in OwnedForms) dialog.Close();
            _certificate?.Dispose(); PersistHistory(); _canClose = true; Close();
        }
    }
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.O) { e.SuppressKeyPress = true; PickFiles(); }
        else if (e.Control && e.KeyCode == Keys.Oemcomma) { e.SuppressKeyPress = true; _tabs.SelectedIndex = 3; }
        else if (e.KeyCode == Keys.F5) { e.SuppressKeyPress = true; _ = RefreshDiscoveryAsync(); }
        else if (e.KeyCode == Keys.Delete && _files.Focused) { e.SuppressKeyPress = true; RemoveSelectedFile(); }
    }
    private void Post(Action action)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke((Action)(() => { if (!IsDisposed) action(); })); }
        catch (InvalidOperationException) when (_closing || IsDisposed) { }
    }
    private Task<T> OnUiAsync<T>(Func<Task<T>> action)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (_closing || IsDisposed) { completion.SetCanceled(); return completion.Task; }
        Post(async () => { try { completion.TrySetResult(await action()); } catch (Exception ex) { completion.TrySetException(ex); } });
        return completion.Task;
    }
    private static bool IsUserError(Exception ex) => ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or
        System.Net.Sockets.SocketException or System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException or OperationCanceledException;
    private void ShowError(string message, Exception ex) { if (!_closing) MessageBox.Show(this, $"{message}\r\n\r\n{ex.Message}", "Lantern", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    private sealed record SelectedFile(string Path, long Size);
}
