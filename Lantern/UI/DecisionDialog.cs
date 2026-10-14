namespace Lantern.UI;

/// <summary>A modeless approval window so progress, cancellation and shutdown keep working.</summary>
internal sealed class DecisionDialog : Form
{
    public bool Accepted { get; private set; }
    public DecisionDialog(string title, string description, string details, string acceptText)
    {
        Text = title; Font = new Font("Segoe UI", 10); StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(650, 440); MinimumSize = new Size(580, 400); MaximizeBox = false; MinimizeBox = false;
        BackColor = Color.White; ShowInTaskbar = false;
        var layout = Ui.Page(Ui.Auto(), Ui.Fill(), Ui.Auto(), Ui.Auto());
        var heading = Ui.Label(description); heading.MaximumSize = new Size(580, 0);
        layout.Controls.Add(heading, 0, 0);
        layout.Controls.Add(new TextBox { Text = details, Multiline = true, ReadOnly = true, Dock = DockStyle.Fill,
            ScrollBars = ScrollBars.Vertical, AccessibleName = "Request details" }, 0, 1);
        layout.Controls.Add(Ui.Label("This request expires after two minutes. Closing this window declines it."), 0, 2);
        var accept = Ui.Button(acceptText, (_, _) => { Accepted = true; Close(); }, true);
        var reject = Ui.Button("Decline", (_, _) => Close());
        layout.Controls.Add(Ui.Toolbar(reject, accept), 0, 3);
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
