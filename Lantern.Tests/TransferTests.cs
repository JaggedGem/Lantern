using Lantern.Models;
using Xunit;

namespace Lantern.Tests;

public sealed class TransferTests
{
    private static readonly Guid LocalId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid RemoteId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset Created = new(2026, 10, 8, 12, 0, 0, TimeSpan.FromHours(3));
    private static readonly DateTimeOffset Started = Created.AddSeconds(10);
    private static readonly DateTimeOffset Finished = Created.AddSeconds(20);
    private static readonly TransferError Failure = new(TransferFailureKind.Connection, "Peer disconnected.");

    private static TransferFile File(string path = "file.bin", long size = 100)
        => new(Guid.NewGuid(), path, size);

    private static Transfer Pending(TransferDirection direction = TransferDirection.Sending, long size = 100)
        => direction == TransferDirection.Sending
            ? Transfer.CreateSending(LocalId, RemoteId, [File(size: size)], Created)
            : Transfer.CreateReceiving(Guid.NewGuid(), RemoteId, LocalId, [File(size: size)], Created);

    private static Transfer Waiting(Transfer transfer)
        => (transfer.Direction == TransferDirection.Sending ? transfer.MarkConnecting() : transfer)
            .MarkWaitingForAcceptance();

    private static Transfer InState(TransferDirection direction, TransferStatus status)
    {
        var pending = Pending(direction);
        return status switch
        {
            TransferStatus.Pending => pending,
            TransferStatus.Connecting => pending.MarkConnecting(),
            TransferStatus.WaitingForAcceptance => Waiting(pending),
            TransferStatus.Transferring => Waiting(pending).Start(Started).ReportProgress(100),
            TransferStatus.Completed => Waiting(pending).Start(Started).ReportProgress(100).Complete(Finished),
            TransferStatus.Cancelled => pending.Cancel(Finished),
            TransferStatus.Failed => pending.Fail(Failure, Finished),
            TransferStatus.Rejected => Waiting(pending).Reject(Finished),
            _ => throw new ArgumentOutOfRangeException(nameof(status))
        };
    }

    [Fact]
    public void BothPeersShareOperationAndManifestIdentityWithOppositeLocalDirections()
    {
        var files = new[] { File("Trip/photo.jpg", 3_000_000_000), File("Trip/notes.txt", 7) };
        var outgoing = Transfer.CreateSending(LocalId, RemoteId, files, Created);
        var incoming = Transfer.CreateReceiving(outgoing.Id, LocalId, RemoteId, files, Created.AddSeconds(1));

        Assert.NotEqual(Guid.Empty, outgoing.Id);
        Assert.Equal(outgoing.Id, incoming.Id);
        Assert.Equal(outgoing.SourceDeviceId, incoming.SourceDeviceId);
        Assert.Equal(outgoing.DestinationDeviceId, incoming.DestinationDeviceId);
        Assert.Equal(LocalId, outgoing.SourceDeviceId);
        Assert.Equal(RemoteId, outgoing.DestinationDeviceId);
        Assert.Equal(TransferDirection.Sending, outgoing.Direction);
        Assert.Equal(TransferDirection.Receiving, incoming.Direction);
        Assert.Equal(files, outgoing.Files);
        Assert.Equal(files, incoming.Files);
        Assert.Equal(3_000_000_007L, outgoing.TotalBytes);
        Assert.Equal(outgoing.TotalBytes, incoming.TotalBytes);
        Assert.Equal(TransferStatus.Pending, outgoing.Status);
        Assert.Equal(0, outgoing.TransferredBytes);
        Assert.Equal(Created.ToUniversalTime(), outgoing.CreatedAt);
        Assert.Equal(TimeSpan.Zero, outgoing.CreatedAt.Offset);
        Assert.Null(outgoing.StartedAt);
        Assert.Null(outgoing.FinishedAt);
        Assert.Null(outgoing.Error);
    }

