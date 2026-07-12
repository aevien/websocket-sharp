# websocket-sharp v1.3.0

This release continues the Unity/.NET 4.x modernization of the fork while
preserving the existing `websocket-sharp` assembly identity.

## Highlights

- Bounded server handshake execution with configurable active and pending
  limits for both `WebSocketServer` and `HttpServer`.
- Strict WebSocket handshake body policy: upgrade and successful handshake
  bodies are rejected, while HTTP error bodies are capped at 64 KiB.
- Bounded, opt-in redirect handling for status codes 301, 302, 303, 307, and
  308, including relative locations and explicit WSS-to-WS downgrade control.
- Cross-origin redirects no longer forward HTTP credentials, cookies, custom
  headers, or TLS client certificates, including reconnect paths.
- Digest authentication uses the redirected request path and current proxy
  CONNECT authority.
- Built-in handshake Debug logs redact request paths, queries, credentials,
  cookies, custom values, response bodies, and reflected untrusted values.
- Expanded `net472` unit, stress, public API, and Unity/IL2CPP static coverage.

## Compatibility

- Target framework: `net472`.
- Unity: managed plugin for .NET 4.x-compatible Editor, Standalone, and tested
  mobile/IL2CPP targets.
- WebGL: not supported by this managed TCP socket implementation.
- Assembly name and strong name remain unchanged.
- `AssemblyVersion` remains `1.0.2.32832` for existing Unity binary references.
- File and product version: `1.3.0.0`.

## Assets

- `websocket-sharp.dll`: the signed `net472` library.
- `websocket-sharp-v1.3.0-unity-net472.zip`: DLL, license, README, and these
  release notes.
- `SHA256SUMS.txt`: SHA-256 checksums for the DLL and ZIP.
