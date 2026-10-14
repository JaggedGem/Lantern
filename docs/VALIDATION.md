# Backend and desktop validation

Verified in the managed Linux workspace on 2026-10-14 with .NET SDK 10.0.401/runtime 10.0.12. Local execution is distinguished from Windows cross-compilation and external CI.

## Current desktop implementation checks

The UI integration preserves the remote backend at `0847044`; its only application-core behavior change is the validated, backward-compatible system/light/dark theme preference. All previous backend regression cases remain.

- The final portable Release suite passes **327 cases**, with zero failures and zero skips (322 existing cases plus five desktop preference cases).
- The complete Release solution, including the WinForms app and three Windows STA UI workflow tests, builds with warnings treated as errors: **zero warnings and zero errors**.
- Updated self-contained single-file **win-x64 application publishing succeeds**.
- `git diff --check` is clean. No UI logic placeholders remain.
- Native UI workflows cover settings/theme restart, explicit pairing and nested folder sending, and incoming acceptance/destination choice/rejection. They are cross-compiled here and configured for Windows CI; they have **not been executed locally** because this executor is Linux. External CI results have not been observed.
- Desktop handlers now include tray/notifications, startup changes, themes, diagnostics, per-monitor DPI, keyboard actions, resource-backed static labels and clean asynchronous shutdown. See `UI.md`.

The historical backend evidence below remains useful; its earlier test counts describe the preceding backend revision.

## Automated checks

- The preserved remote implementation's 299 existing backend cases passed after extraction into `Lantern.Core`.
- The extended Release suite passes 322 cases, with zero failures and zero skipped cases on Linux. It retains the original domain, framing, networking, discovery, transfer and application-startup coverage.
- Added checks cover missing/malformed/mismatching SHA-256, receive byte limits, explicit TLS pairing, unknown/changed pins, certificate-bound Hello identity, plaintext rejection, persisted identity/trust/settings/history, installation ownership, notification events, received destination history, rotating logs, average speed/ETA, bounded folder expansion/link rejection/cancellation, blocked control-message writes, duplicate/unknown payload fields and rapid UDP shutdown.
- The complete Release solution cross-build succeeds with zero warnings and zero errors, including the Windows Forms host and the portable CLI/tests.
- Self-contained single-file `win-x64` publishing succeeds for both application and CLI. Generated executables are approximately 111 MiB and 71 MiB respectively, including their .NET runtimes.
- `git diff --check` is clean. No transfer logic stubs remain in the core or CLI.

Commands:

```sh
dotnet build Lantern.sln -c Release
dotnet test Lantern.Tests -c Release
dotnet publish Lantern/Lantern.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
dotnet publish Lantern.Harness/Lantern.Harness.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

The sandbox initially blocked the test runner's local sockets; networking tests were then run with the required execution permission. Package restore and vulnerability auditing subsequently completed successfully through the configured proxy. No audit setting was disabled.

One final run exposed a UDP shutdown race in the inherited discovery implementation: `UdpClient.Dispose` could race a heartbeat's `SendAsync` socket-field read. Shutdown now cancels and drains all I/O loops before disposal. A dedicated regression rapidly starts/stops discovery 50 times.

## Local performance measurement

`Lantern.Harness benchmark` creates two mutually pinned TLS peers, streams a temporary file, verifies SHA-256, publishes the receive batch, waits for acknowledgment and cleans up. Each invocation starts a fresh process; figures include handshake/JIT/startup costs and reflect this managed host, not a physical LAN or guaranteed hardware throughput.

| File | Elapsed | Throughput | Process peak working set |
| --- | --- | --- | --- |
| 32 MiB | 1.040 s | 30.77 MiB/s | 54.1 MiB |
| 128 MiB | 2.838 s | 45.10 MiB/s | 96.7 MiB |
| 512 MiB | 4.856 s | 105.43 MiB/s | 114.5 MiB |

The 512 MiB case exceeds process peak memory by more than four times and completes through bounded chunks. Peak working set includes the runtime, JIT, TLS, GC heap and operating-system buffers; it is not a formal bound for every workload. Physical multi-device/disk/network performance remains to be measured.

## Remaining platform gates

- Windows runtime execution and the Windows CI result have not been observed in this Linux session.
- Physical two-machine discovery/transfers and multi-adapter/Wi-Fi interruption behavior need field checks.
- Validate disk-full, ACL and reparse handling on supported Windows filesystems. The Linux link-rejection test does not establish Windows junction behavior.
- PowerShell and Inno Setup are unavailable here. Cross-published inputs exist, but the PowerShell archive script and installer/uninstaller must run on Windows before release. Review signing and firewall prompts there.
- Exercise the implemented WinForms UI, notification/tray presentation, pairing/consent UX, startup registration, system theme changes, multi-monitor DPI, keyboard/screen-reader accessibility and installer end-user flows on Windows.

These gates distinguish implemented code from observed Windows runtime behavior. A fully validated production release is not claimed.
