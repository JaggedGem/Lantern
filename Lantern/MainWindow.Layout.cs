using Lantern.UI;

namespace Lantern;

public partial class MainWindow
{
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill, Padding = new Point(22, 12), DrawMode = TabDrawMode.OwnerDrawFixed, AccessibleName = "Main navigation" };
    private readonly Label _identityLabel = Ui.Label(Ui.Text("Starting_Lantern"));
    private readonly Label _networkStatus = Ui.Label(Ui.Text("Starting_network_services"));
    private readonly Label _deviceHint = Ui.Label(Ui.Text("Looking_for_Lantern_devices_on_your_local_network"));
    private readonly TextBox _search = new() { Width = 270, PlaceholderText = "Search devices", AccessibleName = "Search nearby devices", Margin = new Padding(0, 4, 14, 4) };
    private readonly ListView _devices = Ui.List(("Device", 290), ("Status", 115), ("Pairing", 145), ("Connection", 170), ("Last seen", 130));
    private readonly ListView _files = Ui.List(("File", 340), ("Size", 110), ("Location", 480));
    private readonly ComboBox _recipient = new() { Width = 350, DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = "Name", AccessibleName = "Recipient", Margin = new Padding(0, 3, 14, 0) };
    private readonly Label _fileHint = Ui.Label(Ui.Text("No_files_selected_Add_files_or_drop_them_here"));
    private readonly ListView _activity = Ui.List(("Files", 250), ("Device", 160), ("Direction", 85), ("Status", 135), ("Progress", 175), ("Started", 140));
    private readonly ProgressBar _transferProgress = new() { Dock = DockStyle.Fill, Height = 14, AccessibleName = "Selected transfer progress", Margin = new Padding(0, 0, 0, 10) };
    private readonly Label _activityHint = Ui.Label(Ui.Text("Your_transfers_will_appear_here"));
    private readonly TextBox _deviceName = new() { Dock = DockStyle.Fill, MaxLength = 128, AccessibleName = "This device's name" };
    private readonly TextBox _receiveFolder = new() { Dock = DockStyle.Fill, AccessibleName = "Receive folder" };
    private readonly NumericUpDown _port = new() { Minimum = 0, Maximum = 65535, Width = 130, AccessibleName = "TCP listening port" };
    private readonly CheckBox _notifications = new() { Text = "Show transfer notifications", AutoSize = true };
    private readonly CheckBox _minimizeToTray = new() { Text = "Keep running in tray when closed or minimized", AutoSize = true };
    private readonly CheckBox _startWithWindows = new() { Text = "Start Lantern when I sign in to Windows", AutoSize = true };
    private readonly ComboBox _theme = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160, AccessibleName = "Theme" };
    private readonly Label _localId = Ui.Label(Ui.Text("Loading_installation_identity"));
    private readonly CheckBox _discoveryEnabled = new() { Text = "Find and announce devices on the local network", AutoSize = true, Checked = true, AccessibleName = "Enable LAN discovery" };
    private readonly TextBox _fingerprint = new() { ReadOnly = true, Multiline = true, Height = 70, Dock = DockStyle.Fill, AccessibleName = "This device's certificate fingerprint" };
    private readonly Label _settingsHint = Ui.Label(Ui.Text("Every_incoming_transfer_requires_your_approval"));
    private Button _sendButton = null!, _removeFileButton = null!, _chooseDeviceButton = null!, _cancelButton = null!, _openTransferButton = null!, _retryButton = null!, _saveSettingsButton = null!;

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(20), BackColor = BackColor };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(Ui.Auto()); root.RowStyles.Add(Ui.Fill()); root.RowStyles.Add(Ui.Auto());
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true, Padding = new Padding(4, 0, 4, 12) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        var brand = Ui.Label(Ui.Text("Lantern"), true); brand.Font = new Font("Segoe UI", 26, FontStyle.Bold);
        header.Controls.Add(brand, 0, 0);
        header.Controls.Add(Ui.Label(Ui.Text("A_simple_way_to_share_files_with_devices_nearby")), 0, 1);
        _identityLabel.TextAlign = ContentAlignment.MiddleRight;
        header.Controls.Add(_identityLabel, 1, 0); header.SetRowSpan(_identityLabel, 2);
        root.Controls.Add(header, 0, 0); root.Controls.Add(_tabs, 0, 1);
        var footer = Ui.Toolbar(_networkStatus, Ui.Button(Ui.Text("Retry_connection"), async (_, _) => await RestartServicesAsync()));
        _retryButton = (Button)footer.Controls[1]; _retryButton.Visible = false;
        _networkStatus.MaximumSize = new Size(720, 0);
        _networkStatus.AccessibleName = "Network status";
        _identityLabel.AccessibleName = "Local device identity";
        _networkStatus.Margin = new Padding(0, 10, 18, 0);
        root.Controls.Add(footer, 0, 2);
        Controls.Add(root);
        BuildNearbyPage(); BuildSendPage(); BuildActivityPage(); BuildSettingsPage();
        _tabs.DrawItem += (_, e) =>
        {
            var selected = e.Index == _tabs.SelectedIndex;
            using var brush = new SolidBrush(selected ? Ui.Accent : _tabs.BackColor);
            e.Graphics.FillRectangle(brush, e.Bounds);
            TextRenderer.DrawText(e.Graphics, _tabs.TabPages[e.Index].Text, Font, e.Bounds,
                selected ? Color.White : _tabs.ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        };
        _search.TextChanged += (_, _) => RenderDevices();
        _devices.SelectedIndexChanged += (_, _) => UpdateActions();
        _devices.DoubleClick += async (_, _) => await ChooseDeviceAsync();
        _files.SelectedIndexChanged += (_, _) => UpdateActions();
        _recipient.FormattingEnabled = true;
        _recipient.Format += (_, e) => { if (e.ListItem is Models.Device device) e.Value = $"{device.Name} ({device.IpAddress})"; };
        _recipient.SelectedIndexChanged += (_, _) => UpdateActions();
        _activity.SelectedIndexChanged += (_, _) => UpdateActions();
        _activity.DoubleClick += (_, _) => ShowTransferDetails();
        EnableFileDrop(this);
    }

    private TabPage AddPage(string title, TableLayoutPanel layout, bool scroll = false)
    {
        var page = new TabPage(title) { BackColor = Color.White, Padding = new Padding(0) };
        if (scroll) { page.AutoScroll = true; layout.Dock = DockStyle.Top; layout.AutoSize = true; layout.AutoSizeMode = AutoSizeMode.GrowAndShrink; }
        page.Controls.Add(layout); _tabs.TabPages.Add(page); return page;
    }

    private void BuildNearbyPage()
    {
        var page = Ui.Page(Ui.Auto(), Ui.Auto(), Ui.Auto(), Ui.Fill(), Ui.Auto());
        page.Controls.Add(Ui.Label(Ui.Text("Devices_nearby"), true), 0, 0);
        page.Controls.Add(Ui.Label(Ui.Text("Open_Lantern_on_another_PC_connected_to_the_same_local_network")), 0, 1);
        _chooseDeviceButton = Ui.Button(Ui.Text("Send_to_selected"), async (_, _) => await ChooseDeviceAsync(), true);
        page.Controls.Add(Ui.Toolbar(_search, _chooseDeviceButton,
            Ui.Button(Ui.Text("Refresh"), (_, _) => RefreshDevices()),
            Ui.Button(Ui.Text("Pair_device"), async (_, _) => await PairDeviceAsync(SelectedDevice)), Ui.Button(Ui.Text("Add_by_IP"), (_, _) => AddManualDevice())), 0, 2);
        page.Controls.Add(_devices, 0, 3); page.Controls.Add(_deviceHint, 0, 4);
        _devices.AccessibleName = "Nearby devices";
        AddPage(Ui.Text("Nearby_devices"), page);
    }

    private void BuildSendPage()
    {
        var page = Ui.Page(Ui.Auto(), Ui.Auto(), Ui.Auto(), Ui.Fill(), Ui.Auto(), Ui.Auto());
        page.Controls.Add(Ui.Label(Ui.Text("Send_files"), true), 0, 0);
        page.Controls.Add(Ui.Label(Ui.Text("Choose_a_recipient_add_your_files_then_send_The_recipient_approves_each_tra")), 0, 1);
        _removeFileButton = Ui.Button(Ui.Text("Remove_selected"), (_, _) => RemoveSelectedFile());
        page.Controls.Add(Ui.Toolbar(Ui.Button(Ui.Text("Add_files"), async (_, _) => await PickFilesAsync(), true), Ui.Button(Ui.Text("Add_folder"), async (_, _) => await PickFolderAsync()), _removeFileButton,
            Ui.Button(Ui.Text("Clear_files"), (_, _) => { _selectedSources.Clear(); RenderFiles(); })), 0, 2);
        page.Controls.Add(_files, 0, 3); page.Controls.Add(_fileHint, 0, 4);
        _sendButton = Ui.Button(Ui.Text("Send_files_2"), async (_, _) => await SendFilesAsync(), true);
        page.Controls.Add(Ui.Toolbar(new Label { Text = "To", AutoSize = true, Margin = new Padding(0, 9, 14, 0) }, _recipient, _sendButton, Ui.Button(Ui.Text("Pair_recipient"), async (_, _) => await PairDeviceAsync(_recipient.SelectedItem as Models.Device))), 0, 5);
        _files.AccessibleName = "Files to send";
        AddPage(Ui.Text("Send_files"), page);
    }

    private void BuildActivityPage()
    {
        var page = Ui.Page(Ui.Auto(), Ui.Auto(), Ui.Fill(), Ui.Auto(), Ui.Auto(), Ui.Auto());
        page.Controls.Add(Ui.Label(Ui.Text("Transfer_activity"), true), 0, 0);
        page.Controls.Add(Ui.Label(Ui.Text("Track_current_transfers_and_your_last_256_finished_transfers_Double_click_a")), 0, 1);
        page.Controls.Add(_activity, 0, 2); page.Controls.Add(_transferProgress, 0, 3); page.Controls.Add(_activityHint, 0, 4);
        _cancelButton = Ui.Button(Ui.Text("Cancel_transfer"), (_, _) => CancelSelectedTransfer());
        _openTransferButton = Ui.Button(Ui.Text("Open_received_folder"), (_, _) => OpenSelectedFolder());
        page.Controls.Add(Ui.Toolbar(_cancelButton, _openTransferButton,
            Ui.Button(Ui.Text("Details"), (_, _) => ShowTransferDetails()),
            Ui.Button(Ui.Text("Clear_finished"), async (_, _) => await ClearHistoryAsync())), 0, 5);
        _activity.AccessibleName = "Transfer history and progress";
        AddPage(Ui.Text("Activity"), page);
    }

    private void BuildSettingsPage()
    {
        var page = Ui.Page(Ui.Auto(), Ui.Auto(), Ui.Auto(), Ui.Auto(), Ui.Auto(), Ui.Auto(), Ui.Fill(), Ui.Auto(), Ui.Auto());
        page.Controls.Add(Ui.Label(Ui.Text("Settings_security"), true), 0, 0);
        page.Controls.Add(Ui.Label(Ui.Text("Changes_apply_when_you_save_Network_services_restart_with_the_new_settings")), 0, 1);
        var form = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, RowCount = 8, Padding = new Padding(0, 0, 0, 16) };
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160)); form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); form.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        for (var row = 0; row < 8; row++) form.RowStyles.Add(Ui.Auto());
        void Field(string label, Control input, int row)
        { form.Controls.Add(Ui.Label(label), 0, row); form.Controls.Add(input, 1, row); input.Margin = new Padding(0, 0, 12, 16); }
        Field("Device name", _deviceName, 0); Field("Receive folder", _receiveFolder, 1);
        form.Controls.Add(Ui.Button(Ui.Text("Browse"), (_, _) => BrowseReceiveFolder()), 2, 1);
        Field("TCP port", _port, 2); form.Controls.Add(_discoveryEnabled, 1, 3);
        form.Controls.Add(_notifications, 1, 4); form.Controls.Add(_minimizeToTray, 1, 5); form.Controls.Add(_startWithWindows, 1, 6);
        _theme.Items.AddRange(["System", "Light", "Dark"]); Field("Theme", _theme, 7);
        page.Controls.Add(form, 0, 2);
        page.Controls.Add(_localId, 0, 3);
        page.Controls.Add(Ui.Label(Ui.Text("Pair_both_PCs_using_their_device_IDs_and_verified_fingerprints_File_transfe")), 0, 4);
        page.Controls.Add(_fingerprint, 0, 5);
        var help = new TextBox { ReadOnly = true, Multiline = true, BorderStyle = BorderStyle.None, Dock = DockStyle.Fill, BackColor = Color.White,
            ScrollBars = ScrollBars.Vertical, AccessibleName = "Security and connection help",
            Text = "Only paired identities can transfer files. Accept only an expected transfer; discovery names are self-reported. Received files are never opened automatically.\r\n\r\nDevices missing? Check that both PCs use the same subnet and that Windows Firewall allows Lantern on private networks (UDP 52845 and your TCP port). You can also add a device by its IP address.\r\n\r\nLantern • Local file sharing • .NET 10 / Windows" };
        help.Margin = new Padding(0, 20, 0, 14); page.Controls.Add(help, 0, 6);
        _saveSettingsButton = Ui.Button(Ui.Text("Save_settings"), async (_, _) => await SaveSettingsAsync(), true);
        page.Controls.Add(Ui.Toolbar(_saveSettingsButton,
            Ui.Button(Ui.Text("Open_receive_folder"), async (_, _) => await OpenFolderAsync(_settings.DownloadDirectory)),
            Ui.Button(Ui.Text("Copy_fingerprint"), (_, _) => CopyFingerprint()),
            Ui.Button(Ui.Text("Paired_devices"), async (_, _) => await ManageTrustAsync()), Ui.Button(Ui.Text("Diagnostics"), async (_, _) => await ShowDiagnosticsAsync())), 0, 7);
        page.Controls.Add(_settingsHint, 0, 8);
        page.RowStyles[6].SizeType = SizeType.Absolute; page.RowStyles[6].Height = 150;
        AddPage(Ui.Text("Settings"), page, scroll: true);
    }

    private void EnableFileDrop(Control control)
    {
        control.AllowDrop = true;
        control.DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        control.DragDrop += async (_, e) => { if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths) { await AddFilesAsync(paths); _tabs.SelectedIndex = 1; } };
        foreach (Control child in control.Controls) EnableFileDrop(child);
    }
}
