using Lantern.Transfers;

namespace Lantern.Models;

public class TransferRequest(FileSender sender, TransferFile[] files)
{
    public Guid TransferId { get; } = Guid.NewGuid();
    public FileSender Sender { get; } = sender;
    public TransferFile[] Files { get; } = files;
    
    public void AcceptTransfer() {
        // Logic to accept the transfer request
    }
    
    public void RejectTransfer() {
        // Logic to reject the transfer request
    }
}