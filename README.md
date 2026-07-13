![Logo](websocket-sharp_logo.png)

## websocket-sharp for Unity and .NET 4.x ##

This fork is maintained for Unity/.NET 4.x usage while keeping the original
`websocket-sharp` assembly identity stable for existing Unity projects.
Fork modifications are copyright (c) 2026 aevien.

Current release:

- Tag: `v1.3.1`
- Release: [websocket-sharp v1.3.1](https://github.com/aevien/websocket-sharp/releases/tag/v1.3.1)
- Target framework: `net472`
- Assembly name: `websocket-sharp`
- Assembly version: `1.0.2.32832` (kept for Unity binary compatibility)
- File/product version: `1.3.1.0`
- WebGL: not supported by this managed socket implementation. Unity WebGL should
  continue to use the browser JavaScript WebSocket layer.

Recent fork changes include safer TLS certificate validation defaults, bounded
client/server handshake timeouts including TLS handshakes, replacement of delegate `BeginInvoke` usage,
async lifecycle fixes, FIFO `SendAsync` ordering, connect-storm protection, lifecycle stress coverage,
stricter RFC 6455 frame validation, bounded receive/send resource limits,
partial-frame receive timeouts, and bounded HTTP/WebSocket handshake parsing.
The test suite also guards the public API surface and Unity/IL2CPP compatibility
against accidental regressions.

websocket-sharp supports:

- [RFC 6455](#supported-websocket-specifications)
- [WebSocket Client](#websocket-client) and [Server](#websocket-server)
- [Per-message Compression](#per-message-compression) extension
- [Secure Connection](#secure-connection)
- [HTTP Authentication](#http-authentication)
- [Query string, Origin header, Cookies, and User headers](#query-string-origin-header-cookies-and-user-headers)
- [Connecting through the HTTP proxy server](#connecting-through-the-http-proxy-server)
- .NET Framework **4.7.2** / Unity **.NET 4.x** compatible environments

### Compatibility scope ###

| Environment | Status |
| --- | --- |
| .NET Framework 4.7.2 | Primary build and test target (`net472`) |
| Unity Editor and Standalone with the .NET 4.x profile | Supported as a managed plugin |
| Unity IL2CPP on platforms with managed TCP sockets | Compatible by design; Windows x64 IL2CPP has been runtime-verified |
| Other Unity IL2CPP targets | Require a runtime test on the actual target platform |
| Unity WebGL | Not supported by this DLL; use the browser JavaScript WebSocket implementation |
| Windows 7 and Windows 8 | Outside this fork's compatibility target |

The library intentionally remains on `net472` to preserve compatibility with
existing Unity projects. It is not a modern .NET (`net8.0`, `net9.0`, or later)
target.

## Branches ##

- `main` contains release-ready code.
- `dev` is used for ongoing development.

## Verification summary ##

The current repository state was verified as a self-built Unity/.NET 4.x DLL.

- Repository normal suite: `135/135` NUnit tests passed on `net472`.
- Repository stress suite: `10/10` stress tests passed on `net472`.
- Examples build: legacy `Example`, `Example2`, `Example3` and modern console
  examples under `Examples` build on `net472`.
- Async compatibility: no `BeginInvoke` / `EndInvoke` usage remains in `websocket-sharp` or tests.
- Assembly identity: assembly name, strong-name token, and `AssemblyVersion("1.0.2.32832")` remain stable for existing Unity references.
- Version metadata: assembly file/informational versions and DLL file/product versions all report `1.3.1.0`.
- Public API snapshot: exported public types, constructors, methods, properties, events, fields, and enum values are compared to a checked-in snapshot.
- Unity/IL2CPP static scan: library sources are checked for known incompatible constructs such as delegate `BeginInvoke`/`EndInvoke`, runtime code generation, `Thread.Abort`, binary serialization, P/Invoke, runtime compilation, remoting, and dynamic assembly loading. This scan is a compatibility gate, not a platform build.
- Unity smoke: the updated DLL was imported into a Unity project with Editor/Standalone plugin settings and passed the project smoke test.
- Unity transport integration: the imported production DLL passed immediate-first-message and FIFO tests through the MST adapter. A Windows x64 IL2CPP player received all first messages and all ordered message pairs without the historical post-open delay.
- TLS/WSS: default certificate validation rejects certificate policy errors, custom validation remains user-controlled, and secure loopback echo works with an explicitly trusted self-signed certificate.
- TLS handshake timeout: silent TLS peers are bounded by client `ConnectionTimeout`, secure `WebSocketServer.HandshakeTimeout`, and secure `HttpServer.HandshakeTimeout`.
- TLS stress: 20 silent TLS handshakes are disconnected by the server timeout while a valid secure echo client still opens, echoes, and closes.
- Async lifecycle: repeated `ConnectAsync` / `SendAsync` / `CloseAsync` cycles complete successfully, including a 500-cycle stress run.
- Async send ordering: immediate binary sends from server `OnOpen` passed 1000/1000
  external probe connections, and 20000/20000 sequential `SendAsync` pairs
  arrived in call order. The repository suite also covers blocked and throwing
  callbacks, bounded queue rejection, close/reconnect cancellation, and stale
  compressed payload disposal.
- Connection timeout: silent TCP peers do not keep `Connect()` waiting for the old hardcoded timeout.
- Proxy path: HTTP CONNECT tunnel echo, silent proxy timeout, failed proxy response, 407 without credentials, and Basic proxy auth retry after a closed challenge connection are covered.
- Server handshake timeout: silent or slow TCP handshakes are disconnected without blocking valid WebSocket handshakes.
- Bounded server handshakes: 200 silent clients with limits of 4 active and 8 pending handshakes rejected 188 excess connections while process thread count grew by only 4 worker threads; a valid echo client connected after recovery.
- Bounded HTTP upgrade handshakes: an `HttpServer` configured for 2 active and 1 pending handshake opened 3 of 20 blocked upgrade requests, rejected 17, recovered for echo, and passed `Stop` / `Start` reuse.
- Shutdown isolation: a blocked user handshake callback kept the server in `ShuttingDown`, prevented restart with a live old worker, and allowed a clean stop, restart, and echo after the callback exited.
- Handshake parser limits: oversized handshake headers, too-long request/header lines, and header-count flooding are rejected before a WebSocket session starts.
- Handshake body limits: upgrade requests and successful handshake responses reject any body before reading it; HTTP error bodies stop at `64 KiB`; declared `1 GiB`, chunked, and `101 Content-Length: 1` probes all transmitted `0` body bytes before disconnect.
- Chunked challenge compatibility: a chunked `407 Proxy Authentication Required` response is handled by closing the unusable connection, reconnecting with Basic credentials, opening the tunnel, and completing echo.
- Redirect policy: status codes `301`, `302`, `303`, `307`, and `308`, relative
  locations, bounded loops, redirected Digest paths, reconnects, cross-origin
  WSS host changes, and explicit WSS-to-WS downgrade opt-in are covered.
- Redirect credential isolation: HTTP credentials, cookies, user headers, and
  TLS client certificates are not forwarded across origins; reconnecting to a
  redirected origin does not restore the original secrets.
- Proxy redirects: Digest proxy authentication recomputes the `CONNECT`
  authority after a cross-origin redirect instead of reusing the first target.
- Handshake log safety: Debug logs preserve request/status lines, header names,
  and normalized structural WebSocket facts while redacting request paths,
  query values, authentication, cookies, custom header values, untrusted reason
  phrases, response secrets, and HTTP error bodies on client,
  `WebSocketServer`, and `HttpServer` paths.
- Client handshake abuse: malicious server responses with too many headers, too-long status/header lines, or invalid status lines are rejected without opening the WebSocket or hanging `Connect()`.
- Load coverage: 100 concurrent clients completed 1000 ordered echo messages
  each, for 100000 async text sends and callbacks without loss, duplication,
  ordering errors, or stranded sessions.
- Connect storm coverage: 50 simultaneous `ConnectAsync` clients open and close without ThreadPool starvation.
- Resource lifecycle: repeated connect-storm and slow-handshake rounds return sessions to zero and do not show steady-state thread drift beyond the accepted bounds.
- Resource abuse stress: 50 rejected handshake-flood clients and 25 fragment-limit clients complete without blocking a valid echo client or stranding sessions.
- Close lifecycle: repeated `Close` / `CloseAsync` / `Dispose` calls, abrupt raw TCP disconnects, protocol-error close frames, and exception-throwing close/error handlers return server sessions to zero.
- Protocol frames: payload boundaries `125`, `126`, and `66000` bytes round-trip; fragmented text can receive interleaved ping; reserved opcodes, unexpected RSV flags, invalid continuation sequences, close during fragmentation, and malformed frames close protocol-error sessions.
- Close-frame validation: one-byte payloads, invalid/reserved close codes, invalid UTF-8 reasons, oversized control payloads, and non-minimal extended length encoding are covered.
- Compression: permessage-deflate text echo, fragmented compressed input, corrupt compressed payloads, and compressed control-frame protocol errors are covered.
- Payload limits: oversized single frames, fragmented messages over the assembled-message limit, many small fragments over the assembled-message limit, and compressed messages that inflate past the configured limit close with `1009 TooBig` without delivering `OnMessage`.
- Receive timeout: idle open connections are not closed by the timeout, but partial frame header/payload stalls close with protocol error without delivering `OnMessage`.

## Build ##

websocket-sharp is built as a single assembly, **websocket-sharp.dll**.

This fork uses SDK-style projects targeting `net472`. A development build of
the library alone can be created with:

```powershell
dotnet build websocket-sharp\websocket-sharp.csproj -c Release
```

The release DLL is written to:

```text
websocket-sharp\bin\Release\net472\websocket-sharp.dll
```

Repository tests:

```powershell
dotnet test tests\WebSocketSharp.Tests\WebSocketSharp.Tests.csproj -c Release
dotnet test tests\WebSocketSharp.StressTests\WebSocketSharp.StressTests.csproj -c Release --filter TestCategory=Stress
```

A production release must pass the same gates as CI before packaging. The .NET
8 SDK is used to build the SDK-style projects while the resulting library still
targets .NET Framework 4.7.2:

```powershell
dotnet restore websocket-sharp.sln
dotnet build websocket-sharp.sln -c Release --no-restore -p:TreatWarningsAsErrors=true
dotnet test tests\WebSocketSharp.Tests\WebSocketSharp.Tests.csproj -c Release --no-build --no-restore
dotnet test tests\WebSocketSharp.StressTests\WebSocketSharp.StressTests.csproj -c Release --no-build --no-restore --filter TestCategory=Stress
.\scripts\Build-Release.ps1 -Version 1.3.1 -SkipBuild
```

`Build-Release.ps1` packages an already verified build. It does not run the test
suites itself, so do not use it as a substitute for the commands above.

GitHub Actions:

- `CI` runs on `main`, `dev`, and pull requests to those branches.
- `CI` builds the full solution and runs both normal and stress suites.
- `Stress Tests` can also be started manually from the Actions tab.

## Install ##

### GitHub Release ###

Download the Unity release from the GitHub release page:

- [websocket-sharp.dll](https://github.com/aevien/websocket-sharp/releases/download/v1.3.1/websocket-sharp.dll)
- [websocket-sharp-v1.3.1-unity-net472.zip](https://github.com/aevien/websocket-sharp/releases/download/v1.3.1/websocket-sharp-v1.3.1-unity-net472.zip)
- [SHA256SUMS.txt](https://github.com/aevien/websocket-sharp/releases/download/v1.3.1/SHA256SUMS.txt)

Verify downloaded release files against `SHA256SUMS.txt` before importing them.

### Self Build ###

Add `websocket-sharp.dll` (for example,
`websocket-sharp\bin\Release\net472\websocket-sharp.dll`) to your project's
assembly references.

For Unity, place the DLL under `Assets`, normally in `Assets/Plugins` or the
plugin folder owned by the transport that uses it. Preserve the existing Unity
`.meta` file when replacing an already imported DLL.

Recommended Unity import settings for this managed DLL:

- `Auto Reference`: enabled
- `Validate References`: enabled
- `Any Platform`: disabled when you need explicit platform control
- Include `Editor`, `Standalone`, and any mobile/IL2CPP target you actually test
- Exclude `WebGL`; use the browser JavaScript WebSocket layer there
- In Player Settings, use the `.NET Framework` API Compatibility Level where the
  Unity version exposes that choice

For IL2CPP builds, keep this DLL as a managed plugin. Repository verification
includes a source-level scan for known incompatible constructs such as runtime
code generation and delegate `BeginInvoke`/`EndInvoke`. Windows x64 IL2CPP has
also passed a runtime transport test. The source scan does not replace IL2CPP
compilation and runtime testing on every mobile or console target used by your
project.

## Fork API Reference ##

This section documents the public configuration added by this fork. The values
are per `WebSocket`, per server host, or per server behavior; the static
`Default*` fields expose defaults for inspection and are not global mutable
settings.

### Configuration lifecycle ###

- Set client `WebSocket` properties before `Connect()` or `ConnectAsync()`.
  They can be changed only while `ReadyState` is `New` or `Closed`; otherwise
  the setter throws `InvalidOperationException`.
- Set `WebSocketBehavior` properties in its constructor or in the initializer
  passed to `AddWebSocketService`. Changing them after the session has been
  registered throws `InvalidOperationException`.
- Set `WebSocketServer` and `HttpServer` handshake properties before `Start()`.
  Their setters accept values only while the server state is `Ready` or `Stop`;
  a set attempted while the server is running is ignored.
- Invalid numeric values throw `ArgumentOutOfRangeException`.

### Published defaults ###

| Read-only field | Value | Used by |
| --- | ---: | --- |
| `WebSocket.DefaultMaxFramePayloadLength` | `16 MiB` | `WebSocket.MaxFramePayloadLength`, `WebSocketBehavior.MaxFramePayloadLength` |
| `WebSocket.DefaultMaxMessagePayloadLength` | `64 MiB` | `WebSocket.MaxMessagePayloadLength`, `WebSocketBehavior.MaxMessagePayloadLength` |
| `WebSocket.DefaultMaxMessageEventQueueLength` | `1024` | Receive-event queues |
| `WebSocket.DefaultMaxAsyncSendQueueLength` | `256` | Asynchronous send queues |
| `WebSocket.DefaultFrameReadTimeout` | `10 seconds` | Partial-frame reads |

### WebSocket client and connection settings ###

| Property | Default | Valid values | Effect |
| --- | ---: | --- | --- |
| `ConnectionTimeout` | `10 seconds` | `> 0` | Bounds each TCP connect, proxy response, TLS authentication, and WebSocket handshake response stage. Client instances only. |
| `FrameReadTimeout` | `10 seconds` | `> 0` | Inactivity timeout after a frame has started. Progress resets the timer; an idle open connection is unaffected. A timeout closes with `1002` (`ProtocolError`). |
| `MaxFramePayloadLength` | `16 MiB` | `>= 125` bytes | Rejects an individual received frame above the limit with close code `1009` (`TooBig`). |
| `MaxMessagePayloadLength` | `64 MiB` | `> 0` bytes | Rejects an assembled or decompressed message above the limit with close code `1009`; `OnMessage` is not invoked for it. |
| `MaxMessageEventQueueLength` | `1024` | `> 0` | Bounds received events waiting for `OnMessage`, including ping events when `EmitOnPing` is enabled. Overflow closes with `1008` (`PolicyViolation`). |
| `MaxAsyncSendQueueLength` | `256` | `> 0` | Bounds accepted asynchronous send operations, including active and callback-pending work. Rejected operations complete with `false`. |
| `EnableRedirection` | `false` | `true` or `false` | Enables HTTP redirect handling. Redirects remain disabled unless this existing property is explicitly enabled. Client instances only. |
| `MaxRedirections` | `5` | `0..100` | Maximum redirects followed when `EnableRedirection` is enabled. `0` rejects every redirect. Client instances only. |
| `AllowInsecureRedirection` | `false` | `true` or `false` | Allows an explicit `wss://` to `ws://` downgrade. Keep `false` in production unless the downgrade is intentional. Client instances only. |

`MaxFramePayloadLength` and `MaxMessagePayloadLength` are receive limits. They
do not resize outgoing messages. The two limits are independent, so the message
limit may be lower than the frame limit. Keep the message limit at least as large
as the largest application message you expect after decompression. The message
event queue limit counts waiting events, not the handler currently executing.

`ConnectionTimeout` is a per-stage timeout, not one deadline for the entire
`Connect()` operation. Authentication retries, proxy retries, and redirect hops
can each consume another timeout interval. Timeout values are represented
internally as whole milliseconds and are effectively clamped to the range from
`1` through `Int32.MaxValue` milliseconds.

Complete client configuration example:

```csharp
using System;
using WebSocketSharp;

var ws = new WebSocket ("wss://example.com/socket");

ws.ConnectionTimeout = TimeSpan.FromSeconds (10);
ws.FrameReadTimeout = TimeSpan.FromSeconds (10);
ws.MaxFramePayloadLength = 2L * 1024 * 1024;
ws.MaxMessagePayloadLength = 8L * 1024 * 1024;
ws.MaxMessageEventQueueLength = 512;
ws.MaxAsyncSendQueueLength = 256;

ws.EnableRedirection = true;
ws.MaxRedirections = 5;
ws.AllowInsecureRedirection = false;

ws.OnOpen += (sender, e) => ws.SendAsync ("ready", completed => {
  if (!completed)
    Console.Error.WriteLine ("The send was rejected or canceled.");
});

ws.OnMessage += (sender, e) => Console.WriteLine (e.Data);
ws.OnError += (sender, e) => Console.Error.WriteLine (e.Message);
ws.ConnectAsync ();
```

### WebSocketBehavior session settings ###

Each server session receives its own `WebSocketBehavior` instance. These
properties copy their values to the session's underlying `WebSocket` when the
session is registered:

| Property | Default | Valid values | Effect |
| --- | ---: | --- | --- |
| `FrameReadTimeout` | `10 seconds` | `> 0` | Partial-frame read timeout for this service session. |
| `MaxFramePayloadLength` | `16 MiB` | `>= 125` bytes | Per-session received frame limit. |
| `MaxMessagePayloadLength` | `64 MiB` | `> 0` bytes | Per-session assembled/decompressed message limit. |
| `MaxMessageEventQueueLength` | `1024` | `> 0` | Per-session receive-event queue limit. |
| `MaxAsyncSendQueueLength` | `256` | `> 0` | Per-session asynchronous send queue limit. |

```csharp
wssv.AddWebSocketService<Echo> (
  "/Echo",
  session => {
    session.FrameReadTimeout = TimeSpan.FromSeconds (5);
    session.MaxFramePayloadLength = 1024 * 1024;
    session.MaxMessagePayloadLength = 4 * 1024 * 1024;
    session.MaxMessageEventQueueLength = 256;
    session.MaxAsyncSendQueueLength = 128;
  }
);
```

### Server handshake settings ###

The same host-level API is available on `WebSocketServer` and `HttpServer`:

| Property | Default | Valid values | Effect |
| --- | ---: | --- | --- |
| `HandshakeTimeout` | `10 seconds` | `> 0` | Bounds the first HTTP/WebSocket request. On secure servers, TLS and the first request each receive a timeout interval. |
| `MaxConcurrentHandshakes` | `128` | `>= 1` | Maximum handshakes executing at once. It does not limit established sessions or CCU. |
| `MaxPendingHandshakes` | `4096` | `>= 1` | Maximum accepted handshakes waiting for a worker. Excess connections are closed and rejection counts are rate-limited in the log. |

`MaxConcurrentHandshakes` is not a client or CCU limit. For example, a value of
`128` permits 128 connections to perform the opening handshake at the same time;
after they open, those sessions no longer occupy handshake capacity.
The maximum admitted opening load is the active limit plus the pending limit.
When the pending queue is full, the TCP connection is closed without a WebSocket
session; the rejection warning is logged for the first rejection and then every
256 rejections to avoid log flooding.

```csharp
var wssv = new WebSocketServer (4649) {
  HandshakeTimeout = TimeSpan.FromSeconds (10),
  MaxConcurrentHandshakes = 128,
  MaxPendingHandshakes = 4096
};

wssv.AddWebSocketService<Echo> ("/Echo");
wssv.Start ();
```

Use the same properties on `HttpServer` when it accepts WebSocket upgrades.
Normal HTTP requests are not dispatched through the bounded WebSocket handshake
queue.

`Stop()` waits up to five seconds for handshake workers. If user handshake code
is still blocked, the server remains in `ShuttingDown` and cannot restart yet.
Release the callback and call `Stop()` again before `Start()`.

### Async send ordering and lifecycle ###

Accepted `SendAsync` operations are written by one FIFO dispatcher per physical
connection. This guarantees call order for sequential sends accepted by that
connection, including sends made immediately from `OnOpen`. An arbitrary delay
after `OnOpen` is not required by the library.

- A slow or throwing completion callback does not block later network writes.
- Queue overflow rejects the operation and invokes its completion callback with
  `false`.
- Closing the connection rejects new sends, cancels queued operations with
  `false`, and disposes their internally owned payload streams.
- Reconnecting the same `WebSocket` creates a fresh dispatcher, so data queued
  for the old connection cannot be sent through the new one.
- Completion callbacks are asynchronous application callbacks. Synchronize
  shared state and marshal work to Unity's main thread yourself.

FIFO applies to physical network writes, not to completion callback execution.
An operation rejected immediately because the queue is full or the connection
state changed may receive its `false` callback synchronously.

### Redirect, credential, TLS, and log safety ###

Client redirects remain opt-in through the existing `EnableRedirection`
property. Redirects from `wss://` to `ws://` are rejected unless
`AllowInsecureRedirection` is explicitly enabled. A cross-origin redirect does
not forward HTTP credentials, cookies, custom user headers, or TLS client
certificates. The isolation also applies when the same `WebSocket` instance
reconnects after a redirect.

Redirect status codes `301`, `302`, `303`, `307`, and `308` are supported,
including relative `Location` values. Origins match only when scheme, host, and
port all match. The server-certificate validation callback remains configured
across a redirect, while the TLS target host is updated to the redirected host.

The default WSS certificate callback accepts only certificates that pass the
platform's certificate validation without `SslPolicyErrors`. A custom callback
is responsible for complete validation; never use a callback that returns
`true` for every certificate in production.

Client and server TLS protocols default to `SslProtocols.None`, allowing the
operating system to select enabled protocols. Certificate revocation checking is
off by default. On a secure server, client certificates are optional by default;
the default client-certificate callback accepts a valid certificate or no client
certificate, but rejects certificate name and chain errors. Configure mTLS,
revocation, and explicit protocol policy on `SslConfiguration` before
`Connect()` or `Start()` when your deployment requires them. TLS behavior and
certificate stores remain platform-dependent in Unity.

Built-in handshake Debug logs are sanitized. Request paths, query values,
authorization, cookies, custom header values, untrusted reason phrases, response
secrets, and HTTP error bodies are redacted. Public HTTP/WebSocket context
`ToString()` methods retain their historical raw formatting for API
compatibility, so do not write those values to production logs without your own
redaction.

### Fixed handshake parser guardrails ###

These safety limits are intentionally fixed and do not have public setters. The
`8 KiB`/`2 KiB`/`64 fields` limits apply to the direct `WebSocketServer` and
client-side HTTP/WebSocket handshake parser. `HttpServer` uses its own HTTP
listener parser with a `32 KiB` aggregate request-input limit.

| Input | Limit or policy |
| --- | --- |
| Complete handshake header section | `8 KiB` |
| Request, status, or individual header line | `2 KiB` |
| Parsed header fields | `64` |
| WebSocket upgrade request body | Rejected; expected size is `0 bytes` |
| Successful WebSocket or proxy handshake response body | Rejected; expected size is `0 bytes` |
| HTTP error response body read during handshake | `64 KiB` |
| `Transfer-Encoding` on upgrade requests or successful handshake responses | Rejected |
| `Transfer-Encoding` on HTTP error responses | Body is skipped and the connection is closed before authentication, proxy, or redirect retry |

## Usage ##

### WebSocket Client ###

```csharp
using System;
using WebSocketSharp;

namespace Example
{
  public class Program
  {
    public static void Main (string[] args)
    {
      using (var ws = new WebSocket ("ws://localhost:4649/Echo")) {
        ws.OnMessage += (sender, e) =>
                          Console.WriteLine ("Server says: " + e.Data);

        ws.Connect ();
        ws.Send ("Hello");
        Console.ReadKey (true);
      }
    }
  }
}
```

#### Step 1 ####

Required namespace.

```csharp
using WebSocketSharp;
```

The `WebSocket` class exists in the `WebSocketSharp` namespace.

#### Step 2 ####

Creating a new instance of the `WebSocket` class with the WebSocket URL to connect.

```csharp
var ws = new WebSocket ("ws://example.com");
```

The `WebSocket` class inherits the `System.IDisposable` interface, so you can create it with the `using` statement.

```csharp
using (var ws = new WebSocket ("ws://example.com")) {
  ...
}
```

This will **close** the WebSocket connection with status code `1001` (going away) when the control leaves the `using` block.

#### Step 3 ####

Setting the `WebSocket` events.

##### WebSocket.OnOpen Event #####

This event occurs when the WebSocket connection has been established.

```csharp
ws.OnOpen += (sender, e) => {
               ...
             };
```

`System.EventArgs.Empty` is passed as `e`, so you do not need to use it.

##### WebSocket.OnMessage Event #####

This event occurs when the `WebSocket` instance receives a message.

```csharp
ws.OnMessage += (sender, e) => {
                  ...
                };
```

A `WebSocketSharp.MessageEventArgs` instance is passed as `e`.

If you would like to get the message data, you should access `e.Data` or `e.RawData` property.

`e.Data` property returns a `string`, so it is mainly used to get the **text** message data.

`e.RawData` property returns a `byte[]`, so it is mainly used to get the **binary** message data.

```csharp
if (e.IsText) {
  // Do something with e.Data.
  ...

  return;
}

if (e.IsBinary) {
  // Do something with e.RawData.
  ...

  return;
}
```

And if you would like to notify that a **ping** has been received, via this event, you should set the `WebSocket.EmitOnPing` property to `true`.

```csharp
ws.EmitOnPing = true;
ws.OnMessage += (sender, e) => {
                  if (e.IsPing) {
                    // Do something to notify that a ping has been received.
                    ...

                    return;
                  }
                };
```

##### WebSocket.OnError Event #####

This event occurs when the `WebSocket` instance gets an error.

```csharp
ws.OnError += (sender, e) => {
                ...
              };
```

A `WebSocketSharp.ErrorEventArgs` instance is passed as `e`.

If you would like to get the error message, you should access `e.Message` property.

`e.Message` property returns a `string` that represents the error message.

And `e.Exception` property returns a `System.Exception` instance that represents the cause of the error if it is due to an exception.

##### WebSocket.OnClose Event #####

This event occurs when the WebSocket connection has been closed.

```csharp
ws.OnClose += (sender, e) => {
                ...
              };
```

A `WebSocketSharp.CloseEventArgs` instance is passed as `e`.

If you would like to get the reason for the close, you should access `e.Code` or `e.Reason` property.

`e.Code` property returns a `ushort` that represents the status code for the close.

`e.Reason` property returns a `string` that represents the reason for the close.

#### Step 4 ####

Connecting to the WebSocket server.

```csharp
ws.Connect ();
```

If you would like to connect to the server asynchronously, you should use the `WebSocket.ConnectAsync ()` method.

#### Step 5 ####

Sending data to the WebSocket server.

```csharp
ws.Send (data);
```

The `WebSocket.Send` method is overloaded.

You can use the `WebSocket.Send (string)`, `WebSocket.Send (byte[])`, `WebSocket.Send (System.IO.FileInfo)`, or `WebSocket.Send (System.IO.Stream, int)` method to send the data.

If you would like to send the data asynchronously, you should use the `WebSocket.SendAsync` method.

```csharp
ws.SendAsync (data, completed);
```

Sequential calls accepted by the same open connection are written in FIFO order.
If the bounded async queue is full, or a queued operation is canceled by close,
its completion callback receives `false`.

If you would like to do something when the send is complete, set `completed` to
an `Action<bool>` delegate. Completion callbacks run independently from the FIFO
network writer, so callback code should still provide its own synchronization.

#### Step 6 ####

Closing the WebSocket connection.

```csharp
ws.Close (code, reason);
```

If you would like to close the connection explicitly, you should use the `WebSocket.Close` method.

The `WebSocket.Close` method is overloaded.

You can use the `WebSocket.Close ()`, `WebSocket.Close (ushort)`, `WebSocket.Close (WebSocketSharp.CloseStatusCode)`, `WebSocket.Close (ushort, string)`, or `WebSocket.Close (WebSocketSharp.CloseStatusCode, string)` method to close the connection.

If you would like to close the connection asynchronously, you should use the `WebSocket.CloseAsync` method.

### WebSocket Server ###

```csharp
using System;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace Example
{
  public class Echo : WebSocketBehavior
  {
    protected override void OnMessage (MessageEventArgs e)
    {
      Send (e.Data);
    }
  }

  public class Program
  {
    public static void Main (string[] args)
    {
      var wssv = new WebSocketServer (System.Net.IPAddress.Loopback, 4649);

      wssv.AddWebSocketService<Echo> ("/Echo");
      wssv.Start ();
      Console.ReadKey (true);
      wssv.Stop ();
    }
  }
}
```

#### Step 1 ####

Required namespace.

```csharp
using WebSocketSharp.Server;
```

The `WebSocketBehavior` and `WebSocketServer` classes exist in the `WebSocketSharp.Server` namespace.

#### Step 2 ####

Creating the class that inherits the `WebSocketBehavior` class.

For example, if you would like to provide an echo service,

```csharp
using System;
using WebSocketSharp;
using WebSocketSharp.Server;

public class Echo : WebSocketBehavior
{
  protected override void OnMessage (MessageEventArgs e)
  {
    Send (e.Data);
  }
}
```

And if you would like to provide a chat service,

```csharp
using System;
using WebSocketSharp;
using WebSocketSharp.Server;

public class Chat : WebSocketBehavior
{
  private string _suffix;

  public Chat ()
  {
    _suffix = String.Empty;
  }

  public string Suffix {
    get {
      return _suffix;
    }

    set {
      _suffix = value ?? String.Empty;
    }
  }

  protected override void OnMessage (MessageEventArgs e)
  {
    Sessions.Broadcast (e.Data + _suffix);
  }
}
```

You can define the behavior of any WebSocket service by creating the class that inherits the `WebSocketBehavior` class.

If you override the `WebSocketBehavior.OnMessage (MessageEventArgs)` method, it will be called when the `WebSocket` used in a session in the service receives a message.

And if you override the `WebSocketBehavior.OnOpen ()`, `WebSocketBehavior.OnError (ErrorEventArgs)`, and `WebSocketBehavior.OnClose (CloseEventArgs)` methods, each of them will be called when each of the `WebSocket` events (`OnOpen`, `OnError`, and `OnClose`) occurs.

The `WebSocketBehavior.Send` method can send data to the client on a session in the service.

If you would like to get the sessions in the service, you should access the `WebSocketBehavior.Sessions` property (returns a `WebSocketSharp.Server.WebSocketSessionManager`).

The `WebSocketBehavior.Sessions.Broadcast` method can send data to every client in the service.

#### Step 3 ####

Creating a new instance of the `WebSocketServer` class.

```csharp
var wssv = new WebSocketServer (4649);

wssv.AddWebSocketService<Echo> ("/Echo");
wssv.AddWebSocketService<Chat> ("/Chat");
wssv.AddWebSocketService<Chat> ("/ChatWithNyan", s => s.Suffix = " Nyan!");
```

You can add any WebSocket service to your `WebSocketServer` with the specified behavior and absolute path to the service, by using the `WebSocketServer.AddWebSocketService<TBehavior> (string)` or `WebSocketServer.AddWebSocketService<TBehavior> (string, Action<TBehavior>)` method.

The type of `TBehavior` must inherit the `WebSocketBehavior` class, and must have a public parameterless constructor.

So you can use a class in the above Step 2 to add the service.

The parameterless constructor uses port `80`, which may require elevated
privileges and can conflict with another service. Prefer an explicit
unprivileged endpoint such as
`new WebSocketServer (IPAddress.Loopback, 4649)` for local development.

#### Step 4 ####

Starting the WebSocket server.

```csharp
wssv.Start ();
```

#### Step 5 ####

Stopping the WebSocket server.

```csharp
wssv.Stop ();
```

### HTTP Server with the WebSocket ###

websocket-sharp includes `WebSocketSharp.Server.HttpServer`, an HTTP listener
implementation that can also accept WebSocket upgrade requests.

You can add any WebSocket service to your `HttpServer` with the specified behavior and path to the service, by using the `HttpServer.AddWebSocketService<TBehavior> (string)` or `HttpServer.AddWebSocketService<TBehavior> (string, Action<TBehavior>)` method.

```csharp
var httpsv = new HttpServer (4649);

httpsv.AddWebSocketService<Echo> ("/Echo");
httpsv.AddWebSocketService<Chat> ("/Chat");
httpsv.AddWebSocketService<Chat> ("/ChatWithNyan", s => s.Suffix = " Nyan!");
```

For more information, see the local `Example3` folder.

### WebSocket Extensions ###

#### Per-message Compression ####

websocket-sharp supports the per-message compression extension, but does not support context takeover.

As a WebSocket client, if you would like to enable this extension, you should set the `WebSocket.Compression` property to a compression method before calling the connect method.

```csharp
ws.Compression = CompressionMethod.Deflate;
```

And then the client will send the following header in the handshake request to the server.

    Sec-WebSocket-Extensions: permessage-deflate; server_no_context_takeover; client_no_context_takeover

If the server supports this extension, it will return the same header which has the corresponding value.

So eventually this extension will be available when the client receives the header in the handshake response.

#### Ignoring the extensions ####

As a WebSocket server, if you would like to ignore the extensions requested from a client, you should set the `WebSocketBehavior.IgnoreExtensions` property to `true` in your `WebSocketBehavior` constructor or initializing it, such as the following.

```csharp
wssv.AddWebSocketService<Chat> (
  "/Chat",
  s => s.IgnoreExtensions = true // To ignore the extensions requested from a client.
);
```

If it is set to `true`, the service will not return the Sec-WebSocket-Extensions header in its handshake response.

This can be useful when diagnosing extension negotiation or when a service must
operate without compression.

### Secure Connection ###

websocket-sharp supports the secure connection with **SSL/TLS**.

As a WebSocket client, you should create a new instance of the `WebSocket` class with a **wss** scheme WebSocket URL.

```csharp
var ws = new WebSocket ("wss://example.com");
```

To provide custom server-certificate validation, set
`WebSocket.SslConfiguration.ServerCertificateValidationCallback` before
connecting.

```csharp
ws.SslConfiguration.ServerCertificateValidationCallback =
  (sender, certificate, chain, sslPolicyErrors) =>
    sslPolicyErrors == System.Net.Security.SslPolicyErrors.None;
```

The default callback accepts only certificates that pass platform validation
without `SslPolicyErrors`. For self-signed or private certificates, provide a
custom `ServerCertificateValidationCallback` and validate the expected
certificate explicitly, for example by pinning its thumbprint or public key.
Never use a production callback that unconditionally returns `true`. See
[Examples/SecureAndProxyClient](Examples/SecureAndProxyClient) for an explicit
thumbprint-pinning example.

As a WebSocket server, you should create a new instance of the `WebSocketServer` or `HttpServer` class with some settings for the secure connection, such as the following.

```csharp
var wssv = new WebSocketServer (5963, true);
wssv.SslConfiguration.ServerCertificate = new X509Certificate2 (
                                            "/path/to/cert.pfx", "password for cert.pfx"
                                          );
```

### HTTP Authentication ###

websocket-sharp supports HTTP Authentication with Basic and Digest schemes.

As a WebSocket client, you should set a pair of user name and password for the HTTP authentication, by using the `WebSocket.SetCredentials (string, string, bool)` method before calling the connect method.

```csharp
ws.SetCredentials ("nobita", "password", preAuth);
```

If `preAuth` is `true`, the client will send the credentials for the Basic authentication in the first handshake request to the server.

Otherwise, it will send the credentials for either the Basic or Digest (determined by the unauthorized response to the first handshake request) authentication in the second handshake request to the server.

As a WebSocket server, you should set an HTTP authentication scheme, a realm, and any function to find the user credentials before calling the start method, such as the following.

```csharp
wssv.AuthenticationSchemes = AuthenticationSchemes.Basic;
wssv.Realm = "WebSocket Test";
wssv.UserCredentialsFinder = id => {
    var name = id.Name;

    // Return user name, password, and roles.
    return name == "nobita"
           ? new NetworkCredential (name, "password", "gunfighter")
           : null; // If the user credentials are not found.
  };
```

If you would like to provide the Digest authentication, you should set such as the following.

```csharp
wssv.AuthenticationSchemes = AuthenticationSchemes.Digest;
```

### Query string, Origin header, Cookies, and User headers ###

#### Query string ####

As a WebSocket client, if you would like to send the query string in the handshake request, you should create a new instance of the `WebSocket` class with a WebSocket URL that includes query string parameters.

```csharp
var ws = new WebSocket ("ws://example.com/?name=nobita");
```

As a WebSocket server, if you would like to get the query string included in a handshake request, you should access the `WebSocketBehavior.QueryString` property, such as the following.

```csharp
public class Chat : WebSocketBehavior
{
  private string _name;
  ...

  protected override void OnOpen ()
  {
    _name = QueryString["name"];
  }

  ...
}
```

#### Origin header ####

As a WebSocket client, if you would like to send the Origin header in the handshake request, you should set the `WebSocket.Origin` property to an allowable value before calling the connect method.

```csharp
ws.Origin = "http://example.com";
```

As a WebSocket server, if you would like to validate the Origin header, you should set a validation for it with your `WebSocketBehavior`, for example, by using the `WebSocketServer.AddWebSocketService<TBehavior> (string, Action<TBehavior>)` method with initializing, such as the following.

```csharp
wssv.AddWebSocketService<Chat> (
  "/Chat",
  s => {
    s.OriginValidator =
      val => {
        // Check the value of the Origin header, and return true if valid.

        Uri origin;

        return !val.IsNullOrEmpty ()
               && Uri.TryCreate (val, UriKind.Absolute, out origin)
               && origin.Host == "example.com";
      };
  }
);
```

#### Cookies ####

As a WebSocket client, if you would like to send the cookies in the handshake request, you should set any cookie by using the `WebSocket.SetCookie (WebSocketSharp.Net.Cookie)` method before calling the connect method.

```csharp
ws.SetCookie (new Cookie ("name", "nobita"));
```

As a WebSocket server, if you would like to respond to the cookies, you should set a response action for it with your `WebSocketBehavior`, for example, by using the `WebSocketServer.AddWebSocketService<TBehavior> (string, Action<TBehavior>)` method with initializing, such as the following.

```csharp
wssv.AddWebSocketService<Chat> (
  "/Chat",
  s => {
    s.CookiesResponder =
      (reqCookies, resCookies) => {
        foreach (var cookie in reqCookies) {
          cookie.Expired = true;

          resCookies.Add (cookie);
        }
      };
  }
);
```

#### User headers ####

As a WebSocket client, if you would like to send the user headers in the handshake request, you should set any user defined header by using the `WebSocket.SetUserHeader (string, string)` method before calling the connect method.

```csharp
ws.SetUserHeader ("RequestForID", "ID");
```

And if you would like to get the user headers included in the handshake response, you should access the `WebSocket.HandshakeResponseHeaders` property after the handshake is done.

```csharp
var id = ws.HandshakeResponseHeaders["ID"];
```

As a WebSocket server, if you would like to respond to the user headers, you should set a response action for it with your `WebSocketBehavior`, for example, by using the `WebSocketServer.AddWebSocketService<TBehavior> (string, Action<TBehavior>)` method with initializing, such as the following.

```csharp
wssv.AddWebSocketService<Chat> (
  "/Chat",
  s => {
    s.UserHeadersResponder =
      (reqHeaders, userHeaders) => {
        var val = reqHeaders["RequestForID"];

        if (!val.IsNullOrEmpty ())
          userHeaders[val] = s.ID;
      };
  }
);
```

### Connecting through the HTTP proxy server ###

websocket-sharp supports to connect through the HTTP proxy server.

If you would like to connect to a WebSocket server through the HTTP proxy server, you should set the proxy server URL, and if necessary, a pair of user name and password for the proxy server authentication (Basic/Digest), by using the `WebSocket.SetProxy (string, string, string)` method before calling the connect method.

```csharp
var ws = new WebSocket ("ws://example.com");
ws.SetProxy ("http://localhost:3128", "nobita", "password");
```

If your proxy restricts `CONNECT` destinations, make sure the WebSocket target
host and port are allowed by the proxy configuration.

The proxy URL must use `http://host[:port]` and cannot contain a path. Passing
`null` or an empty URL disables a previously configured proxy. Configure the
proxy only while the client is `New` or `Closed`. Proxy `407` retries support
Basic and Digest authentication, including challenge responses that close the
connection or use chunked transfer encoding. `ConnectionTimeout` applies
separately to the proxy TCP connection and each proxy response.

### Logging ###

The `WebSocket` class has its own logging function.

You can use it with the `WebSocket.Log` property (returns a `WebSocketSharp.Logger`).

So if you would like to change the current logging level (`WebSocketSharp.LogLevel.Error` as the default), you should set the `WebSocket.Log.Level` property to any of the `LogLevel` enum values.

```csharp
ws.Log.Level = LogLevel.Debug;
```

Messages below `LogLevel.Debug` are not emitted in this configuration.

And if you would like to output a log, you should use any of the output methods. The following outputs a log with `LogLevel.Debug`.

```csharp
ws.Log.Debug ("This is a debug message.");
```

The `WebSocketServer` and `HttpServer` classes have the same logging function.

## Examples ##

Examples using websocket-sharp are split into the original layout examples and
newer documented examples under `Examples`.

### Example ###

`Example` is an interactive console client. By default it connects to
`ws://localhost:4649/Chat`; pass a URL argument to connect to another endpoint.

### Example2 ###

`Example2` starts a loopback WebSocket server with `/Echo` and `/Chat` services.
It demonstrates explicit handshake, frame, message, receive, and send queue
limits.

### Example3 ###

`Example3` starts a loopback HTTP server that serves `Public/index.html` and
accepts WebSocket handshake requests for `/Echo` and `/Chat`.

Open `http://localhost:4649` to do **WebSocket Echo Test** with your web browser while Example3 is running.

### Examples/ClientLifecycle ###

`Examples/ClientLifecycle` demonstrates `ConnectAsync`, `SendAsync`,
`CloseAsync`, lifecycle events, send completion tracking, and queueing callbacks
back to an application/main thread.

### Examples/ServerWithLimits ###

`Examples/ServerWithLimits` is a compact loopback echo server focused on
handshake concurrency, bounded queues, payload limits, and graceful shutdown.

### Examples/SecureAndProxyClient ###

`Examples/SecureAndProxyClient` demonstrates secure client options: WSS
certificate validation, explicit certificate thumbprint pinning, proxy,
compression, origin, user headers, connection timeout, and bounded redirect
policy.

### Examples/UnityClientLifecycle ###

`Examples/UnityClientLifecycle` is a source-only `MonoBehaviour` example. It is
not built by `dotnet` because it references `UnityEngine`; copy it into a Unity
project that already references `websocket-sharp.dll`. It shows main-thread
dispatch from websocket callbacks, `OnDisable` / `OnDestroy` cleanup, and WebGL
exclusion.

See [Examples/README.md](Examples/README.md) for build and run commands.

## Supported WebSocket Specifications ##

websocket-sharp supports **RFC 6455**.

- WebSocket protocol client and server behavior follows RFC 6455.
- Per-message compression support is available without context takeover.

## License ##

websocket-sharp is provided under the MIT License. See `LICENSE.txt`.
