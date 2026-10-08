# Phase 2 verification checklist

Updated 2026-10-08. Checked items describe implemented/automatically verified behavior; field gates remain unchecked.

- [x] Persistent nonempty installation identity, explicit storage failures and isolated storage tests.
- [x] Typed local/remote device data; installation identity separate from connections/transfers.
- [x] Required bounded UDP announcement schema, source-endpoint IP, ID/type/version/name/port validation.
- [x] ID-based self-filtering, deduplication and endpoint/name updates.
- [x] Heartbeat/expiration loops and configurable centralized options.
- [x] Truthful LastSeen and monotonic elapsed-time expiration.
- [x] Retained Offline devices, observable Offline → Online recovery and explicit offline pruning.
- [x] Detached device snapshots, exclusive state changes and callbacks after locks.
- [x] Quiet heartbeats and material-update/status notifications.
- [x] Active IPv4 interface directed broadcasts, loopback/tunnel exclusion and independent expected-error reporting.
- [x] Startup-token semantics documented; supervised lifetime, terminal disposal and joined shutdown.
- [x] Actual UDP receipt leading to a connectable advertised TCP endpoint verified in the managed test host.
- [x] Application composition advertises its live TCP port and rolls back failed discovery startup.
- [x] Shared-port tests serialized through their repository xUnit collection.
- [ ] Automatic broadcast discovery between two physical Windows machines.
- [ ] Wi-Fi/Ethernet/multiple-adapter and adapter-disappearance field checks on Windows.
- [ ] Windows firewall, restart and shutdown field checks.

No authentication, IPv6, cross-subnet discovery, or performance benchmark is claimed. See docs/PHASE_PROGRESS.md for the full roadmap and docs/FILE_TRANSFERS.md for headless field commands.
