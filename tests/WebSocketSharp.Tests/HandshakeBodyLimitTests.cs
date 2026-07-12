using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using NUnit.Framework;
using WebSocketSharp.Server;

namespace WebSocketSharp.Tests
{
  [TestFixture]
  [NonParallelizable]
  public sealed class HandshakeBodyLimitTests
  {
    private static readonly TimeSpan DisconnectTimeout = TimeSpan.FromSeconds (5);
    private static readonly TimeSpan EarlyDisconnectTimeout = TimeSpan.FromSeconds (1);

    [TestCase (false)]
    [TestCase (true)]
    public void ClientRejectsHandshakeResponseBodyBeforeReadingIt (bool chunked)
    {
      var bodyHeader = GetBodyHeader (chunked);

      AssertClientRejectsHandshakeBody (
        bodyHeader,
        chunked ? "chunked" : "content-length-1GiB"
      );
    }

    [TestCase (1)]
    [TestCase (65536)]
    public void ClientRejectsSmallBodyOn101BeforeReadingIt (int contentLength)
    {
      AssertClientRejectsHandshakeBody (
        String.Format ("Content-Length: {0}\r\n", contentLength),
        String.Format ("101-content-length-{0}", contentLength)
      );
    }

    private static void AssertClientRejectsHandshakeBody (string bodyHeader, string mode)
    {

      using (var server = RawHandshakeResponseServer.Start (bodyHeader))
      using (var client = new WebSocket (server.GetUrl ())) {
        var elapsed = Stopwatch.StartNew ();
        var openCount = 0;

        client.ConnectionTimeout = DisconnectTimeout;
        client.Log.Output = (data, path) => { };
        client.OnOpen += (sender, e) => Interlocked.Increment (ref openCount);
        client.Connect ();

        elapsed.Stop ();

        WaitUntil (
          () => server.Completed,
          DisconnectTimeout,
          "The raw response peer did not observe the client disconnect."
        );

        Assert.That (server.HeadersSent, Is.True);
        Assert.That (server.Error, Is.Null);
        Assert.That (openCount, Is.EqualTo (0));
        Assert.That (client.ReadyState, Is.Not.EqualTo (WebSocketState.Open));
        Assert.That (elapsed.Elapsed, Is.LessThan (TimeSpan.FromSeconds (2)));
        Assert.That (
          server.BodyBytesSent,
          Is.EqualTo (0),
          "The client read handshake body bytes instead of rejecting the headers."
        );

        TestContext.WriteLine (
          "Client body proof: mode={0}, bodyBytesSent={1}, elapsed={2}.",
          mode,
          server.BodyBytesSent,
          elapsed.Elapsed
        );

        AssertClientRecoveryEcho ();
      }
    }

    [Test]
    public void ClientAcceptsValidRawHandshakeWithoutBodyHeaders ()
    {
      using (
        var server = RawHandshakeResponseServer.Start (
          String.Empty,
          101,
          0,
          false,
          true
        )
      )
      using (var client = new WebSocket (server.GetUrl ())) {
        var openCount = 0;

        client.ConnectionTimeout = DisconnectTimeout;
        client.Log.Output = (data, path) => { };
        client.OnOpen += (sender, e) => Interlocked.Increment (ref openCount);
        client.Connect ();

        WaitUntil (
          () => server.Completed,
          DisconnectTimeout,
          "The valid raw handshake control did not complete."
        );

        Assert.That (server.HeadersSent, Is.True);
        Assert.That (server.Error, Is.Null);
        Assert.That (openCount, Is.EqualTo (1));
      }
    }

