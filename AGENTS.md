# Lantern project context

Before working on this repository, read `docs/PROJECT_CONTEXT.md` and the sections of `docs/PROJECT_SPECIFICATION.md` relevant to the requested phase. The specification is the user's full, canonical architecture and roadmap; the existing code establishes what actually exists.

The latest review is `docs/PHASE_1_2_REVIEW.md`. Phase 3 is implemented; its completed plan is `docs/PHASE_3_PLAN.md` and its API guide is `docs/TRANSFER_DOMAIN.md`. The earlier-phase review findings remain open except for test-port collection isolation. Do not treat a documented repair recommendation as an implemented fix. Update project context when verified behavior or accepted decisions change.

Preserve the distinction between Device, Connection, Message, and Transfer. Keep UI, discovery, transport framing, protocol serialization, and file I/O in their own responsibilities. Build on correct existing code. Stay within the phase the user requests; Phase 3 is transfer-domain modeling, Phase 4 is actual file movement, and Phase 5 is WinForms UI.

Use modern async .NET APIs for I/O, explicit ownership, cancellation, strong types, and meaningful tests. Do not add interfaces or class hierarchies without a concrete need. Do not treat discovery as authenticated trust.

This file and the documentation provide durable context for assistants working with this repository. They do not establish automatic memory for unrelated chats.
