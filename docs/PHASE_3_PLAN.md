# Lantern: comprehensive Phase 3 implementation plan

Prepared: 2026-10-08. Baseline: `ac5bd0bb096566e5fbec07bd021ca28d6e914415`.

Status: proposed design and execution plan; not implemented. Read `PROJECT_SPECIFICATION.md` sections 36–41 and 80, plus `PHASE_1_2_REVIEW.md`, before implementation.

## Outcome and phase boundary

Phase 3 delivers a small, testable transfer domain. It represents one operation between two application installations, its file manifest, direction, lifecycle state, byte progress, timestamps and failure information. It supports single/multiple files and nested logical relative paths without reading files or moving bytes.

At the end, a headless unit test can create an outgoing transfer, represent a matching incoming transfer with the same ID/manifest, advance each through valid states, record progress, and complete, cancel, reject or fail them while preserving invariants. There is no simulated networking hidden behind acceptance methods and no no-op API pretending to send files.

Explicitly deferred:

- Phase 4: TransferManager, protocol request/accept/reject payloads, connection coordination, actual streaming, folder enumeration, source-file lookup, destination resolution, file cleanup and progress dispatch.
- Phase 5: selection/review windows, incoming request dialogs, progress controls, notifications and WinForms binding.
- Phase 6: full operational retry, concurrency policy, disk/network recovery and shutdown coordination.
- Phase 7: hashing and integrity verification. Phase 4 must already validate sizes; final integrity requirements will later gate completion on verified hashes.
- Phase 8: TLS, authentication, trust and certificate management.
- Phase 9: persistent transfer history, settings and packaging.

Basic path representation, legal transitions and cancellation state are domain correctness, so they belong here. They do not implement the later filesystem, cancellation-token, or network mechanisms.

## Relationship to earlier-phase repairs

The review found earlier-phase defects. Repair them in separate changes rather than quietly rewriting networking during Phase 3.

- Pure transfer-domain implementation can start now; it depends on stable participant identifiers, not live discovery objects or sockets.
- Before claiming Phases 1–2 complete, satisfy the repair gates in the review.
- Before Phase 4 uses discovered endpoints for real transfers, verify transport cancellation/disposal, immutable discovery snapshots, retained presence, validated announcements, persistent identity and real LAN behavior.
- A backend project split can make domain/network tests portable, but it is optional prerequisite infrastructure, not permission to reorganize the entire application.

## Existing code to preserve and replace

Keep `Lantern.Models` for domain types and preserve working TCP/discovery/protocol classes. There is no need to rename namespaces or invent a new service/interface hierarchy.

| Existing item | Plan |
| --- | --- |
| `Models/TransferFile.cs` | Replace its unvalidated local-path shape with immutable logical file metadata |
| `Models/TransferRequest.cs` | Remove after verifying references; it currently couples the model to FileSender and exposes no-op accept/reject methods |
| `Transfers/FileSender.cs` | Remove the unused empty placeholder; implement a real sender in Phase 4 |
| `Models/Device.cs`, `LocalDevice.cs` | Keep their identity role; do not store live mutable Device objects inside Transfer |
| `Message`, payloads, serializer, framing | Leave unchanged in Phase 3; extend typed payloads for negotiation only in Phase 4 |
| Empty WinForms UI | Leave phase-specific UI work for Phase 5 |

The inspected revision has no production consumers of TransferRequest or FileSender beyond their own placeholder relationship. Repeat a reference search before removing anything, since the repository may have changed. `TransferFile.FilePath` and `FileHash` likewise have no live usage in this revision. If new callers exist, migrate them explicitly; do not keep an ambiguous absolute/relative path or meaningless compatibility methods merely to avoid changing placeholders.

## Proposed type set

Use these concrete domain types under `Lantern/Models`:

| Type | Role |
| --- | --- |
| `Transfer` | Immutable, validated operation state; methods return the next immutable version |
| `TransferFile` | Immutable entry in the logical manifest |
| `TransferDirection` | `Sending`, `Receiving` from this application's perspective |
| `TransferStatus` | `Pending`, `Connecting`, `WaitingForAcceptance`, `Transferring`, `Completed`, `Cancelled`, `Failed`, `Rejected` |
| `TransferFailureKind` | Small typed classification: `Connection`, `FileSystem`, `Protocol`, `Unexpected` |
| `TransferError` | Failure classification plus a nonempty human-readable description |

