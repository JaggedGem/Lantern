# Lantern

Windows LAN file transfer in C# and .NET 10. The backend is implemented; the WinForms UI is the next implementation step.

- Portable `Lantern.Core` owns discovery, immutable transfer state, safe file/folder selection and bounded streaming.
- Application transfers require mutually authenticated TLS with explicitly paired SHA-256 certificate fingerprints. UDP discovery provides untrusted, visible LAN hints.
- Each file is checked for exact size and SHA-256 integrity before the complete batch is published. No existing destination files are overwritten.
- Consent, cancellation, connect/request/read/write deadlines, concurrency and size limits, staged cleanup and orderly shutdown are implemented.
- Preferences, device identity, peer trust, bounded transfer history, notification events, throughput/ETA, rotating diagnostics and per-user startup registration are available to the future UI.
- The console harness exercises the complete backend. Windows portable publishing and a per-user Inno Setup installer definition are included.

```sh
dotnet build Lantern.sln -c Release
dotnet test Lantern.Tests -c Release
dotnet run --project Lantern.Harness -- identity <data-directory>
```

Core/tests/CLI target `net10.0` and run on Linux or Windows. The desktop host targets `net10.0-windows`; cross-compilation is enabled. No new runtime NuGet dependencies were added.

Read [current status](docs/PHASE_PROGRESS.md), [application and transfer APIs](docs/FILE_TRANSFERS.md), [durable context](docs/PROJECT_CONTEXT.md), and the [canonical specification](docs/PROJECT_SPECIFICATION.md). The original domain model and its [transition rules](docs/TRANSFER_DOMAIN.md) remain intact.

Windows runtime, physical LAN, WinForms/DPI/accessibility, and installer execution remain platform verification gates. The backend's automated checks and a local encrypted-transfer measurement do not substitute for those gates.
