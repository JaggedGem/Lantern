# Lantern Phase 1

This phase implements the TCP networking and protocol foundation for Lantern.

## What is included

- `Lantern.Networking.NetworkServer` for accepting TCP connections
- `Lantern.Networking.NetworkClient` for outgoing TCP connections
- `Lantern.Networking.Connection` for length-prefixed message exchange
- `Lantern.Networking.Protocol.ProtocolSerializer` for JSON protocol serialization
- Automated tests under `Lantern.Tests`

## Run the tests

```powershell
dotnet test .\Lantern.sln
```

## Manual handshake concept

1. Start a `NetworkServer` on a chosen port.
2. Connect with `NetworkClient`.
3. Exchange `Hello` messages through the returned `Connection` objects.
4. Dispose or stop the server cleanly.

