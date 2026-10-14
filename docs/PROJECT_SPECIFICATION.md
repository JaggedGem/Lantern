# LAN File Transfer Application
## Complete Project Context, Architecture, Requirements & Development Roadmap

> **Purpose of this document:**  
> This document is the canonical context/specification for an ongoing .NET Windows Forms LAN file-transfer application.  
> Give this entire document to another AI coding assistant before asking it to work on the project. It should treat this document as the project's architectural context and development roadmap.

---

# 1. Project Overview

I am developing a **LAN file-transfer application for Windows** using **C# and modern .NET Windows Forms**.

The application is intended to allow computers on the same local network to:

- automatically discover each other
- identify available devices
- select another device
- send one or more files
- eventually send folders
- accept/reject incoming transfers
- display transfer progress
- verify file integrity
- handle network failures gracefully
- provide a polished, modern, easy-to-use Windows UI

The application is primarily intended for **fast, convenient transfers between trusted devices on the same LAN**.

It should feel like a polished modern desktop application rather than a school-project prototype.

The implementation should prioritize:

1. Correctness
2. Reliability
3. Maintainability
4. Type safety
5. Performance
6. Clean architecture
7. Good UX/UI
8. Security
9. Testability

---

# 2. Technology

The application is being developed with:

- C#
- modern .NET
- Windows Forms
- Windows desktop environment

The project should use modern idiomatic .NET APIs wherever practical.

Use asynchronous APIs for:

- TCP networking
- UDP networking
- file I/O
- long-running background operations

The application should support cancellation and clean shutdown throughout the architecture.

---

# 3. Development Philosophy

This project is intentionally being developed in **multiple phases**.

Do not implement everything at once.

Each phase should produce a coherent, working layer that future phases can build upon.

The project should avoid "vibe coding" architecture where everything is thrown into a few giant classes.

I want to understand and maintain the project myself.

Therefore:

- classes should have clear responsibilities
- names should be descriptive
- dependencies should be obvious
- abstractions should exist for real reasons
- code should be type-safe
- APIs should be predictable
- networking should not leak into UI
- UI should not contain networking logic
- file transfer should not be mixed with discovery
- protocol serialization should not be mixed with transport framing
- device state should not be mixed with transfer state

---

# 4. Important Architectural Principle

The application contains several distinct concepts.

They must NOT be conflated.

## Device

A `Device` means:

> "A known application instance on the LAN."

Example:

```text
Device
├── Id
├── Name
├── IP address
├── TCP port
├── LastSeen
└── Status
```

A `Device` does NOT represent a TCP connection.

---

## Connection

A `Connection` means:

> "An active TCP communication channel with another application instance."

It is responsible for reliably transporting protocol messages.

It does NOT know what those messages mean.

---

## Message

A `Message` means:

> "A protocol-level piece of information exchanged between peers."

Examples:

- Hello
- transfer request
- transfer acceptance
- transfer rejection
- transfer metadata
- etc.

The protocol will grow over time.

---

## Transfer

A `Transfer` means:

> "A particular file/folder transfer operation."

It contains transfer-specific state such as:

- sender
- receiver
- files
- direction
- progress
- status
- errors
- timestamps

A transfer is not a device.

A transfer is not a connection.

---

# 5. Current Architecture

The intended high-level architecture is:

```text
Application
│
├── Networking
│   ├── NetworkServer
│   ├── NetworkClient
│   └── Connection
│
├── Protocol
│   ├── Message
│   ├── MessageType
│   └── ProtocolSerializer
│
├── Discovery
│   ├── Device
│   ├── DeviceStatus
│   ├── DeviceDiscovery
│   └── Discovery protocol models
│
├── Transfers
│   ├── Transfer
│   ├── TransferFile
│   ├── TransferStatus
│   ├── TransferDirection
│   ├── TransferManager
│   ├── FileSender
│   └── FileReceiver
│
├── UI
│   ├── MainWindow
│   ├── SendFilesWindow
│   ├── ReceiveWindow
│   ├── TransferDetailsWindow
│   └── SettingsWindow
│
├── Security
│   └── authentication/encryption/etc.
│
├── Settings
│   └── application configuration
│
└── Tests
    ├── Networking
    ├── Protocol
    ├── Discovery
    ├── Transfers
    └── Integration
```

This is a conceptual architecture.

Do not blindly create every class listed here if the actual implementation can achieve the same responsibilities with fewer, better-designed classes.

Avoid unnecessary abstraction.

---

# 6. Architectural Rules

## Rule 1 — UI must not own networking

The WinForms UI should never directly manipulate:

- `TcpClient`
- `NetworkStream`
- `UdpClient`
- raw sockets
- file streams for transfers
- protocol framing

Instead:

```text
UI
 ↓
application/domain layer
 ↓
networking/transfer layer
```

