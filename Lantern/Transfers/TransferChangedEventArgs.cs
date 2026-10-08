using Lantern.Models;

namespace Lantern.Transfers;

public sealed class TransferChangedEventArgs(Transfer transfer) : EventArgs
{
    public Transfer Transfer { get; } = transfer;
}
