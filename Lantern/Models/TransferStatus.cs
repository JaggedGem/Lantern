namespace Lantern.Models;

public enum TransferStatus
{
    Pending,
    Connecting,
    WaitingForAcceptance,
    Transferring,
    Completed,
    Cancelled,
    Failed,
    Rejected
}
