using Lantern.UI;

namespace Lantern;

public partial class MainWindow
{
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill, Padding = new Point(22, 12), AccessibleName = "Main navigation" };
    private readonly Label _identityLabel = Ui.Label("Starting Lantern…");
    private readonly Label _networkStatus = Ui.Label("Starting network services…");
    private readonly Label _deviceHint = Ui.Label("Looking for Lantern devices on your local network…");
    private readonly TextBox _search = new() { Width = 270, PlaceholderText = "Search devices", AccessibleName = "Search nearby devices", Margin = new Padding(0, 4, 14, 4) };
    private readonly ListView _devices = Ui.List(("Device", 250), ("Address", 230), ("Status", 140), ("Last seen", 180));
    private readonly ListView _files = Ui.List(("File", 340), ("Size", 110), ("Location", 480));
    private readonly ComboBox _recipient = new() { Width = 350, DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = "Name", AccessibleName = "Recipient", Margin = new Padding(0, 3, 14, 0) };
    private readonly Label _fileHint = Ui.Label("No files selected. Add files or drop them here.");
    private readonly ListView _activity = Ui.List(("Files", 250), ("Device", 160), ("Direction", 85), ("Status", 135), ("Progress", 175), ("Started", 140));
    private readonly ProgressBar _transferProgress = new() { Dock = DockStyle.Fill, Height = 14, AccessibleName = "Selected transfer progress", Margin = new Padding(0, 0, 0, 10) };
    private readonly Label _activityHint = Ui.Label("Your transfers will appear here.");
    private readonly TextBox _deviceName = new() { Dock = DockStyle.Fill, MaxLength = 80, AccessibleName = "This device's name" };
    private readonly TextBox _receiveFolder = new() { Dock = DockStyle.Fill, AccessibleName = "Receive folder" };
    private readonly NumericUpDown _port = new() { Minimum = 1024, Maximum = 65535, Width = 130, AccessibleName = "TCP listening port" };
    private readonly CheckBox _discoveryEnabled = new() { Text = "Find and announce devices on the local network", AutoSize = true, Checked = true, AccessibleName = "Enable LAN discovery" };
    private readonly TextBox _fingerprint = new() { ReadOnly = true, Multiline = true, Height = 70, Dock = DockStyle.Fill, AccessibleName = "This device's certificate fingerprint" };
    private readonly Label _settingsHint = Ui.Label("Every incoming transfer requires your approval.");
    private Button _sendButton = null!, _removeFileButton = null!, _chooseDeviceButton = null!, _cancelButton = null!, _openTransferButton = null!, _retryButton = null!, _saveSettingsButton = null!;

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(20), BackColor = BackColor };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(Ui.Auto()); root.RowStyles.Add(Ui.Fill()); root.RowStyles.Add(Ui.Auto());
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true, Padding = new Padding(4, 0, 4, 12) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        var brand = Ui.Label("Lantern", true); brand.Font = new Font("Segoe UI", 26, FontStyle.Bold);
        header.Controls.Add(brand, 0, 0);
        header.Controls.Add(Ui.Label("A simple way to share files with devices nearby."), 0, 1);
        _identityLabel.TextAlign = ContentAlignment.MiddleRight;
        header.Controls.Add(_identityLabel, 1, 0); header.SetRowSpan(_identityLabel, 2);
        root.Controls.Add(header, 0, 0); root.Controls.Add(_tabs, 0, 1);
        var footer = Ui.Toolbar(_networkStatus, Ui.Button("&Retry connection", async (_, _) => await RestartServicesAsync()));
        _retryButton = (Button)footer.Controls[1]; _retryButton.Visible = false;
        _networkStatus.AccessibleName = "Network status";
        _identityLabel.AccessibleName = "Local device identity";
        _networkStatus.Margin = new Padding(0, 10, 18, 0);
        root.Controls.Add(footer, 0, 2);
        Controls.Add(root);
        BuildNearbyPage(); BuildSendPage(); BuildActivityPage(); BuildSettingsPage();
        _search.TextChanged += (_, _) => RenderDevices();
        _devices.SelectedIndexChanged += (_, _) => UpdateActions();
        _devices.DoubleClick += (_, _) => ChooseDevice();
        _files.SelectedIndexChanged += (_, _) => UpdateActions();
        _recipient.FormattingEnabled = true;
        _recipient.Format += (_, e) => { if (e.ListItem is Models.Device device) e.Value = $"{device.Name} ({device.IpAddress})"; };
        _recipient.SelectedIndexChanged += (_, _) => UpdateActions();
        _activity.SelectedIndexChanged += (_, _) => UpdateActions();
        _activity.DoubleClick += (_, _) => ShowTransferDetails();
        EnableFileDrop(this);
    }

    private TabPage AddPage(string title, TableLayoutPanel layout)
    {
        var page = new TabPage(title) { BackColor = Color.White, Padding = new Padding(0) };
        page.Controls.Add(layout); _tabs.TabPages.Add(page); return page;
    }

    private void BuildNearbyPage()
    {
        var page = Ui.Page(Ui.Auto(), Ui.Auto(), Ui.Auto(), Ui.Fill(), Ui.Auto());
        page.Controls.Add(Ui.Label("Devices nearby", true), 0, 0);
        page.Controls.Add(Ui.Label("Open Lantern on another PC connected to the same local network."), 0, 1);
        _chooseDeviceButton = Ui.Button("&Send to selected", (_, _) => ChooseDevice(), true);
        page.Controls.Add(Ui.Toolbar(_search, _chooseDeviceButton,
            Ui.Button("&Refresh", async (_, _) => await RefreshDiscoveryAsync()),
            Ui.Button("Add by &IP…", (_, _) => AddManualDevice())), 0, 2);
        page.Controls.Add(_devices, 0, 3); page.Controls.Add(_deviceHint, 0, 4);
        _devices.AccessibleName = "Nearby devices";
        AddPage("Nearby devices", page);
    }

    private void BuildSendPage()
    {
        var page = Ui.Page(Ui.Auto(), Ui.Auto(), Ui.Auto(), Ui.Fill(), Ui.Auto(), Ui.Auto());
        page.Controls.Add(Ui.Label("Send files", true), 0, 0);
        page.Controls.Add(Ui.Label("Choose a recipient, add your files, then send. The recipient approves each transfer."), 0, 1);
        _removeFileButton = Ui.Button("&Remove selected", (_, _) => RemoveSelectedFile());
        page.Controls.Add(Ui.Toolbar(Ui.Button("&Add files…", (_, _) => PickFiles(), true), _removeFileButton,
            Ui.Button("Clear files", (_, _) => { _files.Items.Clear(); UpdateActions(); })), 0, 2);
        page.Controls.Add(_files, 0, 3); page.Controls.Add(_fileHint, 0, 4);
        _sendButton = Ui.Button("&Send files", async (_, _) => await SendFilesAsync(), true);
        page.Controls.Add(Ui.Toolbar(new Label { Text = "To", AutoSize = true, Margin = new Padding(0, 9, 14, 0) }, _recipient, _sendButton), 0, 5);
        _files.AccessibleName = "Files to send";
        AddPage("Send files", page);
    }

    private void BuildActivityPage()
    {
        var page = Ui.Page(Ui.Auto(), Ui.Auto(), Ui.Fill(), Ui.Auto(), Ui.Auto(), Ui.Auto());
        page.Controls.Add(Ui.Label("Transfer activity", true), 0, 0);
        page.Controls.Add(Ui.Label("Track current transfers and your last 100 completed transfers. Double-click a row for details."), 0, 1);
        page.Controls.Add(_activity, 0, 2); page.Controls.Add(_transferProgress, 0, 3); page.Controls.Add(_activityHint, 0, 4);
        _cancelButton = Ui.Button("&Cancel transfer", (_, _) => CancelSelectedTransfer());
        _openTransferButton = Ui.Button("Open received &folder", (_, _) => OpenSelectedFolder());
        page.Controls.Add(Ui.Toolbar(_cancelButton, _openTransferButton,
            Ui.Button("Details…", (_, _) => ShowTransferDetails()),
            Ui.Button("Clear finished", (_, _) => ClearHistory())), 0, 5);
        _activity.AccessibleName = "Transfer history and progress";
        AddPage("Activity", page);
    }

    private void BuildSettingsPage()
    {
        var page = Ui.Page(Ui.Auto(), Ui.Auto(), Ui.Auto(), Ui.Auto(), Ui.Auto(), Ui.Auto(), Ui.Fill(), Ui.Auto(), Ui.Auto());
        page.Controls.Add(Ui.Label("Settings & security", true), 0, 0);
        page.Controls.Add(Ui.Label("Changes apply when you save. Network services restart with the new settings."), 0, 1);
        var form = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, Padding = new Padding(0, 0, 0, 16) };
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160)); form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); form.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        void Field(string label, Control input, int row)
        { form.RowStyles.Add(Ui.Auto()); form.Controls.Add(Ui.Label(label), 0, row); form.Controls.Add(input, 1, row); input.Margin = new Padding(0, 0, 12, 16); }
        Field("Device name", _deviceName, 0); Field("Receive folder", _receiveFolder, 1);
        form.Controls.Add(Ui.Button("Browse…", (_, _) => BrowseReceiveFolder()), 2, 1);
        Field("TCP port", _port, 2); form.Controls.Add(_discoveryEnabled, 1, 3);
        page.Controls.Add(form, 0, 2);
        page.Controls.Add(Ui.Label("Verify this device", true), 0, 3);
        page.Controls.Add(Ui.Label("Before trusting this PC, compare this fingerprint on the sending PC. Transfers use TLS encryption and SHA-256 verification."), 0, 4);
        page.Controls.Add(_fingerprint, 0, 5);
        var help = new TextBox { ReadOnly = true, Multiline = true, BorderStyle = BorderStyle.None, Dock = DockStyle.Fill, BackColor = Color.White,
            ScrollBars = ScrollBars.Vertical, AccessibleName = "Security and connection help",
            Text = "Incoming device names are self-reported. Accept files only from an expected sender and check their IP address. Received files are never opened automatically.\r\n\r\nDevices missing? Check that both PCs use the same subnet and that Windows Firewall allows Lantern on private networks (UDP 52845 and your TCP port). You can also add a device by its IP address.\r\n\r\nLantern • Local file sharing • .NET 10 / Windows" };
        help.Margin = new Padding(0, 20, 0, 14); page.Controls.Add(help, 0, 6);
        _saveSettingsButton = Ui.Button("&Save settings", async (_, _) => await SaveSettingsAsync(), true);
        page.Controls.Add(Ui.Toolbar(_saveSettingsButton,
            Ui.Button("Open receive folder", (_, _) => OpenFolder(_settings.ReceiveFolder)),
            Ui.Button("Copy fingerprint", (_, _) => CopyFingerprint()),
            Ui.Button("Forget trusted devices", (_, _) => ForgetTrustedDevices())), 0, 7);
        page.Controls.Add(_settingsHint, 0, 8);
        AddPage("Settings", page);
    }

    private void EnableFileDrop(Control control)
    {
        control.AllowDrop = true;
        control.DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        control.DragDrop += (_, e) => { if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths) { AddFiles(paths); _tabs.SelectedIndex = 1; } };
        foreach (Control child in control.Controls) EnableFileDrop(child);
    }
}
