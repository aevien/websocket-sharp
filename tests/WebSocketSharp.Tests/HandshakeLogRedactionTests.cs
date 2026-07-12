using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using NUnit.Framework;
using WebSocketSharp.Net;
using WebSocketSharp.Server;

namespace WebSocketSharp.Tests
{
  [TestFixture]
  [NonParallelizable]
  public sealed class HandshakeLogRedactionTests
  {
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds (5);

    [Test]
    public void ClientDebugLogRedactsInitialHandshakeSecrets ()
    {
      const string querySecret = "query-secret-value";
      const string credentialSecret = "credential-secret-value";
      const string cookieSecret = "cookie-secret-value";
      const string headerSecret = "header-secret-value";
      var logs = new LogCapture ();

      using (
        var server = LoopbackServer.Start (
          value => value.AddWebSocketService<EchoBehavior> ("/echo")
        )
      )
      using (
        var client = new WebSocket (
          server.GetUrl ("/echo") + "?access_token=" + querySecret
        )
      ) {
        client.Log.Level = LogLevel.Debug;
        client.Log.Output = logs.Add;
        client.SetCredentials ("log-user", credentialSecret, true);
        client.SetCookie (new WebSocketSharp.Net.Cookie ("session", cookieSecret));
        client.SetUserHeader ("X-MST-Secret", headerSecret);

        client.Connect ();

        Assert.That (client.ReadyState, Is.EqualTo (WebSocketState.Open));
        client.Close ();
      }

      var output = logs.Join ();
      var basicToken = Convert.ToBase64String (
                         Encoding.UTF8.GetBytes (
                           "log-user:" + credentialSecret
                         )
                       );

      Assert.That (
        output,
        Does.Contain ("GET <path; segments=1; query=true> HTTP/1.1")
      );
      Assert.That (output, Does.Contain ("Authorization: <redacted>"));
      Assert.That (output, Does.Contain ("Cookie: <redacted>"));
      Assert.That (output, Does.Contain ("X-MST-Secret: <redacted>"));
      Assert.That (output, Does.Contain ("Upgrade: websocket=true; tokens=1"));
      Assert.That (output, Does.Contain ("Sec-WebSocket-Version: 13"));
      Assert.That (output, Does.Not.Contain (querySecret));
      Assert.That (output, Does.Not.Contain (credentialSecret));
      Assert.That (output, Does.Not.Contain (cookieSecret));
      Assert.That (output, Does.Not.Contain (headerSecret));
      Assert.That (output, Does.Not.Contain (basicToken));
    }

    [Test]
    public void ClientDebugLogRedactsAuthenticationRetry ()
    {
      const string password = "retry-password-secret";
      var logs = new LogCapture ();

      using (var server = RawHandshakeServer.StartAuthenticationRetry ())
      using (var client = new WebSocket (server.GetUrl ("/echo"))) {
        client.Log.Level = LogLevel.Debug;
        client.Log.Output = logs.Add;
        client.SetCredentials ("retry-user", password, false);

        client.Connect ();

        Assert.That (server.Completed.Wait (Timeout), Is.True);
        Assert.That (server.SecondRequest, Does.Contain ("Authorization: Basic "));
      }

      var output = logs.Join ();
      var basicToken = Convert.ToBase64String (
                         Encoding.UTF8.GetBytes ("retry-user:" + password)
                       );

      Assert.That (output, Does.Contain ("Authorization: <redacted>"));
      Assert.That (output, Does.Not.Contain (password));
      Assert.That (output, Does.Not.Contain (basicToken));
    }

