using System.Diagnostics;
using Lantern.Application;
using Lantern.Configuration;
using Lantern.Models;
using Lantern.Transfers;
using Lantern.UI;

namespace Lantern;

public partial class MainWindow : Form
{
    private readonly string _dataDirectory;
    private readonly SettingsStore _settingsStore;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly Dictionary<Guid, Device> _knownDevices = new();
    private readonly Dictionary<Guid, Device> _manualDevices = new();
    private readonly Dictionary<Guid, TransferRow> _transfers = new();
    private readonly Dictionary<Guid, DecisionDialog> _incomingDialogs = new();
    private readonly Dictionary<Guid, TransferDetailsWindow> _detailWindows = new();
    private readonly List<TransferSourceFile> _selectedSources = new();
    private LanternSettings _settings = new();
    private LanternApplication? _application;
    private CancellationTokenSource? _serviceLifetime;
    private System.Windows.Forms.Timer? _refreshTimer;
    private NotifyIcon? _tray;
    private bool _restarting, _selecting, _closing, _canClose, _exitRequested, _fatalError;

    public MainWindow(string? settingsDirectory = null)
    {
        _dataDirectory = Path.GetFullPath(settingsDirectory ?? LanternApplication.DefaultDataDirectory);
        _settingsStore = new SettingsStore(Path.Combine(_dataDirectory, "settings.json"));
        InitializeComponent();
        Shown += async (_, _) => await InitializeAsync();
        FormClosing += OnClosing;
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnSystemPreferenceChanged;
        KeyDown += OnKeyDown;
        Resize += (_, _) => { if (WindowState == FormWindowState.Minimized && _settings.MinimizeToTray) Hide(); };
        UpdateActions();
    }

    internal LanternApplication? Backend => _application;
    internal void RequestExit() { _exitRequested = true; ShowMainWindow(); Close(); }

    private async Task InitializeAsync()
    {
        try { _settings = await Task.Run(_settingsStore.Load); }
        catch (Exception ex) when (IsUserError(ex)) { ShowError("Settings could not be loaded. Save corrected settings to replace them.", ex); }
        if (_closing) return;
        LoadSettingsFields(); CreateTray(); ApplyAppearance();
        await RestartServicesAsync();
        if (_closing) return;
        _refreshTimer = new System.Windows.Forms.Timer(components!) { Interval = 3000 };
        _refreshTimer.Tick += (_, _) => { RefreshDevices(); UpdateActions(); };
        _refreshTimer.Start();
    }

    private async Task StopApplicationAsync()
    {
        _serviceLifetime?.Cancel();
        if (_application is { } application)
        {
            _application = null;
            await application.DisposeAsync();
        }
        _serviceLifetime?.Dispose(); _serviceLifetime = null;
    }

