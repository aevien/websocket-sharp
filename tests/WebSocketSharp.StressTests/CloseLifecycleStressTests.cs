using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using NUnit.Framework;
using WebSocketSharp.Server;

namespace WebSocketSharp.StressTests
{
  [TestFixture]
  [NonParallelizable]
  [Category ("Stress")]
  public sealed class CloseLifecycleStressTests
  {
    private const int DefaultClientCount = 50;
    private const int DefaultAbruptClientCount = 25;
    private const int DefaultTimeoutSeconds = 15;

    [Test]
    public void ConcurrentCloseAndAbruptDisconnectDoNotStrandSessions ()
    {
      var clientCount = GetPositiveInt ("WEBSOCKET_SHARP_CLOSE_STRESS_CLIENTS", DefaultClientCount);
      var abruptClientCount = GetPositiveInt ("WEBSOCKET_SHARP_CLOSE_STRESS_ABRUPT_CLIENTS", DefaultAbruptClientCount);
      var timeout = TimeSpan.FromSeconds (
        GetPositiveInt ("WEBSOCKET_SHARP_CLOSE_STRESS_TIMEOUT_SECONDS", DefaultTimeoutSeconds)
      );
      var elapsed = Stopwatch.StartNew ();

      using (
        var server = StressLoopbackServer.Start (
          s => {
            s.Log.Output = (data, path) => { };
            s.AddWebSocketService<EchoBehavior> ("/echo");
          }
        )
      ) {
        var sessions = server.WebSocketServices["/echo"].Sessions;

        RunConcurrentCloseRound (server.GetUrl ("/echo"), sessions, clientCount, timeout);
        RunAbruptDisconnectRound (server.Port, sessions, abruptClientCount, timeout);
      }

      elapsed.Stop ();
      TestContext.WriteLine (
        "Completed close lifecycle stress with {0} concurrent close clients and {1} abrupt raw disconnect clients in {2}.",
        clientCount,
        abruptClientCount,
        elapsed.Elapsed
      );
    }

    private static WebSocket CreateClient (
      string url,
      int clientIndex,
      CountdownEvent opened,
      CountdownEvent closed,
      ConcurrentQueue<string> errors,
      int[] openSeen,
      int[] closeSeen
    )
    {
      var client = new WebSocket (url);

      client.OnOpen += (sender, e) => {
        if (Interlocked.Exchange (ref openSeen[clientIndex], 1) == 0)
          opened.Signal ();
      };

      client.OnError += (sender, e) => {
        errors.Enqueue (String.Format ("Client {0} error: {1}", clientIndex, e.Exception ?? new Exception (e.Message)));
      };

      client.OnClose += (sender, e) => {
        if (Interlocked.Exchange (ref closeSeen[clientIndex], 1) == 0)
          closed.Signal ();
      };

      return client;
    }

    private static TcpClient ConnectRawWebSocket (int port, string path, TimeSpan timeout)
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

      var response = ReadHttpResponseHeader (stream, timeout);

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

    private static int GetPositiveInt (string variableName, int defaultValue)
    {
      var value = Environment.GetEnvironmentVariable (variableName);
      int parsed;

      if (!Int32.TryParse (value, out parsed) || parsed < 1)
        return defaultValue;

      return parsed;
    }

    private static string ReadHttpResponseHeader (NetworkStream stream, TimeSpan timeout)
    {
      var buffer = new byte[1];
      var data = new MemoryStream ();
      var previous = new byte[3];
      var deadline = DateTime.UtcNow.Add (timeout);

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

    private static void RunAbruptDisconnectRound (
      int port,
      WebSocketSessionManager sessions,
      int clientCount,
      TimeSpan timeout
    )
    {
      var clients = new List<TcpClient> (clientCount);

      try {
        for (var i = 0; i < clientCount; i++)
          clients.Add (ConnectRawWebSocket (port, "/echo", timeout));

        WaitUntil (
          () => sessions.Count == clientCount,
          timeout,
          "The server did not register every raw WebSocket session before abrupt disconnect."
        );

        foreach (var client in clients) {
          client.Client.LingerState = new LingerOption (true, 0);
          client.Close ();
        }

        WaitUntil (
          () => sessions.Count == 0,
          timeout,
          "The server kept sessions after abrupt raw TCP disconnects."
        );
      }
      finally {
        foreach (var client in clients)
          client.Close ();
      }
    }

    private static void RunConcurrentCloseRound (
      string url,
      WebSocketSessionManager sessions,
      int clientCount,
      TimeSpan timeout
    )
    {
      using (var opened = new CountdownEvent (clientCount))
      using (var closed = new CountdownEvent (clientCount)) {
        var clients = new List<WebSocket> (clientCount);
        var errors = new ConcurrentQueue<string> ();
        var openSeen = new int[clientCount];
        var closeSeen = new int[clientCount];

        try {
          for (var i = 0; i < clientCount; i++)
            clients.Add (CreateClient (url, i, opened, closed, errors, openSeen, closeSeen));

          foreach (var client in clients)
            client.ConnectAsync ();

          WaitFor (opened, timeout, "Not all close-stress clients opened.");
          AssertNoErrors (errors);

          WaitUntil (
            () => sessions.Count == clientCount,
            timeout,
            "The server did not observe the expected close-stress session count."
          );

          foreach (var client in clients) {
            client.CloseAsync ();
            client.CloseAsync ();
            ((IDisposable) client).Dispose ();
          }

          WaitFor (closed, timeout, "Not all close-stress clients closed.");
          WaitUntil (
            () => sessions.Count == 0,
            timeout,
            "The server kept sessions after concurrent repeated close/dispose."
          );
        }
        finally {
          foreach (var client in clients)
            ((IDisposable) client).Dispose ();
        }
      }
    }

    private static void AssertNoErrors (ConcurrentQueue<string> errors)
    {
      if (errors.IsEmpty)
        return;

      Assert.Fail (String.Join (Environment.NewLine, errors.Take (10).ToArray ()));
    }

    private static void WaitFor (CountdownEvent signal, TimeSpan timeout, string message)
    {
      Assert.That (
        signal.Wait (timeout),
        Is.True,
        String.Format ("{0} Remaining: {1}.", message, signal.CurrentCount)
      );
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
