# Backend validation

Verified in the managed Linux workspace on 2026-10-14 with .NET SDK 10.0.401/runtime 10.0.12. Local execution is distinguished from Windows cross-compilation and external CI.

## Automated checks

- The preserved remote implementation's 299 existing backend cases passed after extraction into `Lantern.Core`.
- The extended Release suite passes 322 cases, with zero failures and zero skipped cases on Linux. It retains the original domain, framing, networking, discovery, transfer and application-startup coverage.
- Added checks cover missing/malformed/mismatching SHA-256, receive byte limits, explicit TLS pairing, unknown/changed pins, certificate-bound Hello identity, plaintext rejection, persisted identity/trust/settings/history, installation ownership, notification events, received destination history, rotating logs, average speed/ETA, bounded folder expansion/link rejection/cancellation, blocked control-message writes, duplicate/unknown payload fields and rapid UDP shutdown.
- The complete Release solution cross-build succeeds with zero warnings and zero errors, including the Windows Forms shell and the portable CLI/tests.
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
- WinForms UI, notification/tray presentation, pairing/consent UX, startup preference wiring, DPI, keyboard accessibility and installer end-user flows remain the next UI/release work.

These gates do not represent unimplemented file-transfer backend logic, and no finished UI or production-ready end-user release is claimed.