    private async Task RestartServicesAsync()
    {
        if (_restarting || _closing) return;
        _restarting = true; _networkStatus.Text = "Restarting network services…";
        _retryButton.Enabled = false; _saveSettingsButton.Enabled = false; UpdateActions();
        await _lifecycle.WaitAsync();
        try
        {
            await StopApplicationAsync();
            if (_closing) return;
            var application = await Task.Run(() => LanternApplication.Open(_dataDirectory));
            _application = application;
            _serviceLifetime = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            application.DevicesChanged += (_, _) => Post(RefreshDevices);
            application.Transfers.TransferChanged += (_, args) => Post(() => OnTransferChanged(args));
            application.Transfers.IncomingTransferRequested += (_, request) => Post(() => _ = PresentIncomingAsync(request));
            application.Error += (_, error) => Post(() => { _networkStatus.Text = $"Last issue: {error.Message}"; Theme.SetRole(_networkStatus, ThemeRole.Danger); });
            application.NotificationRequested += (_, args) => Post(() => NotifyTransfer(args));
            await application.StartAsync(_lifetime.Token);
            if (_closing) return;
            _settings = application.Settings;
            _identityLabel.Text = $"{_settings.DeviceName}\r\nTCP {application.LocalDevice!.Port}";
            _localId.Text = $"This device: {application.DeviceId:D}";
            _fingerprint.Text = Ui.Fingerprint(application.Fingerprint);
            foreach (var entry in application.History.GetEntries())
            {
                if (_transfers.ContainsKey(entry.Id)) continue;
                var peerId = entry.Direction == TransferDirection.Sending ? entry.DestinationDeviceId : entry.SourceDeviceId;
                var row = TransferRow.From(entry, PeerName(peerId)); _transfers[row.Id] = row; RenderTransfer(row);
            }
            _networkStatus.Text = $"Ready • TCP {application.LocalDevice.Port} • {(_settings.DiscoveryEnabled ? "LAN discovery on" : "Discovery off — add devices by IP")}";
            Theme.SetRole(_networkStatus, ThemeRole.Muted); _retryButton.Visible = false;
            RefreshDevices();
        }
        catch (Exception ex) when (IsUserError(ex))
        {
            await StopApplicationAsync();
            _networkStatus.Text = $"Network unavailable: {ex.Message}";
            Theme.SetRole(_networkStatus, ThemeRole.Danger); _retryButton.Visible = true;
        }
        finally
        {
            _lifecycle.Release(); _restarting = false; _retryButton.Enabled = true; _saveSettingsButton.Enabled = true; UpdateActions();
        }
    }

    private Device? SelectedDevice => _devices.SelectedItems.Count > 0 ? _devices.SelectedItems[0].Tag as Device : null;
    private string PeerName(Guid id) => _knownDevices.TryGetValue(id, out var device) ? device.Name : id.ToString("D");
    private void RefreshDevices()
    {
        if (_closing) return;
        _knownDevices.Clear();
        foreach (var device in _manualDevices.Values) _knownDevices[device.Id] = device;
        if (_application is { } application) foreach (var device in application.GetDevices()) _knownDevices[device.Id] = device;
        RenderDevices();
    }

