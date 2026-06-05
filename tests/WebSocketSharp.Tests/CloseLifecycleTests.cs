using System;
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
  public sealed class CloseLifecycleTests
  {
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds (5);

    private const int OpcodeContinuation = 0x0;

    [Test]
    public void RepeatedCloseAsyncAndDisposeAreIdempotent ()
    {
      using (var server = LoopbackServer.Start (s => s.AddWebSocketService<EchoBehavior> ("/echo")))
      using (var client = new WebSocket (server.GetUrl ("/echo"))) {
        var sessions = server.WebSocketServices["/echo"].Sessions;
        var opened = new ManualResetEventSlim ();
        var closed = new ManualResetEventSlim ();
        var closeEvents = 0;
        var error = default (Exception);

        client.OnOpen += (sender, e) => opened.Set ();
        client.OnError += (sender, e) => error = e.Exception ?? new Exception (e.Message);
        client.OnClose += (sender, e) => {
          Interlocked.Increment (ref closeEvents);
          closed.Set ();
        };

        client.ConnectAsync ();

        WaitFor (opened, "The client did not open.");
        AssertNoError (error);
        Assert.That (sessions.Count, Is.EqualTo (1));

        client.CloseAsync ();
        client.CloseAsync ();
        ((IDisposable) client).Dispose ();

        WaitFor (closed, "The client did not close.");
        WaitUntil (() => sessions.Count == 0, "The server kept a session after repeated close/dispose.");

        Assert.That (closeEvents, Is.EqualTo (1));
        Assert.That (client.ReadyState, Is.EqualTo (WebSocketState.Closed));
      }
    }

    [Test]
    public void DisposeAfterSynchronousCloseDoesNotEmitSecondClose ()
    {
      using (var server = LoopbackServer.Start (s => s.AddWebSocketService<EchoBehavior> ("/echo")))
      using (var client = new WebSocket (server.GetUrl ("/echo"))) {
        var sessions = server.WebSocketServices["/echo"].Sessions;
        var opened = new ManualResetEventSlim ();
        var closed = new ManualResetEventSlim ();
        var closeEvents = 0;

        client.OnOpen += (sender, e) => opened.Set ();
        client.OnClose += (sender, e) => {
          Interlocked.Increment (ref closeEvents);
          closed.Set ();
        };

        client.Connect ();

        WaitFor (opened, "The client did not open.");
        Assert.That (sessions.Count, Is.EqualTo (1));

        client.Close ();
        ((IDisposable) client).Dispose ();
        ((IDisposable) client).Dispose ();

        WaitFor (closed, "The client did not close.");
        WaitUntil (() => sessions.Count == 0, "The server kept a session after close/dispose.");

        Assert.That (closeEvents, Is.EqualTo (1));
        Assert.That (client.ReadyState, Is.EqualTo (WebSocketState.Closed));
      }
    }

    [Test]
    public void InvalidCloseArgumentsDoNotChangeOpenConnection ()
    {
      using (var server = LoopbackServer.Start (s => s.AddWebSocketService<EchoBehavior> ("/echo")))
      using (var client = new WebSocket (server.GetUrl ("/echo"))) {
        var opened = new ManualResetEventSlim ();
        var closed = new ManualResetEventSlim ();

        client.OnOpen += (sender, e) => opened.Set ();
        client.OnClose += (sender, e) => closed.Set ();

        client.Connect ();

        WaitFor (opened, "The client did not open.");

        Assert.Throws<ArgumentOutOfRangeException> (() => client.Close (999, String.Empty));
        Assert.Throws<ArgumentException> (() => client.Close (1011, String.Empty));
        Assert.Throws<ArgumentException> (() => client.Close ((ushort) CloseStatusCode.NoStatus, "reason"));
        Assert.Throws<ArgumentOutOfRangeException> (
          () => client.Close (CloseStatusCode.Normal, new string ('r', 124))
        );

        Assert.That (client.ReadyState, Is.EqualTo (WebSocketState.Open));
        Assert.That (closed.IsSet, Is.False);

        client.Close ();

        WaitFor (closed, "The client did not close after invalid argument checks.");
      }
    }

    [Test]
    public void OnCloseHandlerExceptionDoesNotPreventClosedStateOrSessionCleanup ()
    {
      using (var server = LoopbackServer.Start (s => s.AddWebSocketService<EchoBehavior> ("/echo")))
      using (var client = new WebSocket (server.GetUrl ("/echo"))) {
        var sessions = server.WebSocketServices["/echo"].Sessions;
        var opened = new ManualResetEventSlim ();
        var closed = new ManualResetEventSlim ();
        var closeEvents = 0;

        client.Log.Output = (data, path) => { };
        client.OnOpen += (sender, e) => opened.Set ();
        client.OnClose += (sender, e) => {
          Interlocked.Increment (ref closeEvents);
          closed.Set ();

          throw new InvalidOperationException ("close handler failed");
        };

        client.Connect ();

        WaitFor (opened, "The client did not open.");

        client.Close ();

        WaitFor (closed, "The client did not close.");
        WaitUntil (
          () => sessions.Count == 0,
          "The server kept a session after an exception-throwing OnClose handler."
        );

        Assert.That (closeEvents, Is.EqualTo (1));
        Assert.That (client.ReadyState, Is.EqualTo (WebSocketState.Closed));
      }
    }

    [Test]
    public void OnErrorHandlerExceptionDoesNotBreakOpenConnectionOrCloseLifecycle ()
    {
      using (var server = LoopbackServer.Start (s => s.AddWebSocketService<EchoBehavior> ("/echo")))
      using (var client = new WebSocket (server.GetUrl ("/echo"))) {
        var opened = new ManualResetEventSlim ();
        var errorSeen = new ManualResetEventSlim ();
        var closed = new ManualResetEventSlim ();

        client.Log.Output = (data, path) => { };
        client.OnOpen += (sender, e) => opened.Set ();
        client.OnMessage += (sender, e) => {
          throw new InvalidOperationException ("message handler failed");
        };
        client.OnError += (sender, e) => {
          errorSeen.Set ();

          throw new InvalidOperationException ("error handler failed");
        };
        client.OnClose += (sender, e) => closed.Set ();

        client.Connect ();

        WaitFor (opened, "The client did not open.");

        client.Send ("trigger-error");

        WaitFor (errorSeen, "The client did not emit OnError after OnMessage threw.");
        Assert.That (client.ReadyState, Is.EqualTo (WebSocketState.Open));

        client.Close ();

        WaitFor (closed, "The client did not close after an exception-throwing OnError handler.");
      }
    }

    [Test]
    public void AbruptTcpDisconnectRemovesServerSession ()
    {
      using (
        var server = LoopbackServer.Start (
          s => {
            s.Log.Output = (data, path) => { };
            s.AddWebSocketService<EchoBehavior> ("/echo");
          }
        )
      )
      using (var client = ConnectRawWebSocket (server.Port, "/echo")) {
        var sessions = server.WebSocketServices["/echo"].Sessions;

        WaitUntil (() => sessions.Count == 1, "The server did not register the raw WebSocket session.");

        client.Client.LingerState = new LingerOption (true, 0);
        client.Close ();

        WaitUntil (() => sessions.Count == 0, "The server kept a session after abrupt TCP disconnect.");
      }
    }

    [Test]
    public void ProtocolErrorCloseIsReportedToRawClientAndRemovesSession ()
    {
      using (
        var server = LoopbackServer.Start (
          s => {
            s.Log.Output = (data, path) => { };
            s.AddWebSocketService<EchoBehavior> ("/echo");
          }
        )
      )
      using (var client = ConnectRawWebSocket (server.Port, "/echo")) {
        var sessions = server.WebSocketServices["/echo"].Sessions;
        var stream = client.GetStream ();

        WaitUntil (() => sessions.Count == 1, "The server did not register the raw WebSocket session.");

        WriteClientFrame (stream, OpcodeContinuation, Encoding.UTF8.GetBytes ("bad"), true, true);

        Assert.That (ReadServerCloseCode (stream), Is.EqualTo ((ushort) CloseStatusCode.ProtocolError));
        WaitUntil (() => sessions.Count == 0, "The server kept a session after protocol-error close.");
      }
    }

    [Test]
    public void BehaviorOnCloseExceptionDoesNotStrandServerSession ()
    {
      ThrowingCloseBehavior.Reset ();

      using (
        var server = LoopbackServer.Start (
          s => {
            s.Log.Output = (data, path) => { };
            s.AddWebSocketService<ThrowingCloseBehavior> ("/throw-close");
          }
        )
      )
      using (var client = new WebSocket (server.GetUrl ("/throw-close"))) {
        var sessions = server.WebSocketServices["/throw-close"].Sessions;
        var opened = new ManualResetEventSlim ();
        var closed = new ManualResetEventSlim ();

        client.OnOpen += (sender, e) => opened.Set ();
        client.OnClose += (sender, e) => closed.Set ();

        client.Connect ();

        WaitFor (opened, "The client did not open.");

        client.Close ();

        WaitFor (closed, "The client did not close.");
        WaitUntil (
          () => sessions.Count == 0,
          "The server kept a session after an exception-throwing behavior OnClose."
        );

        Assert.That (ThrowingCloseBehavior.CloseCount, Is.EqualTo (1));
      }
    }

    [Test]
    public void BehaviorOnErrorExceptionDoesNotBreakCloseLifecycle ()
    {
      ThrowingErrorBehavior.Reset ();

      using (
        var server = LoopbackServer.Start (
          s => {
            s.Log.Output = (data, path) => { };
            s.AddWebSocketService<ThrowingErrorBehavior> ("/throw-error");
          }
        )
      )
      using (var client = new WebSocket (server.GetUrl ("/throw-error"))) {
        var sessions = server.WebSocketServices["/throw-error"].Sessions;
        var opened = new ManualResetEventSlim ();
        var closed = new ManualResetEventSlim ();

        client.OnOpen += (sender, e) => opened.Set ();
        client.OnClose += (sender, e) => closed.Set ();

        client.Connect ();

        WaitFor (opened, "The client did not open.");

        client.Send ("trigger-server-error");

        WaitUntil (
          () => ThrowingErrorBehavior.ErrorCount == 1,
          "The behavior did not receive OnError after OnMessage threw."
        );

        Assert.That (client.ReadyState, Is.EqualTo (WebSocketState.Open));

        client.Close ();

        WaitFor (closed, "The client did not close.");
        WaitUntil (
          () => sessions.Count == 0,
          "The server kept a session after an exception-throwing behavior OnError."
        );
      }
    }

    private static void AssertNoError (Exception error)
    {
      if (error != null)
        Assert.Fail (error.ToString ());
    }

    private static TcpClient ConnectRawWebSocket (int port, string path)
    {
      var client = new TcpClient ();
      var key = CreateWebSocketKey ();

      client.Connect (IPAddress.Loopback, port);

      var request = String.Format (
        "GET {0} HTTP/1.1\r\nHost: 127.0.0.1:{1}\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Key: {2}\r\nSec-WebSocket-Version: 13\r\n\r\n",
        path,
        port,
        key
      );
      var stream = client.GetStream ();
      var bytes = Encoding.ASCII.GetBytes (request);

      stream.Write (bytes, 0, bytes.Length);

      var response = ReadHttpResponseHeader (stream);

      if (!response.StartsWith ("HTTP/1.1 101 ", StringComparison.Ordinal))
        Assert.Fail ("The raw WebSocket handshake did not receive a 101 response: " + response);

      return client;
    }

    private static string CreateWebSocketKey ()
    {
      var bytes = new byte[16];

      using (var rng = RandomNumberGenerator.Create ())
        rng.GetBytes (bytes);

      return Convert.ToBase64String (bytes);
    }

    private static bool IsDisconnected (TcpClient client)
    {
      try {
        return !client.Connected ||
               (client.Client.Poll (0, SelectMode.SelectRead) && client.Client.Available == 0);
      }
      catch (ObjectDisposedException) {
        return true;
      }
      catch (SocketException) {
        return true;
      }
    }

    private static byte[] ReadExactly (NetworkStream stream, int length)
    {
      var buffer = new byte[length];
      var offset = 0;
      var deadline = DateTime.UtcNow.Add (Timeout);

      while (offset < length && DateTime.UtcNow < deadline) {
        if (!stream.DataAvailable) {
          Thread.Sleep (10);
          continue;
        }

        var read = stream.Read (buffer, offset, length - offset);

        if (read <= 0)
          break;

        offset += read;
      }

      if (offset != length)
        Assert.Fail ("Timed out while reading a WebSocket frame.");

      return buffer;
    }

    private static ushort ReadServerCloseCode (NetworkStream stream)
    {
      var header = ReadExactly (stream, 2);
      var opcode = header[0] & 0x0f;
      var payloadLength = header[1] & 0x7f;

      Assert.That (opcode, Is.EqualTo (0x8), "The server did not send a close frame.");
      Assert.That (payloadLength, Is.GreaterThanOrEqualTo (2), "The close frame did not include a close code.");

      var payload = ReadExactly (stream, payloadLength);

      return (ushort) ((payload[0] << 8) | payload[1]);
    }

    private static string ReadHttpResponseHeader (NetworkStream stream)
    {
      var buffer = new byte[1];
      var data = new MemoryStream ();
      var previous = new byte[3];
      var deadline = DateTime.UtcNow.Add (Timeout);

      while (DateTime.UtcNow < deadline) {
        if (!stream.DataAvailable) {
          Thread.Sleep (10);
          continue;
        }

        var read = stream.Read (buffer, 0, 1);

        if (read <= 0)
          break;

        data.Write (buffer, 0, read);

        if (previous[0] == '\r' &&
            previous[1] == '\n' &&
            previous[2] == '\r' &&
            buffer[0] == '\n')
          return Encoding.ASCII.GetString (data.ToArray ());

        previous[0] = previous[1];
        previous[1] = previous[2];
        previous[2] = buffer[0];
      }

      Assert.Fail ("Timed out while reading the raw WebSocket handshake response.");
      return null;
    }

    private static void WaitFor (ManualResetEventSlim signal, string message)
    {
      Assert.That (signal.Wait (Timeout), Is.True, message);
    }

    private static void WaitUntil (Func<bool> predicate, string message)
    {
      var deadline = DateTime.UtcNow.Add (Timeout);

      while (DateTime.UtcNow < deadline) {
        if (predicate ())
          return;

        Thread.Sleep (10);
      }

      Assert.That (predicate (), Is.True, message);
    }

    private static void WriteClientFrame (
      NetworkStream stream,
      int opcode,
      byte[] payload,
      bool fin,
      bool mask
    )
    {
      var frame = new MemoryStream ();
      var firstByte = (byte) opcode;

      if (fin)
        firstByte |= 0x80;

      frame.WriteByte (firstByte);

      if (payload.Length <= 125) {
        frame.WriteByte ((byte) ((mask ? 0x80 : 0x00) | payload.Length));
      }
      else if (payload.Length <= UInt16.MaxValue) {
        frame.WriteByte ((byte) ((mask ? 0x80 : 0x00) | 126));
        frame.WriteByte ((byte) ((payload.Length >> 8) & 0xff));
        frame.WriteByte ((byte) (payload.Length & 0xff));
      }
      else {
        Assert.Fail ("Close lifecycle tests do not support raw frames over UInt16.MaxValue.");
      }

      var data = (byte[]) payload.Clone ();

      if (mask) {
        var maskingKey = new byte[] { 0x11, 0x22, 0x33, 0x44 };

        frame.Write (maskingKey, 0, maskingKey.Length);
        Mask (data, maskingKey);
      }

      frame.Write (data, 0, data.Length);

      var bytes = frame.ToArray ();

      stream.Write (bytes, 0, bytes.Length);
    }

    private static void Mask (byte[] payload, byte[] maskingKey)
    {
      for (var i = 0; i < payload.Length; i++)
        payload[i] = (byte) (payload[i] ^ maskingKey[i % 4]);
    }

    public sealed class EchoBehavior : WebSocketBehavior
    {
      protected override void OnMessage (MessageEventArgs e)
      {
        if (e.IsText)
          Send (e.Data);
        else
          Send (e.RawData);
      }
    }

    public sealed class ThrowingCloseBehavior : WebSocketBehavior
    {
      private static int _closeCount;

      public static int CloseCount {
        get {
          return Thread.VolatileRead (ref _closeCount);
        }
      }

      public static void Reset ()
      {
        Thread.VolatileWrite (ref _closeCount, 0);
      }

      protected override void OnClose (CloseEventArgs e)
      {
        Interlocked.Increment (ref _closeCount);

        throw new InvalidOperationException ("behavior close handler failed");
      }
    }

    public sealed class ThrowingErrorBehavior : WebSocketBehavior
    {
      private static int _errorCount;

      public static int ErrorCount {
        get {
          return Thread.VolatileRead (ref _errorCount);
        }
      }

      public static void Reset ()
      {
        Thread.VolatileWrite (ref _errorCount, 0);
      }

      protected override void OnError (ErrorEventArgs e)
      {
        Interlocked.Increment (ref _errorCount);

        throw new InvalidOperationException ("behavior error handler failed");
      }

      protected override void OnMessage (MessageEventArgs e)
      {
        throw new InvalidOperationException ("behavior message handler failed");
      }
    }
  }
}
