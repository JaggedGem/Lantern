# Lantern: review through Phase 2

Review date: 2026-10-08. Baseline commit: `ac5bd0bb096566e5fbec07bd021ca28d6e914415`.

## Verdict

The existing code is worth building on. Phase 1 has the main transport and protocol pieces; Phase 2 has discovery and presence mechanisms. Neither should currently be marked fully complete against the canonical specification. The most serious gaps are in lifecycle correctness, discovery ownership, retained offline presence, and input validation. The old Phase 2 completion documents overstate what is implemented and tested.

This was a review and planning task. Production source and existing tests were not changed. The review adds project context, findings and a Phase 3 plan. Pure Phase 3 model work can proceed independently, but the repair gates below must be satisfied before declaring the earlier phases complete or relying on them for live transfers.

## Scope and evidence

Inspected every production C# file, project/solution configuration, the two test source files, console handshake harness, README, discovery documentation, Phase 2 summary/checklist, and the full supplied specification. Networking, protocol, discovery, device identity, lifecycle and tests were reviewed in detail. Transfer placeholders and the empty UI were checked for architectural implications.

Observed validation:

| Check | Result and limits |
| --- | --- |
| Original solution build | `dotnet build Lantern.sln --no-restore -p:EnableWindowsTargeting=true -m:1` succeeded; incremental build reported 0 warnings and 0 errors |
| Existing tests, serialized collections | 32 passed, 0 failed, 0 skipped, using the pre-existing source-linked `net10.0` harness in `.lantern-setup/Tests` |
| Existing tests, default collection parallelism | 30 passed, 2 failed; `DeviceDiscovery_RepeatedStartStopWorks` and `DeviceDiscovery_StartsAndStops` failed with `SocketException: Address already in use` |
| Focused review probes | 16 defect observations reproduced, plus a positive check preserving 64 distinct concurrent-send frames; these are diagnostic checks, not 17 existing unit tests |
| Windows runtime / physical LAN | Not run; Linux cross-compilation and source-linked backend execution do not establish Windows runtime or two-machine discovery behavior |

The sandbox initially blocked sockets, including the test runner's own loopback communication. The test run completed after local socket execution was approved by automatic review. No packages were downloaded for these checks. The SDK already present in `/workspace/.dotnet` was version 10.0.401.

Diagnostic source, final JSON observations, baseline TRX, and default-parallel TRX are preserved in `docs/review-evidence/`. The evidence README explains the cross-platform harness and how to rerun the probes. Source observations below are explicitly distinguished from runtime reproductions.

## What is already sound

- Device identity, TCP connections and protocol messages are represented separately. Transfer placeholders are not intertwined with discovery.
- Backend code does not depend on WinForms controls or forms. Networking and discovery can be compiled without the UI, as demonstrated by the source-linked harness.
- The TCP message envelope is strongly typed, with nonempty message IDs and compatible typed payloads. Existing `MessagePayload` records are a modest, useful type-safety boundary; a rewrite to untyped payloads would not improve the architecture.
- JSON serialization contains no network I/O. The internal framing component is delegated to by `Connection`, which is a reasonable implementation of Connection owning framing.
- TCP framing uses 4-byte big-endian lengths, validates incoming lengths before renting a payload buffer, and reads until all bytes arrive.
- Send and receive semaphores prevent overlapping logical operations. The 64-frame diagnostic check confirms the ordinary concurrent-send path preserves complete frames.
- Outbound connect failures dispose the newly allocated client. Cancellation is accepted by connect/send/receive APIs.
- Discovery keys peers by GUID, filters the local GUID, and takes peer IP from the UDP source endpoint. Duplicate announcements and IP changes update an existing entry.
- Discovery uses small bounded packets, async UDP receive/send, periodic heartbeats and cancellable delays. The 15-second threshold tolerates several missed 3-second heartbeats.

## Phase 1 findings

### P01 — High: cancellation after partial framing leaves a reusable but desynchronized channel

Location: `Lantern/Networking/Protocol/FramedMessageChannel.cs:38`, `:67`, `:114`.

After any prefix or payload bytes have been consumed, a cancelled receive exits, releases the lock, and forgets the partially read frame. The next receive interprets remaining bytes as a new prefix. Likewise, cancellation or failure between outbound prefix and payload leaves incomplete bytes on the wire, while a later send is still permitted.