    [Test]
    public void ClientDebugLogRedactsResponseHeadersAndBody ()
    {
      const string cookieSecret = "response-cookie-secret";
      const string headerSecret = "response-header-secret";
      const string bodySecret = "response-body-secret";
      var logs = new LogCapture ();

      using (
        var server = RawHandshakeServer.StartErrorResponse (
          cookieSecret,
          headerSecret,
          bodySecret
        )
      )
      using (var client = new WebSocket (server.GetUrl ("/echo"))) {
        client.Log.Level = LogLevel.Debug;
        client.Log.Output = logs.Add;
        client.Connect ();

        Assert.That (client.ReadyState, Is.Not.EqualTo (WebSocketState.Open));
        Assert.That (server.Completed.Wait (Timeout), Is.True);
      }

      var output = logs.Join ();

      Assert.That (output, Does.Contain ("HTTP/1.1 400 Bad Request"));
      Assert.That (output, Does.Contain ("Set-Cookie: <redacted>"));
      Assert.That (output, Does.Contain ("X-Refresh-Token: <redacted>"));
      Assert.That (output, Does.Contain ("Body: <omitted; 20 bytes>"));
      Assert.That (output, Does.Not.Contain (cookieSecret));
      Assert.That (output, Does.Not.Contain (headerSecret));
      Assert.That (output, Does.Not.Contain (bodySecret));
    }

    [Test]
    public void ClientDebugLogDoesNotTrustStructuralResponseValues ()
    {
      const string password = "reflected-password-secret";
      var logs = new LogCapture ();

      using (var server = RawHandshakeServer.StartReflectedErrorResponse ())
      using (var client = new WebSocket (server.GetUrl ("/echo"))) {
        client.Log.Level = LogLevel.Debug;
        client.Log.Output = logs.Add;
        client.SetCredentials ("reflected-user", password, true);
        client.Connect ();

        Assert.That (server.Completed.Wait (Timeout), Is.True);
        Assert.That (client.ReadyState, Is.Not.EqualTo (WebSocketState.Open));
      }

      var output = logs.Join ();
      var basicToken = Convert.ToBase64String (
                         Encoding.UTF8.GetBytes (
                           "reflected-user:" + password
                         )
                       );

      Assert.That (output, Does.Contain ("HTTP/1.1 400 Bad Request"));
      Assert.That (output, Does.Contain ("Upgrade: websocket=true; tokens=2"));
      Assert.That (output, Does.Contain ("Connection: upgrade=false; close=true; tokens=2"));
      Assert.That (output, Does.Not.Contain (password));
      Assert.That (output, Does.Not.Contain (basicToken));
    }

    [TestCase (false)]
    [TestCase (true)]
    public void RejectedServerHandshakeLogIsRedacted (bool useHttpServer)
    {
      const string querySecret = "server-query-secret";
      const string authorizationSecret = "server-authorization-secret";
      const string cookieSecret = "server-cookie-secret";
      const string headerSecret = "server-header-secret";
      var logs = new LogCapture ();
      var port = LoopbackServer.GetFreeTcpPort ();
      WebSocketServer webSocketServer = null;
      HttpServer httpServer = null;

      try {
        if (useHttpServer) {
          httpServer = new HttpServer (IPAddress.Loopback, port);
          httpServer.Log.Level = LogLevel.Debug;
          httpServer.Log.Output = logs.Add;
          httpServer.AddWebSocketService<EchoBehavior> ("/echo");
          httpServer.Start ();
        }
        else {
          webSocketServer = new WebSocketServer (IPAddress.Loopback, port);
          webSocketServer.Log.Level = LogLevel.Debug;
          webSocketServer.Log.Output = logs.Add;
          webSocketServer.AddWebSocketService<EchoBehavior> ("/echo");
          webSocketServer.Start ();
        }

        using (var client = new TcpClient ()) {
          client.Connect (IPAddress.Loopback, port);

          var request = "GET /echo?access_token=" + querySecret + " HTTP/1.1\r\n"
                        + "Host: 127.0.0.1:" + port + "\r\n"
                        + "Upgrade: websocket\r\n"
                        + "Connection: Upgrade\r\n"
                        + "Sec-WebSocket-Key: invalid-key\r\n"
                        + "Sec-WebSocket-Version: 12\r\n"
                        + "Authorization: Basic " + authorizationSecret + "\r\n"
                        + "Cookie: session=" + cookieSecret + "\r\n"
                        + "X-MST-Secret: " + headerSecret + "\r\n\r\n";
          var bytes = Encoding.ASCII.GetBytes (request);
          var stream = client.GetStream ();

          stream.Write (bytes, 0, bytes.Length);
          stream.Flush ();

          var captured = SpinWait.SpinUntil (
                           () => logs.Join ().Contains ("X-MST-Secret: <redacted>"),
                           Timeout
                         );

          Assert.That (captured, Is.True, logs.Join ());
        }
      }
      finally {
        if (httpServer != null)
          httpServer.Stop ();

        if (webSocketServer != null)
          webSocketServer.Stop ();
      }

      var output = logs.Join ();

      Assert.That (
        output,
        Does.Contain ("GET <path; segments=1; query=true> HTTP/1.1")
      );
      Assert.That (output, Does.Contain ("Authorization: <redacted>"));
      Assert.That (output, Does.Contain ("Cookie: <redacted>"));
      Assert.That (output, Does.Contain ("X-MST-Secret: <redacted>"));
      Assert.That (output, Does.Not.Contain (querySecret));
      Assert.That (output, Does.Not.Contain (authorizationSecret));
      Assert.That (output, Does.Not.Contain (cookieSecret));
      Assert.That (output, Does.Not.Contain (headerSecret));
    }

