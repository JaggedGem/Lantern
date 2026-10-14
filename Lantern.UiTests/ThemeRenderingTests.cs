using System.Drawing.Imaging;
using Lantern.Models;
using Lantern.UI;
using Xunit;

namespace Lantern.UiTests;

public sealed class ThemeRenderingTests
{
    [Fact]
    public Task SwitchingThemes_KeepsControlsAndLateAddedContentReadable() => OnStaAsync(() =>
    {
        using var window = new Form { ClientSize = new Size(760, 540), Font = new Font("Segoe UI", 10) };
        var body = Ui.Page(Ui.Auto(), Ui.Auto(), Ui.Auto(), Ui.Fill());
        var primary = Ui.Button("Send files", (_, _) => { }, true);
        var secondary = Ui.Button("Add files", (_, _) => { });
        var error = Ui.Label("Permission denied"); Theme.SetRole(error, ThemeRole.Danger);
        var combo = new ThemedComboBox { Width = 220, DropDownStyle = ComboBoxStyle.DropDownList };
        combo.Items.AddRange(["Office PC", "Laptop"]); combo.SelectedIndex = 0;
        var input = new ThemedTextBox { Text = "Device name", Width = 200 };
        var readOnly = new ThemedTextBox { ReadOnly = true, Text = "Verified identity", Width = 200 };
        var check = new ThemedCheckBox { Text = "Show notifications", AutoSize = true, Checked = true };
        var tabs = new ThemedTabControl { Dock = DockStyle.Fill };
        var list = Ui.List(("Device", 250), ("Status", 250));
        list.Items.Add(new ListViewItem(["Office PC", "Online"])); list.Items.Add(new ListViewItem(["Laptop", "Offline"]));
        var tab = new TabPage("Nearby"); tab.Controls.Add(list); tabs.TabPages.Add(tab); tabs.TabPages.Add(new TabPage("Settings"));
        body.Controls.Add(Ui.Toolbar(primary, secondary, combo), 0, 0);
        body.Controls.Add(Ui.Toolbar(input, readOnly, check), 0, 1); body.Controls.Add(error, 0, 2); body.Controls.Add(tabs, 0, 3);
        window.Controls.Add(body); window.Show();
        foreach (var preference in new[] { "Dark", "Light", "Dark" })
        {
            var palette = preference == "Dark" ? ThemePalette.Dark : ThemePalette.Light;
            Theme.Apply(window, preference); System.Windows.Forms.Application.DoEvents();
            Assert.Same(palette, primary is IThemedControl themed ? themed.Palette : null);
            Assert.Equal(palette.Danger, error.ForeColor);
            Assert.Equal(palette.Input, combo.BackColor); Assert.Equal(palette.Text, combo.ForeColor);
            Assert.Equal(palette.Raised, readOnly.BackColor); Assert.Equal(palette.Text, readOnly.ForeColor);
            Assert.False(tab.UseVisualStyleBackColor); Assert.True(list.OwnerDraw);
            Assert.True(ThemePalette.Contrast(primary.ForeColor, primary.BackColor) >= 4.5);
            primary.Enabled = false;
            Assert.True(ThemePalette.Contrast(primary.ForeColor, primary.BackColor) >= 4.5);
            primary.Enabled = true;
            Assert.Equal(palette.OnPrimary, primary.ForeColor);
            Assert.Equal(palette.Primary, primary.BackColor);
            var late = new ThemedTextBox { ReadOnly = true, Text = "Late status", Width = 100 };
            ((FlowLayoutPanel)body.Controls[1]).Controls.Add(late);
            Assert.Equal(palette.Raised, late.BackColor); Assert.Equal(palette.Text, late.ForeColor);
            late.Dispose();
            using var snapshot = new Bitmap(window.Width, window.Height);
            window.DrawToBitmap(snapshot, new Rectangle(Point.Empty, snapshot.Size));
            var directory = Path.Combine(AppContext.BaseDirectory, "TestResults"); Directory.CreateDirectory(directory);
            snapshot.Save(Path.Combine(directory, $"theme-{preference.ToLowerInvariant()}.png"), ImageFormat.Png);
        }
        window.Close();
    });

