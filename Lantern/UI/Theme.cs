using Microsoft.Win32;

namespace Lantern.UI;

internal static class Theme
{
    public static void Apply(Control root, string preference)
    {
        var dark = preference == "Dark";
        if (preference == "System")
        {
            try { using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"); dark = key?.GetValue("AppsUseLightTheme") is int value && value == 0; }
            catch (System.Security.SecurityException) { }
        }
        ApplyColors(root, dark);
    }
    private static void ApplyColors(Control control, bool dark)
    {
        var background = dark ? Color.FromArgb(27, 33, 44) : Color.White;
        var foreground = dark ? Color.FromArgb(232, 237, 245) : Ui.Ink;
        if (control is Button button)
        {
            var primary = button.BackColor == Ui.Accent;
            button.BackColor = primary ? Ui.Accent : background; button.ForeColor = primary ? Color.White : foreground;
        }
        else if (control is not ProgressBar)
        {
            control.BackColor = background; control.ForeColor = foreground;
            if (control is TextBoxBase { ReadOnly: true }) control.BackColor = dark ? Color.FromArgb(37, 45, 59) : Color.FromArgb(245, 247, 251);
        }
        if (control is ListView list) foreach (ListViewItem item in list.Items) item.ForeColor = foreground;
        foreach (Control child in control.Controls) ApplyColors(child, dark);
    }
}