---

## Rule 2 — Networking must not know about UI

Networking code must not reference:

- Forms
- Controls
- MessageBox
- ListView
- DataGridView
- UI-specific state

The networking layer must be usable without a UI.

---

## Rule 3 — Discovery is not transfer

`DeviceDiscovery` answers:

> Which devices are currently available?

`TransferManager` answers:

> What transfers are happening?

Do not put transfer state into device state.

---

## Rule 4 — Protocol serialization is not transport

`ProtocolSerializer` converts:

```text
Message ↔ bytes
```

It should not:

- read from `NetworkStream`
- write to `NetworkStream`
- add TCP framing
- manage sockets
- manage connections

---

## Rule 5 — Connection owns TCP framing

TCP is a byte stream.

It does not preserve application-level message boundaries.

Therefore the application uses length-prefixed framing:

```text
[message length][serialized message bytes]
```

`Connection` is responsible for:

1. serializing a `Message`
2. determining its byte length
3. writing the length prefix
4. writing the serialized message
5. reading the length prefix
6. reading exactly that many bytes
7. deserializing the bytes
8. returning the resulting `Message`

`ProtocolSerializer` only performs serialization/deserialization.

---

# 7. Phase 1 — Networking & Protocol Foundation

Phase 1 establishes the basic TCP communication layer.

The intended components are:

```text
NetworkServer
NetworkClient
Connection
Message
MessageType
ProtocolSerializer
```

---

## 7.1 NetworkServer

`NetworkServer` is responsible for:

- creating/listening on the TCP endpoint
- accepting incoming TCP connections
- creating `Connection` objects
- notifying the application when a connection is accepted
- asynchronous accept loops
- cancellation
- graceful shutdown
- resource disposal

It must NOT know about:

- files
- transfers
- discovery
- UI

Conceptually:

```text
NetworkServer
    ↓
accept TCP connection
    ↓
Connection
```

---

# 8. NetworkClient

`NetworkClient` is responsible for connecting to another known endpoint.

Conceptually:

```text
IP address + TCP port
        ↓
NetworkClient
        ↓
TCP connection
        ↓
Connection
```

It should support:

- asynchronous connection
- cancellation
- connection errors
- connection refused
- timeout handling where appropriate
- proper resource ownership

It should not know about:

- files
- discovery
- UI
- transfer semantics

---

# 9. Connection

`Connection` is the main abstraction over an active TCP connection.

Its responsibilities include:

- sending protocol messages
- receiving protocol messages
- TCP framing
- partial-read handling
- partial-write handling
- cancellation
- graceful close
- disposal
- preventing concurrent sends from corrupting the stream

Conceptually its API should support:

```text
Send(Message)
Receive()
Close()
Dispose()
```

Exact signatures should follow the existing project.

---

# 10. TCP Framing

The application must use explicit message framing.

Conceptually:

```text
┌──────────────────────┬─────────────────────────────┐
│ Message Length       │ Serialized Message          │
└──────────────────────┴─────────────────────────────┘
```

The receiver:

1. reads the fixed-size length prefix
2. validates the length
3. allocates a suitable buffer
4. reads exactly that many bytes
5. deserializes the bytes

Never assume:

```text
ReadAsync(...)
```

returns all requested bytes.

TCP can split one logical message across multiple reads.

A correct "read exactly N bytes" mechanism is required.

---

# 11. Maximum Message Size

The protocol must impose a maximum metadata/message size.

Never trust a network peer to provide a reasonable length.

For example, a malicious peer must not be able to send:

```text
MessageLength = several gigabytes
```

and cause the application to attempt a huge allocation.

The maximum should be centralized and configurable/constant.

---

# 12. Concurrent Sends

Multiple parts of the application may eventually want to send messages over one connection.

The implementation must prevent this:

```text
Task A → writes half a message
Task B → writes another message
Task A → writes remaining bytes
```

which would corrupt the stream.

Therefore sends must be serialized appropriately.

Do not assume callers will magically coordinate.

---

# 13. Message Model

The initial protocol uses one general `Message` model rather than a huge inheritance hierarchy.

Conceptually:

```text
Message
├── Type
├── MessageId
└── Payload
```

with a `MessageType` enum.

Do NOT create unnecessary classes such as:

```text
HelloMessage
TransferRequestMessage
TransferAcceptedMessage
MessageBase
IMessage
IMessageHandler
IProtocolManager
```

unless a future architectural need actually justifies them.

The initial system should remain simple.

---

# 14. ProtocolSerializer

`ProtocolSerializer` is responsible only for:

```text
Message → serialized bytes
bytes → Message
```

The initial implementation should use a modern, strongly typed serialization mechanism such as `System.Text.Json`.

It must NOT:

- access sockets
- access streams
- perform TCP framing
- manage transfers
- know about files

---

