namespace Lantern.UI;

/// <summary>A modeless approval window so progress, cancellation and shutdown keep working.</summary>
internal sealed class DecisionDialog : Form
{
    public string DestinationDirectory => _destination?.Text.Trim() ?? "";
    private TextBox? _destination;
    public bool Accepted { get; private set; }
    public DecisionDialog(string title, string description, string details, string acceptText, string? destination = null)
    {
        AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        Text = title; Font = new Font("Segoe UI", 10); StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(650, 440); MinimumSize = new Size(580, 400); MaximizeBox = false; MinimizeBox = false;
        BackColor = Color.White; ShowInTaskbar = false;
        var layout = Ui.Page(Ui.Auto(), Ui.Fill(), Ui.Auto(), Ui.Auto(), Ui.Auto());
        var heading = Ui.Label(description); heading.MaximumSize = new Size(580, 0);
        layout.Controls.Add(heading, 0, 0);
        layout.Controls.Add(new TextBox { Text = details, Multiline = true, ReadOnly = true, Dock = DockStyle.Fill,
            ScrollBars = ScrollBars.Vertical, AccessibleName = "Request details" }, 0, 1);
        layout.Controls.Add(Ui.Label(Ui.Text("This_request_has_a_limited_time_to_respond_Closing_this_window_declines_it")), 0, 2);
        var accept = Ui.Button(acceptText, (_, _) => { Accepted = true; Close(); }, true);
        var reject = Ui.Button(Ui.Text("Decline"), (_, _) => Close());
        if (destination is not null)
        {
            _destination = new TextBox { Text = destination, Width = 420, AccessibleName = "Receive destination" };
            layout.Controls.Add(Ui.Toolbar(_destination, Ui.Button(Ui.Text("Browse"), (_, _) =>
            {
                using var picker = new FolderBrowserDialog { SelectedPath = _destination.Text };
                if (picker.ShowDialog(this) == DialogResult.OK) _destination.Text = picker.SelectedPath;
            })), 0, 3);
        }
        layout.Controls.Add(Ui.Toolbar(reject, accept), 0, 4);
        CancelButton = reject;
        Controls.Add(layout);
        Shown += (_, _) => reject.Focus();
    }

    public async Task<bool> AskAsync(Form owner, CancellationToken token)
    {
        if (token.IsCancellationRequested) return false;
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        FormClosed += (_, _) => completion.TrySetResult(Accepted);
        Show(owner);
        using var registration = token.Register(() =>
        {
            try { if (IsHandleCreated && !IsDisposed) BeginInvoke((Action)Close); }
            catch (InvalidOperationException) { }
        });
        return await completion.Task;
    }
}