    public sealed class EchoBehavior : WebSocketBehavior
    {
    }

    private sealed class LogCapture
    {
      private readonly List<string> _messages = new List<string> ();

      internal void Add (LogData data, string path)
      {
        lock (_messages)
          _messages.Add (data.Message);
      }

      internal string Join ()
      {
        lock (_messages)
          return String.Join ("\n", _messages.ToArray ());
      }
    }

    private sealed class RawHandshakeServer : IDisposable
    {
      private enum Mode
      {
        AuthenticationRetry,
        ErrorResponse,
        ReflectedErrorResponse
      }

      private readonly string _bodySecret;
      private readonly string _cookieSecret;
      private readonly string _headerSecret;
      private readonly TcpListener _listener;
      private readonly Mode _mode;
      private readonly Thread _thread;

      private RawHandshakeServer (
        Mode mode,
        string cookieSecret,
        string headerSecret,
        string bodySecret
      )
      {
        _mode = mode;
        _cookieSecret = cookieSecret;
        _headerSecret = headerSecret;
        _bodySecret = bodySecret;
        _listener = new TcpListener (IPAddress.Loopback, 0);
        _listener.Start ();
        Port = ((IPEndPoint) _listener.LocalEndpoint).Port;
        Completed = new ManualResetEventSlim ();
        _thread = new Thread (Run);
        _thread.IsBackground = true;
        _thread.Start ();
      }

      internal ManualResetEventSlim Completed { get; private set; }

      private int Port { get; set; }

      internal string SecondRequest { get; private set; }

      internal static RawHandshakeServer StartAuthenticationRetry ()
      {
        return new RawHandshakeServer (
          Mode.AuthenticationRetry,
          null,
          null,
          null
        );
      }

      internal static RawHandshakeServer StartErrorResponse (
        string cookieSecret,
        string headerSecret,
        string bodySecret
      )
      {
        return new RawHandshakeServer (
          Mode.ErrorResponse,
          cookieSecret,
          headerSecret,
          bodySecret
        );
      }

      internal static RawHandshakeServer StartReflectedErrorResponse ()
      {
        return new RawHandshakeServer (
          Mode.ReflectedErrorResponse,
          null,
          null,
          null
        );
      }

      public void Dispose ()
      {
        _listener.Stop ();
        _thread.Join (1000);
        Completed.Dispose ();
      }

      internal string GetUrl (string path)
      {
        return String.Format ("ws://127.0.0.1:{0}{1}", Port, path);
      }

