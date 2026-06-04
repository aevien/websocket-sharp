using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using WebSocketSharp.Server;

namespace WebSocketSharp.StressTests
{
  [TestFixture]
  [NonParallelizable]
  [Category ("Stress")]
  public sealed class ConnectStormStressTests
  {
    private const int DefaultClientCount = 50;
    private const int DefaultTimeoutSeconds = 60;

    [Test]
    public void ManyClientsCanConnectAsyncAtOnceWithoutThreadPoolStarvation ()
    {
      var clientCount = GetPositiveInt ("WEBSOCKET_SHARP_CONNECT_STORM_CLIENTS", DefaultClientCount);
      var timeout = TimeSpan.FromSeconds (
        GetPositiveInt ("WEBSOCKET_SHARP_CONNECT_STORM_TIMEOUT_SECONDS", DefaultTimeoutSeconds)
      );
      var elapsed = Stopwatch.StartNew ();

      using (var server = StressLoopbackServer.Start (s => s.AddWebSocketService<EchoBehavior> ("/echo")))
      using (var opened = new CountdownEvent (clientCount))
      using (var closed = new CountdownEvent (clientCount)) {
        var sessions = server.WebSocketServices["/echo"].Sessions;
        var clients = new List<WebSocket> (clientCount);
        var errors = new ConcurrentQueue<string> ();
        var openSeen = new int[clientCount];
        var closeSeen = new int[clientCount];

        try {
          for (var i = 0; i < clientCount; i++)
            clients.Add (CreateClient (server.GetUrl ("/echo"), i, opened, closed, errors, openSeen, closeSeen));

          foreach (var client in clients)
            client.ConnectAsync ();

          WaitFor (opened, timeout, "Not all connect-storm clients opened.");
          AssertNoErrors (errors);

          WaitUntil (
            () => sessions.Count == clientCount,
            timeout,
            "The stress server did not observe the expected connect-storm session count."
          );

          foreach (var client in clients)
            client.CloseAsync ();

          WaitFor (closed, timeout, "Not all connect-storm clients closed.");

          WaitUntil (
            () => sessions.Count == 0,
            timeout,
            "The stress server kept sessions after connect-storm clients closed."
          );
        }
        finally {
          foreach (var client in clients)
            ((IDisposable) client).Dispose ();
        }
      }

      elapsed.Stop ();
      TestContext.WriteLine (
        "Completed connect storm with {0} simultaneous ConnectAsync clients in {1}.",
        clientCount,
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

    private static void AssertNoErrors (ConcurrentQueue<string> errors)
    {
      if (errors.IsEmpty)
        return;

      Assert.Fail (String.Join (Environment.NewLine, errors.Take (10).ToArray ()));
    }

    private static int GetPositiveInt (string variableName, int defaultValue)
    {
      var value = Environment.GetEnvironmentVariable (variableName);
      int parsed;

      if (!Int32.TryParse (value, out parsed) || parsed < 1)
        return defaultValue;

      return parsed;
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
