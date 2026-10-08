# Phase 2: discovery and presence — current implementation

Updated 2026-10-08 after the Phase 1–2 review repairs. The implementation is complete in code and automated checks; two-machine/multiple-adapter Windows field validation remains pending. The original review is retained in docs/PHASE_1_2_REVIEW.md and Git history.

- Nonempty installation IDs are atomically persisted; corrupt/unreadable/unwritable storage is reported instead of silently generating replacement identities. Test storage paths are isolated; an exclusive companion file provides single-writer process coordination.
- Required UDP fields, supported version/type, source endpoint, ID, name and port are validated before state changes. Packets are at most 4096 bytes and names at most 128 characters.
- Discovery deduplicates/filter-self by ID and updates IP/name/port from actual announcements. Material changes notify; heartbeat-only updates stay quiet.
- LastSeen represents actual observations. Offline timing uses monotonic TimeProvider timestamps. Offline peers are retained and recover to Online; removal is explicit through ForgetOfflineDevice.
- Collection/event devices are detached snapshots. State changes are exclusively synchronized and subscribers are invoked after the lock is released.
- Per-interface directed IPv4 broadcasts use active non-loopback/non-tunnel adapters. One interface's expected failure is reported while others continue; adapter enumeration is refreshed each heartbeat. IPv6/cross-subnet discovery are not implemented.
- StartAsync's token covers startup. StopAsync joins all supervised background loops; disposal is terminal and concurrent stops share teardown. Completion exposes unexpected failures; Error/Trace expose expected socket failures.
- DiscoveryOptions centralizes UDP port, heartbeat/offline/expiration intervals and the peer-count bound (defaults: 52845, 3s/15s/5s, 256 peers).
- LanternApplication starts the TCP listener first and advertises its actual bound port, then stops discovery before draining transfers/listeners.

No measured CPU/network/scalability benchmarks are claimed. See docs/PHASE_PROGRESS.md and docs/FILE_TRANSFERS.md for current verification and field steps. The canonical next phases are 3 transfer domain (complete), 4 actual file transfer (complete backend), then 5 WinForms UI.
