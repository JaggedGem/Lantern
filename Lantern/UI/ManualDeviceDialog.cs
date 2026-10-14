using System.Net;
using System.Security.Cryptography;
using System.Text;
using Lantern.Models;

namespace Lantern.UI;

internal sealed class ManualDeviceDialog : Form
{
    private readonly TextBox _address = new() { Dock = DockStyle.Fill, PlaceholderText = "192.168.1.20", AccessibleName = "Device IP address" };
    private readonly NumericUpDown _port = new() { Minimum = 1, Maximum = 65535, Value = 52846, AccessibleName = "Device TCP port" };
    private readonly Label _error = Ui.Label("");
    public Device? Device { get; private set; }

    public ManualDeviceDialog()
    {
        Text = "Add a device by IP"; Font = new Font("Segoe UI", 10); ClientSize = new Size(520, 290);
        FormBorderStyle = FormBorderStyle.FixedDialog; StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
        var layout = Ui.Page(Ui.Auto(), Ui.Auto(), Ui.Auto(), Ui.Auto(), Ui.Auto(), Ui.Auto());
        layout.Controls.Add(Ui.Label("Enter the address and TCP port shown on the other PC."), 0, 0);
        layout.Controls.Add(_address, 0, 1);
        layout.Controls.Add(Ui.Toolbar(new Label { Text = "TCP port", AutoSize = true, Margin = new Padding(0, 8, 16, 8) }, _port), 0, 2);
        _error.ForeColor = Color.Firebrick; layout.Controls.Add(_error, 0, 3);
        var add = Ui.Button("Add device", (_, _) => AddDevice(), true);
        var cancel = Ui.Button("Cancel", (_, _) => { DialogResult = DialogResult.Cancel; Close(); });
        layout.Controls.Add(Ui.Toolbar(add, cancel), 0, 4); Controls.Add(layout);
        AcceptButton = add; CancelButton = cancel;
    }

    private void AddDevice()
    {
        if (!IPAddress.TryParse(_address.Text.Trim(), out var address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.Broadcast) || address.IsIPv6Multicast ||
            (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && address.GetAddressBytes()[0] >= 224))
        { _error.Text = "Enter a valid unicast IPv4 or IPv6 address."; return; }
        var port = (int)_port.Value;
        var id = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"{address}:{port}"))[..16]);
        Device = new Device(id, address.ToString(), address, port);
        Device.UpdateStatus(DeviceStatus.Online);
        DialogResult = DialogResult.OK; Close();
    }
}
