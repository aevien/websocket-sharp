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
  }
}
