# ServerWithLimits

SDK-style `net472` console example for a loopback WebSocket echo server with
explicit resource limits.

It demonstrates:

- `WebSocketServer(IPAddress.Loopback, port)` so the sample is local by default.
- `HandshakeTimeout` before `Start()`.
- Per-service `MaxFramePayloadLength`, `MaxMessagePayloadLength`,
  `MaxMessageEventQueueLength`, `MaxAsyncSendQueueLength`, and
  `FrameReadTimeout`.
- Graceful shutdown with `Stop()`.

Run:

```powershell
dotnet run --project .\Examples\ServerWithLimits\ServerWithLimits.csproj
```

Then connect a client to:

```text
ws://localhost:4649/Echo
```
