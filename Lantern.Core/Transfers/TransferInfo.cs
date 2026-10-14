namespace Lantern.Transfers;

public enum TransferState { Preparing, Connecting, AwaitingApproval, Sending, Receiving, Completed, Rejected, Canceled, Failed }
public enum TransferDirection { Send, Receive }

public sealed record TransferInfo(Guid Id, string Peer, string Files, TransferDirection Direction,
    TransferState State, long BytesTransferred, long TotalBytes, DateTimeOffset StartedAt,
    string? Error = null, string? SavedFolder = null)
{
    public bool IsFinished => State is TransferState.Completed or TransferState.Rejected or TransferState.Canceled or TransferState.Failed;
    public int Percent => TotalBytes == 0 ? (State == TransferState.Completed ? 100 : 0) : (int)Math.Clamp(BytesTransferred * 100d / TotalBytes, 0, 100);
}
