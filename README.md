![Logo](websocket-sharp_logo.png)

## Welcome to websocket-sharp! ##

This fork is maintained for Unity/.NET 4.x usage while keeping the original
`websocket-sharp` assembly identity stable for existing Unity projects.

Current preview:

- Tag: `v1.0.5-unity-preview.1`
- Release: [websocket-sharp v1.0.5 Unity Preview 1](https://github.com/aevien/websocket-sharp/releases/tag/v1.0.5-unity-preview.1)
- Target framework: `net472`
- Assembly name: `websocket-sharp`
- Assembly version: `1.0.2.32832` (kept for Unity binary compatibility)
- File/product version: `1.0.5.0`
- WebGL: not supported by this managed socket implementation. Unity WebGL should
  continue to use the browser JavaScript WebSocket layer.

Recent fork changes include safer TLS certificate validation defaults, bounded
client/server handshake timeouts, replacement of delegate `BeginInvoke` usage,
async lifecycle fixes, connect-storm protection, lifecycle stress coverage,
stricter RFC 6455 frame validation, bounded receive/send resource limits,
and partial-frame receive timeouts.

websocket-sharp supports:

- [RFC 6455](#supported-websocket-specifications)
- [WebSocket Client](#websocket-client) and [Server](#websocket-server)
- [Per-message Compression](#per-message-compression) extension
- [Secure Connection](#secure-connection)
- [HTTP Authentication](#http-authentication)
- [Query string, Origin header, Cookies, and User headers](#query-string-origin-header-cookies-and-user-headers)
- [Connecting through the HTTP proxy server](#connecting-through-the-http-proxy-server)
- .NET Framework **4.7.2** / Unity **.NET 4.x** compatible environments

## Branches ##

- `main` contains release-ready code.
- `dev` is used for ongoing development.

## Verification summary ##

This preview was verified as a self-built Unity/.NET 4.x DLL.

- Repository normal suite: `49/49` NUnit tests passed on `net472`.
- Repository stress suite: `6/6` stress tests passed on `net472`.
- Async compatibility: no `BeginInvoke` / `EndInvoke` usage remains in `websocket-sharp` or tests.
- Assembly identity: assembly name, strong-name token, and `AssemblyVersion("1.0.2.32832")` remain stable for existing Unity references.
- Version metadata: file version and product version both report `1.0.5.0`.
- Unity smoke: the updated DLL was imported into a Unity project with Editor/Standalone plugin settings and passed the project smoke test.
- TLS/WSS: default certificate validation rejects certificate policy errors, custom validation remains user-controlled, and secure loopback echo works with an explicitly trusted self-signed certificate.
- Async lifecycle: repeated `ConnectAsync` / `SendAsync` / `CloseAsync` cycles complete successfully, including a 500-cycle stress run.
- Connection timeout: silent TCP peers do not keep `Connect()` waiting for the old hardcoded timeout.
- Server handshake timeout: silent or slow TCP handshakes are disconnected without blocking valid WebSocket handshakes.
- Load coverage: 50 concurrent clients completed 100 echo messages each, for 5000 async text echo sends and callbacks.
- Connect storm coverage: 50 simultaneous `ConnectAsync` clients open and close without ThreadPool starvation.
- Resource lifecycle: repeated connect-storm and slow-handshake rounds return sessions to zero and do not show steady-state thread drift beyond the accepted bounds.
- Close lifecycle: repeated `CloseAsync` / `Dispose` calls and abrupt raw TCP disconnects return server sessions to zero.
- Protocol frames: payload boundaries `125`, `126`, and `66000` bytes round-trip; fragmented text can receive interleaved ping; malformed frames close protocol-error sessions.
- Close-frame validation: one-byte payloads, invalid/reserved close codes, invalid UTF-8 reasons, oversized control payloads, and non-minimal extended length encoding are covered.
- Payload limits: oversized single frames, fragmented messages over the assembled-message limit, and compressed messages that inflate past the configured limit close with `1009 TooBig` without delivering `OnMessage`.
- Receive timeout: idle open connections are not closed by the timeout, but partial frame header/payload stalls close with protocol error without delivering `OnMessage`.

## Build ##

websocket-sharp is built as a single assembly, **websocket-sharp.dll**.

This fork uses an SDK-style project targeting `net472`.

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

GitHub Actions:

- `CI` runs on `main`, `dev`, and pull requests to those branches.
- `Stress Tests` is manual and can be started from the Actions tab.

## Install ##

### GitHub Release ###

Download the Unity preview from the GitHub release page:

- [websocket-sharp.dll](https://github.com/aevien/websocket-sharp/releases/download/v1.0.5-unity-preview.1/websocket-sharp.dll)
- [websocket-sharp-v1.0.5-unity-preview.1-unity-net472.zip](https://github.com/aevien/websocket-sharp/releases/download/v1.0.5-unity-preview.1/websocket-sharp-v1.0.5-unity-preview.1-unity-net472.zip)

### Self Build ###

You should add your websocket-sharp.dll (e.g. `/path/to/websocket-sharp/bin/Release/net472/websocket-sharp.dll`) to the library references of your project.

If you would like to use that dll in your Unity project, you should add it to any folder of your project (e.g. `Assets/Plugins`) in the **Unity Editor**.

Recommended Unity import settings for this managed DLL:

- `Auto Reference`: enabled
- `Validate References`: enabled
- `Any Platform`: disabled when you need explicit platform control
- Include `Editor`, `Standalone`, and any mobile/IL2CPP target you actually test
- Exclude `WebGL`; use the browser JavaScript WebSocket layer there
- Assembly target should show `.NET 4.x`

For IL2CPP builds, keep this DLL as a managed plugin. The fork does not use
runtime code generation or delegate `BeginInvoke`/`EndInvoke`, so it is suitable
for Unity .NET 4.x profiles where managed sockets are available.

## Runtime Limits ##

The fork keeps the original API shape but adds bounded defaults for common
resource risks in old WebSocket stacks:

- `WebSocket.MaxFramePayloadLength`: default `16 MiB`
- `WebSocket.MaxMessagePayloadLength`: default `64 MiB`
- `WebSocket.MaxMessageEventQueueLength`: default `1024`
- `WebSocket.MaxAsyncSendQueueLength`: default `256`
- `WebSocket.FrameReadTimeout`: default `10 seconds`

Set these values before `Connect`, `ConnectAsync`, or server `Accept`. For
server services, set the matching properties on `WebSocketBehavior` in
`AddWebSocketService`, for example:

```csharp
wssv.AddWebSocketService<Echo> (
  "/Echo",
  s => {
    s.MaxFramePayloadLength = 1024 * 1024;
    s.MaxMessagePayloadLength = 4 * 1024 * 1024;
    s.FrameReadTimeout = TimeSpan.FromSeconds (5);
  }
);
```

`FrameReadTimeout` does not close an idle open connection with no incoming
bytes. It applies after a peer starts a WebSocket frame and then stalls while
the rest of that frame is being read.

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
      using (var ws = new WebSocket ("ws://dragonsnest.far/Laputa")) {
        ws.OnMessage += (sender, e) =>
                          Console.WriteLine ("Laputa says: " + e.Data);

        ws.Connect ();
        ws.Send ("BALUS");
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

And also if you would like to do something when the send is complete, you should set `completed` to any `Action<bool>` delegate.

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
  public class Laputa : WebSocketBehavior
  {
    protected override void OnMessage (MessageEventArgs e)
    {
      var msg = e.Data == "BALUS"
                ? "Are you kidding?"
                : "I'm not available now.";

      Send (msg);
    }
  }

  public class Program
  {
    public static void Main (string[] args)
    {
      var wssv = new WebSocketServer ("ws://dragonsnest.far");

      wssv.AddWebSocketService<Laputa> ("/Laputa");
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

If you create a new instance of the `WebSocketServer` class without a port number, it sets the port number to **80**. So it is necessary to run with root permission.

    $ sudo mono example2.exe

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

I have modified the `System.Net.HttpListener`, `System.Net.HttpListenerContext`, and some other classes from Mono to create an HTTP server that allows to accept the WebSocket handshake requests.

So websocket-sharp provides the `WebSocketSharp.Server.HttpServer` class.

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

I think this is useful when you get something error in connecting the server and exclude the extensions as a cause of the error.

### Secure Connection ###

websocket-sharp supports the secure connection with **SSL/TLS**.

As a WebSocket client, you should create a new instance of the `WebSocket` class with a **wss** scheme WebSocket URL.

```csharp
var ws = new WebSocket ("wss://example.com");
```

If you would like to set a custom validation for the server certificate, you should set the `WebSocket.SslConfiguration.ServerCertificateValidationCallback` property to a callback for it.

```csharp
ws.SslConfiguration.ServerCertificateValidationCallback =
  (sender, certificate, chain, sslPolicyErrors) => {
    // Do something to validate the server certificate.
    ...

    return true; // If the server certificate is valid.
  };
```

The default callback accepts only certificates that pass platform validation
without `SslPolicyErrors`. For self-signed or private certificates, provide a
custom `ServerCertificateValidationCallback` and validate the expected
certificate explicitly.

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

If your proxy restricts CONNECT destinations, make sure the WebSocket target port is allowed by the proxy configuration.

```
# Example proxy policy may need to allow CONNECT to the target WebSocket port.
```

### Logging ###

The `WebSocket` class has the own logging function.

You can use it with the `WebSocket.Log` property (returns a `WebSocketSharp.Logger`).

So if you would like to change the current logging level (`WebSocketSharp.LogLevel.Error` as the default), you should set the `WebSocket.Log.Level` property to any of the `LogLevel` enum values.

```csharp
ws.Log.Level = LogLevel.Debug;
```

The above means a log with lower than `LogLevel.Debug` cannot be outputted.

And if you would like to output a log, you should use any of the output methods. The following outputs a log with `LogLevel.Debug`.

```csharp
ws.Log.Debug ("This is a debug message.");
```

The `WebSocketServer` and `HttpServer` classes have the same logging function.

## Examples ##

Examples using websocket-sharp.

### Example ###

`Example` connects to the server executed by `Example2` or `Example3`.

### Example2 ###

`Example2` starts a WebSocket server.

### Example3 ###

`Example3` starts an HTTP server that allows to accept the WebSocket handshake requests.

Open `http://localhost:4649` to do **WebSocket Echo Test** with your web browser while Example3 is running.

## Supported WebSocket Specifications ##

websocket-sharp supports **RFC 6455**.

- WebSocket protocol client and server behavior follows RFC 6455.
- Per-message compression support is available without context takeover.

## License ##

websocket-sharp is provided under the MIT License. See `LICENSE.txt`.