Reproduced: a stream delivered two prefix bytes, then cancelled. Retrying receive raised `InvalidDataException` because framing had been lost. The outbound consequence follows from the separate prefix/payload writes and lack of a faulted state.

Recommendation: choose and document a policy. The simple reliable policy is to terminate/fault the connection after an interrupted frame operation has begun. Cancellation while waiting for a semaphore, before stream I/O, can safely leave it usable. Preserving partial-frame state is possible but adds complexity unnecessary for this phase. Do not retry protocol failures on the same channel blindly.

Required tests: cancellation during prefix/payload reads and writes, cancellation before ownership of the operation, EOF during prefix/body, rejection of later operations on a faulted connection.

### P02 — High: disposal races in-flight operations and semaphore waiters

Location: `FramedMessageChannel.cs:56`, `:95`, `:102`.

`Dispose` disposes both semaphores before closing the stream. An active send/receive can then execute `Release` on a disposed semaphore in its `finally`. Pending waiters also lack an explicit channel-shutdown cancellation mechanism.

Reproduced: disposing during a blocked send produced `ObjectDisposedException` for `System.Threading.SemaphoreSlim`, masking the stream operation's closure exception.

Recommendation: define ownership of in-flight work and close the stream to unblock I/O, cancel shutdown-aware waiters, and defer semaphore disposal until operations have exited (or use a deliberately documented safe lifetime strategy). Make repeated close/dispose harmless. `Connection` should dispose its client even if its channel cleanup fails.

Required tests: dispose during blocked receive/send, several queued operations at shutdown, bounded completion of every caller, repeated sync/async disposal.

### P03 — Medium: server lifecycle and accepted-connection ownership are incomplete

Location: `Lantern/Networking/NetworkServer.cs:55`, `:85`, `:132`, `:142`.

Reproduced: `DisposeAsync` followed by `StartAsync` restarts the server. There is no disposed state.

Source observations: an accepted connection with no subscriber is never disposed by the server; a throwing subscriber faults the accept task and exits the server; unexpected accept-loop failure clears the only task reference and prevents later `StopAsync` from observing it. Disposal of the shutdown source is shared between stop and the accept-loop `finally`. Concurrent stops return as soon as `_isRunning` is false, even if a first stop is still draining; a new start can overlap that drain.

Recommendation: serialize lifecycle transitions, retain the stopping/faulted task until observed, expose unexpected failure, make disposal terminal, and explicitly define who owns an accepted connection and when handoff succeeds. Dispose connections which are not handed off. The server need not own every successfully handed-off connection; the eventual application coordinator can own those.

The startup cancellation token currently checks only entry. This can be a valid startup-token contract, but it must not be described as lifetime cancellation. Define startup versus lifetime semantics consistently for server and discovery.

### P04 — Medium: malformed protocol input has inconsistent exception types

Location: `Lantern/Networking/Protocol/ProtocolSerializer.cs:75`, `:115`.

Reproduced: the root `[]` raises `InvalidOperationException`; message type `2147483648` raises `FormatException`, rather than `JsonException`. Callers handling malformed protocol input as `JsonException` can misclassify these as programming errors.

Recommendation: check root object kind, use `TryGetInt32`, and normalize invalid envelope input to a documented protocol/JSON error. Define duplicate-property handling and whether unknown envelope fields are allowed; payload unknown-member rejection currently does not apply to the manually parsed envelope. Maintain a single documented compatibility policy.

### P05 — Medium: important transport guarantees are not tested

Location: `Lantern.Tests/NetworkingTests.cs:13`.

The 12 networking tests cover Hello/Error serialization, a few malformed messages, sequential framing, partial reads, an oversized incoming length, basic server lifecycle, connect refusal/pre-cancellation, and a Hello/close exchange. They do not cover concurrent send safety, zero/negative lengths, truncated body/prefix, outgoing oversize, mid-operation cancellation, disposal with active work, or accept-handler failure. The review's positive concurrent-send probe fills an evidence gap but should become a proper regression test in a repair change.

### P06 — Low: protocol limits, compatibility and address support need explicit contracts

