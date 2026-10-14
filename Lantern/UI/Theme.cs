using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Lantern.UI;

internal enum ThemeRole { Default, Canvas, Surface, Muted, Danger, Success }

internal static class Theme
{
    private sealed class State
    {
        public ThemeRole Role;
        public ThemePalette? Palette;
    }
    private static readonly ConditionalWeakTable<Control, State> States = new();

    public static ThemePalette Resolve(string preference)
    {
        if (SystemInformation.HighContrast)
            return ThemePalette.Light with
            {
                IsDark = SystemColors.Window.GetBrightness() < 0.5f,
                Canvas = SystemColors.Window, Surface = SystemColors.Window, Raised = SystemColors.Window,
                Input = SystemColors.Window, Text = SystemColors.WindowText, Muted = SystemColors.WindowText,
                Border = SystemColors.WindowText, Primary = SystemColors.Highlight, PrimaryHover = SystemColors.Highlight,
                PrimaryPressed = SystemColors.Highlight, OnPrimary = SystemColors.HighlightText,
                Selection = SystemColors.Highlight, OnSelection = SystemColors.HighlightText,
                DisabledSurface = SystemColors.Window, DisabledText = SystemColors.GrayText,
                Danger = SystemColors.WindowText, Success = SystemColors.WindowText
            };
        var dark = preference == "Dark";
        if (preference == "System")
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                dark = key?.GetValue("AppsUseLightTheme") is int value && value == 0;
            }
            catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        }
        return dark ? ThemePalette.Dark : ThemePalette.Light;
    }

    public static ThemePalette PaletteFor(Control control) => States.TryGetValue(control, out var state) && state.Palette is { } palette
        ? palette : control.Parent is { } parent ? PaletteFor(parent) : ThemePalette.Light;

    public static void SetRole(Control control, ThemeRole role)
    {
        var state = GetState(control);
        state.Role = role;
        if (state.Palette is { } palette) ApplySingle(control, state, palette);
    }

    public static void Apply(Control root, string preference) => ApplyTree(root, Resolve(preference));

    private static State GetState(Control control) => States.GetValue(control, target =>
    {
        var state = new State();
        target.ControlAdded += (_, args) => { if (state.Palette is { } palette && args.Control is { } added) ApplyTree(added, palette); };
        target.HandleCreated += (_, _) => { if (state.Palette is { } palette) ApplyNative(target, palette); };
        return state;
    });

    private static void ApplyTree(Control control, ThemePalette palette)
    {
        var state = GetState(control); state.Palette = palette;
        ApplySingle(control, state, palette);
        foreach (Control child in control.Controls) ApplyTree(child, palette);
        if (control.ContextMenuStrip is { } menu) ApplyTree(menu, palette);
    }

    private static void ApplySingle(Control control, State state, ThemePalette palette)
    {
        var inherited = control.Parent?.BackColor ?? palette.Surface;
        control.BackColor = control is Form || state.Role == ThemeRole.Canvas ? palette.Canvas :
            control is TabPage || state.Role == ThemeRole.Surface ? palette.Surface : inherited;
        control.ForeColor = state.Role switch
        {
            ThemeRole.Muted => palette.Muted,
            ThemeRole.Danger => palette.Danger,
            ThemeRole.Success => palette.Success,
            _ => palette.Text
        };
        switch (control)
        {
            case TextBoxBase input:
                input.BackColor = input.ReadOnly ? palette.Raised : palette.Input;
                input.ForeColor = palette.Text;
                break;
            case ComboBox combo:
                combo.BackColor = palette.Input; combo.ForeColor = palette.Text;
                break;
            case UpDownBase number:
                number.BackColor = palette.Input; number.ForeColor = palette.Text;
                break;
            case TabPage page:
                page.UseVisualStyleBackColor = false;
                break;
            case Button button:
                button.UseVisualStyleBackColor = false;
                var primary = button is ThemedButton { Primary: true };
                button.BackColor = !button.Enabled ? palette.DisabledSurface : primary ? palette.Primary : palette.Surface;
                button.ForeColor = !button.Enabled ? palette.DisabledText : primary ? palette.OnPrimary : palette.Text;
                button.FlatAppearance.BorderColor = primary ? palette.Primary : palette.Border;
                button.FlatAppearance.MouseOverBackColor = primary ? palette.PrimaryHover : palette.Raised;
                button.FlatAppearance.MouseDownBackColor = primary ? palette.PrimaryPressed : palette.Selection;
                break;
            case CheckBox check:
                check.UseVisualStyleBackColor = false;
                break;
            case ListView list:
                list.BackColor = palette.Surface; list.ForeColor = palette.Text;
                foreach (ListViewItem item in list.Items) item.ForeColor = ItemForeground(item, palette);
                break;
            case ProgressBar progress:
                progress.BackColor = palette.Input; progress.ForeColor = palette.Primary;
                break;
            case ToolStrip strip:
                ApplyMenu(strip, palette);
                break;
        }
        if (control is IThemedControl themed) themed.Palette = palette;
        ApplyNative(control, palette);
        control.Invalidate(true);
    }

    internal static Color ItemForeground(ListViewItem item, ThemePalette palette) => item.Tag is TransferRow transfer
        ? transfer.State switch
        {
            Models.TransferStatus.Failed => palette.Danger,
            Models.TransferStatus.Completed => palette.Success,
            _ => palette.Text
        } : palette.Text;

    private static void ApplyMenu(ToolStrip menu, ThemePalette palette)
    {
        menu.BackColor = palette.Surface; menu.ForeColor = palette.Text;
        menu.Renderer = new MenuRenderer(palette);
        foreach (ToolStripItem item in menu.Items)
        {
            item.BackColor = palette.Surface; item.ForeColor = palette.Text;
            if (item is ToolStripDropDownItem { HasDropDownItems: true } nested) ApplyMenu(nested.DropDown, palette);
        }
    }

    private static void ApplyNative(Control control, ThemePalette palette)
    {
        if (!control.IsHandleCreated || !OperatingSystem.IsWindows()) return;
        if (control is Form)
        {
            var dark = palette.IsDark ? 1 : 0;
            // Supported Windows versions theme the non-client title bar; older versions safely ignore it.
            if (DwmSetWindowAttribute(control.Handle, 20, ref dark, sizeof(int)) != 0)
                DwmSetWindowAttribute(control.Handle, 19, ref dark, sizeof(int));
        }
        else if (control is TextBoxBase or ListView or ComboBox or UpDownBase)
            SetWindowTheme(control.Handle, palette.IsDark ? "DarkMode_Explorer" : "Explorer", null);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr window, string applicationName, string? idList);

    private sealed class MenuRenderer(ThemePalette palette) : ToolStripProfessionalRenderer(new MenuColors(palette))
    {
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        { e.TextColor = !e.Item.Enabled ? palette.DisabledText : e.Item.Selected ? palette.OnSelection : palette.Text; base.OnRenderItemText(e); }
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        { e.ArrowColor = e.Item?.Enabled == true ? palette.Text : palette.DisabledText; base.OnRenderArrow(e); }
    }
    private sealed class MenuColors(ThemePalette palette) : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => palette.Surface;
        public override Color MenuBorder => palette.Border;
        public override Color MenuItemBorder => palette.Border;
        public override Color MenuItemSelected => palette.Selection;
        public override Color MenuItemSelectedGradientBegin => palette.Selection;
        public override Color MenuItemSelectedGradientEnd => palette.Selection;
        public override Color MenuItemPressedGradientBegin => palette.Selection;
        public override Color MenuItemPressedGradientMiddle => palette.Selection;
        public override Color MenuItemPressedGradientEnd => palette.Selection;
        public override Color ImageMarginGradientBegin => palette.Surface;
        public override Color ImageMarginGradientMiddle => palette.Surface;
        public override Color ImageMarginGradientEnd => palette.Surface;
        public override Color SeparatorDark => palette.Border;
        public override Color SeparatorLight => palette.Surface;
    }
}
