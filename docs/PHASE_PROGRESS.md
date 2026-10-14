# Implementation progress

Updated 2026-10-14. The user authorized completing all non-UI logic ahead of UI implementation, with milestone commits and a final push to `origin/work`.

| Phase | Implementation | Verification / remaining gate |
| --- | --- | --- |
| 1: networking/protocol | Complete, portable core | TCP/framing/ownership/cancellation regressions; Windows runtime field checks pending |
| 2: discovery/presence | Complete backend | Directed IPv4 broadcasts, retained offline presence, endpoint updates, bounded detached snapshots; physical multi-adapter LAN checks pending |
| 3: transfer domain | Complete, preserved | Existing immutable metadata, validated manifests, legal transitions, typed failures and domain tests retained |
| 4: actual transfer | Complete backend | Files, nested selections, explicit consent, bounded chunks, exact byte counts, staged publication, acknowledgments |
| 5: WinForms UI | Next implementation step | Forms remain a shell; wire the documented application API to the UI |
| 6: robustness | Complete backend | Disconnects, deadlines, cancellation, shutdown, concurrency/size/path limits, permission/disk errors and cleanup |
| 7: integrity | Complete | Incremental SHA-256 for each file, including empty files; missing/malformed/mismatched digests cannot publish |
| 8: security | Complete backend | TLS 1.2/1.3, mutual certificate pinning, certificate-bound installation IDs, explicit pairing/revocation, hostile input validation; discovery remains an untrusted hint |
| 9: production support | Non-UI logic implemented | Persisted preferences/history/trust, notification events, startup API, logs/diagnostics, progress/ETA, benchmark command, publish script, installer definition and CI; UI adapters and Windows installer execution remain |

The implementation builds the Windows desktop host plus the portable CLI and tests. Windows execution, installer/uninstaller execution and physical two-machine discovery are not established by Linux cross-compilation. No completed UI or production release is claimed.

The underlying transport intentionally does not retry a possibly committed transfer: a lost final acknowledgment can leave a verified receive batch and a failed sender. A fresh user-initiated attempt gets a new transfer ID and requires fresh consent. Resume and automatic retransmission are not part of this implementation.

See `VALIDATION.md` for exact automated and performance evidence and `FILE_TRANSFERS.md` for APIs, security boundaries and manual gates.
