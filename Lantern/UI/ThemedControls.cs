using System.Drawing.Drawing2D;
using System.ComponentModel;

namespace Lantern.UI;

internal interface IThemedControl
{
    ThemePalette Palette { get; set; }
}

internal static class ThemeDrawing
{
    public static void Fill(Graphics graphics, Rectangle bounds, Color color)
    {
        using var brush = new SolidBrush(color);
        graphics.FillRectangle(brush, bounds);
    }

    public static void Border(Graphics graphics, Rectangle bounds, Color color)
    {
        if (bounds.Width < 1 || bounds.Height < 1) return;
        using var pen = new Pen(color);
        graphics.DrawRectangle(pen, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
    }
}

/// <summary>Retains native Button keyboard, mnemonic, accessibility and dialog behavior.</summary>
internal sealed class ThemedButton : Button, IThemedControl
{
    private bool _hovered, _pressed;
    [DefaultValue(false)]
    public bool Primary { get; init; }
    private ThemePalette _palette = ThemePalette.Light;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ThemePalette Palette
    {
        get => _palette;
        set { _palette = value; UpdateColors(); Invalidate(); }
    }
    private void UpdateColors()
    {
        BackColor = !Enabled ? Palette.DisabledSurface : Primary ? Palette.Primary : Palette.Surface;
        ForeColor = !Enabled ? Palette.DisabledText : Primary ? Palette.OnPrimary : Palette.Text;
    }

    public ThemedButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        FlatStyle = FlatStyle.Flat;
        UseVisualStyleBackColor = false;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var palette = Palette;
        var background = !Enabled ? palette.DisabledSurface : Primary
            ? (_pressed ? palette.PrimaryPressed : _hovered ? palette.PrimaryHover : palette.Primary)
            : (_pressed ? palette.Selection : _hovered ? palette.Raised : palette.Surface);
        var foreground = !Enabled ? palette.DisabledText : Primary ? palette.OnPrimary : _pressed ? palette.OnSelection : palette.Text;
        ThemeDrawing.Fill(e.Graphics, ClientRectangle, Parent?.BackColor ?? palette.Surface);
        var bounds = Rectangle.Inflate(ClientRectangle, -1, -1);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var radius = Math.Max(4, 6 * DeviceDpi / 96);
        using var shape = new GraphicsPath();
        var diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        if (diameter < 1) return;
        shape.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        shape.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        shape.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        shape.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        shape.CloseFigure();
        using var fill = new SolidBrush(background); e.Graphics.FillPath(fill, shape);
        using var border = new Pen(Primary && Enabled ? background : palette.Border); e.Graphics.DrawPath(border, shape);
        var textBounds = Rectangle.Inflate(bounds, -Padding.Horizontal / 2, -Padding.Vertical / 2);
        TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, foreground,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
            TextFormatFlags.EndEllipsis | (ShowKeyboardCues ? 0 : TextFormatFlags.HidePrefix));
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(bounds, -5, -5), foreground, background);
    }
    protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hovered = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) _pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode is Keys.Space or Keys.Enter) { _pressed = true; Invalidate(); } base.OnKeyDown(e); }
    protected override void OnKeyUp(KeyEventArgs e) { _pressed = false; Invalidate(); base.OnKeyUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { UpdateColors(); Invalidate(); base.OnEnabledChanged(e); }
}

