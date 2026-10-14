namespace Lantern.Models;

/// <summary>
/// Immutable state for one transfer. Transition methods return a new version;
/// the application coordinator must own and sequence updates to the current version.
/// </summary>
public sealed class Transfer
{
    private Transfer(Guid id, Guid sourceDeviceId, Guid destinationDeviceId,
        TransferDirection direction, IReadOnlyList<TransferFile> files, long totalBytes,
        DateTimeOffset createdAt)
    {
        Id = id;
        SourceDeviceId = sourceDeviceId;
        DestinationDeviceId = destinationDeviceId;
        Direction = direction;
        Files = files;
        TotalBytes = totalBytes;
        CreatedAt = createdAt.ToUniversalTime();
        Status = TransferStatus.Pending;
    }

    private Transfer(Transfer previous, TransferStatus status, long? transferredBytes = null,
        DateTimeOffset? startedAt = null, DateTimeOffset? finishedAt = null, TransferError? error = null)
    {
        Id = previous.Id;
        SourceDeviceId = previous.SourceDeviceId;
        DestinationDeviceId = previous.DestinationDeviceId;
        Direction = previous.Direction;
        Files = previous.Files;
        TotalBytes = previous.TotalBytes;
        CreatedAt = previous.CreatedAt;
        Status = status;
        TransferredBytes = transferredBytes ?? previous.TransferredBytes;
        StartedAt = startedAt?.ToUniversalTime() ?? previous.StartedAt;
        FinishedAt = finishedAt?.ToUniversalTime();
        Error = error;
    }