# 15. Initial Protocol

Phase 1 should establish the basic protocol infrastructure.

Initially there should be enough message types to verify communication, such as:

```text
Hello
Error
```

The protocol will grow in later phases.

---

# 16. Hello Handshake

A minimal hello exchange should prove that:

- TCP works
- framing works
- serialization works
- deserialization works
- both peers can identify the protocol

The handshake should not yet become a complicated authentication system.

Security/authentication comes later.

---

# 17. Phase 1 Testing

Tests should cover:

- serialization
- deserialization
- framing
- partial reads
- multiple consecutive messages
- invalid lengths
- malformed JSON
- connection establishment
- connection refusal
- cancellation
- shutdown
- resource disposal
- hello exchange
- concurrent send safety

Phase 1 should be considered complete when the application can reliably exchange protocol messages over TCP.

---

# 18. Phase 2 — LAN Device Discovery & Presence

Phase 2 adds automatic LAN discovery.

The objective is:

> Multiple running application instances on the same LAN automatically discover each other and maintain a current list of available devices.

---

# 19. Device Model

A `Device` represents a remote application instance.

It should contain appropriate strongly typed data such as:

```text
Id
Name
IpAddress
Port
LastSeen
Status
```

Recommended types:

```text
Id          → Guid
Name        → string
IpAddress   → IPAddress
Port        → int
LastSeen    → DateTimeOffset
Status      → DeviceStatus
```

Do not represent IP addresses, ports, IDs, or timestamps as arbitrary strings.

---

# 20. Persistent Device Identity

Every installation needs a persistent device ID.

It should be generated once and persisted.

The lifecycle is:

```text
Application starts
        ↓
Load device ID
        ↓
If none exists:
    generate Guid
    persist it
        ↓
Use ID for discovery
```

The ID must survive application restarts.

The device ID identifies the **application installation**, not the physical hardware.

---

# 21. Device Name

Each application instance should advertise a human-readable device name.

The name is for users.

It is NOT the identity.

Two devices may have the same name.

Identity must always be based on:

```text
Device.Id
```

not:

```text
Device.Name
```

or:

```text
Device.IpAddress
```

---

# 22. Device Status

Use a dedicated `DeviceStatus` enum.

At minimum:

```text
Unknown
Online
Offline
Unavailable
```

Do not put transfer states here.

Incorrect:

```text
DeviceStatus.Sending
DeviceStatus.Receiving
DeviceStatus.Transferring
```

Those belong to `TransferStatus`.

---

# 23. LastSeen

Track the last time a device was observed.

Use:

```text
DateTimeOffset
```

This enables presence tracking.

A device should not become offline merely because one heartbeat was lost.

UDP is unreliable.

The application should tolerate several missed announcements.

---

# 24. Discovery Transport

Use UDP for LAN discovery.

Do NOT scan every IP address in the subnet.

Do NOT attempt:

```text
192.168.1.1
192.168.1.2
192.168.1.3
...
```

Use broadcast or an appropriate multicast mechanism.

The discovery transport should be encapsulated inside `DeviceDiscovery`.

---

# 25. Discovery Protocol

Discovery announcements should contain enough information to identify the sender and locate its TCP server.

At minimum:

```text
DeviceId
DeviceName
TcpPort
ProtocolVersion
MessageType
```

The source IP should normally be taken from the UDP packet's source endpoint rather than blindly trusting a self-reported IP.

---

# 26. Discovery Lifecycle

`DeviceDiscovery` should support:

```text
Created
    ↓
Started
    ↓
Listening + announcing + heartbeat
    ↓
Stopping
    ↓
Stopped
```

It must support:

- asynchronous operation
- cancellation
- clean shutdown
- socket disposal

No infinite uncancellable loops.

---

# 27. Heartbeats

Devices periodically announce their presence.

Conceptually:

```text
announce
wait
announce
wait
announce
...
```

The heartbeat interval should be reasonably short.

The offline timeout should allow several missed heartbeats.

Avoid excessive network traffic.

---

# 28. Offline Detection

A device becomes offline when:

```text
CurrentTime - LastSeen > OfflineTimeout
```

Do not delete it immediately.

Instead transition:

```text
Online → Offline
```

If it later announces itself again:

```text
Offline → Online
```

This allows temporary network interruptions to recover naturally.

---

# 29. Duplicate Devices

Discovery packets are repeated.

Therefore duplicate announcements must not create duplicate devices.

Use:

```text
Device.Id
```

as the unique key.

Conceptually:

```text
Dictionary<Guid, Device>
```

or an equivalent structure.

---

# 30. IP Changes

A device's IP may change.

For example:

```text
Device A
192.168.1.20
```

later:

```text
Device A
192.168.1.37
```

The device ID remains the same.

Update the existing device.

Do not create a second device.

---

# 31. Local Device Filtering

