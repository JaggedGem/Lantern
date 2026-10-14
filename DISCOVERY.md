# LAN discovery and presence

See PHASE2_SUMMARY.md, PHASE2_CHECKLIST.md and docs/PHASE_PROGRESS.md for current completion/verification status. The original design review is docs/PHASE_1_2_REVIEW.md.

DeviceDiscovery is an IPv4 UDP announcement service. Defaults: UDP 52845, 3-second heartbeat, 15-second offline threshold, 5-second expiration scan, 4096-byte packets and at most 256 retained peers. DiscoveryOptions centralizes these values. Installation IDs are persistent GUIDs, independent of names, addresses and TCP connections.

Wire announcement: `{"v":1,"t":1,"id":"<nonempty-guid>","n":"<name>","p":<tcp-port>}`. All fields are required; unknown types/versions, bad IDs/names/ports and oversize packets are dropped. IP is taken from the actual source endpoint. Discovery does not establish trust.

Repeated observations refresh LastSeen and monotonic presence timestamps. Expiration marks Offline without deleting peers or pretending an observation occurred. A new announcement restores Online. Explicit ForgetOfflineDevice removes a retained offline entry. The peer-count cap bounds unauthenticated discovery storage.

GetDiscoveredDevices and event args contain detached Device snapshots. Mutating a snapshot cannot alter discovery state. DeviceDiscovered, DeviceUpdated, DeviceStatusChanged and DeviceRemoved notify material changes after internal synchronization; pure heartbeats do not spam subscribers. Callbacks must not synchronously block shutdown of their own service.

Each heartbeat refreshes eligible active IPv4 adapters and sends their directed broadcasts. Loopback/tunnel adapters are excluded. Expected receive/send/interface errors are reported through Error and Trace, with bounded receive retry delay. Unexpected loop/subscriber failures fault Completion and drain the other loops. Owners must observe Completion or StopAsync; networking never dispatches WinForms controls.

StartAsync's token controls startup only. StopAsync controls lifetime and waits for every loop. Disposal is terminal. LanternApplication composes the TCP listener first and advertises the actual bound port. Windows multi-machine and multi-interface behavior is a field-validation gate, not inferred from unit tests or Linux cross-compilation.