    [Fact]
    public Task NativeComboArrowAndTabStrip_PaintTheChosenSurface() => OnStaAsync(() =>
    {
        using var window = new Form { ClientSize = new Size(600, 300) };
        var combo = new ThemedComboBox { Left = 16, Top = 16, Width = 250, DropDownStyle = ComboBoxStyle.DropDownList };
        combo.Items.Add("Office PC"); combo.SelectedIndex = 0;
        var tabs = new ThemedTabControl { Left = 16, Top = 60, Width = 550, Height = 200 };
        tabs.TabPages.Add("Nearby"); tabs.TabPages.Add("Settings");
        window.Controls.Add(combo); window.Controls.Add(tabs); window.Show();
        foreach (var preference in new[] { "Dark", "Light" })
        {
            var palette = preference == "Dark" ? ThemePalette.Dark : ThemePalette.Light;
            Theme.Apply(window, preference); System.Windows.Forms.Application.DoEvents();
            using var comboImage = new Bitmap(combo.Width, combo.Height); combo.DrawToBitmap(comboImage, combo.ClientRectangle);
            AssertRgb(palette.Input, comboImage.GetPixel(combo.Width - 10, combo.Height - 5));
            using var tabImage = new Bitmap(tabs.Width, tabs.Height); tabs.DrawToBitmap(tabImage, tabs.ClientRectangle);
            AssertRgb(palette.Canvas, tabImage.GetPixel(tabs.Width - 10, 6));
        }
        window.Close();
    });

    [Fact]
    public Task DialogErrorsAndMenus_RetainTheirRolesAcrossThemeChanges() => OnStaAsync(() =>
    {
        using var pairing = new PairingDialog(Guid.NewGuid(), new string('A', 64));
        using var manual = new ManualDeviceDialog();
        using var incoming = new DecisionDialog("Incoming files", "Accept expected files", "report.txt", "Accept files", Path.GetTempPath());
        using var details = new TransferDetailsWindow(new TransferRow(Guid.NewGuid(), Guid.NewGuid(), "Peer", "report.txt", 1,
            TransferDirection.Receiving, TransferStatus.Failed, 0, 100, DateTimeOffset.UtcNow, "Disk is full"), _ => { });
        using var menu = new ContextMenuStrip(); menu.Items.Add("Open Lantern"); menu.Items.Add("Unavailable").Enabled = false;
        foreach (var preference in new[] { "Dark", "Light", "Dark" })
        {
            var palette = preference == "Dark" ? ThemePalette.Dark : ThemePalette.Light;
            foreach (var dialog in new Form[] { pairing, manual, incoming, details })
            {
                Theme.Apply(dialog, preference);
                foreach (var text in Descendants(dialog).OfType<TextBox>())
                    Assert.True(ThemePalette.Contrast(text.ForeColor, text.BackColor) >= 4.5);
                Assert.All(Descendants(dialog).OfType<IThemedControl>(), control => Assert.Same(palette, control.Palette));
            }
            Theme.Apply(menu, preference);
            Assert.Equal(palette.Surface, menu.BackColor); Assert.Equal(palette.Text, menu.Items[0].ForeColor);
            Assert.IsAssignableFrom<ToolStripProfessionalRenderer>(menu.Renderer);
        }
    });

    private static void AssertRgb(Color expected, Color actual) => Assert.Equal((expected.R, expected.G, expected.B), (actual.R, actual.G, actual.B));

    private static IEnumerable<Control> Descendants(Control root)
    { foreach (Control child in root.Controls) { yield return child; foreach (var nested in Descendants(child)) yield return nested; } }
    private static Task OnStaAsync(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { System.Windows.Forms.Application.EnableVisualStyles(); action(); completion.TrySetResult(); }
            catch (Exception error) { completion.TrySetException(error); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }
}