    [Test]
    public void ClientRejectsErrorBodyAbove64KiBBeforeReadingIt ()
    {
      using (
        var server = RawHandshakeResponseServer.Start (
          "Content-Length: 65537\r\n",
          401
        )
      )
      using (var client = new WebSocket (server.GetUrl ())) {
        var openCount = 0;

        client.ConnectionTimeout = DisconnectTimeout;
        client.Log.Output = (data, path) => { };
        client.OnOpen += (sender, e) => Interlocked.Increment (ref openCount);
        client.Connect ();

        WaitUntil (
          () => server.Completed,
          DisconnectTimeout,
          "The oversized error response did not cause an early disconnect."
        );

        Assert.That (server.HeadersSent, Is.True);
        Assert.That (server.Error, Is.Null);
        Assert.That (openCount, Is.EqualTo (0));
        Assert.That (client.ReadyState, Is.Not.EqualTo (WebSocketState.Open));
        Assert.That (server.BodyBytesSent, Is.EqualTo (0));

        TestContext.WriteLine (
          "Error body proof: declared=65537, limit=65536, bodyBytesSent={0}.",
          server.BodyBytesSent
        );

        AssertClientRecoveryEcho ();
      }
    }

    [Test]
    public void ClientReadsErrorBodyAt64KiBLimit ()
    {
      using (
        var server = RawHandshakeResponseServer.Start (
          "Content-Length: 65536\r\n",
          401,
          65536
        )
      )
      using (var client = new WebSocket (server.GetUrl ())) {
        var openCount = 0;
        var elapsed = Stopwatch.StartNew ();

        client.ConnectionTimeout = DisconnectTimeout;
        client.Log.Output = (data, path) => { };
        client.OnOpen += (sender, e) => Interlocked.Increment (ref openCount);
        client.Connect ();
        elapsed.Stop ();

        WaitUntil (
          () => server.Completed,
          DisconnectTimeout,
          "The error response at the limit did not complete."
        );

        Assert.That (server.HeadersSent, Is.True);
        Assert.That (server.Error, Is.Null);
        Assert.That (openCount, Is.EqualTo (0));
        Assert.That (client.ReadyState, Is.Not.EqualTo (WebSocketState.Open));
        Assert.That (client.HandshakeResponseHeaders, Is.Not.Null);
        Assert.That (
          client.HandshakeResponseHeaders["Content-Length"],
          Is.EqualTo ("65536")
        );
        Assert.That (server.BodyBytesSent, Is.EqualTo (65536));
        Assert.That (elapsed.Elapsed, Is.LessThan (TimeSpan.FromSeconds (3)));

        TestContext.WriteLine (
          "Error body boundary proof: declared=65536, parsed={0}, bodyBytesSent={1}, elapsed={2}.",
          client.HandshakeResponseHeaders["Content-Length"],
          server.BodyBytesSent,
          elapsed.Elapsed
        );

        AssertClientRecoveryEcho ();
      }
    }

