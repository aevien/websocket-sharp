# ClientLifecycle

SDK-style `net472` console example for the asynchronous client lifecycle.

It demonstrates:

- `ConnectAsync` with an explicit open/error/close wait.
- `SendAsync` completion tracking.
- `CloseAsync` and close callback handling.
- Routing websocket-sharp callbacks through a queue before touching
  application state. This is the same pattern Unity projects should use from
  `MonoBehaviour.Update()`.

Start `Example2`, `Example3`, or `Examples\ServerWithLimits` first, then run:

```powershell
dotnet run --project .\Examples\ClientLifecycle\ClientLifecycle.csproj -- ws://localhost:4649/Echo
```

Expected output includes an open event, send completion, echoed text, and close
event. No Unity assemblies are referenced by this console project.