The final two small types provide actual value: callers can distinguish failure categories without parsing text or keeping exception/socket objects inside the model. Do not add repositories, coordinators, handlers, factories as separate classes, interfaces, base transfer classes or a per-status class hierarchy.

### Why immutable versions

The current discovery code demonstrates the risk of sharing mutable backend objects with consumers. Immutable Transfer and TransferFile instances avoid that problem from the start. Controlled transition methods return a new Transfer with the same stable ID; callers cannot change its manifest or status using setters.

Use a sealed class with a private constructor and get-only properties, rather than publicly settable/init-only record properties that permit `with` expressions to bypass validation. TransferManager in Phase 4 will own the current version and serialize changes. Immutability makes snapshots safe but does not itself prevent a coordinator from losing concurrent updates; single-owner update sequencing is still required.

Alternative: a mutable aggregate with internal methods is viable if every consumer gets a detached immutable snapshot and ownership is explicit. It requires more machinery and has no advantage for the present pure-domain phase. Prefer the immutable design unless implementation reveals a concrete obstacle.

## TransferFile contract

Recommended shape:

| Property | Type | Contract |
| --- | --- | --- |
| `Id` | `Guid` | Nonempty, stable within the transfer, unique among manifest entries |
| `RelativePath` | `string` | Validated canonical logical path using `/` separators |
| `FileName` | `string` | Derived from the final relative-path segment; never independently supplied |
| `SizeBytes` | `long` | Nonnegative; supports files larger than 2 GiB |
| `ModifiedAt` | `DateTimeOffset?` | Optional metadata supplied by the caller; normalize to UTC |

Use an explicit nonempty ID constructor plus a simple static creation method for new outgoing metadata if useful. Receiving metadata must preserve the sender's entry IDs. File IDs are logical manifest identifiers, not filesystem inode IDs.

Do not store a source absolute path, destination absolute path, Stream, System.IO.FileInfo, FileSender, or Device in this type. Do not calculate a hash or query file metadata. Extension can be derived later if the UI needs it; do not store a second potentially inconsistent filename/extension field now.

### Logical path rules

Use deterministic rules independent of the test host's operating system:

1. A path must be nonempty and relative. Examples: `photo.jpg`, `Trip/day-1/photo.jpg`.
2. Canonical domain values use `/`. Backslashes are rejected here; the future selection builder converts local separators before constructing metadata.
3. Reject rooted paths, drive prefixes, UNC paths, leading/trailing separators, and repeated separators producing empty segments.
4. Reject `.` or `..` segments; never silently resolve traversal in an incoming path.
5. Reject NUL/control characters, Windows-invalid filename characters, alternate-data-stream colon syntax, segments ending in a dot or space, and reserved Windows device names (also when followed by an extension).
6. Preserve valid Unicode and casing; do not lowercase display paths. At manifest level, use `StringComparer.OrdinalIgnoreCase` for conservative collision rejection on the Windows destination.
7. A path string never proves a destination is safe. Phase 4 must combine it with the chosen root, verify containment, handle reparse points/symlinks, enforce real destination constraints, and decide overwrite behavior.

Keep validation local to TransferFile initially. Extract one small internal path validator only if logic becomes unwieldy or there is a second real consumer. Do not rely solely on `Path.GetInvalidFileNameChars` on Linux; Windows semantics must be explicit.

Do not introduce arbitrary manifest-count/path-length limits without a reason. Phase 4 will set wire limits and destination-specific limits. The existing 1 MiB message cap means request serialization may need bounded manifest sizes or segmentation later; Phase 3 metadata alone does not solve that protocol problem.

### Folder representation

Nested RelativePath values preserve file hierarchy, e.g. `Project/src/Main.cs` and `Project/assets/logo.png`. Choosing which top-level folder name to include is a Phase 4 selection-builder policy. Phase 3 does not enumerate directories or represent empty folders. Empty-folder transfer, collisions across selected roots, symlinks and attribute preservation need a deliberate later design.

## Transfer contract

Recommended properties:

| Property | Type | Invariant |
| --- | --- | --- |
| `Id` | `Guid` | Nonempty and stable across all versions and both peer representations |
| `SourceDeviceId` | `Guid` | Nonempty installation identity |
| `DestinationDeviceId` | `Guid` | Nonempty, different from source |
| `Direction` | `TransferDirection` | Defined enum value; local viewpoint |
| `Files` | `IReadOnlyList<TransferFile>` | Nonempty, defensively owned immutable entries |
| `Status` | `TransferStatus` | Changes only through legal transition methods |
| `TotalBytes` | `long` | Checked sum of file sizes; calculated once from the fixed manifest |
| `TransferredBytes` | `long` | Cumulative, monotonic; between 0 and TotalBytes |
| `CreatedAt` | `DateTimeOffset` | UTC, fixed at creation |
| `StartedAt` | `DateTimeOffset?` | Set once when entering Transferring |
| `FinishedAt` | `DateTimeOffset?` | Set once on any terminal outcome |
| `Error` | `TransferError?` | Present exactly for Failed; absent for other states |

`FinishedAt` names the end of successful, failed, rejected or cancelled work unambiguously. Do not label every terminal time `CompletedAt` if that would imply success. A successful completion time is FinishedAt when Status is Completed.

Creation invariants:

- Validate all IDs and enum values; no Guid.Empty, undefined values, or self-transfer.
- Copy an input enumerable exactly once into privately owned storage; expose a true read-only wrapper, not an array cast to IReadOnlyList.
- Reject null entries, empty manifests, duplicate file IDs and case-insensitive duplicate relative paths.
- Reject file/directory-prefix collisions such as file `a` alongside `a/b.txt`.
- Use checked long arithmetic. Reject an aggregate beyond long.MaxValue with a clear argument-validation error, rather than wrapping to a negative total.
- Zero-byte files are valid; a transfer with one or more zero-byte files is valid. An empty file list is not a zero-byte transfer.
- The manifest, participants, transfer ID, direction, CreatedAt and TotalBytes never change after creation.

### Creation APIs

Use two named creation methods to make direction and participants obvious:

- `CreateSending(localDeviceId, destinationDeviceId, files, createdAt, transferId?)`: source is local, destination is remote. Generate the transfer ID if omitted; reject an explicitly empty ID.
- `CreateReceiving(transferId, sourceDeviceId, localDeviceId, files, createdAt)`: source is remote, destination is local. Require the sender-supplied nonempty transfer ID; do not invent a new logical operation ID on receipt.

Both begin Pending with 0 transferred bytes and no start/finish/error data. The sender and receiver have separate local state/timestamps/directions while sharing operation identity and manifest. A transfer ID is independent of a protocol message ID; multiple messages will refer to one transfer in Phase 4.

Storing only stable participant IDs avoids retaining a mutable discovered endpoint or an obsolete IP. Phase 4 resolves a fresh endpoint when connecting and preserves display-name snapshots separately if history requires them. It must verify the connected peer's claimed installation identity without confusing that identity claim with authenticated trust.

## Legal lifecycle and transition APIs

Every transition validates the current state and returns a new immutable Transfer with the same ID. No public general-purpose `SetStatus` or constructor restoring an arbitrary state is needed now.

| Current status | Permitted next statuses | Details |
| --- | --- | --- |
| Pending | Connecting, WaitingForAcceptance, Cancelled, Failed | Connecting only for Sending; direct WaitingForAcceptance only for Receiving |
| Connecting | WaitingForAcceptance, Cancelled, Failed | Only Sending reaches this state |
| WaitingForAcceptance | Transferring, Rejected, Cancelled, Failed | Acceptance must have occurred in the future coordinator before entering Transferring |
| Transferring | Completed, Cancelled, Failed | Completion requires exact total bytes |
| Completed / Cancelled / Failed / Rejected | None | Terminal; retries create a new transfer ID |

Suggested narrow methods:

- `MarkConnecting()` and `MarkWaitingForAcceptance()`.
- `Start(startedAt)` sets StartedAt exactly once.
- `ReportProgress(transferredBytes)` records cumulative bytes only while Transferring.
- `Complete(finishedAt)` checks exact byte count.
- `Cancel(finishedAt)` works from any nonterminal state.
- `Reject(finishedAt)` works only while WaitingForAcceptance.
- `Fail(error, finishedAt)` requires nonnull validated error and any nonterminal state.

Invalid metadata arguments throw a clear ArgumentException-family exception. Illegal state changes throw InvalidOperationException. Use consistent behavior for attempts to change a terminal operation; the future coordinator can make repeated external cancellation commands idempotent without mutating terminal snapshots.

Do not add Accepted or Verifying solely for anticipated UI labels. Acceptance is a decision leading to Transferring. Phase 7 may add Verifying when it implements a real integrity step. At that point, Completed must mean integrity succeeded.