    [TestCase (false, false)]
    [TestCase (false, true)]
    [TestCase (true, false)]
    [TestCase (true, true)]
    public void ServerRejectsUpgradeBodyBeforeReadingItAndRecovers (
      bool useHttpServer,
      bool chunked
    )
    {
      var port = LoopbackServer.GetFreeTcpPort ();
      WebSocketServer webSocketServer = null;
      HttpServer httpServer = null;
      WebSocketServiceManager services;
      var tracker = new SessionTracker ();

      try {
        if (useHttpServer) {
          httpServer = new HttpServer (IPAddress.Loopback, port);
          httpServer.HandshakeTimeout = DisconnectTimeout;
          httpServer.Log.Output = (data, path) => { };
          httpServer.AddWebSocketService<TrackedEchoBehavior> (
            "/echo",
            behavior => behavior.Configure (tracker)
          );
          httpServer.Start ();
          services = httpServer.WebSocketServices;
        }
        else {
          webSocketServer = new WebSocketServer (IPAddress.Loopback, port);
          webSocketServer.HandshakeTimeout = DisconnectTimeout;
          webSocketServer.Log.Output = (data, path) => { };
          webSocketServer.AddWebSocketService<TrackedEchoBehavior> (
            "/echo",
            behavior => behavior.Configure (tracker)
          );
          webSocketServer.Start ();
          services = webSocketServer.WebSocketServices;
        }

        var result = ProbeServer (port, GetBodyHeader (chunked));

        Assert.That (result.Disconnected, Is.True);
        Assert.That (result.Elapsed, Is.LessThan (TimeSpan.FromSeconds (2)));
        Assert.That (
          result.BodyBytesSent,
          Is.EqualTo (0),
          "The server read handshake body bytes instead of rejecting the headers."
        );
        Assert.That (services["/echo"].Sessions.Count, Is.EqualTo (0));
        Assert.That (
          tracker.OpenCount,
          Is.EqualTo (0),
          "The invalid upgrade briefly opened a WebSocket session."
        );

        RoundTrip (port, "recovery-echo");

        WaitUntil (
          () => services["/echo"].Sessions.Count == 0,
          DisconnectTimeout,
          "The recovery echo session was not released."
        );
        Assert.That (tracker.OpenCount, Is.EqualTo (1));

        TestContext.WriteLine (
          "Server body proof: server={0}, mode={1}, bodyBytesSent={2}, elapsed={3}.",
          useHttpServer ? "HttpServer" : "WebSocketServer",
          chunked ? "chunked" : "content-length-1GiB",
          result.BodyBytesSent,
          result.Elapsed
        );
      }
      finally {
        if (httpServer != null)
          httpServer.Stop ();

        if (webSocketServer != null)
          webSocketServer.Stop ();
      }
    }

    private static string ComputeWebSocketAccept (string key)
    {
      var source = key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

      using (var sha1 = SHA1.Create ()) {
        var hash = sha1.ComputeHash (Encoding.ASCII.GetBytes (source));

        return Convert.ToBase64String (hash);
      }
    }

    private static string GetBodyHeader (bool chunked)
    {
      return chunked
             ? "Transfer-Encoding: chunked\r\n"
             : "Content-Length: 1073741824\r\n";
    }

    private static string GetHeaderValue (string header, string name)
    {
      var prefix = name + ":";
      var lines = header.Split (new[] { "\r\n" }, StringSplitOptions.None);

      foreach (var line in lines) {
        if (line.StartsWith (prefix, StringComparison.OrdinalIgnoreCase))
          return line.Substring (prefix.Length).Trim ();
      }

      return null;
    }

    private static bool IsDisconnected (TcpClient client)
    {
      try {
        var socket = client.Client;

        return socket.Poll (0, SelectMode.SelectRead) && socket.Available == 0;
      }
      catch {
        return true;
      }
    }

    private static void AssertClientRecoveryEcho ()
    {
      using (
        var server = LoopbackServer.Start (
          s => {
            s.Log.Output = (data, path) => { };
            s.AddWebSocketService<TrackedEchoBehavior> (
              "/echo",
              behavior => behavior.Configure (new SessionTracker ())
            );
          }
        )
      ) {
        RoundTrip (server.Port, "client-recovery-echo");
      }
    }

    private static BodyProbeResult ProbeServer (int port, string bodyHeader)
    {
      using (var client = new TcpClient ()) {
        client.SendTimeout = 1000;
        client.Connect (IPAddress.Loopback, port);

        var request = String.Format (
          "GET /echo HTTP/1.1\r\nHost: 127.0.0.1:{0}\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\nSec-WebSocket-Version: 13\r\n{1}\r\n",
          port,
          bodyHeader
        );
        var stream = client.GetStream ();
        var requestBytes = Encoding.ASCII.GetBytes (request);
        var elapsed = Stopwatch.StartNew ();

        stream.Write (requestBytes, 0, requestBytes.Length);
        stream.Flush ();

        var disconnected = WaitForDisconnect (client, EarlyDisconnectTimeout);
        var bodyBytesSent = 0;

        if (!disconnected)
          bodyBytesSent = StreamProbeBody (client, stream, 8 * 1024 * 1024);

        disconnected = disconnected || WaitForDisconnect (client, DisconnectTimeout);
        elapsed.Stop ();

        return new BodyProbeResult (bodyBytesSent, disconnected, elapsed.Elapsed);
      }
    }