    [Fact]
    public void SendingPreservesAnExplicitOperationId()
    {
        var id = Guid.NewGuid();
        var transfer = Transfer.CreateSending(LocalId, RemoteId, [File()], Created, id);
        Assert.Equal(id, transfer.Id);
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    public void RejectsEmptyOperationOrParticipantIds(bool sending, int emptyIndex)
    {
        var ids = new[] { Guid.NewGuid(), LocalId, RemoteId };
        ids[emptyIndex] = Guid.Empty;
        Assert.Throws<ArgumentException>(() => sending
            ? Transfer.CreateSending(ids[1], ids[2], [File()], Created, ids[0])
            : Transfer.CreateReceiving(ids[0], ids[1], ids[2], [File()], Created));
    }

    [Theory]
    [InlineData(TransferDirection.Sending)]
    [InlineData(TransferDirection.Receiving)]
    public void RejectsSelfTransfer(TransferDirection direction)
        => Assert.Throws<ArgumentException>(() => direction == TransferDirection.Sending
            ? Transfer.CreateSending(LocalId, LocalId, [File()], Created)
            : Transfer.CreateReceiving(Guid.NewGuid(), LocalId, LocalId, [File()], Created));

    [Fact]
    public void OwnsItsManifestAndPublishesAReadOnlyCollection()
    {
        var first = File("first.txt");
        var input = new List<TransferFile> { first };
        var transfer = Transfer.CreateSending(LocalId, RemoteId, input, Created);
        input[0] = File("replacement.txt");
        input.Add(File("extra.txt"));

        Assert.Single(transfer.Files);
        Assert.Same(first, transfer.Files[0]);
        var published = Assert.IsAssignableFrom<IList<TransferFile>>(transfer.Files);
        Assert.True(published.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => published[0] = File());
        Assert.Equal(100, transfer.TotalBytes);
    }

    [Fact]
    public void EnumeratesManifestOnlyOnce()
    {
        var enumerations = 0;
        IEnumerable<TransferFile> Manifest()
        {
            enumerations++;
            if (enumerations > 1) throw new InvalidOperationException("Manifest enumerated again.");
            yield return File("folder/first.txt", 10);
            yield return File("folder/second.txt", 20);
        }

        var transfer = Transfer.CreateSending(LocalId, RemoteId, Manifest(), Created);
        var next = transfer.MarkConnecting();
        Assert.Equal(1, enumerations);
        Assert.Equal(30, next.TotalBytes);
        Assert.Same(transfer.Files, next.Files);
    }

    [Fact]
    public void RejectsNullEmptyAndNullEntryManifests()
    {
        Assert.Throws<ArgumentNullException>(() => Transfer.CreateSending(LocalId, RemoteId, null!, Created));
        Assert.Throws<ArgumentException>(() => Transfer.CreateSending(LocalId, RemoteId, [], Created));
        Assert.Throws<ArgumentException>(() => Transfer.CreateSending(LocalId, RemoteId, [null!], Created));
    }

    [Fact]
    public void RejectsDuplicateFileIdsEvenForDistinctPaths()
    {
        var id = Guid.NewGuid();
        var files = new[] { new TransferFile(id, "a.txt", 1), new TransferFile(id, "b.txt", 2) };
        Assert.Throws<ArgumentException>(() => Transfer.CreateSending(LocalId, RemoteId, files, Created));
    }

    [Theory]
    [InlineData("file.txt", "file.txt")]
    [InlineData("folder/file.txt", "FOLDER/FILE.txt")]
    [InlineData("a", "a/b.txt")]
    [InlineData("A/B/c.txt", "a/b")]
    public void RejectsPathAndFileDirectoryCollisionsInEitherOrder(string first, string second)
    {
        var files = new[] { File(first), File(second) };
        Assert.Throws<ArgumentException>(() => Transfer.CreateSending(LocalId, RemoteId, files, Created));
        Assert.Throws<ArgumentException>(() => Transfer.CreateSending(LocalId, RemoteId, files.Reverse(), Created));
    }

    [Fact]
    public void FilePrefixesWhichAreNotSegmentsDoNotCollide()
    {
        var transfer = Transfer.CreateSending(LocalId, RemoteId, [File("a"), File("ab/c.txt")], Created);
        Assert.Equal(2, transfer.Files.Count);
    }

    [Fact]
    public void ChecksTotalOverflowAndAllowsTheLargestSupportedTotal()
    {
        var transfer = Transfer.CreateSending(LocalId, RemoteId, [File("large", long.MaxValue), File("empty", 0)], Created);
        Assert.Equal(long.MaxValue, transfer.TotalBytes);
        var exception = Assert.Throws<ArgumentException>(() => Transfer.CreateSending(LocalId, RemoteId,
            [File("large", long.MaxValue), File("extra", 1)], Created));
        Assert.IsType<OverflowException>(exception.InnerException);
    }

    // The documented graph is expressed independently of the implementation.
    public static IEnumerable<object[]> Transitions()
    {
        var sendingEdges = new HashSet<(TransferStatus, TransferStatus)>
        {
            (TransferStatus.Pending, TransferStatus.Connecting),
            (TransferStatus.Pending, TransferStatus.Cancelled),
            (TransferStatus.Pending, TransferStatus.Failed),
            (TransferStatus.Connecting, TransferStatus.WaitingForAcceptance),
            (TransferStatus.Connecting, TransferStatus.Cancelled),
            (TransferStatus.Connecting, TransferStatus.Failed),
            (TransferStatus.WaitingForAcceptance, TransferStatus.Transferring),
            (TransferStatus.WaitingForAcceptance, TransferStatus.Rejected),
            (TransferStatus.WaitingForAcceptance, TransferStatus.Cancelled),
            (TransferStatus.WaitingForAcceptance, TransferStatus.Failed),
            (TransferStatus.Transferring, TransferStatus.Completed),
            (TransferStatus.Transferring, TransferStatus.Cancelled),
            (TransferStatus.Transferring, TransferStatus.Failed)
        };
        var receivingEdges = sendingEdges.Where(edge => edge.Item1 != TransferStatus.Connecting
            && edge.Item2 != TransferStatus.Connecting).ToHashSet();
        receivingEdges.Add((TransferStatus.Pending, TransferStatus.WaitingForAcceptance));

        foreach (var direction in Enum.GetValues<TransferDirection>())
        foreach (var from in Enum.GetValues<TransferStatus>())
        foreach (var to in Enum.GetValues<TransferStatus>().Where(status => status != TransferStatus.Pending))
        {
            if (direction == TransferDirection.Receiving && from == TransferStatus.Connecting) continue;
            yield return [direction, from, to, (direction == TransferDirection.Sending ? sendingEdges : receivingEdges).Contains((from, to))];
        }
    }

    [Theory]
    [MemberData(nameof(Transitions))]
    public void EnforcesEveryDocumentedStateTransition(TransferDirection direction, TransferStatus from,
        TransferStatus to, bool allowed)
    {
        var original = InState(direction, from);
        Transfer Transition() => to switch
        {
            TransferStatus.Connecting => original.MarkConnecting(),
            TransferStatus.WaitingForAcceptance => original.MarkWaitingForAcceptance(),
            TransferStatus.Transferring => original.Start(Started),
            TransferStatus.Completed => original.Complete(Finished),
            TransferStatus.Cancelled => original.Cancel(Finished),
            TransferStatus.Failed => original.Fail(Failure, Finished),
            TransferStatus.Rejected => original.Reject(Finished),
            _ => throw new ArgumentOutOfRangeException(nameof(to))
        };

        if (!allowed)
        {
            Assert.Throws<InvalidOperationException>(() => Transition());
            return;
        }

        var next = Transition();
        Assert.NotSame(original, next);
        Assert.Equal(from, original.Status);
        Assert.Equal(to, next.Status);
        Assert.Equal(original.Id, next.Id);
        Assert.Equal(original.SourceDeviceId, next.SourceDeviceId);
        Assert.Equal(original.DestinationDeviceId, next.DestinationDeviceId);
        Assert.Equal(original.Direction, next.Direction);
        Assert.Equal(original.CreatedAt, next.CreatedAt);
        Assert.Equal(original.TotalBytes, next.TotalBytes);
        Assert.Equal(original.TransferredBytes, next.TransferredBytes);
        Assert.Same(original.Files, next.Files);
        Assert.Equal(to == TransferStatus.Failed ? Failure : null, next.Error);
        var terminal = to is TransferStatus.Completed or TransferStatus.Cancelled or TransferStatus.Failed or TransferStatus.Rejected;
        Assert.Equal(terminal ? Finished.ToUniversalTime() : (DateTimeOffset?)null, next.FinishedAt);
    }

    [Fact]
    public void CumulativeProgressCreatesSnapshotsAndRequiresExplicitCompletion()
    {
        var started = Waiting(Pending()).Start(Started);
        var partial = started.ReportProgress(40);
        var allBytes = partial.ReportProgress(100);
        var completed = allBytes.Complete(Finished);

        Assert.Equal(0, started.TransferredBytes);
        Assert.Equal(40, partial.TransferredBytes);
        Assert.Equal(TransferStatus.Transferring, allBytes.Status);
        Assert.Null(allBytes.FinishedAt);
        Assert.Same(partial, partial.ReportProgress(40));
        Assert.Equal(TransferStatus.Completed, completed.Status);
        Assert.Equal(Started.ToUniversalTime(), completed.StartedAt);
        Assert.Equal(Finished.ToUniversalTime(), completed.FinishedAt);
        Assert.Equal(TimeSpan.Zero, completed.StartedAt!.Value.Offset);
        Assert.Equal(TimeSpan.Zero, completed.FinishedAt!.Value.Offset);
        Assert.Null(completed.Error);
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(0L)]
    [InlineData(39L)]
    [InlineData(101L)]
    [InlineData(long.MaxValue)]
    public void RejectsNegativeDecreasingAndExcessiveProgress(long bytes)
    {
        var transfer = Waiting(Pending()).Start(Started).ReportProgress(40);
        Assert.Throws<ArgumentOutOfRangeException>(() => transfer.ReportProgress(bytes));
        Assert.Equal(40, transfer.TransferredBytes);
    }

    [Theory]
    [InlineData(TransferStatus.Pending)]
    [InlineData(TransferStatus.Connecting)]
    [InlineData(TransferStatus.WaitingForAcceptance)]
    [InlineData(TransferStatus.Completed)]
    [InlineData(TransferStatus.Cancelled)]
    [InlineData(TransferStatus.Failed)]
    [InlineData(TransferStatus.Rejected)]
    public void ProgressIsOnlyAllowedDuringDataTransfer(TransferStatus state)
        => Assert.Throws<InvalidOperationException>(() => InState(TransferDirection.Sending, state).ReportProgress(0));

    [Fact]
    public void CannotCompleteBeforeAllBytesAreHandled()
    {
        var transfer = Waiting(Pending()).Start(Started).ReportProgress(99);
        Assert.Throws<InvalidOperationException>(() => transfer.Complete(Finished));
    }

    [Fact]
    public void ZeroByteManifestStillRequiresAcceptanceStartAndExplicitCompletion()
    {
        var pending = Pending(size: 0);
        Assert.Throws<InvalidOperationException>(() => pending.Complete(Finished));
        var waiting = Waiting(pending);
        Assert.Throws<InvalidOperationException>(() => waiting.Complete(Finished));
        var completed = waiting.Start(Started).Complete(Finished);
        Assert.Equal(0, completed.TotalBytes);
        Assert.Equal(0, completed.TransferredBytes);
        Assert.Equal(TransferStatus.Completed, completed.Status);
    }

    [Fact]
    public void ProgressSupportsLongByteCounts()
    {
        var completed = Waiting(Pending(size: long.MaxValue)).Start(Started)
            .ReportProgress(long.MaxValue).Complete(Finished);
        Assert.Equal(long.MaxValue, completed.TransferredBytes);
    }

    [Fact]
    public void CancelAndFailPreservePartialProgressAndHaveDistinctErrorSemantics()
    {
        var transfer = Waiting(Pending()).Start(Started).ReportProgress(40);
        var cancelled = transfer.Cancel(Finished);
        var failed = transfer.Fail(Failure, Finished);
        Assert.Equal(40, cancelled.TransferredBytes);
        Assert.Equal(40, failed.TransferredBytes);
        Assert.Null(cancelled.Error);
        Assert.Same(Failure, failed.Error);
        Assert.Equal(Started.ToUniversalTime(), cancelled.StartedAt);
        Assert.Equal(Started.ToUniversalTime(), failed.StartedAt);
        Assert.Throws<ArgumentNullException>(() => transfer.Fail(null!, Finished));
    }

    [Fact]
    public void OutcomesBeforeDataTransferDoNotInventAStartTime()
    {
        var pending = Pending();
        foreach (var outcome in new[] { pending.Cancel(Finished), pending.Fail(Failure, Finished), Waiting(pending).Reject(Finished) })
        {
            Assert.Null(outcome.StartedAt);
            Assert.Equal(Finished.ToUniversalTime(), outcome.FinishedAt);
            Assert.Equal(0, outcome.TransferredBytes);
        }
    }

    [Fact]
    public void RejectsEventTimesBeforeCreationOrStart()
    {
        var pending = Pending();
        var waiting = Waiting(pending);
        var started = waiting.Start(Started).ReportProgress(100);
        Assert.Throws<ArgumentOutOfRangeException>(() => waiting.Start(Created.AddTicks(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => pending.Cancel(Created.AddTicks(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => pending.Fail(Failure, Created.AddTicks(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => waiting.Reject(Created.AddTicks(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => started.Complete(Started.AddTicks(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => started.Cancel(Started.AddTicks(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => started.Fail(Failure, Started.AddTicks(-1)));
        Assert.Equal(TransferStatus.Transferring, started.Status);
    }

    [Fact]
    public void SameInstantWithDifferentOffsetsIsValid()
    {
        var waiting = Waiting(Pending());
        var started = waiting.Start(Created.ToOffset(TimeSpan.FromHours(-5)));
        var cancelled = started.Cancel(Created.ToOffset(TimeSpan.FromHours(8)));
        Assert.Equal(Created.ToUniversalTime(), cancelled.StartedAt);
        Assert.Equal(Created.ToUniversalTime(), cancelled.FinishedAt);
    }
}