## Progress and time invariants

- Cumulative byte updates must be `>=` the previous count and `<= TotalBytes`; negative or decreasing values are rejected. An identical count is a harmless no-op.
- Progress does not automatically change status to Completed. The coordinator explicitly declares completion after all required checks and peer acknowledgments.
- Completed requires `TransferredBytes == TotalBytes`. Pending/waiting cannot complete, even for a zero-byte manifest; zero-byte files still pass through acceptance and transfer state.
- Cancelled/Failed keep the last known byte count. Never set progress to 100% to make a cancelled UI look finished.
- Expose bytes as the source of truth. If a convenience fraction is added, define zero-total behavior (0 before completion, 1 after Completed) and avoid integer division; the UI should never equate byte fraction with success.
- Pass timestamps explicitly to creation/start/finish APIs and normalize DateTimeOffset to UTC. This removes hidden wall-clock dependence from domain tests without introducing a clock interface into every model.
- Validate CreatedAt <= StartedAt <= FinishedAt when timestamps exist. A transfer cancelled/rejected/failed before Start has no StartedAt. Terminal timestamps are fixed.
- Wall-clock corrections are an orchestration concern: the future coordinator must supply coherent event times and use monotonic timestamps for speed/ETA. Do not store throughput, ETA, cancellation tokens, delegates or timers in the Transfer model.

## Failure information

TransferError contains a defined TransferFailureKind and a nonempty description. Store data callers can inspect; do not retain Exception objects, stack traces, socket handles or arbitrary object payloads. Low-level diagnostic details belong in logs, and the UI may later map categories to friendly/localized text.

Rejecting a request or cancelling by choice is a dedicated status, not a failure disguised by an error string. Protocol-level rejection reasons can be added with the negotiation implementation in Phase 4; do not create unused wire DTOs in Phase 3.

## Execution plan and reviewable changes

### Step 0 — Confirm the baseline and test arrangement

- Repeat source/reference inspection and compare changes since the reviewed commit.
- Record Phase 1–2 repair work separately; do not assume older checklists mean those phases are finished.
- Use the existing tests on Windows. On Linux, pure backend execution needs the source-linked harness or an explicitly scoped `Lantern.Core` extraction.
- If extracting Lantern.Core, move backend models/networking/transfer code with namespaces preserved; WinForms references it, tests/harness reference Core. Keep that change separately reviewable and do not invent new architectural interfaces.

Acceptance: a reproducible way to run pure model tests and no accidental WinForms dependency in domain code.

### Step 1 — Implement file metadata and domain enums

- Add direction/status/failure enums and TransferError.
- Replace TransferFile with its validated immutable logical-path shape.
- Add deterministic Windows-relative-path and metadata tests, including very large sizes and zero-byte files.

Acceptance: valid file metadata can be created without disk access; invalid paths/IDs/sizes are rejected on Windows and Linux consistently.

### Step 2 — Implement Transfer creation and manifest invariants

- Add sealed immutable Transfer with named sending/receiving factories.
- Add defensive collection ownership, stable IDs, participant direction, manifest collision checks and checked totals.
- Add construction/immutability tests, especially input-array mutation and receiving ID preservation.

Acceptance: one or many files and nested paths work; malformed or overflowing manifests cannot create a Transfer.

### Step 3 — Implement legal state changes, progress and timestamps

- Implement only the narrow transition APIs described above.
- Add exhaustive state-transition tests, byte bounds, completion constraints, error invariants and deterministic timestamp tests.
- Add a pure-domain example of outgoing/incoming snapshots for the same logical transfer; it performs no networking or file I/O.

Acceptance: invalid transitions fail clearly, terminal states cannot be changed, progress cannot regress/overflow, and timestamps/failure data remain coherent.

### Step 4 — Remove misleading placeholders and document the domain

- Remove unused TransferRequest and empty FileSender after the reference search; migrate any newly discovered callers.
- Add a concise domain guide describing IDs, logical paths, immutable updates and later coordinator ownership.
- Update PROJECT_CONTEXT with the implemented API, tests and remaining earlier-phase work.

Acceptance: no public accept/reject method silently does nothing, no domain reference to sender/socket/UI types, and later implementation has a clear entry point.

### Step 5 — Final validation and Phase 4 handoff

