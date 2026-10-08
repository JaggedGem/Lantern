# Lantern: durable project context

Updated: 2026-10-08. Original review baseline: `ac5bd0bb096566e5fbec07bd021ca28d6e914415`.

## How to resume work

1. Read this file for verified current state and user preferences.
2. Read `PROJECT_SPECIFICATION.md` for the complete, verbatim user-provided specification (45,301 bytes).
3. Read `PHASE_1_2_REVIEW.md` before considering the first two phases complete.
4. Read `PHASE_3_PLAN.md` before implementing transfer-domain work.
5. Inspect current code and changes; these documents describe the reviewed revision, not a permanent claim about future code.

The latest user instructions authorize continued implementation, milestone commits, and pushing all completed commits at the end to origin branch `work`. Phases 3 and 4 are implemented. Phase 4 also repairs the earlier transport/discovery prerequisites. See `PHASE_PROGRESS.md` for completed phases and remaining verification, `TRANSFER_DOMAIN.md` for the pure models, and `FILE_TRANSFERS.md` for application composition, transfer APIs and manual Windows LAN checks. Phase 5's WinForms UI is the next implementation milestone.

These are repository files, not account-wide ChatGPT memory. Future chats using this repository can load them through `AGENTS.md`. A separate chat without this repository will need the context files supplied again.

## Product and engineering preferences

Lantern is a Windows LAN file-transfer desktop application in C# and modern .NET Windows Forms. The goal is to discover nearby application instances, select a device, choose files, send them after recipient acceptance, show progress, verify integrity, and handle failures gracefully. Folders come later. The finished UI should be polished, responsive, accessible, and simple enough that users need not understand networking.

User priorities, in order: correctness, reliability, maintainability, type safety, performance, clean architecture, good UX/UI, security, testability. Development is deliberately phased; do not implement everything at once. The user wants to understand and maintain the project. Use descriptive names, explicit dependencies, small cohesive classes, and abstractions with real justification. Avoid unnecessary rewrites of working architecture.

Core distinctions:

| Concept | Responsibility |
| --- | --- |
| Device | Application installation identity and presence; never a connection or transfer |
| DeviceDiscovery | UDP discovery, announcements, last-seen tracking and availability |
| NetworkServer / NetworkClient | Accepting / establishing TCP connections |
| Connection | Reliable framed message transport; not message semantics |
| Message / ProtocolSerializer | Typed protocol information / conversion to and from bytes |
| Transfer | One file operation's identity, participants, metadata and state |
| TransferManager | Negotiation, serialized transfer state, cancellation and connection ownership |
| FileSender / FileReceiver | Bounded file streaming and staged receiver publication |
| UI | Presentation and user input through application APIs |

Discovery is not trust. UDP and TCP input are untrusted. Keep maximum message/packet sizes, validation, path safety, and cancellation explicit. Metadata models must never open files or sockets. Use established encryption and authentication in the security phase.

## Canonical phase order

1. TCP networking and protocol foundation.
2. LAN discovery and presence.
3. Transfer-domain models and state invariants.
4. Actual file transfer, negotiation, progress, streaming, path safety and cleanup.
5. WinForms UI.
6. Reliability and robustness.
7. File integrity, including SHA-256 verification.
8. Security, encryption and peer authentication.
9. Production polish, preferences, diagnostics and packaging.

Some older Phase 2 documents incorrectly label later phases; use this order and the complete specification.

## Verified repository state

Repository root: `/workspace/Lantern`; origin: `https://github.com/JaggedGem/Lantern.git`. Earlier Phase 3 commits were pushed to `work`; subsequent milestone commits are authorized for the same destination. The solution contains `Lantern`, `Lantern.Tests`, and `Lantern.Harness`. The main application and tests target `net10.0-windows`; the application enables WinForms. Nullable references and implicit usings are enabled. Tests use xUnit.