    private static string ReadHttpHeader (NetworkStream stream)
    {
      using (var data = new MemoryStream ()) {
        var current = new byte[1];
        var previous = new byte[3];

        while (data.Length <= 8192) {
          var read = stream.Read (current, 0, 1);

          if (read <= 0)
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

      throw new InvalidDataException ("The HTTP request header is too large.");
    }

    private static void RoundTrip (int port, string payload)
    {
      using (var received = new ManualResetEventSlim ())
      using (var client = new WebSocket (String.Format ("ws://127.0.0.1:{0}/echo", port))) {
        string actual = null;
        Exception error = null;

        client.ConnectionTimeout = DisconnectTimeout;
        client.Log.Output = (data, path) => { };
        client.OnMessage += (sender, e) => {
          actual = e.Data;
          received.Set ();
        };
        client.OnError += (sender, e) => {
          error = e.Exception ?? new Exception (e.Message);
          received.Set ();
        };

        client.Connect ();
        Assert.That (client.ReadyState, Is.EqualTo (WebSocketState.Open));

        client.Send (payload);

        Assert.That (received.Wait (DisconnectTimeout), Is.True);
        Assert.That (error, Is.Null);
        Assert.That (actual, Is.EqualTo (payload));

        client.Close ();
      }
    }

    private static int StreamProbeBody (
      TcpClient client,
      NetworkStream stream,
      int maxProbeLength
    )
    {
      var buffer = new byte[4096];
      var sent = 0;

      while (sent < maxProbeLength && !IsDisconnected (client)) {
        try {
          var count = Math.Min (buffer.Length, maxProbeLength - sent);

          stream.Write (buffer, 0, count);
          sent += count;
        }
        catch {
          break;
        }
      }

      return sent;
    }

    private static bool WaitForDisconnect (TcpClient client, TimeSpan timeout)
    {
      var deadline = DateTime.UtcNow.Add (timeout);

      while (DateTime.UtcNow < deadline) {
        if (IsDisconnected (client))
          return true;

        Thread.Sleep (10);
      }

      return IsDisconnected (client);
    }

    private static void WaitUntil (Func<bool> predicate, TimeSpan timeout, string message)
    {
      var deadline = DateTime.UtcNow.Add (timeout);

      while (DateTime.UtcNow < deadline) {
        if (predicate ())
          return;

        Thread.Sleep (10);
      }

      Assert.That (predicate (), Is.True, message);
    }

    private sealed class BodyProbeResult
    {
      internal BodyProbeResult (int bodyBytesSent, bool disconnected, TimeSpan elapsed)
      {
        BodyBytesSent = bodyBytesSent;
        Disconnected = disconnected;
        Elapsed = elapsed;
      }

      internal int BodyBytesSent { get; private set; }

      internal bool Disconnected { get; private set; }

      internal TimeSpan Elapsed { get; private set; }
    }

    internal sealed class SessionTracker
    {
      private int _openCount;

      internal int OpenCount {
        get {
          return Thread.VolatileRead (ref _openCount);
        }
      }

      internal void RecordOpen ()
      {
        Interlocked.Increment (ref _openCount);
      }
    }

    private sealed class RawHandshakeResponseServer : IDisposable
    {
      private readonly string _bodyHeader;
      private readonly bool _closeAfterHeaders;
      private Exception _error;
      private volatile bool _headersSent;
      private readonly int _maxBodyBytesToSend;
      private readonly bool _probeBody;
      private readonly int _statusCode;
      private TcpClient _client;
      private readonly TcpListener _listener;
      private readonly Thread _thread;
      private int _bodyBytesSent;
      private volatile bool _completed;

      private RawHandshakeResponseServer (
        TcpListener listener,
        string bodyHeader,
        int statusCode,
        int maxBodyBytesToSend,
        bool probeBody,
        bool closeAfterHeaders
      )
      {
        _listener = listener;
        _bodyHeader = bodyHeader;
        _closeAfterHeaders = closeAfterHeaders;
        _maxBodyBytesToSend = maxBodyBytesToSend;
        _probeBody = probeBody;
        _statusCode = statusCode;
        Port = ((IPEndPoint) listener.LocalEndpoint).Port;

        _thread = new Thread (Run);
        _thread.IsBackground = true;
        _thread.Start ();
      }

      internal int BodyBytesSent {
        get {
          return Thread.VolatileRead (ref _bodyBytesSent);
        }
      }

      internal bool Completed {
        get {
          return _completed;
        }
      }

      internal Exception Error {
        get {
          return _error;
        }
      }

      internal bool HeadersSent {
        get {
          return _headersSent;
        }
      }

      private int Port { get; set; }

      internal static RawHandshakeResponseServer Start (
        string bodyHeader,
        int statusCode = 101,
        int maxBodyBytesToSend = 8 * 1024 * 1024,
        bool probeBody = true,
        bool closeAfterHeaders = false
      )
      {
        var listener = new TcpListener (IPAddress.Loopback, 0);

        listener.Start ();

        return new RawHandshakeResponseServer (
          listener,
          bodyHeader,
          statusCode,
          maxBodyBytesToSend,
          probeBody,
          closeAfterHeaders
        );
      }

      public void Dispose ()
      {
        _listener.Stop ();

        if (_client != null)
          _client.Close ();

        _thread.Join (1000);
      }

      internal string GetUrl ()
      {
        return String.Format ("ws://127.0.0.1:{0}/echo", Port);
      }

      private void Run ()
      {
        try {
          _client = _listener.AcceptTcpClient ();
          _client.SendTimeout = 1000;
          _client.ReceiveTimeout = 5000;

          var stream = _client.GetStream ();
          var request = ReadHttpHeader (stream);
          var key = GetHeaderValue (request, "Sec-WebSocket-Key");
          var response = _statusCode == 101
                         ? "HTTP/1.1 101 Switching Protocols\r\n"
                           + "Upgrade: websocket\r\n"
                           + "Connection: Upgrade\r\n"
                           + "Sec-WebSocket-Accept: " + ComputeWebSocketAccept (key) + "\r\n"
                           + _bodyHeader
                           + "\r\n"
                         : "HTTP/1.1 401 Unauthorized\r\n"
                           + "Connection: close\r\n"
                           + _bodyHeader
                           + "\r\n";
          var responseBytes = Encoding.ASCII.GetBytes (response);

          stream.Write (responseBytes, 0, responseBytes.Length);
          stream.Flush ();
          _headersSent = true;

          if (_closeAfterHeaders) {
            _client.Close ();
            return;
          }

          if (_probeBody && !WaitForDisconnect (_client, EarlyDisconnectTimeout)) {
            var sent = StreamProbeBody (_client, stream, _maxBodyBytesToSend);

            Interlocked.Exchange (ref _bodyBytesSent, sent);
          }

          WaitForDisconnect (_client, DisconnectTimeout);
        }
        catch (Exception ex) {
          _error = ex;
        }
        finally {
          _completed = true;
        }
      }
    }

    public sealed class TrackedEchoBehavior : WebSocketBehavior
    {
      private SessionTracker _tracker;

      internal void Configure (SessionTracker tracker)
      {
        _tracker = tracker;
      }

      protected override void OnOpen ()
      {
        _tracker.RecordOpen ();
      }

      protected override void OnMessage (MessageEventArgs e)
      {
        if (e.IsText)
          Send (e.Data);
        else
          Send (e.RawData);
      }
    }
  }
}