- Build the original solution and run meaningful domain tests plus the existing suites using reproducible repository settings.
- Confirm changes do not add network/file I/O to models, mutate discovery behavior, or introduce UI changes.
- Review documentation against implemented signatures and recheck the Phase 3 definition of done.
- Record Phase 4 questions about manifest wire bounds, byte-channel design, overwrite policy, request timeout, source-path mapping and destination safety; do not implement answers here.

Acceptance: Phase 3 completion checklist below passes and remaining Phase 1–2 defects are honestly tracked.

## Meaningful test matrix

| Area | Required cases |
| --- | --- |
| File IDs and sizes | Empty ID, negative size, zero bytes, >2 GiB, long.MaxValue metadata |
| Valid paths | Top-level filename, nested hierarchy, Unicode names, derived filename |
| Unsafe paths | Empty, rooted, UNC, drive/ADS colon, backslash, repeated separators, `.`, `..`, control characters, invalid Windows characters, trailing dot/space, reserved device names with extensions |
| Participants | Empty source/destination, equal IDs, correct source/destination for both factories |
| Transfer IDs | Generated nonempty ID; specified ID preserved; receiving representation matches sender |
| Manifest ownership | Caller mutates original array/list after creation; exposed collection cannot alter storage; immutable file entries |
| Manifest validation | Null/empty inputs, null entry, duplicate IDs, duplicate/case-colliding paths, file-directory-prefix collision |
| Total sizes | Multiple-file sum, zero-byte total, checked overflow, no overflow beyond 2 GiB |
| Transitions | Every legal edge and every illegal edge in the table; direction-specific Connecting rules |
| Progress | Negative, decreasing, identical, increasing, beyond total, before Start, after terminal |
| Completion | Cannot complete early; can complete at exact total; explicit completion for zero-byte files |
| Outcomes | Cancellation preserves partial bytes; failure requires error; reject only while waiting; all terminal states remain fixed |
| Timestamps | Fixed created/start/finish values; UTC normalization; pre-start terminal outcomes; out-of-order event times rejected |
| Error data | Undefined failure kind, empty description, error present only for Failed |
| Separation | Domain tests succeed without disk fixtures, network sockets or forms |

Use xUnit theories for the transition/path matrix. Test behavior and invariants, not every trivial property getter. Do not use Thread.Sleep, real discovery ports, actual file contents, or system application-data directories for Phase 3 tests.

## Definition of done

- [ ] Transfer and file entries have stable nonempty IDs.
- [ ] Direction and lifecycle states are explicit enums with legal transitions.
- [ ] Source/destination are stable installation IDs, independent of IP and connections.
- [ ] Metadata supports multiple files, zero-byte files, large sizes and nested relative paths.
- [ ] Collections and entries cannot be mutated externally.
- [ ] Duplicate/colliding paths and overflowing totals are rejected.
- [ ] Byte progress, completion, timestamps and error data satisfy documented invariants.
- [ ] Domain code performs no file/network I/O and references no forms or transfer implementations.
- [ ] Misleading placeholder no-op acceptance methods are removed or migrated.
- [ ] Meaningful domain tests pass through reproducible test commands.
- [ ] Original solution builds and earlier-phase regressions are checked.
- [ ] Implemented APIs and remaining earlier-phase gaps are recorded in project context.
- [ ] No Phase 4+ implementation was added prematurely.

## Phase 4 handoff and unresolved choices

After Phase 3, TransferManager can own immutable Transfer versions, resolve current device endpoints, negotiate manifests, and call sender/receiver implementations. Those implementations must map logical file IDs to local source paths, resolve safe destinations, stream bounded buffers, cancel real work, clean partial files, and report cumulative successfully handled bytes.

Resolve these when implementing that phase:

1. How control messages and raw/file-chunk bytes coexist. Current Connection transports bounded JSON messages; do not inject arbitrary bytes into its stream without a defined protocol. Choose binary framing or a separate data connection deliberately.
2. Maximum request manifest size/count versus the existing message cap; reject or segment explicitly.
3. Whether simultaneous requests, incoming limits and request timeouts are supported initially.
4. How overwrite conflicts, nested-root collisions, symlinks/reparse points, empty directories and source changes are handled.
5. Which successful byte/acknowledgment and size checks are required before completion; later SHA-256 validation will strengthen that boundary.
6. Which errors are user actions, expected operational failures or protocol violations, and how cancellation reaches all owned work.

None of these requires complicating the Phase 3 model with speculative managers, sockets or UI state now.
