using Lantern.Models;

namespace Lantern.Transfers;

public sealed class TransferChangedEventArgs(
    Transfer transfer,
    Guid? currentFileId = null,
    string? destinationDirectory = null
) : EventArgs
{
    public Transfer Transfer { get; } = transfer;
    public Guid? CurrentFileId { get; } = currentFileId;
    public string? DestinationDirectory { get; } = destinationDirectory;
}
