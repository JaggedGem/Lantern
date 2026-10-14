#nullable enable
namespace Lantern;

public partial class MainWindow
{
    private System.ComponentModel.IContainer? components;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refreshTimer?.Dispose();
            components?.Dispose();
            _lifetime.Dispose();
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        SuspendLayout();
        AutoScaleMode = AutoScaleMode.Font;
        Font = new Font("Segoe UI", 10);
        ClientSize = new Size(1120, 800);
        MinimumSize = new Size(980, 700);
        StartPosition = FormStartPosition.CenterScreen;
        Text = "Lantern — Share nearby";
        BackColor = Color.FromArgb(243, 246, 251);
        ForeColor = UI.Ui.Ink;
        KeyPreview = true;
        BuildLayout();
        ResumeLayout(true);
    }
}
