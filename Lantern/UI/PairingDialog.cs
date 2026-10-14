using Lantern.Models;

namespace Lantern.UI;

internal sealed class PairingDialog : Form
{
    private readonly TextBox _peerId = new() { Dock = DockStyle.Fill, AccessibleName = "Peer device ID" };
    private readonly TextBox _pin = new() { Dock = DockStyle.Fill, Multiline = true, Height = 66, AccessibleName = "Verified peer fingerprint" };
    private readonly Label _error = Ui.Label(Ui.Text("Text"));
    public Guid PeerId { get; private set; }
    public string PeerFingerprint { get; private set; } = "";

    public PairingDialog(Guid localId, string localFingerprint, Device? device = null)
    {
        AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Pair devices"; Font = new Font("Segoe UI", 10); ClientSize = new Size(700, 580);
        MinimumSize = new Size(620, 560); StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
        var layout = Ui.Page(Ui.Auto(), Ui.Auto(), Ui.Auto(), Ui.Auto(), Ui.Auto(), Ui.Auto(), Ui.Fill(), Ui.Auto(), Ui.Auto());
        layout.Controls.Add(Ui.Label(Ui.Text("Pair_on_both_PCs"), true), 0, 0);
        var guide = Ui.Label(Ui.Text("Exchange_device_IDs_and_fingerprints_with_the_other_person_through_a_truste"));
        guide.MaximumSize = new Size(640, 0); layout.Controls.Add(guide, 0, 1);
        layout.Controls.Add(new TextBox { Text = $"This PC's device ID:\r\n{localId:D}\r\n\r\nSHA-256 fingerprint:\r\n{Ui.Fingerprint(localFingerprint)}", ReadOnly = true, Multiline = true, Height = 128, Dock = DockStyle.Fill, AccessibleName = "Local pairing identity" }, 0, 2);
        layout.Controls.Add(Ui.Label(Ui.Text("Other_PC_s_device_ID")), 0, 3);
        _peerId.Text = device?.Id.ToString("D") ?? ""; layout.Controls.Add(_peerId, 0, 4);
        layout.Controls.Add(Ui.Label(Ui.Text("Other_PC_s_verified_SHA_256_fingerprint")), 0, 5); layout.Controls.Add(_pin, 0, 6);
        _error.ForeColor = Color.Firebrick; layout.Controls.Add(_error, 0, 7);
        var save = Ui.Button(Ui.Text("Trust_verified_identity"), (_, _) => Save(localId), true);
        var cancel = Ui.Button(Ui.Text("Cancel"), (_, _) => { DialogResult = DialogResult.Cancel; Close(); });
        layout.Controls.Add(Ui.Toolbar(save, cancel), 0, 8);
        Controls.Add(layout); AcceptButton = save; CancelButton = cancel;
    }
    private void Save(Guid localId)
    {
        var pin = string.Concat(_pin.Text.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
        if (!Guid.TryParse(_peerId.Text, out var id) || id == Guid.Empty || id == localId || pin.Length != 64 || !pin.All(Uri.IsHexDigit))
        { _error.Text = "Enter the other PC's valid device ID and its full 64-character fingerprint."; return; }
        PeerId = id; PeerFingerprint = pin; DialogResult = DialogResult.OK; Close();
    }
}
