# Lantern

Windows LAN file-transfer application in C# and .NET 10 Windows Forms. Phase 3's transfer domain is implemented; actual file movement is Phase 4 work.

## What is included

- `Lantern.Networking.NetworkServer` for accepting TCP connections
- `Lantern.Networking.NetworkClient` for outgoing TCP connections
- `Lantern.Networking.Connection` for length-prefixed message exchange
- `Lantern.Networking.Protocol.ProtocolSerializer` for JSON protocol serialization
- Automated tests under `Lantern.Tests`
- UDP LAN discovery and presence mechanisms (see the review for outstanding correctness gaps)
- Immutable transfer/file metadata, validated manifests, explicit lifecycle transitions, progress and typed errors

## Project context

- [Complete specification](docs/PROJECT_SPECIFICATION.md)
- [Current project context](docs/PROJECT_CONTEXT.md)
- [Phase 1–2 review and remaining repairs](docs/PHASE_1_2_REVIEW.md)
- [Completed Phase 3 plan](docs/PHASE_3_PLAN.md)
- [Transfer domain API and examples](docs/TRANSFER_DOMAIN.md)

## Run the tests

```powershell
dotnet test .\Lantern.sln
```

Run on Windows with the .NET 10 SDK. The suite includes 208 transfer-domain cases and 32 networking/discovery tests. Discovery tests sharing the fixed UDP port are serialized through their xUnit collection; other collections can run in parallel. Domain models perform no file or network I/O.

Linux cross-compilation requires `-p:EnableWindowsTargeting=true`; executing the WinForms-referencing test project requires WindowsDesktop. The managed review workspace uses a source-linked net10.0 backend/test harness for Linux verification.

## Manual handshake concept

1. Start a `NetworkServer` on a chosen port.
2. Connect with `NetworkClient`.
3. Exchange `Hello` messages through the returned `Connection` objects.
4. Dispose or stop the server cleanly.