    private void RenderDevices()
    {
        if (_closing) return;
        var selectedId = SelectedDevice?.Id;
        var recipientId = (_recipient.SelectedItem as Device)?.Id;
        var search = _search.Text.Trim();
        var devices = _knownDevices.Values.OrderByDescending(d => d.Status == DeviceStatus.Online).ThenBy(d => d.Name).ToArray();
        var trust = _application?.Trust.GetTrustedPeers();
        var visible = devices.Where(d => d.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase) || d.IpAddress.ToString().Contains(search, StringComparison.OrdinalIgnoreCase)).ToArray();
        var visibleIds = visible.Select(d => d.Id.ToString()).ToHashSet();
        _devices.BeginUpdate();
        foreach (var item in _devices.Items.Cast<ListViewItem>().Where(i => !visibleIds.Contains(i.Name)).ToArray()) _devices.Items.Remove(item);
        foreach (var device in visible)
        {
            var manual = _manualDevices.ContainsKey(device.Id);
            var values = new[] { device.Name, manual ? "Manual" : device.Status.ToString(), trust?.ContainsKey(device.Id) == true ? "Paired locally" : "Pair first",
                $"{device.IpAddress}:{device.Port}", manual ? "Added by IP" : $"{Math.Max(0, (int)(DateTimeOffset.UtcNow - device.LastSeen).TotalSeconds)}s ago" };
            var item = _devices.Items[device.Id.ToString()];
            if (item is null)
            {
                item = new ListViewItem(values) { Name = device.Id.ToString(), Tag = device };
                _devices.Items.Add(item); item.Selected = selectedId == device.Id;
            }
            else { for (var i = 0; i < values.Length; i++) item.SubItems[i].Text = values[i]; item.Tag = device; }
        }
        _devices.EndUpdate();
        var online = devices.Where(d => d.Status == DeviceStatus.Online).ToArray();
        var existing = _recipient.Items.Cast<Device>().ToArray();
        if (!existing.Select(d => (d.Id, d.Name, d.IpAddress, d.Port)).SequenceEqual(online.Select(d => (d.Id, d.Name, d.IpAddress, d.Port))))
        {
            _recipient.BeginUpdate(); _recipient.Items.Clear();
            foreach (var device in online) _recipient.Items.Add(device);
            _recipient.SelectedItem = _recipient.Items.Cast<Device>().FirstOrDefault(d => d.Id == recipientId);
            if (_recipient.SelectedIndex < 0 && _recipient.Items.Count == 1) _recipient.SelectedIndex = 0;
            _recipient.EndUpdate();
        }
        _deviceHint.Text = devices.Length == 0 ? "No devices yet. Open Lantern on another PC or add a device by IP." :
            _devices.Items.Count == 0 ? "No devices match your search." : $"{devices.Count(d => d.Status == DeviceStatus.Online)} online • Pair both PCs before your first transfer.";
        UpdateActions();
    }

    private async Task ChooseDeviceAsync()
    {
        if (SelectedDevice is not { Status: DeviceStatus.Online } device) return;
        _recipient.SelectedItem = device; _tabs.SelectedIndex = 1;
        if (_selectedSources.Count == 0) await PickFilesAsync();
    }
    private void AddManualDevice()
    {
        using var dialog = new ManualDeviceDialog(); Theme.Apply(dialog, _settings.Theme);
        if (dialog.ShowDialog(this) == DialogResult.OK && dialog.Device is { } device) RegisterManualDevice(device);
    }
    internal void RegisterManualDevice(Device device)
    {
        if (device.Id == _application?.DeviceId) { MessageBox.Show(this, "Choose another PC's device ID.", "This is your device"); return; }
        _manualDevices[device.Id] = device; RefreshDevices(); _recipient.SelectedItem = _knownDevices.GetValueOrDefault(device.Id);
    }

    private async Task PickFilesAsync()
    {
        using var picker = new OpenFileDialog { Title = "Choose files to send", Multiselect = true, CheckFileExists = true, Filter = "All files (*.*)|*.*" };
        if (picker.ShowDialog(this) == DialogResult.OK) { await AddFilesAsync(picker.FileNames); _tabs.SelectedIndex = 1; }
    }
    private async Task PickFolderAsync()
    {
        using var picker = new FolderBrowserDialog { Description = "Choose a folder to send", UseDescriptionForTitle = true };
        if (picker.ShowDialog(this) == DialogResult.OK) { await AddFilesAsync([picker.SelectedPath]); _tabs.SelectedIndex = 1; }
    }
    internal async Task AddFilesAsync(IEnumerable<string> paths)
    {
        if (_selecting || _closing) return;
        _selecting = true; UpdateActions();
        try
        {
            var additions = await TransferSelection.FromPathsAsync(paths, _settings.ToTransferOptions(), _lifetime.Token);
            var combined = _selectedSources.Concat(additions).DistinctBy(f => f.SourcePath, StringComparer.OrdinalIgnoreCase).ToArray();
            if (combined.Length > _settings.MaximumFilesPerTransfer || combined.Sum(f => f.Metadata.SizeBytes) > _settings.MaximumTransferBytes)
                throw new InvalidDataException("The selection exceeds the configured file or size limit.");
            if (combined.Select(f => f.Metadata.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != combined.Length)
                throw new InvalidDataException("These selections contain conflicting relative names. Choose their parent folder instead.");
            _selectedSources.Clear(); _selectedSources.AddRange(combined); RenderFiles();
        }
        catch (Exception ex) when (IsUserError(ex)) { ShowError("The selection could not be added.", ex); }
        finally { _selecting = false; UpdateActions(); }
    }
    private void RenderFiles()
    {
        _files.BeginUpdate(); _files.Items.Clear();
        foreach (var file in _selectedSources) _files.Items.Add(new ListViewItem([file.Metadata.RelativePath, Ui.Size(file.Metadata.SizeBytes), Path.GetDirectoryName(file.SourcePath) ?? ""]) { Tag = file });
        _files.EndUpdate(); UpdateActions();
    }
    private void RemoveSelectedFile()
    {
        foreach (ListViewItem item in _files.SelectedItems) if (item.Tag is TransferSourceFile file) _selectedSources.Remove(file);
        RenderFiles();
    }
    private async Task SendFilesAsync()
    {
        if (_recipient.SelectedItem is not Device device || _application is not { } application || _selectedSources.Count == 0 || _selecting || _restarting) return;
        if (!application.Trust.GetTrustedPeers().ContainsKey(device.Id))
        {
            await PairDeviceAsync(device);
            if (!application.Trust.GetTrustedPeers().ContainsKey(device.Id)) return;
        }
        var files = _selectedSources.ToArray();
        try
        {
            var task = application.Transfers.SendAsync(device, files, _lifetime.Token);
            _selectedSources.Clear(); RenderFiles(); _tabs.SelectedIndex = 2;
            await task;
        }
        catch (Exception ex) when (IsUserError(ex)) { ShowError("Could not start the transfer. Check that both PCs are paired and online.", ex); }
    }

    private async Task PairDeviceAsync(Device? device)
    {
        if (_application is not { } application || _restarting) return;
        using var dialog = new PairingDialog(application.DeviceId, application.Fingerprint, device); Theme.Apply(dialog, _settings.Theme);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            await Task.Run(() => application.Trust.TrustPeer(dialog.PeerId, dialog.PeerFingerprint));
            _settingsHint.Text = "Identity trusted locally. Pair this PC's identity on the other PC too.";
            RenderDevices();
        }
        catch (Exception ex) when (IsUserError(ex)) { ShowError("Could not save the verified identity.", ex); }
    }

    private async Task PresentIncomingAsync(IncomingTransferRequestEventArgs request)
    {
        if (_closing || _serviceLifetime is null || _application is null) { request.Reject(); return; }
        var transfer = request.Transfer;
        if (_transfers.TryGetValue(transfer.Id, out var existing) && existing.IsFinished) { request.Reject(); return; }
        var details = $"Paired sender: {PeerName(transfer.SourceDeviceId)}\r\nDevice ID: {transfer.SourceDeviceId:D}\r\nFiles: {transfer.Files.Count}\r\nTotal: {Ui.Size(transfer.TotalBytes)}\r\n\r\n" +
            string.Join("\r\n", transfer.Files.Select(f => $"{f.RelativePath} — {Ui.Size(f.SizeBytes)}"));
        using var dialog = new DecisionDialog("Incoming files", "Accept this expected transfer? A verified batch is saved in a new folder. Existing files are never overwritten.", details, "Accept files", _settings.DownloadDirectory);
        Theme.Apply(dialog, _settings.Theme); _incomingDialogs[transfer.Id] = dialog;
        try
        {
            ShowMainWindow();
            if (await dialog.AskAsync(this, _serviceLifetime.Token))
            {
                var directory = dialog.DestinationDirectory;
                if (!Path.IsPathFullyQualified(directory)) throw new ArgumentException("Choose an absolute receive folder.");
                var accepted = await Task.Run(() => { Directory.CreateDirectory(directory); return request.Accept(directory); });
                if (!accepted && !_closing) _activityHint.Text = "The request expired or was canceled before it was accepted.";
            }
            else request.Reject();
        }
        catch (Exception ex) when (IsUserError(ex)) { request.Reject(); ShowError("The incoming transfer could not be accepted.", ex); }
        finally { _incomingDialogs.Remove(transfer.Id); }
    }

    private void OnTransferChanged(TransferChangedEventArgs args)
    {
        var transfer = args.Transfer;
        var peerId = transfer.Direction == TransferDirection.Sending ? transfer.DestinationDeviceId : transfer.SourceDeviceId;
        var row = TransferRow.From(args, PeerName(peerId));
        if (_transfers.TryGetValue(row.Id, out var previous) && row.SavedFolder is null) row = row with { SavedFolder = previous.SavedFolder };
        _transfers[row.Id] = row; RenderTransfer(row);
        if (_detailWindows.TryGetValue(row.Id, out var details)) details.UpdateTransfer(row);
        if (row.IsFinished)
        {
            if (_incomingDialogs.TryGetValue(row.Id, out var dialog)) dialog.Close();
            foreach (var old in _transfers.Values.Where(t => t.IsFinished).OrderByDescending(t => t.StartedAt).Skip(256).ToArray())
            { _transfers.Remove(old.Id); _activity.Items.RemoveByKey(old.Id.ToString()); }
        }
        UpdateActions();
    }
    private void RenderTransfer(TransferRow transfer)
    {
        var item = _activity.Items.Cast<ListViewItem>().FirstOrDefault(i => i.Name == transfer.Id.ToString());
        var values = new[] { transfer.Files, transfer.Peer, transfer.Direction.ToString(), Ui.Status(transfer.State),
            $"{transfer.Percent}% • {Ui.Size(transfer.BytesTransferred)} / {Ui.Size(transfer.TotalBytes)}", transfer.StartedAt.LocalDateTime.ToString("g") };
        if (item is null) { item = new ListViewItem(values) { Name = transfer.Id.ToString(), Tag = transfer }; _activity.Items.Insert(0, item); }
        else { for (var i = 0; i < values.Length; i++) item.SubItems[i].Text = values[i]; item.Tag = transfer; }
        item.ToolTipText = transfer.Error ?? transfer.Files;
        item.ForeColor = Theme.ItemForeground(item, Theme.PaletteFor(_activity));
    }
    private TransferRow? SelectedTransfer => _activity.SelectedItems.Count > 0 ? _activity.SelectedItems[0].Tag as TransferRow : null;
    private void CancelSelectedTransfer() { if (SelectedTransfer is { IsFinished: false } transfer) _application?.Transfers.Cancel(transfer.Id); }
    private void OpenSelectedFolder() { if (SelectedTransfer?.SavedFolder is { } folder) _ = OpenFolderAsync(folder); }
    private void ShowTransferDetails()
    {
        if (SelectedTransfer is not { } transfer) return;
        if (_detailWindows.TryGetValue(transfer.Id, out var existing)) { existing.Activate(); return; }
        var dialog = new TransferDetailsWindow(transfer, id => _application?.Transfers.Cancel(id));
        _detailWindows[transfer.Id] = dialog; Theme.Apply(dialog, _settings.Theme);
        dialog.FormClosed += (_, _) => { _detailWindows.Remove(transfer.Id); dialog.Dispose(); };
        dialog.Show(this);
    }
    private async Task ClearHistoryAsync()
    {
        if (_application is not { } application) return;
        try
        {
            var finished = _transfers.Values.Where(t => t.IsFinished).ToArray();
            await Task.Run(application.History.Clear);
            foreach (var transfer in finished)
            { _transfers.Remove(transfer.Id); _activity.Items.RemoveByKey(transfer.Id.ToString()); }
            UpdateActions();
        }
        catch (Exception ex) when (IsUserError(ex)) { ShowError("Could not clear history.", ex); }
    }
    private void UpdateActions()
    {
        _sendButton.Enabled = !_restarting && !_closing && !_selecting && _application is not null && _recipient.SelectedItem is Device && _selectedSources.Count > 0;
        _chooseDeviceButton.Enabled = SelectedDevice is { Status: DeviceStatus.Online };
        _removeFileButton.Enabled = !_selecting && _files.SelectedItems.Count > 0;
        _cancelButton.Enabled = SelectedTransfer is { IsFinished: false };
        _openTransferButton.Enabled = SelectedTransfer is { SavedFolder: not null };
        _transferProgress.Value = SelectedTransfer?.Percent ?? 0;
        _fileHint.Text = _selecting ? "Preparing selection…" : _selectedSources.Count == 0 ? "No files selected. Add files, add a folder, or drop them here." :
            $"{_selectedSources.Count} file(s) • {Ui.Size(_selectedSources.Sum(f => f.Metadata.SizeBytes))} • Pair both PCs before sending";
        var active = _transfers.Values.Count(t => !t.IsFinished);
        _activityHint.Text = _transfers.Count == 0 ? "Your transfers will appear here." : $"{active} active • {_transfers.Values.Count(t => t.State == TransferStatus.Completed)} completed • Select a row for details, speed and estimated time.";
    }

    private void LoadSettingsFields()
    {
        _deviceName.Text = _settings.DeviceName; _receiveFolder.Text = _settings.DownloadDirectory; _port.Value = _settings.TcpPort;
        _discoveryEnabled.Checked = _settings.DiscoveryEnabled; _notifications.Checked = _settings.ShowNotifications;
        _minimizeToTray.Checked = _settings.MinimizeToTray; _startWithWindows.Checked = _settings.StartWithWindows; _theme.SelectedItem = _settings.Theme;
    }
    private void BrowseReceiveFolder()
    {
        using var picker = new FolderBrowserDialog { Description = "Choose where to save received files", UseDescriptionForTitle = true, SelectedPath = _receiveFolder.Text };
        if (picker.ShowDialog(this) == DialogResult.OK) _receiveFolder.Text = picker.SelectedPath;
    }
    private async Task SaveSettingsAsync()
    {
        if (_restarting || _closing) return;
        try
        {
            var updated = _settings with { DeviceName = _deviceName.Text.Trim(), DownloadDirectory = _receiveFolder.Text.Trim(), TcpPort = (int)_port.Value,
                DiscoveryEnabled = _discoveryEnabled.Checked, ShowNotifications = _notifications.Checked, MinimizeToTray = _minimizeToTray.Checked,
                StartWithWindows = _startWithWindows.Checked, Theme = _theme.SelectedItem as string ?? "System" };
            updated.Validate();
            if (_transfers.Values.Any(t => !t.IsFinished) && MessageBox.Show(this, "Saving settings cancels active transfers and restarts network services. Continue?", "Apply settings", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            var previousStartup = _settings.StartWithWindows;
            await Task.Run(() =>
            {
                Directory.CreateDirectory(updated.DownloadDirectory);
                var probe = Path.Combine(updated.DownloadDirectory, $".lantern-write-test-{Guid.NewGuid():N}");
                using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
                if (updated.StartWithWindows != previousStartup)
                {
                    var executable = Path.Combine(AppContext.BaseDirectory, "Lantern.exe");
                    WindowsStartupRegistration.SetEnabled(updated.StartWithWindows, executable);
                    try { _settingsStore.Save(updated); }
                    catch { WindowsStartupRegistration.SetEnabled(previousStartup, executable); throw; }
                }
                else _settingsStore.Save(updated);
            });
            _settings = updated; ApplyAppearance();
            await RestartServicesAsync(); _settingsHint.Text = "Settings saved. Every incoming transfer requires your approval.";
        }
        catch (Exception ex) when (IsUserError(ex)) { ShowError("Settings could not be saved.", ex); }
    }

    private async Task ManageTrustAsync()
    {
        if (_application is not { } application) return;
        using var dialog = new Form { Text = "Paired devices", Font = Font, ClientSize = new Size(760, 430), StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false };
        var layout = Ui.Page(Ui.Auto(), Ui.Fill(), Ui.Auto());
        var hint = Ui.Label("Revoke a device to block future handshakes and cancel its active transfers."); layout.Controls.Add(hint, 0, 0);
        var list = Ui.List(("Device", 250), ("Verified fingerprint", 430));
        void Render()
        {
            list.Items.Clear(); foreach (var pin in application.Trust.GetTrustedPeers()) list.Items.Add(new ListViewItem([PeerName(pin.Key), Ui.Fingerprint(pin.Value)]) { Tag = pin.Key });
        }
        Render(); layout.Controls.Add(list, 0, 1);
        layout.Controls.Add(Ui.Toolbar(Ui.Button("Revoke selected", async (_, _) =>
        {
            if (list.SelectedItems.Count == 0 || list.SelectedItems[0].Tag is not Guid id) return;
            try
            {
                await Task.Run(() =>
                {
                    application.Trust.RemovePeer(id);
                    foreach (var transfer in application.Transfers.GetTransfers().Where(t => t.SourceDeviceId == id || t.DestinationDeviceId == id)) application.Transfers.Cancel(transfer.Id);
                });
                if (!dialog.IsDisposed) Render(); RenderDevices();
            }
            catch (Exception ex) when (IsUserError(ex)) { ShowError("Could not revoke the device.", ex); }
        }), Ui.Button("Close", (_, _) => dialog.Close())), 0, 2);
        dialog.Controls.Add(layout); Theme.Apply(dialog, _settings.Theme); dialog.ShowDialog(this); await Task.CompletedTask;
    }
    private async Task ShowDiagnosticsAsync()
    {
        if (_application is not { } application) { await OpenFolderAsync(_dataDirectory); return; }
        var data = application.GetDiagnostics();
        using var dialog = new Form { Text = "Lantern diagnostics", ClientSize = new Size(720, 450), Font = Font, StartPosition = FormStartPosition.CenterParent };
        var layout = Ui.Page(Ui.Fill(), Ui.Auto());
        layout.Controls.Add(new ThemedTextBox { ReadOnly = true, Multiline = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical,
            Text = $"Device: {data.DeviceId:D}\r\nListening port: {data.ListeningPort}\r\nDiscovery: {data.DiscoveryRunning}\r\nKnown devices: {data.KnownDevices}\r\nActive transfers: {data.ActiveTransfers}\r\nHistory: {data.HistoryEntries}\r\nCertificate expires: {data.CertificateExpiresAt.ToLocalTime():g}\r\n\r\nSHA-256: {Ui.Fingerprint(data.CertificateFingerprint)}\r\n\r\nLog: {data.LogPath}" }, 0, 0);
        layout.Controls.Add(Ui.Toolbar(Ui.Button("Open logs folder", async (_, _) => await OpenFolderAsync(Path.GetDirectoryName(data.LogPath)!)), Ui.Button("Close", (_, _) => dialog.Close())), 0, 1);
        dialog.Controls.Add(layout); Theme.Apply(dialog, _settings.Theme); dialog.ShowDialog(this);
    }
    private void CopyFingerprint()
    {
        try { if (_application is { } application) Clipboard.SetText($"Device ID: {application.DeviceId:D}\r\nSHA-256: {application.Fingerprint}"); }
        catch (System.Runtime.InteropServices.ExternalException ex) { ShowError("The clipboard is unavailable.", ex); }
    }
    private async Task OpenFolderAsync(string folder)
    {
        try { await Task.Run(() => { Directory.CreateDirectory(folder); Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true, Verb = "open" }); }); }
        catch (Exception ex) when (IsUserError(ex) || ex is System.ComponentModel.Win32Exception) { ShowError("Could not open the folder.", ex); }
    }

    private void ApplyAppearance()
    {
        Theme.Apply(this, _settings.Theme);
        foreach (var window in OwnedForms) Theme.Apply(window, _settings.Theme);
        if (_tray?.ContextMenuStrip is { } menu) Theme.Apply(menu, _settings.Theme);
        foreach (var transfer in _transfers.Values) RenderTransfer(transfer);
    }

    private void OnSystemPreferenceChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs args) => Post(ApplyAppearance);

    private void CreateTray()
    {
        var menu = new ContextMenuStrip(components!);
        menu.Items.Add("Open Lantern", null, (_, _) => ShowMainWindow());
        menu.Items.Add("Send files…", null, async (_, _) => { ShowMainWindow(); await PickFilesAsync(); });
        menu.Items.Add("Settings", null, (_, _) => { ShowMainWindow(); _tabs.SelectedIndex = 3; });
        menu.Items.Add(new ToolStripSeparator()); menu.Items.Add("Exit", null, (_, _) => RequestExit());
        _tray = new NotifyIcon(components!) { Icon = SystemIcons.Application, Text = "Lantern — share nearby", ContextMenuStrip = menu, Visible = true };
        _tray.DoubleClick += (_, _) => ShowMainWindow();
        _tray.BalloonTipClicked += (_, _) => { ShowMainWindow(); _tabs.SelectedIndex = 2; };
    }
    private void NotifyTransfer(TransferChangedEventArgs args)
    {
        if (_closing || !_settings.ShowNotifications || _tray is null) return;
        _tray.ShowBalloonTip(5000, $"Transfer {Ui.Status(args.Transfer.Status).ToLowerInvariant()}",
            $"{args.Transfer.Files.Count} file(s) • {Ui.Size(args.Transfer.TotalBytes)}", args.Transfer.Status == TransferStatus.Failed ? ToolTipIcon.Warning : ToolTipIcon.Info);
    }
    private void ShowMainWindow() { Show(); if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal; Activate(); }

    private async void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (_canClose) return;
        e.Cancel = true;
        if (_closing) return;
        if (e.CloseReason == CloseReason.UserClosing && _settings.MinimizeToTray && !_exitRequested) { Hide(); return; }
        if (!_fatalError && e.CloseReason != CloseReason.WindowsShutDown && _transfers.Values.Any(t => !t.IsFinished) && MessageBox.Show(this, "Exit Lantern and cancel active transfers?", "Exit Lantern", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) { _exitRequested = false; return; }
        _closing = true; _refreshTimer?.Stop(); _lifetime.Cancel(); Enabled = false;
        await _lifecycle.WaitAsync();
        try { await StopApplicationAsync(); }
        catch (Exception ex) when (IsUserError(ex)) { Trace.TraceError(ex.ToString()); }
        finally
        {
            _lifecycle.Release(); foreach (var dialog in OwnedForms) dialog.Close();
            if (_tray is not null) _tray.Visible = false; _canClose = true; Close();
        }
    }
    internal async Task HandleUnexpectedErrorAsync(Exception error)
    {
        if (_fatalError || _closing) return;
        _fatalError = true;
        try { await Task.Run(() => _application?.Log.Write("ui", error.ToString())); }
        catch (Exception loggingError) { Trace.TraceError(loggingError.ToString()); }
        MessageBox.Show(this, "An unexpected error occurred. Lantern will close safely. Diagnostic details are in the logs folder inside your Lantern application data.", "Lantern", MessageBoxButtons.OK, MessageBoxIcon.Error);
        RequestExit();
    }

    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.O) { e.SuppressKeyPress = true; await PickFilesAsync(); }
        else if (e.Control && e.KeyCode == Keys.Q) { e.SuppressKeyPress = true; RequestExit(); }
        else if (e.Control && e.KeyCode == Keys.Oemcomma) { e.SuppressKeyPress = true; _tabs.SelectedIndex = 3; }
        else if (e.KeyCode == Keys.F5) { e.SuppressKeyPress = true; RefreshDevices(); }
        else if (e.KeyCode == Keys.Delete && _files.Focused) { e.SuppressKeyPress = true; RemoveSelectedFile(); }
    }
    private void Post(Action action)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke((Action)(() => { if (!IsDisposed) action(); })); }
        catch (InvalidOperationException) when (_closing || IsDisposed) { }
    }
    private static bool IsUserError(Exception ex) => ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or
        System.Net.Sockets.SocketException or System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException or OperationCanceledException or System.Security.SecurityException;
    private void ShowError(string message, Exception ex) { if (!_closing) MessageBox.Show(this, $"{message}\r\n\r\n{ex.Message}", "Lantern", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
}
