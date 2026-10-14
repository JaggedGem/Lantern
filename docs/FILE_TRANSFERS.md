# Application and transfer backend

Use `Lantern.Application.LanternApplication.Open(dataDirectory)` for the configured application. The optional directory defaults to the user's application-data Lantern directory. Only one process can own an installation directory at a time.

```csharp
await using var application = LanternApplication.Open(dataDirectory);
application.Transfers.IncomingTransferRequested += (_, request) =>
{
    // Hand the request to the UI and return promptly. The UI later calls:
    // request.Accept(existingDestinationDirectory), or request.Reject().
};
application.Transfers.TransferChanged += (_, args) =>
{
    // args.Transfer is immutable. args.CurrentFileId identifies a manifest file.
    // args.DestinationDirectory is populated after verified receive publication.
    var progress = TransferProgress.Calculate(args.Transfer, DateTimeOffset.UtcNow);
};
await application.StartAsync(cancellationToken);
await application.SendFilesAsync(discoveredDeviceId, selectedFilesOrFolders, cancellationToken);
await application.StopAsync();
```

Subscribe to `DevicesChanged`, read `GetDevices()` for detached snapshots, and use `Error`, `DiscoveryError`, `NotificationRequested` and `GetDiagnostics()` as needed. Events arrive on backend threads; marshal presentation work to the UI. Await shutdown; never block the UI thread waiting for it. Startup tokens cancel startup, not application lifetime.

No consent subscriber means rejection. Requests expire; late decisions return false. The recipient chooses an existing local destination directory. Wire metadata never selects an absolute destination. The complete verified batch appears under `Lantern-<transfer-id>` in the chosen directory. Existing output directories cause a safe failure. All-empty files are supported; empty directory entries are not represented.

`TransferSelection.FromPathsAsync` expands file/folder selections with count, byte and traversal limits. `TransferSourceFile` separates local source paths from immutable `Models.TransferFile`. The domain APIs in `TRANSFER_DOMAIN.md` remain valid. `TransferFailureKind` now also distinguishes integrity and authentication failures. `TransferProgress.Calculate` uses cumulative bytes and elapsed transfer time to produce average speed/ETA; it does not poll files.

## Pairing and encryption

Inspect `DeviceId` and `Fingerprint` on both installations. Verify the fingerprint using an independently trusted channel, then call `Trust.TrustPeer(peerId, fingerprint)` on both installations. `Trust.RemovePeer` revokes future handshakes; cancel any active transfer too if immediate revocation is required. Discovery advertisements, names and addresses are hints and cannot create trust.

The private ECDSA certificate lives in `identity.pfx` inside the private per-user data directory. Do not share that file. The public fingerprint can be shared. Certificates last five years; expiry/corruption is reported, requiring explicit identity repair and re-pairing. Back up the identity file and device ID together if moving an installation. Certificate pinning deliberately validates exact paired certificates instead of public-CA chains. TLS provides key possession and encrypted file traffic; UDP discovery is still visible, and can be disabled in settings.

`TransferManager` accepts a `TransferSecurity` dependency. Omitting it is rejected unless `TransferOptions.AllowUnencryptedConnections` is explicitly enabled for isolated protocol tests. Application construction always supplies security. Raw `NetworkServer`/`NetworkClient` remain generic transport tools, including the legacy Hello diagnostic commands; they are not application file-transfer entry points.

## Integrity, failure and commit boundaries

The sender and receiver incrementally hash each file while streaming. The completion marker carries SHA-256; absent, malformed or mismatching digests fail the receive before publication. Empty files use the normal SHA-256 digest of empty input. Protocol version 2 is required; old version-1 file-transfer peers cannot transfer.

Each frame is bounded to 1 MiB; chunks are 64 KiB. The default transfer cap is 100 GiB and 1024 files, with two concurrent sessions and 256 retained transfer states. Configure smaller limits as needed. Refused/unavailable peers, stale discovered endpoints, disconnects, deadlines and filesystem failures produce terminal failure with typed errors. Cancellation closes the connection and drains work. Discovery refreshes interface addresses on subsequent announcements; a new explicit send uses the current peer snapshot.

Publication is the receiver commit point. A disconnect after publication can leave a completed receiver and failed sender if the acknowledgment is lost. The batch is retained; it cannot safely be inferred that retrying is required. Automatic retransmission/resume is deliberately absent. A new user-initiated attempt has a new ID and fresh consent.

Reparse ancestors, Windows reserved names, traversal, duplicate/conflicting manifest paths, invalid Unicode, excessive lengths, counts and byte totals are rejected. Outputs use a private staging directory and exclusive file creation. These checks are not a sandbox against a malicious process running as the same OS user. Abrupt process/OS termination can leave a `.partial` directory; it is never reported as a completed batch, and operators may inspect/remove it. Normal errors/cancellation perform cleanup and report cleanup failures.

## Preferences, history and diagnostics

`SettingsStore.Save` atomically persists validated preferences. Recreate/restart the application to apply new immutable preferences. `History.GetEntries()` returns at most 256 terminal summaries; `Clear()` removes saved summaries. History includes published receive destinations but no source paths or file contents. `NotificationRequested` exposes terminal transfer events when enabled. The WinForms UI owns OS notification/tray presentation and calls the Windows-only `WindowsStartupRegistration.SetEnabled` API only after the user changes the startup preference.

`Log` writes rotating JSON-line operational logs. `GetDiagnostics()` exposes counts, listening endpoint, certificate expiry/fingerprint and log location, without private key data. Corrupt settings/trust/history files are reported rather than silently discarded. A history/log write failure does not convert an already committed file batch into a failed transfer.

## CLI exercise

Run `dotnet run --project Lantern.Harness -- <command> ...`, or the published CLI binary. On each computer, use a private data directory:

```text
identity <data-directory>
trust <data-directory> <other-device-id> <verified-other-fingerprint>
receive <data-directory> <download-directory> [port]
send <data-directory> <receiver-id> <receiver-ip> <receiver-port> <files-or-folders...>
settings <data-directory> [device-name download-directory]
history <data-directory>
diagnostics <data-directory>
benchmark [MiB]
```

Pair both directions before transfer. Receive prompts for each request and defaults to rejection. Ctrl+C cancels running operations. Defaults are TCP 52846 and discovery UDP 52845. Allow the application on the Windows private-network firewall only after reviewing the OS prompt; scripts do not silently change firewall policy. The benchmark creates temporary mutually paired loopback peers, transfers and verifies content, reports throughput/peak working set, and removes its files.

## Windows packaging and release gates

Run `./scripts/Publish.ps1` in PowerShell with the .NET 10 SDK to test and produce self-contained Windows application/CLI archives under `artifacts`. `-Runtime win-arm64` produces a portable ARM64 archive. On Windows with Inno Setup 6 available as `ISCC.exe`, use `-BuildInstaller` for a per-user x64 installer. The installer definition does not elevate or silently change startup/firewall settings; uninstall removes installed binaries/shortcuts and preserves downloads and identity data.

Before a release, run the complete tests on Windows, exercise two physical Windows machines and multiple adapters, validate disk-full/ACL/reparse behavior on target filesystems, execute install/uninstall, sign release binaries/installer, and validate the implemented WinForms UI, consent and pairing UX, notifications/tray, startup setting, DPI and keyboard accessibility. See UI.md. CI configuration runs Linux/Windows tests and produces a Windows portable artifact; results must be observed separately from local cross-builds.
