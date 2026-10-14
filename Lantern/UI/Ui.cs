namespace Lantern.UI;

internal static class Ui
{
    private static readonly System.Resources.ResourceManager Resources = new("Lantern.Resources.Strings", typeof(Ui).Assembly);
    public static string Text(string key) => Resources.GetString(key, System.Globalization.CultureInfo.CurrentUICulture) ?? key;

    public static readonly Color Ink = ThemePalette.Light.Text;
    public static readonly Color Accent = ThemePalette.Light.Primary;

    public static Button Button(string text, EventHandler handler, bool primary = false)
    {
        var button = new ThemedButton { Primary = primary, Text = text, AutoSize = true, MinimumSize = new Size(104, 40), Padding = new Padding(14, 5, 14, 5),
            FlatStyle = FlatStyle.Flat, BackColor = primary ? Accent : ThemePalette.Light.Surface, ForeColor = primary ? ThemePalette.Light.OnPrimary : Ink,
            Cursor = Cursors.Hand, Margin = new Padding(0, 0, 10, 0), AccessibleName = text.Replace("&", "") };
        button.FlatAppearance.BorderColor = primary ? Accent : ThemePalette.Light.Border;
        button.Click += handler;
        return button;
    }

    public static Label Label(string text, bool title = false)
    {
        var label = new Label
        {
            Text = text, UseMnemonic = false, AutoSize = true, Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", title ? 17 : 10, title ? FontStyle.Bold : FontStyle.Regular),
            Margin = new Padding(0, 0, 0, 12)
        };
        Theme.SetRole(label, title ? ThemeRole.Default : ThemeRole.Muted);
        return label;
    }

    public static FlowLayoutPanel Toolbar(params Control[] controls)
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = new Padding(0, 0, 0, 14) };
        panel.Controls.AddRange(controls);
        return panel;
    }

    public static ListView List(params (string Name, int Width)[] columns)
    {
        var list = new ThemedListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false,
            MultiSelect = false, ShowItemToolTips = true, BorderStyle = BorderStyle.FixedSingle, BackColor = ThemePalette.Light.Surface, ForeColor = Ink, Margin = new Padding(0, 0, 0, 14) };
        foreach (var column in columns) list.Columns.Add(column.Name, column.Width);
        return list;
    }

    public static TableLayoutPanel Page(params RowStyle[] rows)
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(24), RowCount = rows.Length, BackColor = ThemePalette.Light.Surface };
        Theme.SetRole(panel, ThemeRole.Surface);
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var row in rows) panel.RowStyles.Add(row);
        return panel;
    }
    public static RowStyle Auto() => new(SizeType.AutoSize);
    public static RowStyle Fill() => new(SizeType.Percent, 100);

    public static string Size(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes; var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.##} {units[unit]}";
    }
    public static string Status(Models.TransferStatus status) => status switch
    {
        Models.TransferStatus.WaitingForAcceptance => "Awaiting approval",
        Models.TransferStatus.Pending => "Preparing",
        Models.TransferStatus.Cancelled => "Canceled",
        _ => status.ToString()
    };
    public static string Fingerprint(string value) => string.Join(" ", Enumerable.Range(0, value.Length / 4).Select(i => value.Substring(i * 4, 4)));
}
