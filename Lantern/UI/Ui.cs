namespace Lantern.UI;

internal static class Ui
{
    public static readonly Color Ink = Color.FromArgb(26, 40, 62);
    public static readonly Color Accent = Color.FromArgb(38, 91, 207);
    public static readonly Color Muted = Color.FromArgb(88, 102, 121);

    public static Button Button(string text, EventHandler handler, bool primary = false)
    {
        var button = new Button { Text = text, AutoSize = true, MinimumSize = new Size(100, 36), Padding = new Padding(10, 3, 10, 3),
            FlatStyle = FlatStyle.Flat, BackColor = primary ? Accent : Color.White, ForeColor = primary ? Color.White : Ink,
            Cursor = Cursors.Hand, Margin = new Padding(0, 0, 10, 0), AccessibleName = text.Replace("&", "") };
        button.FlatAppearance.BorderColor = primary ? Accent : Color.FromArgb(205, 214, 226);
        button.Click += handler;
        return button;
    }

    public static Label Label(string text, bool title = false) => new()
    {
        Text = text, UseMnemonic = false, AutoSize = true, ForeColor = title ? Ink : Muted, Dock = DockStyle.Fill,
        Font = new Font("Segoe UI", title ? 17 : 10, title ? FontStyle.Bold : FontStyle.Regular),
        Margin = new Padding(0, 0, 0, 12)
    };

    public static FlowLayoutPanel Toolbar(params Control[] controls)
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = new Padding(0, 0, 0, 14) };
        panel.Controls.AddRange(controls);
        return panel;
    }

    public static ListView List(params (string Name, int Width)[] columns)
    {
        var list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false,
            MultiSelect = false, ShowItemToolTips = true, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.White, ForeColor = Ink, Margin = new Padding(0, 0, 0, 14) };
        foreach (var column in columns) list.Columns.Add(column.Name, column.Width);
        return list;
    }

    public static TableLayoutPanel Page(params RowStyle[] rows)
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(24), RowCount = rows.Length, BackColor = Color.White };
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
    public static string Fingerprint(string value) => string.Join(" ", Enumerable.Range(0, value.Length / 4).Select(i => value.Substring(i * 4, 4)));
}
