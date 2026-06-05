# UnityClientLifecycle

Source-only Unity example for using `websocket-sharp` from a `MonoBehaviour`.

This folder is not built by `dotnet build` because it references `UnityEngine`.
Copy `UnityWebSocketClient.cs` into a Unity project that already references
`websocket-sharp.dll`.

The example demonstrates:

- `ConnectAsync` from `OnEnable`.
- `CloseAsync` ownership from `OnDisable` / `OnDestroy`.
- Main-thread dispatch from websocket-sharp callbacks into `Update()`.
- Explicit frame/message/send queue limits before connecting.
- `UNITY_WEBGL` exclusion. WebGL should use the browser JavaScript WebSocket
  layer instead of this managed socket implementation.

Recommended plugin import settings for `websocket-sharp.dll`:

- `Auto Reference`: enabled
- `Validate References`: enabled
- Include tested managed-socket platforms such as Editor, Standalone, and
  mobile targets
- Exclude WebGL