internal sealed class ThemedComboBox : ComboBox, IThemedControl
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ThemePalette Palette { get; set; } = ThemePalette.Light;
    public ThemedComboBox() { DrawMode = DrawMode.OwnerDrawFixed; FlatStyle = FlatStyle.Flat; ItemHeight = Font.Height + 8; }

    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); ItemHeight = Font.Height + 8 * DeviceDpi / 96; }
    protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); ItemHeight = Font.Height + 8 * DeviceDpi / 96; }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Bounds.Width == 0 || e.Bounds.Height == 0) return;
        var selected = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
        var background = selected ? Palette.Selection : Palette.Input;
        var foreground = !Enabled ? Palette.DisabledText : selected ? Palette.OnSelection : Palette.Text;
        ThemeDrawing.Fill(e.Graphics, e.Bounds, background);
        var text = e.Index >= 0 && e.Index < Items.Count ? GetItemText(Items[e.Index]) : Text;
        TextRenderer.DrawText(e.Graphics, text, e.Font ?? Font, Rectangle.Inflate(e.Bounds, -6, 0), foreground,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        if ((e.State & DrawItemState.Focus) != 0) ControlPaint.DrawFocusRectangle(e.Graphics, e.Bounds, foreground, background);
    }

    protected override void WndProc(ref Message message)
    {
        base.WndProc(ref message);
        if (message.Msg is not (0x000F or 0x0317 or 0x0318) || !IsHandleCreated) return; // WM_PAINT / WM_PRINT / WM_PRINTCLIENT
        using var graphics = message.Msg == 0x000F ? Graphics.FromHwnd(Handle) : Graphics.FromHdc(message.WParam);
        var arrowWidth = Math.Max(SystemInformation.VerticalScrollBarWidth, 22 * DeviceDpi / 96);
        var arrow = new Rectangle(Math.Max(0, Width - arrowWidth - 1), 1, arrowWidth, Math.Max(0, Height - 2));
        ThemeDrawing.Fill(graphics, arrow, Palette.Input);
        var center = new Point(arrow.Left + arrow.Width / 2, arrow.Top + arrow.Height / 2);
        using var pen = new Pen(Enabled ? Palette.Text : Palette.DisabledText, Math.Max(1, DeviceDpi / 96f));
        graphics.DrawLines(pen, [new(center.X - 4, center.Y - 2), new(center.X, center.Y + 2), new(center.X + 4, center.Y - 2)]);
        ThemeDrawing.Border(graphics, ClientRectangle, Focused ? Palette.Primary : Palette.Border);
    }
}

/// <summary>Paint the complete tab strip: native TabControl ignores its BackColor in parts of its client area.</summary>
internal sealed class ThemedTabControl : TabControl, IThemedControl
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ThemePalette Palette { get; set; } = ThemePalette.Light;
    public ThemedTabControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }
    protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(Palette.Canvas);
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Palette.Canvas);
        for (var index = 0; index < TabCount; index++)
        {
            var bounds = GetTabRect(index);
            var selected = index == SelectedIndex;
            ThemeDrawing.Fill(e.Graphics, bounds, selected ? Palette.Surface : Palette.Canvas);
            TextRenderer.DrawText(e.Graphics, TabPages[index].Text, Font, bounds, selected ? Palette.Text : Palette.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            if (selected) ThemeDrawing.Fill(e.Graphics, new Rectangle(bounds.X + 12, bounds.Bottom - 3, Math.Max(0, bounds.Width - 24), 3), Palette.Primary);
            if (selected && Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(bounds, -5, -6), Palette.Text, Palette.Surface);
        }
        ThemeDrawing.Border(e.Graphics, DisplayRectangle, Palette.Border);
    }
    protected override void OnSelectedIndexChanged(EventArgs e) { base.OnSelectedIndexChanged(e); Invalidate(); }
}

