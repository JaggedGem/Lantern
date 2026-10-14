namespace Lantern.Models;

public enum TransferFailureKind
{
    Connection,
    FileSystem,
    Protocol,
    Unexpected
}

/// <summary>Failure data without exceptions, transport objects or UI dependencies.</summary>
public sealed class TransferError
{
    public TransferError(TransferFailureKind kind, string description)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown transfer failure kind.");
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        Kind = kind;
        Description = description;
    }

    public TransferFailureKind Kind { get; }
    public string Description { get; }
}