- TCP framing has a centralized 1 MiB cap. Interrupted frame operations close the channel; queued/pre-cancelled operations and server shutdown have explicit ownership and regression coverage.
- Protocol serialization strictly validates envelopes and supports Hello/Error plus typed transfer negotiation, chunks, per-file completion, overall completion and acknowledgments. Transfer handshakes require matching claimed installation IDs and protocol version; this is not authentication.
- Discovery sends directed IPv4 broadcasts across eligible interfaces. Defaults: UDP 52845, heartbeat 3 seconds, offline threshold 15 seconds, expiration scan 5 seconds, packet limit 4096 bytes, 256 retained peers. Offline entries retain their actual last-seen time. Snapshots are detached, callbacks occur outside locks, metadata changes notify observers, and lifecycle tasks are supervised.
- Persistent identity validates storage and uses exclusive locking plus atomic creation. Persistence failures are surfaced instead of silently replacing identity.
- Immutable Phase 3 models contain only metadata/state. The Phase 4 `Transfers` namespace owns local source paths, negotiation, bounded streaming, progress, timeouts and staged destination cleanup. No existing destination files are overwritten; completion requires all declared bytes and markers, atomic publication and sender acknowledgment.
- `Lantern.Application.LanternApplication` composes stable identity, TCP startup, discovery of the actual bound endpoint, transfers and orderly shutdown. The console harness offers explicit send/receive commands in addition to Hello testing.
- WinForms remains an empty shell. Phase 5 will connect the headless application to the UI. Folder enumeration, production hashes, encryption, persistent history and resume are later work.

## Review verdict and evidence

The original review found lifecycle/framing, discovery ownership, retained presence, validation, identity and interface-handling defects. Subsequent prerequisite commits repair those behaviors and add automated regression coverage. The review's source locations and probe results remain historical evidence, not current defect claims. Windows runtime, physical two-machine LAN and multi-adapter field gates remain open.
Verified at the original review baseline, before Phase 3:

- Original Windows-targeted solution cross-build succeeded on Linux with 0 reported warnings/errors (incremental build).
- Existing tests: 32/32 passed using the pre-existing Linux source-linked harness with collection parallelism disabled.
- Default test settings: 30 passed, 2 failed due to discovery-port collisions.
- Targeted review probes demonstrate defects the existing tests do not cover. They are separate from the production test suite.
- Windows runtime execution, cross-machine broadcast discovery, multi-interface discovery and manual WinForms behavior were not verified here.

See the review for source locations, severity, evidence and repair gates. Older completion checklists and performance claims are not verification evidence.

## Implemented Phase 3

The implemented pure domain provides immutable file metadata and transfer versions, stable transfer/file/participant IDs, direction/status enums, explicit legal transitions, checked total sizes, cumulative byte progress, UTC timestamps and typed errors. The existing namespace style (`Lantern.Models`) is preserved. Models hold no live Device, Connection, sender implementations or local absolute paths. Logical paths follow host-independent Windows filename constraints and reject malformed Unicode. File selection and absolute source/destination mapping now belong to the separate Phase 4 implementation.

Transition APIs return a new immutable Transfer; TransferManager sequences updates as their single owner. See `PHASE_3_PLAN.md` for the completed checklist and `TRANSFER_DOMAIN.md` for actual method signatures, legal transitions and examples.

Phase 3 validation: 208 domain test cases pass. Together with the original 32 networking/discovery tests, all 240 tests pass with default collection settings in the Linux source-linked harness. Discovery-related test classes now share a nonparallel xUnit collection, removing the external global-runsettings workaround; other test collections remain parallel. The Windows-targeted solution cross-builds successfully. Windows runtime behavior remains unverified.

## Implemented Phase 4 and current validation

TransferManager supports acceptance/rejection, bounded manifests and chunks, local cancellation, connection/request/inactivity deadlines, immutable progress events, failure classification and supervised shutdown. FileReceiver writes to a fresh staging directory and publishes a new `Lantern-<transfer-id>` directory only after all files complete. A lost final acknowledgment can leave a completed receiver and failed sender; see `FILE_TRANSFERS.md` for this and cleanup/platform boundaries.

All 299 backend test cases pass with default collection settings: 208 domain, 32 original networking/discovery, 20 reliability, 14 transfer serialization, 22 file-transfer integration and 3 application-startup cases. The original Windows-targeted solution and Linux source-linked CLI harness build with zero warnings/errors. Automated checks include real TCP file transfers and UDP endpoint-to-TCP connectivity on the managed machine. They do not establish physical-LAN, Windows reparse-point or WinForms behavior. Production SHA-256 exchange remains Phase 7 work.
