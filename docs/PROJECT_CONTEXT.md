# Lantern: durable project context

Recorded: 2026-10-08. Reviewed source revision: `ac5bd0bb096566e5fbec07bd021ca28d6e914415`.

## How to resume work

1. Read this file for verified current state and user preferences.
2. Read `PROJECT_SPECIFICATION.md` for the complete, verbatim user-provided specification (45,301 bytes).
3. Read `PHASE_1_2_REVIEW.md` before considering the first two phases complete.
4. Read `PHASE_3_PLAN.md` before implementing transfer-domain work.
5. Inspect current code and changes; these documents describe the reviewed revision, not a permanent claim about future code.

The latest user instructions authorize Phase 3 implementation and commits at appropriate milestones. Phase 3 is now implemented: immutable metadata and transfer state, manifest validation, explicit lifecycle methods, progress/timestamps, typed errors and meaningful tests. The original review itself did not change production source; the subsequent Phase 3 work did. See `TRANSFER_DOMAIN.md` for the implemented API and phase boundary.

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
| TransferManager | Future transfer coordination and lifecycle ownership |
| FileSender / FileReceiver | Future streaming file I/O and byte transmission |
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

Repository root: `/workspace/Lantern`; origin: `https://github.com/JaggedGem/Lantern.git`. No remote changes were made. The solution contains `Lantern`, `Lantern.Tests`, and `Lantern.Harness`. The main application and tests target `net10.0-windows`; the application enables WinForms. Nullable references and implicit usings are enabled. Tests use xUnit.

- `Networking/Connection.cs` wraps a `TcpClient`; the internal `FramedMessageChannel` owns 4-byte big-endian framing, exact reads, send and receive serialization, pooled receive buffers, and a default 1 MiB message cap.
- `NetworkServer` listens on IPv4 Any, accepts asynchronously, exposes accepted connections through an event, and supports start/stop. `NetworkClient` supports async connection and cancellation.
- `Networking/Protocol` contains one `Message` envelope, a nonempty `MessageId` value type, `Hello` / `Error` message types, typed payloads, and a JSON serializer. Preserve this working envelope/payload design instead of replacing it solely to match a conceptual sketch.
- `Networking/Discovery` contains JSON UDP announcements, ID-based deduplication and self-filtering, source-endpoint IP selection, heartbeat and expiration tasks. UDP port 52845; heartbeat 3 seconds; offline threshold 15 seconds; expiration scan 5 seconds; packet limit 4096 bytes; protocol version 1.
- `Models/Device` is currently mutable. `LocalDevice` contains advertised identity, name and TCP port. `LocalDeviceIdentityProvider` stores a JSON ID under the platform application-data directory, intended as `%APPDATA%\Lantern\device-id.json` on Windows.
- WinForms is an empty shell; `Program` and `MainWindow` do not start networking/discovery. The console harness demonstrates a manual Hello exchange.
- Phase 3 now includes immutable `TransferFile` and `Transfer` models, direction/status enums and typed errors under `Lantern.Models`. TransferRequest and the empty FileSender placeholder were removed after confirming there were no live callers. No file movement, transfer manager or transfer protocol messages have been implemented.

## Review verdict and evidence

Phase 1 has a useful foundation but needs lifecycle/framing hardening. Phase 2 is implemented in part and does **not** satisfy its definition of done. Important issues: offline peers are immediately deleted; last-seen is refreshed when marking offline; discovery returns internal mutable objects and calls subscribers under locks; packet validation omits type, required-field checks, nonempty IDs and name bounds; persistence failures silently lose stable identity; interface handling is incomplete; metadata updates lack notifications; broad catches hide errors.

Verified at the original review baseline, before Phase 3:

- Original Windows-targeted solution cross-build succeeded on Linux with 0 reported warnings/errors (incremental build).
- Existing tests: 32/32 passed using the pre-existing Linux source-linked harness with collection parallelism disabled.
- Default test settings: 30 passed, 2 failed due to discovery-port collisions.
- Targeted review probes demonstrate defects the existing tests do not cover. They are separate from the production test suite.
- Windows runtime execution, cross-machine broadcast discovery, multi-interface discovery and manual WinForms behavior were not verified here.

See the review for source locations, severity, evidence and repair gates. Older completion checklists and performance claims are not verification evidence.

## Implemented Phase 3

The implemented pure domain provides immutable file metadata and transfer versions, stable transfer/file/participant IDs, direction/status enums, explicit legal transitions, checked total sizes, cumulative byte progress, UTC timestamps and typed errors. The existing namespace style (`Lantern.Models`) is preserved. Models hold no live Device, Connection, sender implementations or local absolute paths. Logical paths follow host-independent Windows filename constraints and reject malformed Unicode. File selection and absolute source/destination mapping remain Phase 4 work.

Transition APIs return a new immutable Transfer; a future single-owner coordinator must sequence updates. See `PHASE_3_PLAN.md` for the completed checklist and `TRANSFER_DOMAIN.md` for actual method signatures, legal transitions and examples.

Phase 3 validation: 208 domain test cases pass. Together with the original 32 networking/discovery tests, all 240 tests pass with default collection settings in the Linux source-linked harness. Discovery-related test classes now share a nonparallel xUnit collection, removing the external global-runsettings workaround; other test collections remain parallel. The Windows-targeted solution cross-builds successfully. Windows runtime behavior remains unverified.

The production Phase 1–2 repair findings remain open. The test-port collision part of D09 was fixed; discovery ownership, input validation, retained offline presence, stable identity failures, lifecycle races and physical-LAN checks still require their own repair work before Phase 4 live transfer. Do not confuse passing Phase 3 tests with those repairs or with working file transfer.