The application must ignore its own discovery announcements.

This must be determined using:

```text
Device.Id
```

not:

```text
IP address
```

because IP addresses can change.

---

# 32. Discovery Concurrency

Discovery can involve:

- UDP receiving
- heartbeat sending
- expiration checks
- application shutdown
- event notifications

The device collection must be protected from race conditions.

Do not expose a mutable internal collection directly.

Consumers should receive read-only information.

---

# 33. Discovery Events

Future UI and transfer code needs to know when devices change.

The discovery layer should expose an appropriate notification mechanism.

Potential concepts include:

```text
DeviceDiscovered
DeviceUpdated
DeviceStatusChanged
DeviceRemoved
```

Do not automatically implement all of them if a smaller API is cleaner.

Avoid event spam.

A heartbeat should refresh `LastSeen` without necessarily causing a UI update every few seconds.

---

# 34. Network Interface Handling

Computers can have many interfaces:

- Wi-Fi
- Ethernet
- VPN
- Docker
- Hyper-V
- loopback

Discovery should behave sensibly.

At minimum:

- avoid loopback
- handle multiple active interfaces
- do not crash because one interface fails
- continue where possible if one network interface disappears

Do not overengineer this into a massive networking subsystem.

---

# 35. Discovery Security

Full security is later.

However, all UDP packets are untrusted.

Validate:

- packet size
- message type
- protocol version
- device ID
- device name length
- TCP port
- serialization

Do not execute arbitrary operations because of a discovery packet.

Discovery means:

> "I found a peer."

It does NOT mean:

> "I trust the peer."

---

# 36. Phase 3 — Transfer Domain

Once discovery works, create the actual transfer-domain models.

Core concepts:

```text
Transfer
TransferFile
TransferStatus
TransferDirection
```

---

# 37. Transfer

A `Transfer` represents one logical transfer operation.

It should track things such as:

```text
Transfer ID
Source device
Destination device
Direction
Files
Status
Total bytes
Transferred bytes
Created time
Started time
Completed time
Error information
```

Exact structure should be determined during implementation.

Do not put raw networking implementation into the domain model.

---

# 38. TransferDirection

Use a strongly typed enum.

For example:

```text
Sending
Receiving
```

Potentially:

```text
Unknown
```

if genuinely needed.

Do not represent direction using strings.

---

# 39. TransferStatus

Transfer state must be separate from device state.

Possible states:

```text
Pending
Connecting
WaitingForAcceptance
Transferring
Completed
Cancelled
Failed
Rejected
```

Only use states that are actually meaningful.

The transfer state should have clear legal transitions.

---

# 40. TransferFile

Do not call the application model `FileInfo`.

.NET already has:

```text
System.IO.FileInfo
```

Using the same name would create unnecessary ambiguity.

Prefer something like:

```text
TransferFile
```

or:

```text
FileMetadata
```

depending on the final design.

It should describe a file, not perform file I/O.

Potential information:

```text
FileName
RelativePath
Size
Extension
ModifiedTime
Hash
```

Hash may be added later when integrity verification is implemented.

---

# 41. File Metadata vs File I/O

A file metadata model should NOT:

- open files
- read file contents
- send files
- write files
- communicate over TCP

Those responsibilities belong to the transfer implementation.

---

# 42. Phase 4 — Actual File Transfer

This phase implements real file movement.

Components:

```text
TransferManager
FileSender
FileReceiver
```

---

# 43. TransferManager

`TransferManager` coordinates transfers.

It should:

- create transfers
- coordinate connections
- negotiate transfers
- track state
- handle cancellation
- report progress
- coordinate sender/receiver
- manage transfer lifecycle

It should NOT directly contain every byte-level file-transfer detail.

---

# 44. FileSender

`FileSender` is responsible for reading files and transmitting their contents.

It should:

- open files safely
- stream data
- send chunks
- support cancellation
- report progress
- handle read errors
- handle network errors

Do not load entire files into memory.

A 10 GB file should not require 10 GB of RAM.

Use streaming.

---

# 45. FileReceiver

`FileReceiver` is responsible for writing incoming data.

It should:

- create destination files safely
- write incoming chunks
- support cancellation
- report progress
- handle disk errors
- handle permission errors
- clean up incomplete files when appropriate

---

# 46. Chunked Transfer

Large files should be transferred in chunks.

Conceptually:

```text
File
 ↓
Chunk
 ↓
Network
 ↓
Chunk
 ↓
Network
 ↓
...
```

The chunk size should be chosen based on performance and memory considerations.

Do not make it so large that memory usage becomes problematic.

Do not make it so small that protocol overhead becomes excessive.

---

# 47. Multiple Files

The transfer system should support multiple files.

The sender should transmit metadata before the actual file contents.

The receiver should know:

- filename
- relative path
- size
- ordering/identity
- transfer association

The system should not assume every transfer contains exactly one file.

---

# 48. Folder Transfers

Folders should eventually be represented using relative paths.

Example:

```text
Photos/
    2026/
        image1.jpg
        image2.jpg
```

The transfer should preserve the relative structure.

Absolute source paths must NOT be sent as destination paths.

---

# 49. Path Traversal Protection

Incoming paths are untrusted.

Never allow a malicious sender to create:

```text
..\..\..\Windows\...
```

or equivalent traversal paths.

Normalize and validate paths before writing.

The receiver must guarantee that every output path remains inside the user-selected destination directory.

---

# 50. Transfer Progress

The application should eventually track:

```text
Bytes transferred
Total bytes
Percentage
Transfer speed
Estimated remaining time
```

Progress must not be calculated by repeatedly scanning the file system.

Use actual bytes processed.

---

# 51. Cancellation

Users must eventually be able to cancel transfers.

Cancellation must propagate through:

```text
UI
 ↓
TransferManager
 ↓
FileSender/FileReceiver
 ↓
Network
```

Do not merely hide the UI while the transfer continues in the background.

---

# 52. Phase 5 — WinForms UI

Only after the backend is sufficiently stable should the full UI be implemented.

The UI should be modern and polished despite being WinForms.

Main areas:

```text
MainWindow
SendFilesWindow
ReceiveWindow
TransferDetailsWindow
SettingsWindow
```

Exact names may change if better naming is found.

---

# 53. MainWindow

The main window should primarily show:

- available devices
- device status
- device name
- perhaps IP/connection information when useful
- active transfers
- recent transfer information

It should provide obvious actions.

The user should be able to understand the application without reading documentation.

---

# 54. Device UI

Devices should be presented as recognizable selectable items/cards/list entries.

A user should immediately understand:

```text
Which devices are online?
Which device am I selecting?
Which devices are unavailable?
```

Avoid overwhelming the user with technical details.

IP addresses and ports should not dominate the interface.

---

# 55. Send Flow

A typical flow:

```text
Select device
    ↓
Select files/folders
    ↓
Review selection
    ↓
Send
    ↓
Remote device receives request
    ↓
Remote user accepts/rejects
    ↓
Transfer begins
    ↓
Progress shown
    ↓
Completion
```

The UI should clearly communicate each state.

---

# 56. Incoming Transfer UI

When another device requests a transfer, show:

- sender
- number of files
- total size
- destination information where appropriate
- Accept
- Reject

Do not silently write arbitrary incoming files.

User consent should be explicit.

---

# 57. Transfer Details UI

Display:

- file count
- total size
- transferred amount
- progress
- speed
- estimated time
- current file
- status
- error information when relevant

The interface should remain responsive.

Never perform blocking network/file operations on the UI thread.

---

# 58. UI/UX Principles

Use modern UI/UX principles:

- clear hierarchy
- obvious primary actions
- minimal unnecessary information
- consistent spacing
- consistent typography
- clear status indicators
- meaningful empty states
- meaningful loading states
- meaningful error states
- accessible controls
- keyboard navigation
- proper DPI scaling
- responsive layouts
- sensible window resizing

Do not sacrifice usability merely because WinForms is being used.

---

# 59. Phase 6 — Reliability

After transfers work, harden the system.

Handle:

- connection loss
- device disappearance
- Wi-Fi interruptions
- Ethernet disconnects
- application shutdown
- transfer cancellation
- disk-full errors
- permission errors
- invalid paths
- invalid filenames
- destination conflicts
- malformed messages
- timeouts

Add retries only where retries actually make sense.

Do not blindly retry everything.

---

# 60. Concurrent Transfers

Eventually support multiple simultaneous transfers.

The implementation must have clear limits so that:

- CPU usage remains reasonable
- disk I/O does not become chaotic
- memory usage stays bounded
- network throughput remains good

Avoid creating unlimited tasks.

Use controlled concurrency.

---

# 61. Transfer Queue

If multiple transfers are requested, consider a transfer queue.

The queue should make the behavior predictable.

Potential policies:

- one transfer at a time
- limited concurrency
- user-configurable concurrency

Do not add complexity unless it provides actual value.

---

# 62. Phase 7 — File Integrity

Files must eventually be verified after transfer.

Use a cryptographic hash such as:

```text
SHA-256
```

The sender computes the expected hash.

The receiver computes the received hash.

Compare them.

Also verify file size.

Conceptually:

```text
Sender:
    file → SHA-256

Receiver:
    received file → SHA-256

Compare
    ↓
Match → success
Mismatch → integrity failure
```

An integrity failure must never be reported as successful transfer.

---

# 63. Phase 8 — Security

Security comes after the core transfer system is stable.

Potential security features:

- authenticated peers
- encrypted communication
- TLS / `SslStream`
- certificate handling
- secure peer identity
- protocol validation
- replay considerations where relevant
- safe file handling

The LAN should not automatically be treated as trusted.

---

# 64. Encryption

Eventually network communication should be encrypted.

A likely direction is:

```text
TCP
 ↓
SslStream
 ↓
application protocol
```

Do not invent custom encryption.

Do not encrypt individual chunks with homemade cryptography unless there is a very specific reason.

Use established cryptographic primitives and protocols.

---

# 65. Peer Authentication

The application eventually needs a way to determine:

> "Is this actually a device I trust?"

Device discovery alone is insufficient.

Discovery identifies potential peers.

Authentication establishes trust.

These should remain separate concepts.

---

# 66. Phase 9 — Production Polish

After the functional system is complete:

- application settings
- device naming
- persistent preferences
- transfer history
- notifications
- system tray integration
- optional startup
- keyboard accessibility
- DPI/scaling testing
- theme support
- localization readiness
- logging
- diagnostics
- performance profiling
- packaging
- installer
- clean uninstall
- crash handling
- clean application shutdown

---

# 67. Suggested Final Architecture

The eventual application should conceptually resemble:

```text
┌─────────────────────────────────────────────────────┐
│                     WinForms UI                     │
│                                                     │
│ MainWindow / Send / Receive / Details / Settings  │
└──────────────────────────┬──────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────┐
│                Application Layer                    │
│                                                     │
│ Device discovery coordination                       │
│ Transfer coordination                               │
│ Application state                                   │
└───────────────┬───────────────────┬─────────────────┘
                │                   │
                ▼                   ▼
┌───────────────────────┐   ┌────────────────────────┐
│       Discovery       │   │       Transfers        │
│                       │   │                        │
│ Device                │   │ Transfer               │
│ DeviceStatus          │   │ TransferFile           │
│ DeviceDiscovery       │   │ TransferManager        │
│ UDP discovery         │   │ FileSender             │
│ Presence              │   │ FileReceiver           │
└───────────┬───────────┘   └────────────┬───────────┘
            │                            │
            └────────────┬───────────────┘
                         ▼
              ┌─────────────────────┐
              │      Protocol       │
              │                     │
              │ Message             │
              │ MessageType         │
              │ Serializer          │
              └──────────┬──────────┘
                         │
                         ▼
              ┌─────────────────────┐
              │     Networking      │
              │                     │
              │ NetworkServer       │
              │ NetworkClient       │
              │ Connection          │
              │ TCP framing         │
              └─────────────────────┘
```

Security will eventually wrap around the networking/protocol layer.

---

# 68. Error Handling Philosophy

Errors should be classified.

Examples:

## Expected operational errors

- peer disconnected
- connection refused
- network unavailable
- file does not exist
- access denied
- destination full
- user cancelled

These should be handled gracefully.

## Protocol errors

- malformed message
- invalid message type
- invalid length
- unsupported protocol version

These should result in safe rejection/termination as appropriate.

## Programming errors

Unexpected invariant violations or bugs should not be silently swallowed.

Do not use:

```text
catch (Exception)
{
    // ignore
}
```

as a general error-handling strategy.

---

# 69. Resource Management

The application uses:

- TCP sockets
- UDP sockets
- network streams
- file streams
- background tasks
- cancellation tokens

Everything must have explicit ownership.

Use proper disposal patterns.

When the application shuts down:

```text
UI closes
    ↓
application cancellation
    ↓
transfers stop
    ↓
discovery stops
    ↓
servers stop
    ↓
connections close
    ↓
streams dispose
    ↓
application exits
```

Do not leave background tasks running indefinitely.

---

# 70. Performance Philosophy

The application should be efficient without becoming unnecessarily complicated.

Important principles:

- stream large files
- avoid loading entire files into memory
- avoid unnecessary copying
- use async I/O
- avoid excessive allocations
- reuse buffers where beneficial
- avoid event spam
- avoid unnecessary serialization
- avoid polling the LAN
- avoid unlimited concurrency
- avoid unnecessary TCP connections
- keep discovery packets small

Performance should be measured when it becomes relevant rather than prematurely optimizing everything.

---

# 71. Type Safety

Prefer strong types everywhere.

Examples:

```text
Guid
IPAddress
DateTimeOffset
int
enum
readonly/read-only collections
```

Avoid stringly typed systems.

Bad:

```text
status = "online"
direction = "sending"
port = "5000"
deviceId = "abc"
```

Prefer:

```text
DeviceStatus.Online
TransferDirection.Sending
5000
Guid
```

---

# 72. Naming Rules

Names should communicate responsibility.

Prefer:

```text
DeviceDiscovery
NetworkServer
NetworkClient
Connection
ProtocolSerializer
TransferManager
FileSender
FileReceiver
TransferFile
TransferStatus
DeviceStatus
```