internal sealed class ThemedListView : ListView, IThemedControl
{
    private bool _sizingLastColumn;
    private int _minimumLastWidth;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ThemePalette Palette { get; set; } = ThemePalette.Light;
    public ThemedListView() { OwnerDraw = true; DoubleBuffered = true; }
    protected override void OnDrawColumnHeader(DrawListViewColumnHeaderEventArgs e)
    {
        ThemeDrawing.Fill(e.Graphics, e.Bounds, Palette.Raised);
        TextRenderer.DrawText(e.Graphics, e.Header?.Text ?? "", Font, Rectangle.Inflate(e.Bounds, -12, 0), Palette.Muted,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        using var border = new Pen(Palette.Border);
        e.Graphics.DrawLine(border, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
    }
    protected override void OnDrawItem(DrawListViewItemEventArgs e)
    {
        if (View != View.Details) e.DrawDefault = true;
    }
    protected override void OnDrawSubItem(DrawListViewSubItemEventArgs e)
    {
        if (e.Item is null || e.SubItem is null) return;
        var selected = e.Item.Selected;
        var background = selected ? Palette.Selection : e.ItemIndex % 2 == 0 ? Palette.Surface : Palette.Raised;
        var foreground = selected ? Palette.OnSelection : Theme.ItemForeground(e.Item, Palette);
        ThemeDrawing.Fill(e.Graphics, e.Bounds, background);
        TextRenderer.DrawText(e.Graphics, e.SubItem.Text, Font, Rectangle.Inflate(e.Bounds, -12, 0), foreground,
            TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        if (selected && e.ColumnIndex == 0)
            ThemeDrawing.Fill(e.Graphics, new Rectangle(e.Bounds.X, e.Bounds.Y, 3, e.Bounds.Height), Palette.Primary);
        if (selected && Focused && e.ColumnIndex == 0 && ShowFocusCues)
            ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(e.Bounds, -4, -2), Palette.OnSelection, Palette.Selection);
    }
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (Columns.Count > 0) _minimumLastWidth = Math.Max(100, Columns[^1].Width);
        FillLastColumn();
    }
    protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); FillLastColumn(); }
    protected override void OnColumnWidthChanged(ColumnWidthChangedEventArgs e)
    {
        base.OnColumnWidthChanged(e);
        if (!_sizingLastColumn && _minimumLastWidth > 0 && e.ColumnIndex == Columns.Count - 1)
            _minimumLastWidth = Math.Max(100, Columns[^1].Width);
        FillLastColumn();
    }
    protected override void WndProc(ref Message message)
    {
        base.WndProc(ref message);
        if (message.Msg is not (0x000F or 0x0317 or 0x0318) || !IsHandleCreated || BorderStyle == BorderStyle.None) return;
        using var graphics = message.Msg == 0x000F ? Graphics.FromHwnd(Handle) : Graphics.FromHdc(message.WParam);
        ThemeDrawing.Border(graphics, ClientRectangle, Palette.Border);
    }
    private void FillLastColumn()
    {
        if (_sizingLastColumn || !IsHandleCreated || Columns.Count == 0 || _minimumLastWidth == 0) return;
        var space = ClientSize.Width - Columns.Cast<ColumnHeader>().SkipLast(1).Sum(column => column.Width);
        var width = Math.Max(_minimumLastWidth, space);
        if (width == Columns[^1].Width) return;
        _sizingLastColumn = true;
        try { Columns[^1].Width = width; }
        finally { _sizingLastColumn = false; }
    }
}

internal sealed class ThemedCheckBox : CheckBox, IThemedControl
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ThemePalette Palette { get; set; } = ThemePalette.Light;
    public ThemedCheckBox() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); UseVisualStyleBackColor = false; }
    public override Size GetPreferredSize(Size proposedSize)
    {
        var text = TextRenderer.MeasureText(Text, Font);
        return new Size(text.Width + 30 * DeviceDpi / 96, Math.Max(text.Height + 8, 28 * DeviceDpi / 96));
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        ThemeDrawing.Fill(e.Graphics, ClientRectangle, BackColor);
        var size = Math.Max(16, 16 * DeviceDpi / 96);
        var box = new Rectangle(1, (Height - size) / 2, size, size);
        ThemeDrawing.Fill(e.Graphics, box, !Enabled ? Palette.DisabledSurface : Checked ? Palette.Primary : Palette.Input);
        ThemeDrawing.Border(e.Graphics, box, Checked && Enabled ? Palette.Primary : Palette.Border);
        if (Checked)
        {
            using var pen = new Pen(Enabled ? Palette.OnPrimary : Palette.DisabledText, Math.Max(2, 2 * DeviceDpi / 96f));
            e.Graphics.DrawLines(pen, [new(box.Left + size / 5, box.Top + size / 2), new(box.Left + size * 2 / 5, box.Top + size * 3 / 4), new(box.Left + size * 4 / 5, box.Top + size / 4)]);
        }
        var text = new Rectangle(box.Right + 8, 0, Math.Max(0, Width - box.Right - 8), Height);
        TextRenderer.DrawText(e.Graphics, Text, Font, text, Enabled ? Palette.Text : Palette.DisabledText, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, text, Palette.Text, BackColor);
    }
    protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
}