      private static string ComputeAccept (string key)
      {
        var source = key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

        using (var sha1 = SHA1.Create ())
          return Convert.ToBase64String (
            sha1.ComputeHash (Encoding.ASCII.GetBytes (source))
          );
      }

      private static string GetHeader (string request, string name)
      {
        var prefix = name + ":";
        var lines = request.Split (new[] { "\r\n" }, StringSplitOptions.None);

        foreach (var line in lines) {
          if (line.StartsWith (prefix, StringComparison.OrdinalIgnoreCase))
            return line.Substring (prefix.Length).Trim ();
        }

        return null;
      }

      private static string ReadHeader (Stream stream)
      {
        using (var data = new MemoryStream ()) {
          var current = new byte[1];
          var previous = new byte[3];

          while (data.Length <= 8192) {
            if (stream.Read (current, 0, 1) <= 0)
              throw new EndOfStreamException ();

            data.WriteByte (current[0]);

            if (previous[0] == '\r'
                && previous[1] == '\n'
                && previous[2] == '\r'
                && current[0] == '\n')
              return Encoding.ASCII.GetString (data.ToArray ());

            previous[0] = previous[1];
            previous[1] = previous[2];
            previous[2] = current[0];
          }
        }

        throw new InvalidDataException ("The request header is too large.");
      }

      private void Run ()
      {
        try {
          if (_mode == Mode.ErrorResponse) {
            using (var client = _listener.AcceptTcpClient ()) {
              var stream = client.GetStream ();

              ReadHeader (stream);
              WriteErrorResponse (stream);
            }

            return;
          }

          if (_mode == Mode.ReflectedErrorResponse) {
            using (var client = _listener.AcceptTcpClient ()) {
              var stream = client.GetStream ();
              var request = ReadHeader (stream);
              var authorization = GetHeader (request, "Authorization");

              Write (
                stream,
                "HTTP/1.1 400 " + authorization + "\r\n"
                + "Upgrade: websocket, " + authorization + "\r\n"
                + "Connection: close, " + authorization + "\r\n"
                + "Content-Length: 0\r\n\r\n"
              );
            }

            return;
          }

          using (var first = _listener.AcceptTcpClient ()) {
            var stream = first.GetStream ();

            ReadHeader (stream);
            Write (
              stream,
              "HTTP/1.1 401 Unauthorized\r\n"
              + "WWW-Authenticate: Basic realm=\"redaction\"\r\n"
              + "Connection: close\r\n"
              + "Content-Length: 0\r\n\r\n"
            );
          }

          using (var second = _listener.AcceptTcpClient ()) {
            var stream = second.GetStream ();

            SecondRequest = ReadHeader (stream);

            var key = GetHeader (SecondRequest, "Sec-WebSocket-Key");
            Write (
              stream,
              "HTTP/1.1 101 Switching Protocols\r\n"
              + "Upgrade: websocket\r\n"
              + "Connection: Upgrade\r\n"
              + "Sec-WebSocket-Accept: " + ComputeAccept (key) + "\r\n\r\n"
            );

            Thread.Sleep (100);
          }
        }
        catch {
        }
        finally {
          Completed.Set ();
        }
      }

      private static void Write (Stream stream, string response)
      {
        var bytes = Encoding.ASCII.GetBytes (response);

        stream.Write (bytes, 0, bytes.Length);
        stream.Flush ();
      }

      private void WriteErrorResponse (Stream stream)
      {
        var body = Encoding.ASCII.GetBytes (_bodySecret);
        var response = "HTTP/1.1 400 Bad Request\r\n"
                       + "Set-Cookie: session=" + _cookieSecret + "\r\n"
                       + "X-Refresh-Token: " + _headerSecret + "\r\n"
                       + "Content-Length: " + body.Length + "\r\n\r\n";

        Write (stream, response);
        stream.Write (body, 0, body.Length);
        stream.Flush ();
      }
    }
  }
}
