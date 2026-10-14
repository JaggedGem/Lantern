#nullable enable
namespace Lantern;

public partial class MainWindow
{
    private System.ComponentModel.IContainer? components;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnSystemPreferenceChanged;
            _refreshTimer?.Dispose();
            components?.Dispose();
            _lifetime.Dispose();
            _lifecycle.Dispose();
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 10);
        ClientSize = new Size(1120, 800);
        MinimumSize = new Size(980, 700);
        StartPosition = FormStartPosition.CenterScreen;
        Text = "Lantern — Share nearby";
        BackColor = UI.ThemePalette.Light.Canvas;
        ForeColor = UI.Ui.Ink;
        KeyPreview = true;
        BuildLayout();
        ResumeLayout(true);
    }
}