    public Guid Id { get; }
    public Guid SourceDeviceId { get; }
    public Guid DestinationDeviceId { get; }
    public TransferDirection Direction { get; }
    public IReadOnlyList<TransferFile> Files { get; }
    public TransferStatus Status { get; }
    public long TotalBytes { get; }
    public long TransferredBytes { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset? StartedAt { get; }
    public DateTimeOffset? FinishedAt { get; }
    public TransferError? Error { get; }

    public static Transfer CreateSending(Guid localDeviceId, Guid destinationDeviceId,
        IEnumerable<TransferFile> files, DateTimeOffset createdAt, Guid? transferId = null)
    {
        ValidateId(localDeviceId, nameof(localDeviceId));
        ValidateId(destinationDeviceId, nameof(destinationDeviceId));
        var id = transferId ?? Guid.NewGuid();
        ValidateId(id, nameof(transferId));
        return Create(id, localDeviceId, destinationDeviceId, TransferDirection.Sending, files, createdAt);
    }

    public static Transfer CreateReceiving(Guid transferId, Guid sourceDeviceId, Guid localDeviceId,
        IEnumerable<TransferFile> files, DateTimeOffset createdAt)
    {
        ValidateId(transferId, nameof(transferId));
        ValidateId(sourceDeviceId, nameof(sourceDeviceId));
        ValidateId(localDeviceId, nameof(localDeviceId));
        return Create(transferId, sourceDeviceId, localDeviceId, TransferDirection.Receiving, files, createdAt);
    }

    public Transfer MarkConnecting()
    {
        EnsureStatus(TransferStatus.Pending);
        if (Direction != TransferDirection.Sending)
            throw new InvalidOperationException("An incoming transfer does not initiate a connection.");
        return new Transfer(this, TransferStatus.Connecting);
    }

    public Transfer MarkWaitingForAcceptance()
    {
        EnsureStatus(Direction == TransferDirection.Sending ? TransferStatus.Connecting : TransferStatus.Pending);
        return new Transfer(this, TransferStatus.WaitingForAcceptance);
    }

    /// <summary>Enter the data-transfer state after the coordinator has confirmed acceptance.</summary>
    public Transfer Start(DateTimeOffset startedAt)
    {
        EnsureStatus(TransferStatus.WaitingForAcceptance);
        ValidateTimestamp(startedAt, nameof(startedAt));
        return new Transfer(this, TransferStatus.Transferring, startedAt: startedAt);
    }

    /// <summary>Record cumulative handled bytes; reaching the total does not imply success.</summary>
    public Transfer ReportProgress(long transferredBytes)
    {
        EnsureStatus(TransferStatus.Transferring);
        if (transferredBytes < TransferredBytes || transferredBytes > TotalBytes)
            throw new ArgumentOutOfRangeException(nameof(transferredBytes), "Progress must be monotonic and within the total byte count.");
        return transferredBytes == TransferredBytes
            ? this
            : new Transfer(this, Status, transferredBytes: transferredBytes);
    }

    /// <summary>Declare success after the coordinator's required completion checks.</summary>
    public Transfer Complete(DateTimeOffset finishedAt)
    {
        EnsureStatus(TransferStatus.Transferring);
        if (TransferredBytes != TotalBytes)
            throw new InvalidOperationException("A transfer cannot complete before all bytes have been handled.");
        ValidateTimestamp(finishedAt, nameof(finishedAt));
        return new Transfer(this, TransferStatus.Completed, finishedAt: finishedAt);
    }

    public Transfer Cancel(DateTimeOffset finishedAt)
    {
        EnsureNonterminal();
        ValidateTimestamp(finishedAt, nameof(finishedAt));
        return new Transfer(this, TransferStatus.Cancelled, finishedAt: finishedAt);
    }

    public Transfer Reject(DateTimeOffset finishedAt)
    {
        EnsureStatus(TransferStatus.WaitingForAcceptance);
        ValidateTimestamp(finishedAt, nameof(finishedAt));
        return new Transfer(this, TransferStatus.Rejected, finishedAt: finishedAt);
    }

    public Transfer Fail(TransferError error, DateTimeOffset finishedAt)
    {
        EnsureNonterminal();
        ArgumentNullException.ThrowIfNull(error);
        ValidateTimestamp(finishedAt, nameof(finishedAt));
        return new Transfer(this, TransferStatus.Failed, finishedAt: finishedAt, error: error);
    }

    private static Transfer Create(Guid id, Guid sourceDeviceId, Guid destinationDeviceId,
        TransferDirection direction, IEnumerable<TransferFile> files, DateTimeOffset createdAt)
    {
        if (sourceDeviceId == destinationDeviceId)
            throw new ArgumentException("Source and destination must be different installations.", nameof(destinationDeviceId));
        ArgumentNullException.ThrowIfNull(files);
        var manifest = files.ToArray();
        if (manifest.Length == 0)
            throw new ArgumentException("A transfer must contain at least one file.", nameof(files));

        var ids = new HashSet<Guid>();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var file in manifest)
        {
            if (file is null)
                throw new ArgumentException("A manifest cannot contain null files.", nameof(files));
            if (!ids.Add(file.Id))
                throw new ArgumentException("File identifiers must be unique within a transfer.", nameof(files));
            if (!paths.Add(file.RelativePath))
                throw new ArgumentException("File paths must be unique ignoring case.", nameof(files));
            try
            {
                total = checked(total + file.SizeBytes);
            }
            catch (OverflowException exception)
            {
                throw new ArgumentException("Combined file sizes exceed the supported byte count.", nameof(files), exception);
            }
        }

        foreach (var file in manifest)
        {
            for (var index = 0; index < file.RelativePath.Length; index++)
            {
                if (file.RelativePath[index] == '/' && paths.Contains(file.RelativePath[..index]))
                    throw new ArgumentException("A manifest path cannot be both a file and a directory.", nameof(files));
            }
        }

        return new Transfer(id, sourceDeviceId, destinationDeviceId, direction,
            Array.AsReadOnly(manifest), total, createdAt);
    }

    private static void ValidateId(Guid id, string parameterName)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("The identifier cannot be empty.", parameterName);
    }

    private void EnsureStatus(TransferStatus required)
    {
        if (Status != required)
            throw new InvalidOperationException($"The transfer must be {required}; its current state is {Status}.");
    }

    private void EnsureNonterminal()
    {
        if (Status is TransferStatus.Completed or TransferStatus.Cancelled or TransferStatus.Failed or TransferStatus.Rejected)
            throw new InvalidOperationException($"The transfer is already {Status}.");
    }

    private void ValidateTimestamp(DateTimeOffset timestamp, string parameterName)
    {
        if (timestamp < (StartedAt ?? CreatedAt))
            throw new ArgumentOutOfRangeException(parameterName, "Event time cannot precede creation or the start of data transfer.");
    }
}