The 1 MiB cap is a default on an internal channel constructor, not a shared protocol limit available to applications. Outbound messages are fully serialized before the cap is checked. Public APIs do not expose the configured cap. Hello carries an application name/version, but no application-level handshake enforcement, device identity matching or negotiated compatibility exists. TCP server and UDP discovery are IPv4-oriented; arbitrary `IPAddress` acceptance by the client does not establish end-to-end IPv6 support.

Recommendation: centralize a protocol size constant or deliberately expose a connection option, document Hello validation responsibilities and IPv4 support. Device identity exchange belongs to later application negotiation; do not add authentication in the current review or Phase 3.

## Phase 2 findings

### D01 — High: offline devices are deleted immediately, and LastSeen is falsified

Locations: `Lantern/Networking/Discovery/DeviceDiscovery.cs:550`, especially `:573` and `:578`; `Lantern/Models/Device.cs:96`.

The expiration loop marks a peer Offline and removes it in the same pass. This contradicts specification sections 28 and 79. A later heartbeat causes a fresh discovery event instead of an Offline → Online transition of the retained peer. Updating status also refreshes `LastSeen`, making an offline transition look like a new observation.

Reproduced: aging an entry, invoking the expiration check, and announcing it again produced an empty collection after expiry and two discovery events. The offline transition changed the aged last-seen timestamp to the current time.

Recommendation: retain offline entries, refresh LastSeen only on actual observations, and raise one event per real transition. A separate long-term pruning policy may be added when justified; it must not erase peers immediately at the offline threshold. Use an injected `TimeProvider` for deterministic expiration tests and monotonic elapsed-time checks where appropriate.

### D02 — High: mutable references and callbacks under locks violate ownership and concurrency safety

Locations: `DeviceDiscovery.cs:115`, `:425`, `:474`, `:574`; `Lantern/Models/Device.cs`.

`GetDiscoveredDevices` copies the list but returns the internal mutable Device instances; event arguments expose the same instances. Existing devices are mutated while holding an upgradeable-read lock, so other readers may overlap those writes. Subscribers are invoked synchronously while locks are held.

Reproduced: updating the port of a device returned in a snapshot changed discovery's own stored peer. A `DeviceDiscovered` callback which called `GetDiscoveredDevices` aborted before returning due to lock reentry; the packet processor's broad catch hid that exception.

Recommendation: store/return immutable device values or detached snapshots, mutate internal state only with exclusive ownership, capture notifications inside the lock and invoke them outside it. Specify event threading and subscriber exception behavior. A future UI should marshal to its synchronization context; discovery should not know about UI dispatch.

### D03 — High: discovery accepts malformed identities, types and incomplete announcements

Locations: `DeviceDiscovery.cs:373`; `Lantern/Networking/Discovery/DiscoveryProtocol.cs:46`; `Lantern/Models/LocalDevice.cs:27`; `Device.cs:41`.

Version mismatch, empty names, bad ports and packet oversize are rejected, but message type is never checked, `Guid.Empty` is accepted, name length is unbounded within the packet cap, and missing version is replaced by the model's default 1. Device and LocalDevice also allow empty IDs.

Reproduced: type 999, empty GUID, a packet omitting version/type/ID, and a 3500-character name all entered the collection. The processor also accepts loopback-origin packets; local GUID filtering is not a network-interface policy.

Recommendation: require the current schema's fields, validate supported message kinds, nonempty IDs, bounded names, ports, exact version and packet size. Parse UTF-8 directly to avoid silently replacing invalid sequences through string conversion. Give outbound names the same constraints and ensure the serialized announcement always fits the byte cap. A named character limit alone is insufficient if JSON escaping can greatly expand the packet; validate serialized byte size too. Keep malformed-packet rejection separate from subscriber failures.

### D04 — High: persistent identity can silently change and accepts empty stored IDs

Location: `Lantern/Models/LocalDeviceIdentityProvider.cs:20`.

Read failures generate a new ID; writes silently fail; the newly generated ID is still returned. This cannot satisfy stable installation identity. There is no process coordination or atomic replacement, and parsed JsonDocument is not disposed.

Reproduced with an isolated filesystem fixture: forcing the identity file path to be a directory caused two calls to return different GUIDs; a stored empty GUID was returned unchanged.

