using Lantern.Models;

namespace Lantern.Transfers;

/// <summary>An actual asynchronous decision gate; no bytes are written before acceptance.</summary>
public sealed class IncomingTransferRequestEventArgs : EventArgs
{
    private readonly TaskCompletionSource<string?> _decision = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal IncomingTransferRequestEventArgs(Transfer transfer) => Transfer = transfer;
    public Transfer Transfer { get; }
    public bool Accept(string destinationDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        var path = Path.GetFullPath(destinationDirectory);
        if (!Directory.Exists(path)) throw new DirectoryNotFoundException("The destination directory must exist.");
        FileReceiver.RejectReparseAncestors(path);
        return _decision.TrySetResult(path);
    }
    public bool Reject() => _decision.TrySetResult(null);
    internal Task<string?> Decision => _decision.Task;
}