Avoid vague names:

```text
Manager
Helper
Utils
Data
Info
Processor
Handler
Service
```

unless the name accurately describes a well-defined responsibility.

---

# 73. Avoid Overengineering

Do not create abstractions merely because they "look professional."

For example, do not automatically create:

```text
IConnectionFactory
IMessageFactory
IProtocolService
IDeviceRepository
IDiscoveryManager
ITransferCoordinator
IFileProcessor
IMessageHandler
```

just because interfaces exist in other architectures.

Every abstraction must solve a real problem such as:

- testability
- dependency isolation
- multiple implementations
- clear boundary
- lifecycle management

If there is only one implementation and no meaningful reason for an interface, a concrete class may be better.

---

# 74. Testing Philosophy

The application should have tests at multiple levels.

## Unit tests

Test:

- models
- protocol serialization
- framing logic
- validation
- discovery state transitions
- transfer state transitions
- path validation
- integrity verification

## Integration tests

Test:

- TCP client/server
- message exchange
- discovery between instances
- actual file transfers
- cancellation
- disconnects

## Manual UI testing

Test:

- different DPI settings
- different window sizes
- keyboard navigation
- empty states
- errors
- long filenames
- large files
- multiple monitors
- network changes

---

# 75. AI Agent Rules

When an AI agent is asked to implement part of this project, it should follow these rules.

## First inspect the existing code

Before changing anything:

- inspect project structure
- inspect existing classes
- inspect namespaces
- inspect tests
- inspect existing APIs
- understand what previous phases implemented

Do not assume the repository exactly matches this specification.

---

## Do not rewrite working architecture unnecessarily

If an existing implementation is correct and clean, build on it.

Refactor only when:

- necessary for the current phase
- required for correctness
- required for maintainability
- required to remove a clear architectural problem

---

## Stay inside the current phase

If implementing Phase 2, do not suddenly implement:

- transfers
- UI
- encryption
- history

unless explicitly requested.

---

## Explain important architectural decisions

When making a significant design decision, explain:

- what was chosen
- why it was chosen
- what alternatives were considered
- how it affects future phases

---

## Preserve type safety

Avoid:

- `dynamic`
- unnecessary `object`
- unchecked casts
- magic strings
- magic numbers
- nullable abuse

---

## Preserve separation of concerns

Do not put:

```text
networking in forms
file I/O in protocol models
transfer logic in Device
discovery logic in Connection
UI state in backend models
```

---

# 76. Current Development State

The project has already progressed beyond the initial architecture planning.

The networking foundation is being implemented first.

At the current stage:

```text
Phase 1 → Networking & Protocol Foundation
Phase 2 → LAN Device Discovery
```

are the immediate development priorities.

The existing `NetworkServer` and `Connection` classes have already been implemented.

The next major work is the protocol/discovery layer according to the phase roadmap.

When working from the actual repository, inspect what is already present and adapt rather than duplicating existing functionality.

---

# 77. Development Order

The complete planned order is:

```text
PHASE 1
Networking & Protocol Foundation
        ↓
PHASE 2
LAN Device Discovery & Presence
        ↓
PHASE 3
Transfer Domain
        ↓
PHASE 4
Actual File Transfer
        ↓
PHASE 5
WinForms UI
        ↓
PHASE 6
Reliability & Robustness
        ↓
PHASE 7
File Integrity
        ↓
PHASE 8
Security
        ↓
PHASE 9
Production Polish
```

This order is intentional.

---

# 78. Phase 1 Definition of Done

Phase 1 is complete when:

- TCP server works
- TCP client works
- connections are represented by `Connection`
- messages have a clear model
- messages can be serialized/deserialized
- TCP framing works
- partial reads are handled
- partial writes are handled
- message lengths are validated
- concurrent sends cannot corrupt the stream
- cancellation works
- shutdown works
- Hello handshake works
- tests cover the important networking behavior

---

# 79. Phase 2 Definition of Done

Phase 2 is complete when:

- every installation has a persistent device ID
- devices advertise themselves over LAN
- devices automatically discover each other
- the local device is ignored
- duplicate devices are prevented
- device IP changes are handled
- TCP ports are discovered
- device names are discovered
- `LastSeen` is maintained
- heartbeat/presence works
- offline detection works
- online/offline transitions are observable
- malformed packets are safely rejected
- cancellation works
- discovery shuts down cleanly
- tests cover discovery behavior

---

# 80. Phase 3 Definition of Done

Phase 3 is complete when:

- transfers have stable IDs
- transfers have explicit direction
- transfers have explicit status
- files have metadata models
- multiple files can belong to a transfer
- folder structure can be represented
- transfer state is separate from device state
- transfer state is separate from network connection state

---

# 81. Phase 4 Definition of Done