Recommendation: make the storage path injectable for tests, dispose JSON documents, reject empty IDs, write atomically, define a single-writer/process policy, and report persistence failures explicitly. Do not overwrite a possibly valid unreadable identity as if it were missing. Corrupt-file recovery may back up and replace corrupt data under a documented policy. Async I/O can be introduced without inventing a repository abstraction.

### D05 — Medium: lifecycle cancellation and loop failures are not supervised reliably

Locations: `DeviceDiscovery.cs:132`, `:189`, `:278`, `:334`, `:488`, `:519`.

Source observations: the token passed to StartAsync is not linked to background-task lifetime; startup cancellation or a swallowed initial-send failure may return with background discovery running. Stop detaches shared fields and awaits tasks sequentially; one fault can skip joining the remaining loops. Another stop can return before teardown completes, while a new start overlaps it. Loop `finally` blocks clear shared fields without checking which run they belong to, so an older run can clobber task references of a new run. Background faults can leave `IsRunning` true. Sync Dispose from a discovery callback can wait for its own loop or fail lock reentry.

Recommendation: serialize lifecycle changes, use a per-run state holder, await all loops, and expose faults. Explicitly choose startup-token or lifetime-token semantics. With lifetime cancellation, link the token and ensure cancellation drains the service; with startup cancellation, roll back incomplete startup and document StopAsync as the lifetime control. Test cancellation, concurrent stop/dispose and restart during stopping.

### D06 — Medium: endpoint/name changes lack an observable notification

Location: `DeviceDiscovery.cs:432`.

Reproduced: changing a peer's name/port updated the entry while raising no event after its original discovery. The same is true of IP changes by inspection. Event-driven consumers can keep stale endpoints or display stale names.

Recommendation: use one immutable `DeviceChanged` event, or add a narrowly scoped update event, for material changes and status transitions. Heartbeat-only LastSeen updates should remain quiet to avoid event spam. Do not add multiple overlapping events merely because the conceptual architecture lists them.

### D07 — Medium: multiple active interfaces are not handled explicitly

Location: `DeviceDiscovery.cs:154`, `:319`.

Binding IPv4 Any listens broadly, but a single limited-broadcast send to `255.255.255.255` does not establish delivery on every active LAN adapter. There is no enumeration/filtering of loopback, Wi-Fi/Ethernet, or virtual adapters and no per-interface send strategy.

Recommendation: a small IPv4 interface-selection helper and directed broadcasts on eligible up, non-loopback interfaces, with expected failures handled independently. Refresh eligible interfaces periodically or on simple network-change notification. Keep VPN/virtual-interface eligibility explicit and document the policy. Do not add subnet scanning or a general networking framework. Verify on two Windows machines, then on a host with multiple active adapters.

### D08 — Medium: broad catch blocks hide operational and programming failures

Locations: `DeviceDiscovery.cs:328`, `:353`, `:419`; identity provider `:35`, `:59`.

There is no logging despite comments and documents suggesting errors are logged. A throwing subscriber is swallowed by packet handling, and a persistent receive failure can retry without backoff. Outbound oversize and send failures silently disappear.

Recommendation: narrowly handle expected socket/cancellation/JSON errors, expose actionable operational errors through a small diagnostics mechanism, apply bounded retry/backoff where failures persist, and allow programming errors to remain visible. Avoid adding a large logging framework to repair a few hidden catches.

### D09 — Medium: tests are incomplete, non-isolated and fail under default parallel settings

Locations: `Lantern.Tests/NetworkingTests.cs:258`; `Lantern.Tests/DiscoveryIntegrationTests.cs:15`.

There are 17 discovery tests plus 3 discovery integration tests. Multiple test classes bind the same fixed UDP port. The default-parallel run reproduced two bind failures; passing required an external runsettings file not checked into the repository. The identity test uses the real configured application-data path. Negative reflection-based tests skip invocation if the private method cannot be found, so some can pass without exercising the intended behavior.

The integration tests run a single instance for 0.5–1 second, less than the 3-second heartbeat interval. They assert running/empty/no events; they do not verify actual packet transmission, receipt of remote packets, repeated heartbeat behavior, timeout, rediscovery, or multi-interface operation. Swallowed send errors allow announcement tests to pass even if nothing is sent.

Recommendation: isolate disk paths; fail if a reflection target is missing or expose a small internal parser/state seam; group fixed-port tests into one nonparallel collection or make discovery ports configurable for tests. Prefer deterministic clock-controlled presence tests. Add real UDP receive/announce observations and reserve two-machine broadcast checks for a documented manual/integration gate.

