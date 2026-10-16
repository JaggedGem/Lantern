# Lantern: durable project context

Updated 2026-10-16.

The Windows publish failure reported two TLS integration failures. Certificate
imports now use UserKeySet on Windows because Schannel cannot use ephemeral
private keys for TLS credentials; other platforms retain EphemeralKeySet.
PersistKeySet is intentionally omitted so disposal releases temporary imported
key containers. The PFX and paired fingerprints are preserved. A new integration
test reopens both identities and transfers a file. These changes need Windows CI
verification on the final revision. Initial CI passed all 375 backend cases on
Windows and Linux, both solution builds and both portable packaging modes.
It also exposed a receive-dialog lifetime bug: acceptance now captures the
chosen destination before Close disposes its native text box. The UI workflow
asserts that the destination survives closing. The current editing environment
has no .NET SDK or PowerShell and its network proxy is unreachable; execution
validation is performed through GitHub Actions.
Discovery start/stop integration tests select an available UDP port rather than
binding the production default, which Windows runners can reserve/exclude.

`scripts/Publish.ps1` now prompts for Bundled or FrameworkDependent, with an
explicit `-RuntimeMode` for unattended runs. Both app and CLI retain single-file
publishing, tests remain mandatory, and mode-specific output directories and
ZIP/installer names prevent cross-mode collisions. CI passes each mode explicitly.

Read `PROJECT_SPECIFICATION.md` for the original full requirements. The user now explicitly authorizes implementing the full UI, preserving all completed backend work, making milestone commits and pushing all final commits to origin/work. Milestone commits and the final push to `origin/work` are authorized. This supersedes the older request to stop after Phase 4.

The UI session began from an older checkout at `ac5bd0b`. The existing remote `work` branch at `0847044` contained the canonical specification and completed backend. That history was merged and its backend restored before adapting the UI. Do not replace its immutable domain or lose that history.

## Repository and architecture

Repository: `/workspace/Lantern`; origin: `https://github.com/JaggedGem/Lantern.git`; development branch: `work`.

- `Lantern.Core`: portable `net10.0` application, domain, networking/discovery, protocol, transfers, security, configuration and diagnostics.
- `Lantern`: `net10.0-windows` WinForms desktop host referencing the core, with device/send/receive/pairing/activity/details/settings/tray/notification workflows.
- `Lantern.Harness`: portable CLI exercising the backend, explicit pairing/consent, settings/history/diagnostics and benchmarking.
- `Lantern.Tests`: portable xUnit suite. Existing domain/network/transfer coverage is retained; new integrity/TLS/persistence/selection checks extend it.

Keep Device, Connection, Message and Transfer distinct. Models perform no file or socket I/O. `TransferSelection` and `TransferSourceFile` own local paths; protocol metadata never carries absolute source paths. `TransferManager` sequences immutable transfer versions and owns per-transfer connections. `FileSender`/`FileReceiver` stream and verify files. UI owns presentation and input, with no raw networking.

## Implemented non-UI behavior

Discovery uses directed broadcasts on eligible IPv4 interfaces, observes endpoint/name changes, retains offline peers with their actual last-seen timestamp, bounds peer count, returns detached snapshots and supervises lifetime tasks. UDP advertisements are visible and unauthenticated; discovery is never trusted authentication.

Application transfer connections use mutual TLS 1.2/1.3 with persistent ECDSA certificates and explicit SHA-256 pins. Both peers must be paired through a separately trusted channel. Certificates bind installation GUIDs to the TLS handshake; Hello claims must match. New/changed/expired/untrusted certificates fail closed. The low-level manager rejects absent security unless an explicit isolated-test plaintext option is set. Production application construction always supplies TLS.

Wire protocol version 2 adds an incremental SHA-256 digest to each file completion marker. Exact byte counts, chunk IDs/offsets, all per-file markers, hashes and overall completion are required before atomic directory publication. The sender completes only after the receiver acknowledgment. Existing destination files are never overwritten; failed/cancelled staging is cleaned, with cleanup errors logged. Local filesystem permissions remain the boundary against another process running as the same OS user; reparse-point checks cannot provide a universal race-free filesystem sandbox.

Concurrency, selected-file count, retained transfers, transfer bytes, frame size and logical path lengths are bounded. Read/write/connect/handshake/request deadlines and cancellation cover the transfer pipeline. No automatic transfer retry can duplicate an uncertain receive commit. A new explicit retry requires fresh consent.

`LanternApplication.Open(dataDirectory)` loads preferences and owns an installation lock, device identity, private certificate, trust store, history, logs, transfers and optional discovery. Corrupt identity/certificate/preferences are surfaced rather than silently replacing paired identity. Settings are immutable for a running instance: save changes and recreate/restart the application to apply them. Unix private key/config writes use owner permissions; Windows relies on the private per-user application-data directory.

History stores the newest 256 terminal summaries and the received destination directory, without source paths, keys or file contents. Logs rotate at approximately 1 MiB with two retained archives. `NotificationRequested` is a backend event; the WinForms host presents it through the tray icon. `TransferChangedEventArgs` includes the current file ID and published destination; `TransferProgress.Calculate` provides fraction, average throughput and ETA.

Folder selection expands regular files with preserved relative hierarchy, bounded traversal and cancellation, rejecting links/reparse points. Empty directories have no wire representation; completely empty selections are rejected.

## UI implementation and platform gates

The WinForms host now uses `LanternApplication` and its existing transfer/trust/configuration APIs. Device selection, manual IPv4 entry with real peer IDs, pairing on both PCs, file/folder selection, drag/drop, incoming consent/destination choice, progress/details/speed/ETA/cancel, history, receive-folder opening, settings, tray/notifications, startup preference changes, themes, diagnostics and clean shutdown are implemented. No replacement transfer protocol or weaker security backend is used. See `UI.md`.

`Lantern.UiTests` adds Windows STA workflows for settings/theme restart, pairing and nested sending, and incoming accept/reject. The full solution and these tests cross-compile on Linux; native Windows execution is pending observation. Existing backend tests remain intact; desktop theme preference checks extend them. The UI must remain exclusively Windows Forms. Native controls now use explicit paired light/dark palettes and themed rendering for tabs, lists, dropdowns, buttons, checkboxes, progress, text inputs, spinners and tray menus. Dynamic status colors use theme roles; high-contrast settings are honored. Forty-seven portable contrast/hierarchy checks extend the suite to 374 passing cases; Windows pixel/theme regression tests and screenshots are configured but not locally executed on Linux.

Static English labels/buttons have a `.resx` resource base for future localization; dynamic messages remain English.

Rider can produce one distributable EXE via the solution configurations `SingleExe` (framework-dependent) and `SingleExeWithRuntime` (self-contained), both win-x64 by default. The normal Build target publishes after compilation only for those configurations, with embedded symbols/native bundling and no trimming. Outputs are `artifacts/single-exe/<rid>/<configuration>/Lantern.exe`. Debug/Release remain normal builds. See `RIDER_BUILDS.md`, including disabling ReSharper Build for custom MSBuild steps. Both configurations were built and bundle manifests inspected on Linux; Rider/Windows runtime execution remains unobserved.

Publishing scripts and a per-user Inno Setup definition remain. Execute Windows CI, real desktop workflows, DPI/screen-reader/tray/startup checks, installer/uninstaller and physical LAN verification before release. UI implementation does not establish these runtime results.

See `PHASE_PROGRESS.md`, `VALIDATION.md`, and `FILE_TRANSFERS.md`. The original review under `review-evidence` is historical baseline evidence with historical source locations, not a current test harness or current defect report.