Phase 4 is complete when:

- files can actually be sent
- files can actually be received
- multiple files work
- large files are streamed
- memory usage remains bounded
- progress works
- cancellation works
- transfer failures are handled
- incomplete files are cleaned up safely
- destination paths are validated

---

# 82. Phase 5 Definition of Done

Phase 5 is complete when:

- the main device-selection UI works
- devices are displayed clearly
- files can be selected
- outgoing transfers can be initiated
- incoming requests can be accepted/rejected
- progress is visible
- errors are understandable
- UI remains responsive
- backend remains UI-independent
- DPI scaling works properly

---

# 83. Phase 6 Definition of Done

Phase 6 is complete when:

- disconnects are handled
- network changes are handled
- device disappearance is handled
- disk errors are handled
- permissions are handled
- invalid filenames are handled
- concurrent transfers are controlled
- cancellation is reliable
- application shutdown is reliable

---

# 84. Phase 7 Definition of Done

Phase 7 is complete when:

- file sizes are verified
- SHA-256 hashes are calculated
- sender/receiver hashes are compared
- integrity failures are detected
- corrupted transfers are never reported as successful

---

# 85. Phase 8 Definition of Done

Phase 8 is complete when:

- network traffic is encrypted
- peers can be authenticated
- malicious protocol input is safely rejected
- discovery is not treated as trust
- certificates/identity are handled correctly
- path traversal protections are robust
- security-sensitive operations use established cryptography

---

# 86. Phase 9 Definition of Done

Phase 9 is complete when:

- settings work
- device names/preferences persist
- transfer history works if desired
- notifications work
- system tray integration works if desired
- logging is useful
- diagnostics are available
- performance has been evaluated
- packaging/installer works
- clean shutdown works
- application is ready for real users

---

# 87. Overall User Experience Goal

The final application should feel like:

> "Open the app, see the computers around me, select one, choose files, press Send, and everything else just works."

The complexity should be hidden from the user.

The underlying implementation can be sophisticated, but the UX should remain simple.

A typical user should NOT need to understand:

- TCP
- UDP
- ports
- serialization
- framing
- hashes
- sockets
- network interfaces

The application should handle those details automatically.

---

# 88. Final Product Concept

The final experience should roughly be:

```text
┌─────────────────────────────────────────────┐
│              LAN File Transfer              │
│                                             │
│  Available devices                          │
│                                             │
│  🖥 Gaming PC                    Online      │
│  🖥 Laptop                       Online      │
│  🖥 Desktop                      Offline     │
│                                             │
│              [ Send Files ]                 │
│                                             │
│  Active transfers                           │
│  ─────────────────────────────────────────  │
│  photo.zip              73%     82 MB/s     │
│                                             │
└─────────────────────────────────────────────┘
```

Selecting a device:

```text
Select device
      ↓
Select files
      ↓
Review
      ↓
Send
      ↓
Recipient accepts
      ↓
Transfer
      ↓
Integrity verification
      ↓
Completed
```

The user should always understand:

- what is happening
- what device is involved
- how much has transferred
- whether something failed
- what action is required

---

# 89. Most Important Principles

If there is ever a conflict between convenience and architectural correctness, prefer the design that keeps the system understandable and maintainable.

Remember these boundaries:

```text
Device
    = identity/presence

DeviceDiscovery
    = finding devices

NetworkServer
    = accepting TCP connections

NetworkClient
    = establishing TCP connections

Connection
    = reliably transporting protocol messages

Message
    = protocol information

ProtocolSerializer
    = converting messages ↔ bytes

Transfer
    = one logical file operation

TransferManager
    = coordinating transfers

FileSender
    = reading and sending file data

FileReceiver
    = receiving and writing file data

UI
    = presenting state and accepting user input
```

The most important architectural rule is:

> **Each layer should know what it needs to know, and nothing more.**

---

# 90. Instructions to the AI Receiving This Document

You are now working on this project.

Before making changes:

1. Inspect the repository.
2. Determine which phase the implementation is currently in.
3. Identify which components already exist.
4. Compare the implementation with this specification.
5. Do not duplicate existing functionality.
6. Do not implement future phases prematurely.
7. Preserve existing correct architecture.
8. Follow strong typing and naming conventions.
9. Keep UI and backend separated.
10. Write tests for meaningful behavior.
11. Explain significant architectural decisions.
12. Prefer simple, robust solutions over unnecessary abstractions.

If I explicitly ask you to implement a specific phase, focus on that phase only unless a small prerequisite from an earlier phase is genuinely required.

If the existing repository conflicts with this document, **the actual repository is the source of truth for what already exists**, while this document describes the intended product, architecture, principles, and roadmap.

Do not blindly rewrite the repository to match this document.

Instead, bring the implementation progressively toward the architecture described here.

# End of Project Specification