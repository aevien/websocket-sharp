# websocket-sharp v1.3.1

This patch release fixes ordering and lifecycle behavior for asynchronous sends
while preserving the existing public API and Unity assembly identity.

## Highlights

- Sequential `SendAsync` calls accepted by one physical connection are written
  in FIFO order by a single bounded dispatcher.
- Completion callbacks run independently from the network writer, so a slow or
  throwing callback does not block later physical sends.
- `MaxAsyncSendQueueLength` continues to bound queued, active, and
  callback-pending operations.
- Closing a connection rejects new sends and completes waiting operations with
  `false` while disposing their payload streams.
- Reconnect creates a fresh dispatcher. Data queued for an old connection
  cannot be compressed or written through the new connection.
- Regression coverage includes immediate binary sends from server `OnOpen`,
  FIFO ordering, queue overflow, blocked and throwing callbacks, close/reconnect
  cancellation, stale compressed payloads, and ordered concurrent-client stress.

## Verification

- `135/135` `net472` unit tests passed.
- `10/10` `net472` stress tests passed.
- Immediate server `OnOpen` probe: `1000/1000` first messages received.
- FIFO probe: `20000/20000` sequential async-send pairs arrived in call order.
- Sustained ordering stress: `100 CCU` completed `100000` ordered messages
  without loss, duplication, ordering errors, or stranded sessions.
- Public API snapshot and Unity/IL2CPP compatibility gates passed.

## Compatibility

- Target framework: `net472`.
- Unity: managed plugin for .NET 4.x-compatible Editor, Standalone, and tested
  mobile/IL2CPP targets.
- WebGL: not supported by this managed TCP socket implementation.
- Assembly name and strong name remain unchanged.
- `AssemblyVersion` remains `1.0.2.32832` for existing Unity binary references.
- File and product version: `1.3.1.0`.

## Assets

- `websocket-sharp.dll`: the signed `net472` library.
- `websocket-sharp-v1.3.1-unity-net472.zip`: DLL, license, README, and these
  release notes.
- `SHA256SUMS.txt`: SHA-256 checksums for the DLL and ZIP.