internal sealed class ThemedProgressBar : ProgressBar, IThemedControl
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ThemePalette Palette { get; set; } = ThemePalette.Light;
    public ThemedProgressBar() => SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    protected override void OnPaint(PaintEventArgs e)
    {
        ThemeDrawing.Fill(e.Graphics, ClientRectangle, Palette.Input);
        var fill = Rectangle.Inflate(ClientRectangle, -2, -2);
        fill.Width = (int)(fill.Width * ((double)(Value - Minimum) / Math.Max(1, Maximum - Minimum)));
        if (fill.Width > 0) ThemeDrawing.Fill(e.Graphics, fill, Palette.Primary);
        ThemeDrawing.Border(e.Graphics, ClientRectangle, Palette.Border);
    }
    protected override void WndProc(ref Message message)
    {
        base.WndProc(ref message);
        if (message.Msg == 0x0402) Invalidate(); // PBM_SETPOS: native value changes must repaint the managed track.
    }
}

internal sealed class ThemedTextBox : TextBox, IThemedControl
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ThemePalette Palette { get; set; } = ThemePalette.Light;
    protected override void WndProc(ref Message message)
    {
        base.WndProc(ref message);
        if (message.Msg is not (0x000F or 0x0317 or 0x0318) || !IsHandleCreated) return;
        using var graphics = message.Msg == 0x000F ? Graphics.FromHwnd(Handle) : Graphics.FromHdc(message.WParam);
        if (TextLength == 0 && !Focused && !string.IsNullOrEmpty(PlaceholderText))
        {
            ThemeDrawing.Fill(graphics, ClientRectangle, ReadOnly ? Palette.Raised : Palette.Input);
            TextRenderer.DrawText(graphics, PlaceholderText, Font, Rectangle.Inflate(ClientRectangle, -3, 0), Palette.Muted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
        if (BorderStyle != BorderStyle.None) ThemeDrawing.Border(graphics, ClientRectangle, Focused ? Palette.Primary : Palette.Border);
    }
}

internal sealed class ThemedNumericUpDown : NumericUpDown, IThemedControl
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ThemePalette Palette { get; set; } = ThemePalette.Light;

    public ThemedNumericUpDown()
    {
        BorderStyle = BorderStyle.FixedSingle;
        foreach (Control child in Controls)
        {
            if (child is TextBox) continue;
            var renderer = new SpinRenderer(this, child);
            child.HandleCreated += (_, _) => renderer.AssignHandle(child.Handle);
            child.HandleDestroyed += (_, _) => renderer.ReleaseHandle();
            if (child.IsHandleCreated) renderer.AssignHandle(child.Handle);
        }
    }

    protected override void WndProc(ref Message message)
    {
        base.WndProc(ref message);
        if (message.Msg is not (0x000F or 0x0317 or 0x0318) || !IsHandleCreated) return;
        using var graphics = message.Msg == 0x000F ? Graphics.FromHwnd(Handle) : Graphics.FromHdc(message.WParam);
        ThemeDrawing.Border(graphics, ClientRectangle, Focused ? Palette.Primary : Palette.Border);
    }

    private sealed class SpinRenderer(ThemedNumericUpDown owner, Control buttons) : NativeWindow
    {
        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (message.Msg is not (0x000F or 0x0317 or 0x0318) || Handle == IntPtr.Zero) return;
            using var graphics = message.Msg == 0x000F ? Graphics.FromHwnd(Handle) : Graphics.FromHdc(message.WParam);
            var palette = owner.Palette;
            var bounds = buttons.ClientRectangle;
            ThemeDrawing.Fill(graphics, bounds, palette.Input);
            ThemeDrawing.Border(graphics, bounds, palette.Border);
            var center = bounds.Width / 2;
            var top = bounds.Height / 4;
            var bottom = bounds.Height * 3 / 4;
            using var pen = new Pen(owner.Enabled ? palette.Text : palette.DisabledText, Math.Max(1, owner.DeviceDpi / 96f));
            graphics.DrawLines(pen, [new(center - 3, top + 2), new(center, top - 1), new(center + 3, top + 2)]);
            graphics.DrawLines(pen, [new(center - 3, bottom - 2), new(center, bottom + 1), new(center + 3, bottom - 2)]);
        }
    }
}
