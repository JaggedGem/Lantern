# Lantern: durable project context

Updated 2026-10-14.

Read `PROJECT_SPECIFICATION.md` for the original full requirements. The user now explicitly authorizes finishing all non-UI logic, including later-phase robustness, integrity, security and production support, while leaving UI implementation next. Milestone commits and the final push to `origin/work` are authorized. This supersedes the older request to stop after Phase 4.

The session checkout initially contained `main` at `ac5bd0b`. The existing remote `work` branch, through `42be932`, contained the canonical specification and Phase 3/4 implementation. It was fast-forwarded and preserved before extending it. Do not replace its immutable domain or lose that history.

## Repository and architecture

Repository: `/workspace/Lantern`; origin: `https://github.com/JaggedGem/Lantern.git`; development branch: `work`.

- `Lantern.Core`: portable `net10.0` application, domain, networking/discovery, protocol, transfers, security, configuration and diagnostics.
- `Lantern`: `net10.0-windows` WinForms shell referencing the core. UI implementation remains next.
- `Lantern.Harness`: portable CLI exercising the backend, explicit pairing/consent, settings/history/diagnostics and benchmarking.
- `Lantern.Tests`: portable xUnit suite. Existing domain/network/transfer coverage is retained; new integrity/TLS/persistence/selection checks extend it.

Keep Device, Connection, Message and Transfer distinct. Models perform no file or socket I/O. `TransferSelection` and `TransferSourceFile` own local paths; protocol metadata never carries absolute source paths. `TransferManager` sequences immutable transfer versions and owns per-transfer connections. `FileSender`/`FileReceiver` stream and verify files. UI owns presentation and input, with no raw networking.

## Implemented non-UI behavior

Discovery uses directed broadcasts on eligible IPv4 interfaces, observes endpoint/name changes, retains offline peers with their actual last-seen timestamp, bounds peer count, returns detached snapshots and supervises lifetime tasks. UDP advertisements are visible and unauthenticated; discovery is never trusted authentication.

Application transfer connections use mutual TLS 1.2/1.3 with persistent ECDSA certificates and explicit SHA-256 pins. Both peers must be paired through a separately trusted channel. Certificates bind installation GUIDs to the TLS handshake; Hello claims must match. New/changed/expired/untrusted certificates fail closed. The low-level manager rejects absent security unless an explicit isolated-test plaintext option is set. Production application construction always supplies TLS.

Wire protocol version 2 adds an incremental SHA-256 digest to each file completion marker. Exact byte counts, chunk IDs/offsets, all per-file markers, hashes and overall completion are required before atomic directory publication. The sender completes only after the receiver acknowledgment. Existing destination files are never overwritten; failed/cancelled staging is cleaned, with cleanup errors logged. Local filesystem permissions remain the boundary against another process running as the same OS user; reparse-point checks cannot provide a universal race-free filesystem sandbox.

Concurrency, selected-file count, retained transfers, transfer bytes, frame size and logical path lengths are bounded. Read/write/connect/handshake/request deadlines and cancellation cover the transfer pipeline. No automatic transfer retry can duplicate an uncertain receive commit. A new explicit retry requires fresh consent.

`LanternApplication.Open(dataDirectory)` loads preferences and owns an installation lock, device identity, private certificate, trust store, history, logs, transfers and optional discovery. Corrupt identity/certificate/preferences are surfaced rather than silently replacing paired identity. Settings are immutable for a running instance: save changes and recreate/restart the application to apply them. Unix private key/config writes use owner permissions; Windows relies on the private per-user application-data directory.

History stores the newest 256 terminal summaries and the received destination directory, without source paths, keys or file contents. Logs rotate at approximately 1 MiB with two retained archives. `NotificationRequested` is a backend event; the future UI presents it. `TransferChangedEventArgs` includes the current file ID and published destination; `TransferProgress.Calculate` provides fraction, average throughput and ETA.

Folder selection expands regular files with preserved relative hierarchy, bounded traversal and cancellation, rejecting links/reparse points. Empty directories have no wire representation; completely empty selections are rejected.

## Next UI work and platform gates

Use the application API in `FILE_TRANSFERS.md` to build the WinForms device selection, pairing, consent, progress, errors, details and settings flows. UI callbacks must return promptly, marshal to the UI thread, and call consent methods asynchronously without blocking a backend event thread. Implement tray presentation, notifications and user-facing startup preference changes through the existing settings/startup APIs.

Publishing scripts and a per-user Inno Setup definition exist; installers preserve paired identity and downloads on uninstall. Execute the Windows installer/uninstaller and review code signing, firewall UX, physical LAN behavior, DPI, accessibility and real-user workflows before a release. The app remains a UI shell, so backend completion does not imply a finished end-user product.

See `PHASE_PROGRESS.md`, `VALIDATION.md`, and `FILE_TRANSFERS.md`. The original review under `review-evidence` is historical baseline evidence with historical source locations, not a current test harness or current defect report.
