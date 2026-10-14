using System.Net;

namespace Lantern.Models;

/// <summary>An incoming offer. No files are written until it is explicitly accepted.</summary>
public sealed class TransferRequest(Guid transferId, string senderName, IPAddress address, IReadOnlyList<TransferFile> files)
{
    private readonly TaskCompletionSource<bool> _decision = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Guid TransferId { get; } = transferId;
    public string SenderName { get; } = senderName;
    public IPAddress Address { get; } = address;
    public IReadOnlyList<TransferFile> Files { get; } = files;
    public void AcceptTransfer() => _decision.TrySetResult(true);
    public void RejectTransfer() => _decision.TrySetResult(false);
    internal Task<bool> WaitAsync(CancellationToken cancellationToken) => _decision.Task.WaitAsync(cancellationToken);
}
