# Phase 3 transfer domain

Implemented on 2026-10-08. These models describe operations; they do not select, open, send, receive or verify actual files.

## Metadata and identity

`TransferFile(Guid id, string relativePath, long sizeBytes, DateTimeOffset? modifiedAt = null)` is immutable. File and transfer IDs are nonempty GUIDs. Relative paths use `/`, preserve hierarchy and Unicode/casing, and obey explicit Windows filename rules regardless of test host. FileName is derived; metadata has no absolute local path, hash, stream or network reference. Empty files and large sizes are supported.

`Transfer.CreateSending(localDeviceId, destinationDeviceId, files, createdAt, transferId = null)` generates an operation ID unless explicitly supplied. `Transfer.CreateReceiving(transferId, sourceDeviceId, localDeviceId, files, createdAt)` preserves the sender's ID. The source/destination identify installations, not current network endpoints. Direction is local: the same operation is Sending on one peer and Receiving on the other.

The manifest is copied once and exposed through a read-only collection of immutable entries. File IDs and case-insensitive paths must be unique, and a path cannot be both a file and a directory. TotalBytes uses checked long arithmetic. Nested file paths represent folder hierarchy; empty directories and folder enumeration are later work.

## Immutable lifecycle

Every transition returns a new Transfer. Assign that value to the coordinator's current state; previously published versions remain unchanged. Equal progress is a harmless no-op returning the existing instance. There are no status setters or general-purpose state restoration methods.

| Current state | Next states |
| --- | --- |
| Pending, Sending | Connecting, Cancelled, Failed |
| Pending, Receiving | WaitingForAcceptance, Cancelled, Failed |
| Connecting | WaitingForAcceptance, Cancelled, Failed |
| WaitingForAcceptance | Transferring, Rejected, Cancelled, Failed |
| Transferring | Completed, Cancelled, Failed |
| Completed / Cancelled / Failed / Rejected | None |

Use MarkConnecting, MarkWaitingForAcceptance, Start, ReportProgress, Complete, Cancel, Reject and Fail. Illegal transitions throw InvalidOperationException. Invalid IDs, manifests, progress bounds, failure data and timestamps throw ArgumentException-family exceptions.

Progress is cumulative, cannot decrease or exceed TotalBytes, and is allowed only while Transferring. Reaching TotalBytes does not automatically imply completion. Complete requires the exact total and must be called explicitly after the coordinator's completion checks. Zero-byte operations still require acceptance/start. Cancelled/Failed versions preserve partial bytes; only Failed carries TransferError. The error contains a defined TransferFailureKind and a nonempty description, without retaining Exception objects.

CreatedAt, StartedAt and FinishedAt are UTC. Callers supply event times. StartedAt is set only when data transfer starts; FinishedAt represents any terminal outcome, including rejection before start. Event times cannot precede creation or, after start, StartedAt. No model contains clocks, timers, cancellation tokens, handlers or UI events.

## Pure-domain example

```csharp
var senderId = Guid.NewGuid();
var receiverId = Guid.NewGuid();
var now = DateTimeOffset.UtcNow;
var files = new[]
{
    new TransferFile(Guid.NewGuid(), "Trip/photo.jpg", 4096),
    new TransferFile(Guid.NewGuid(), "Trip/empty.txt", 0)
};

var outgoing = Transfer.CreateSending(senderId, receiverId, files, now);
var incoming = Transfer.CreateReceiving(outgoing.Id, senderId, receiverId, files, now);

outgoing = outgoing.MarkConnecting().MarkWaitingForAcceptance();
incoming = incoming.MarkWaitingForAcceptance();

// A real coordinator must negotiate acceptance before calling Start.
outgoing = outgoing.Start(now);
incoming = incoming.Start(now);

// These calls model handled bytes; they do not move data.
outgoing = outgoing.ReportProgress(4096).Complete(now);
incoming = incoming.ReportProgress(4096).Complete(now);
```

## Coordinator and Phase 4 boundary

Immutability protects snapshots, but several independent writers could still lose updates. The Phase 4 TransferManager owns and serializes updates, keeps local path mappings separate from metadata, and resolves discovered endpoints through the headless application API.

Relative-path validation alone does not establish filesystem safety. Phase 4 adds destination containment, reparse checks, fresh staging directories, no-overwrite publication, source-change detection, bounded manifests/chunks and cleanup. See `FILE_TRANSFERS.md` for implemented guarantees and platform verification boundaries.

Completed is an explicit domain state. Phase 4 gates it on actual byte counts, completion markers and publication; the sender additionally requires acknowledgment. Phase 7 will add production hash verification. The domain itself neither moves bytes nor cancels I/O.

## Validation

There are 208 transfer-domain test cases covering metadata/path safety, Unicode, large sizes, manifest ownership/collisions, total overflow, peer identity, all documented lifecycle edges, invalid transitions, progress, timestamps and failure outcomes. They use no file fixtures, discovery sockets or sleeps.

The 32 existing networking/discovery tests are retained. Discovery tests now share one nonparallel xUnit collection because they bind the same fixed UDP port; other test collections can still run in parallel. Later prerequisite commits separately repair the production transport/discovery findings; see the review implementation update.

On Windows, run `dotnet build Lantern.sln` and `dotnet test Lantern.sln`. In this Linux workspace, the original solution was cross-built with EnableWindowsTargeting and model/backend tests ran through the pre-existing source-linked net10.0 test harness. Windows runtime and physical-LAN behavior remain unverified.