### D10 — Medium: Phase 2 documentation is not reliable completion evidence

Locations: `PHASE2_SUMMARY.md`, `PHASE2_CHECKLIST.md`, `DISCOVERY.md`, `README.md`.

Examples: inconsistent test counts (15, 23, versus actual 32 total); claimed logging which does not exist; claimed read-only snapshots and safe external mutation; claimed lifetime cancellation propagation; unmeasured CPU/network/scalability estimates; incorrect later-phase names; claims that startup wires discovery while Program/MainWindow do not. The checklist calls constants configurable and puts constants in a class which does not actually contain most of them.

Recommendation: replace completion claims with verified requirement status, correct the phase order/counts and startup description, and label performance estimates as estimates unless measured. This review supersedes those claims; the earlier documents were retained unchanged as historical material.

## Requirements coverage summary

### Phase 1

| Requirement | Current status |
| --- | --- |
| TCP server / client / Connection abstraction | Implemented; server ownership and lifecycle gaps remain |
| Clear message model and JSON serializer | Implemented; malformed-envelope exception handling incomplete |
| Length prefix and exact reads | Implemented; partial-cancellation recovery/termination incomplete |
| Invalid/max lengths | Incoming bounded; incomplete edge-case tests and centralized policy |
| Concurrent sends | Ordinary path verified by probe; production regression test absent |
| Partial writes | Stream WriteAsync writes the supplied buffer or throws; interrupted-frame handling is the real gap |
| Cancellation and shutdown | Basic coverage; in-flight teardown and cancelled framing need repair |
| Hello exchange | Existing loopback test and console harness; compatibility enforcement not implemented |
| Meaningful tests | 12 existing networking tests; important failure cases missing |

### Phase 2

| Requirement | Current status |
| --- | --- |
| Persistent installation ID | Happy path; failure/empty-ID behavior violates the requirement |
| Identity-based deduplication and self-filtering | Implemented, except invalid IDs are permitted |
| IP/name/port and source-endpoint IP | Implemented; material-update notifications absent |
| UDP announcements and automatic discovery | Mechanisms exist; actual two-machine behavior unverified |
| Heartbeats | Timer exists; real periodic emission not verified by current tests |
| LastSeen | Updated, but also modified when no peer was observed |
| Offline retained and restored Online | Not met; immediate deletion |
| Observable presence changes | Partial; callbacks are unsafe under locks |
| Validated malformed packets | Partial; significant schema and field gaps |
| Read-only consumer state and concurrency | Not met; mutable aliases and reader/writer ownership issues |
| Multiple interfaces / loopback policy | Incomplete |
| Cancellation, shutdown and resources | Basic path works; race/fault paths incomplete |
| Tests | 20 discovery-related tests; coverage and default execution gaps |
| App startup integration | Absent; UI integration is not required in Phase 2, but backend composition needs a headless demonstration before end-to-end claims |

## Repair gates and ordering

1. **Transport lifecycle gate:** repair P01–P04, define accepted-connection ownership and deterministic shutdown, and add tests for interrupted frames and teardown. Preserve existing framing and protocol structure.
2. **Presence ownership gate:** repair D01–D02 and D06: immutable/detached snapshots, exclusive updates, callbacks after locks, retained offline records, truthful LastSeen and quiet heartbeats.
3. **Discovery input/identity gate:** repair D03–D04 and D08, including required fields, bounded names, stable stored identity, and observable expected failures.
4. **Lifecycle/test gate:** repair D05 and D09, with reproducible default `dotnet test`, isolated identity storage, controlled time and bounded stop/dispose tests.
5. **LAN validation gate:** implement/verify D07 and demonstrate two Windows peers discovering each other and using the announced live TCP port. Start the server first, advertise its actual ListeningPort, then stop discovery before closing the listener.
6. **Documentation gate:** update old documents to match verified behavior and canonical phase order. Do not claim benchmarks or cross-machine behavior from the current test suite.

Keep these repairs in their own changes. Phase 3 pure domain modeling does not need sockets or a functioning UI and can be designed/tested independently. Phase 4 live-transfer implementation should begin only after the relevant networking/discovery repair gates pass.
