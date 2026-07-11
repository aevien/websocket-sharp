# websocket-sharp examples

This folder contains modern, documented examples for the maintained `net472`
fork. The old `Example`, `Example2`, and `Example3` folders are still kept for
compatibility with the original project layout, but they now build with the
current SDK-style project format.

## Console examples

### ClientLifecycle

Asynchronous client lifecycle with `ConnectAsync`, `SendAsync`, `CloseAsync`,
callback events, send completion tracking, and a main-thread dispatch queue.

```powershell
dotnet run --project .\Examples\ClientLifecycle\ClientLifecycle.csproj -- ws://localhost:4649/Echo
```

### ServerWithLimits

Loopback echo server with explicit handshake concurrency, pending handshake,
frame, message, event queue, and async send queue limits.

```powershell
dotnet run --project .\Examples\ServerWithLimits\ServerWithLimits.csproj
```

### SecureAndProxyClient

Client-side WSS certificate validation, explicit certificate pinning,
compression, proxy, origin, user header, and connection timeout settings.

```powershell
dotnet run --project .\Examples\SecureAndProxyClient\SecureAndProxyClient.csproj -- --help
```

## Unity source example

### UnityClientLifecycle

Source-only `MonoBehaviour` example for Unity projects. It is not included in
`dotnet build` because it references `UnityEngine`. Copy the source into a Unity
project that already references `websocket-sharp.dll`.

The example shows how to:

- open with `ConnectAsync`;
- send with `SendAsync`;
- close from `OnDisable` / `OnDestroy`;
- move websocket-sharp callbacks onto Unity's main thread via `Update()`;
- keep WebGL excluded because Unity WebGL uses the browser JavaScript WebSocket
  layer.

## Legacy layout examples

- `Example`: interactive console client. By default it connects to
  `ws://localhost:4649/Chat`; pass a URL argument to target another endpoint.
- `Example2`: loopback WebSocket server with `/Echo` and `/Chat`.
- `Example3`: loopback HTTP server that serves `Public\index.html` and hosts
  `/Echo` and `/Chat` WebSocket services.

Build all console examples from the repository root:

```powershell
dotnet build .\Example\Example.csproj -c Release
dotnet build .\Example2\Example2.csproj -c Release
dotnet build .\Example3\Example3.csproj -c Release
dotnet build .\Examples\ClientLifecycle\ClientLifecycle.csproj -c Release
dotnet build .\Examples\ServerWithLimits\ServerWithLimits.csproj -c Release
dotnet build .\Examples\SecureAndProxyClient\SecureAndProxyClient.csproj -c Release
```
