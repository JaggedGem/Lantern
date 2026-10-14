using Lantern.Models;

namespace Lantern.UI;

internal sealed class TransferDetailsWindow : Form
{
    private readonly TextBox _details = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, AccessibleName = "Transfer details" };
    private readonly ProgressBar _progress = new() { Dock = DockStyle.Fill, AccessibleName = "Transfer progress", Height = 18 };
    private readonly Button _cancel;
    public TransferDetailsWindow(TransferRow transfer, Action<Guid> cancel)
    {
        AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Transfer details"; Font = new Font("Segoe UI", 10); ClientSize = new Size(700, 540); MinimumSize = new Size(540, 420);
        StartPosition = FormStartPosition.CenterParent;
        var layout = Ui.Page(Ui.Auto(), Ui.Fill(), Ui.Auto(), Ui.Auto());
        layout.Controls.Add(Ui.Label(Ui.Text("Transfer_details"), true), 0, 0); layout.Controls.Add(_details, 0, 1);
        layout.Controls.Add(_progress, 0, 2);
        _cancel = Ui.Button(Ui.Text("Cancel_transfer_2"), (_, _) => cancel(transfer.Id));
        layout.Controls.Add(Ui.Toolbar(_cancel, Ui.Button(Ui.Text("Close"), (_, _) => Close())), 0, 3);
        Controls.Add(layout); UpdateTransfer(transfer);
    }
    public void UpdateTransfer(TransferRow transfer)
    {
        _progress.Value = transfer.Percent; _cancel.Enabled = !transfer.IsFinished;
        var eta = transfer.Remaining is { } remaining ? $"{remaining.TotalMinutes:0} min {remaining.Seconds}s" : "Calculating…";
        _details.Text = $"Device: {transfer.Peer}\r\nDirection: {transfer.Direction}\r\nStatus: {Ui.Status(transfer.State)}\r\nFiles: {(transfer.FileCount == 0 ? "Summary from history" : transfer.FileCount)}\r\nTotal size: {Ui.Size(transfer.TotalBytes)}\r\nTransferred: {Ui.Size(transfer.BytesTransferred)} ({transfer.Percent}%)\r\nAverage speed: {Ui.Size((long)transfer.Speed)}/s\r\nEstimated remaining: {(transfer.IsFinished ? "—" : eta)}\r\nCurrent file: {transfer.CurrentFile ?? "—"}\r\nStarted: {transfer.StartedAt.LocalDateTime:g}\r\n\r\n{transfer.Files}" +
            (transfer.Error is null ? "" : $"\r\n\r\nError: {transfer.Error}") + (transfer.SavedFolder is null ? "" : $"\r\n\r\nSaved to: {transfer.SavedFolder}");
    }
}
