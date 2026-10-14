using System.Net;
using Lantern.Models;

namespace Lantern.UI;

internal sealed class ManualDeviceDialog : Form
{
    private readonly TextBox _address = new ThemedTextBox() { Dock = DockStyle.Fill, PlaceholderText = "192.168.1.20", AccessibleName = "Device IP address" };
    private readonly TextBox _id = new ThemedTextBox() { Dock = DockStyle.Fill, PlaceholderText = "Device ID from the other PC's Settings", AccessibleName = "Device ID" };
    private readonly TextBox _name = new ThemedTextBox() { Dock = DockStyle.Fill, Text = "My other PC", MaxLength = 128, AccessibleName = "Device name" };
    private readonly NumericUpDown _port = new ThemedNumericUpDown() { Minimum = 1, Maximum = 65535, Value = 52846, AccessibleName = "Device TCP port" };
    private readonly Label _error = Ui.Label(Ui.Text("Text"));
    public Device? Device { get; private set; }

    public ManualDeviceDialog()
    {
        AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Add a device by IP"; Font = new Font("Segoe UI", 10); ClientSize = new Size(580, 450);
        FormBorderStyle = FormBorderStyle.FixedDialog; StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
        var layout = Ui.Page(Ui.Auto(), Ui.Auto(), Ui.Auto(), Ui.Auto(), Ui.Auto(), Ui.Auto(), Ui.Auto(), Ui.Auto());
        var guide = Ui.Label(Ui.Text("Enter_the_other_PC_s_identity_and_connection_details_from_its_Settings_Pair")); guide.MaximumSize = new Size(520, 0);
        layout.Controls.Add(guide, 0, 0);
        layout.Controls.Add(_name, 0, 1); layout.Controls.Add(_id, 0, 2); layout.Controls.Add(_address, 0, 3);
        layout.Controls.Add(Ui.Toolbar(new Label { Text = "TCP port", AutoSize = true, Margin = new Padding(0, 8, 16, 8) }, _port), 0, 4);
        Theme.SetRole(_error, ThemeRole.Danger); layout.Controls.Add(_error, 0, 5);
        var add = Ui.Button(Ui.Text("Add_device"), (_, _) => AddDevice(), true);
        var cancel = Ui.Button(Ui.Text("Cancel"), (_, _) => { DialogResult = DialogResult.Cancel; Close(); });
        layout.Controls.Add(Ui.Toolbar(add, cancel), 0, 6); Controls.Add(layout);
        AcceptButton = add; CancelButton = cancel;
    }
    private void AddDevice()
    {
        if (!IPAddress.TryParse(_address.Text.Trim(), out var address) || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork ||
            address.Equals(IPAddress.Any) || address.Equals(IPAddress.Broadcast) || address.GetAddressBytes()[0] >= 224 ||
            !Guid.TryParse(_id.Text.Trim(), out var id) || id == Guid.Empty || string.IsNullOrWhiteSpace(_name.Text))
        { _error.Text = "Enter a device ID, name and valid unicast IPv4 address."; return; }
        Device = new Device(id, _name.Text.Trim(), address, (int)_port.Value); Device.UpdateStatus(DeviceStatus.Online);
        DialogResult = DialogResult.OK; Close();
    }
}
