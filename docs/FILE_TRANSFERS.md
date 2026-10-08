# Phase 4: actual file-transfer backend

Implemented 2026-10-08. The headless backend negotiates and transfers files; the WinForms interface remains Phase 5 work. See PHASE_PROGRESS.md for implementation versus platform verification.

## Application ownership

`Lantern.Application.LanternApplication` loads a persistent installation ID, starts TransferManager's TCP listener, and then advertises the actual listening port through DeviceDiscovery. Shutdown stops discovery first, cancels transfers, closes their connections, and drains owned workers/listeners. Failed discovery startup rolls back the TCP listener. Startup cancellation tokens cover startup; StopAsync/DisposeAsync own lifetime shutdown.

The application exposes detached device snapshots, DevicesChanged, DiscoveryError/DiscoveryCompletion, and Transfers. UI code should use this layer and marshal callbacks to its UI thread; backend callbacks run independently of the caller's synchronization context. Do not synchronously wait for service shutdown inside one of its own callbacks.

## Public transfer API

```csharp
await using var app = new LanternApplication("Laptop");
app.Transfers.IncomingTransferRequested += (_, request) =>
{
    // A UI should present this decision asynchronously; this example uses a chosen folder.
    request.Accept(chosenExistingDestinationFolder);
    // Alternatively: request.Reject();
};
app.Transfers.TransferChanged += (_, args) => ShowSnapshot(args.Transfer);
await app.StartAsync();

var peer = app.GetDevices().First(device => device.Status == DeviceStatus.Online);
var result = await app.SendFilesAsync(peer.Id, selectedFilePaths, cancellationToken);
```

For headless/direct endpoint use, construct TransferSourceFile values with `FromPath(path, relativePath?)`, then call `Transfers.SendAsync(device, sources, token)`. Absolute SourcePath is a local-only selection value and never appears in wire metadata. A relative path such as `Trip/day-1/photo.jpg` preserves file hierarchy. Folder enumeration and empty-directory representation are not implemented.

`IncomingTransferRequestEventArgs.Transfer` is an immutable waiting-state snapshot. Accept validates an existing destination directory. Accept/Reject are first-decision-wins and return false if the decision has already been made or expired. Without a subscriber, requests are rejected. Expired/stopped requests cannot later start a transfer.

`Cancel(transferId)` cancels real owned I/O; terminal states remain fixed. Expected operational failures produce a terminal Failed/Cancelled Transfer; programming failures are reported through Error and remain observable as task failures. Error callbacks are supervised and logged through Trace. GetTransfers returns immutable state versions; retained terminal history is bounded in memory, not persisted.

## Wire protocol and limits

One operation owns one TCP connection. The existing length-prefixed Connection carries all typed control and chunk messages; no component writes unframed bytes behind it.

1. Both peers exchange Hello with exact protocol version and nonempty installation ID. The sender checks the connected claim against the selected peer; the request's source/destination must match those claims.
2. The sender transmits TransferRequest with operation ID and ordered file metadata (ID, relative path, size, optional modified time).
3. The receiver validates participants, manifest bounds and all domain invariants before presenting the request. It sends TransferAccepted or TransferRejected.
4. For each file, FileChunk messages carry transfer ID, file ID, exact cumulative offset and bytes; FileComplete follows even for zero-byte files.
5. TransferComplete follows all files. The receiver validates every byte count and marker, flushes/closes files, and publishes the directory.
6. Only a matching TransferAcknowledged permits the sender to report Completed. Reaching TotalBytes alone is insufficient.

Bounds: 1 MiB serialized frame cap, 64 KiB chunk bytes, 1024 files per manifest, default two simultaneous incoming/outgoing workers, default 256 retained transfer records. Request timeout defaults to two minutes; inactivity/connect timeout is 30 seconds. Progress state is updated for actual handled bytes; notifications are throttled to approximately ten per second after the first chunk, with immediate state/terminal notifications.

JSON byte arrays use base64. This keeps framing and typed serialization unified and memory bounded, at the cost of encoding overhead. A later measured performance change can introduce a deliberate binary chunk format; raw stream writes mixed with the current framing are not permitted.

## Receive destination and cleanup

An accepted operation stages files under a fresh `.lantern-<random>.partial` child of the selected directory, then atomically renames that directory to `Lantern-<transfer-id>` after complete validation. File order, offsets, identities, chunk limits and sizes are checked. Files use CreateNew, and an existing final directory is never overwritten. A later-file failure does not publish earlier files.

Logical paths obey the Phase 3 Windows rules on every test host. Receiver resolution checks containment and rejects existing reparse-point/symbolic-link ancestors before writes and publication. The selected directory must not be concurrently redirected by local software; descriptor-relative defenses against a hostile local filesystem mutator are not claimed by this phase.

Cancellation, disconnects, invalid messages and timeouts remove the staging tree. OS permission/disk failures can prevent cleanup; that failure is logged instead of deleting unrelated user files. Completed files remain published if the final acknowledgment is lost: the receiver can be Completed while the sender reports failure/uncertainty. Retry/resume/idempotent recovery is later work.

A local cancellation closes the operation connection. Its peer reports the resulting interruption as Failed; the wire does not promise a bilateral Cancelled status. No worker or bytes continue invisibly after cancellation/stop has drained.

## Verification

The full source-linked Linux backend suite passes 299 cases: 208 transfer-domain cases, 32 original networking/discovery cases, 20 added prerequisite reliability cases, 14 transfer serialization cases, 22 real file-transfer integration cases and 3 application/UDP-to-TCP startup cases. The Windows-targeted solution and Linux headless harness build with zero warnings/errors.

The integration suite checks actual bytes for multiple/nested/empty files and a streamed 16 MiB file, cancellation on either side, cleanup, changed/deleted sources, missing markers, invalid identities/offsets/chunk sizes, destination collisions, late decisions, inactivity timeouts, shutdown, and incorrect acknowledgments. Tests compare hashes as evidence; production hash exchange/verification is still Phase 7 work.

Windows runtime, physical two-machine discovery, multiple-adapter behavior, and Windows reparse-point semantics still require field validation. Production encryption/authentication is Phase 8 work; advertised/Hello installation IDs are claims rather than authenticated trust.

## Two Windows machines: headless field verification

Build with the .NET 10 SDK on each machine. Use one application instance per installation/discovery UDP port.

On the receiving machine:

```powershell
dotnet run --project .\Lantern.Harness -- receive "C:\Users\You\Downloads\Lantern"
```

Choose an existing directory. This explicit receive command accepts incoming requests into that directory until Ctrl+C. It prints the receiver installation ID and actual TCP port.

On the sending machine:

```powershell
dotnet run --project .\Lantern.Harness -- send <receiver-id> <receiver-ipv4> <receiver-port> "C:\Data\photo.jpg" "C:\Data\notes.txt"
```

Verify a new `Lantern-<transfer-id>` directory, exact sizes/content, online discovery state through the application API, cancellation/peer shutdown, restart-stable IDs, and device Offline → Online transitions. Check Wi-Fi/Ethernet combinations and Windows firewall permissions. Record results in PHASE_PROGRESS.md; the current automated checks do not establish those field outcomes.
