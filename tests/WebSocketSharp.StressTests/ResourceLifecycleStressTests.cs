using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using NUnit.Framework;
using WebSocketSharp.Server;

namespace WebSocketSharp.StressTests
{
  [TestFixture]
  [NonParallelizable]
  [Category ("Stress")]
  public sealed class ResourceLifecycleStressTests
  {
    private const int DefaultRounds = 5;
    private const int DefaultConnectStormClients = 50;
    private const int DefaultSilentClients = 20;
    private const int DefaultHandshakeTimeoutMilliseconds = 250;
    private const int DefaultTimeoutSeconds = 15;
    private const int DefaultThreadDriftLimit = 8;

    [Test]
    public void RepeatedStormsAndSlowHandshakesDoNotStrandSessionsOrThreads ()
    {
      var rounds = GetPositiveInt ("WEBSOCKET_SHARP_RESOURCE_STRESS_ROUNDS", DefaultRounds);
      var connectStormClients = GetPositiveInt ("WEBSOCKET_SHARP_RESOURCE_STRESS_CONNECT_CLIENTS", DefaultConnectStormClients);
      var silentClients = GetPositiveInt ("WEBSOCKET_SHARP_RESOURCE_STRESS_SILENT_CLIENTS", DefaultSilentClients);
      var handshakeTimeout = TimeSpan.FromMilliseconds (
        GetPositiveInt ("WEBSOCKET_SHARP_RESOURCE_STRESS_HANDSHAKE_TIMEOUT_MILLISECONDS", DefaultHandshakeTimeoutMilliseconds)
      );
      var timeout = TimeSpan.FromSeconds (
        GetPositiveInt ("WEBSOCKET_SHARP_RESOURCE_STRESS_TIMEOUT_SECONDS", DefaultTimeoutSeconds)
      );
      var threadDriftLimit = GetPositiveInt ("WEBSOCKET_SHARP_RESOURCE_STRESS_THREAD_DRIFT_LIMIT", DefaultThreadDriftLimit);
      var elapsed = Stopwatch.StartNew ();

      ForceCleanup ();
      var initialThreads = Process.GetCurrentProcess ().Threads.Count;
      var peakThreadDrift = 0;
      var steadyStateThreads = 0;

      using (
        var server = StressLoopbackServer.Start (
          s => {
            s.HandshakeTimeout = handshakeTimeout;
            s.Log.Output = (data, path) => { };
            s.AddWebSocketService<EchoBehavior> ("/echo");
          }
        )
      ) {
        var sessions = server.WebSocketServices["/echo"].Sessions;

        RunConnectStormRound (server.GetUrl ("/echo"), sessions, connectStormClients, timeout);
        RunSlowHandshakeRound (server, sessions, silentClients, timeout);
        ForceCleanup ();

        steadyStateThreads = Process.GetCurrentProcess ().Threads.Count;

        TestContext.WriteLine (
          "Resource stress warm-up: initial threads={0}, steady-state threads={1}, warm-up drift={2}.",
          initialThreads,
          steadyStateThreads,
          steadyStateThreads - initialThreads
        );

        for (var round = 0; round < rounds; round++) {
          RunConnectStormRound (server.GetUrl ("/echo"), sessions, connectStormClients, timeout);
          RunSlowHandshakeRound (server, sessions, silentClients, timeout);

          ForceCleanup ();

          var currentThreads = Process.GetCurrentProcess ().Threads.Count;
          var threadDrift = currentThreads - steadyStateThreads;

          if (threadDrift > peakThreadDrift)
            peakThreadDrift = threadDrift;

          TestContext.WriteLine (
            "Resource stress round {0}/{1}: sessions={2}, threads={3}, driftFromSteadyState={4}.",
            round + 1,
            rounds,
            sessions.Count,
            currentThreads,
            threadDrift
          );

          Assert.That (sessions.Count, Is.EqualTo (0));
          Assert.That (
            threadDrift,
            Is.LessThanOrEqualTo (threadDriftLimit),
            "Thread count did not return near the baseline after resource stress cleanup."
          );
        }
      }

      ForceCleanup ();
      var finalThreads = Process.GetCurrentProcess ().Threads.Count;
      var finalThreadDrift = finalThreads - initialThreads;
      var finalSteadyStateDrift = finalThreads - steadyStateThreads;

      elapsed.Stop ();
      TestContext.WriteLine (
        "Completed resource lifecycle stress: {0} measured rounds, {1} connect-storm clients, {2} silent clients, initial threads {3}, final threads {4}, final drift {5}, final steady-state drift {6}, peak steady-state drift {7}, elapsed {8}.",
        rounds,
        connectStormClients,
        silentClients,
        initialThreads,
        finalThreads,
        finalThreadDrift,
        finalSteadyStateDrift,
        peakThreadDrift,
        elapsed.Elapsed
      );

      Assert.That (
        finalSteadyStateDrift,
        Is.LessThanOrEqualTo (threadDriftLimit),
        "Final thread count remained above the allowed drift from the steady-state baseline."
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

    private static TcpClient CreateSilentClient (int port)
    {
      var client = new TcpClient ();

      client.Connect (IPAddress.Loopback, port);

      return client;
    }

    private static void AssertNoErrors (ConcurrentQueue<string> errors)
    {
      if (errors.IsEmpty)
        return;

      Assert.Fail (String.Join (Environment.NewLine, errors.Take (10).ToArray ()));
    }

    private static void ForceCleanup ()
    {
      var deadline = DateTime.UtcNow.AddSeconds (3);
      var stableCount = 0;
      var lastThreadCount = -1;

      GC.Collect ();
      GC.WaitForPendingFinalizers ();
      GC.Collect ();

      while (DateTime.UtcNow < deadline) {
        var currentThreadCount = Process.GetCurrentProcess ().Threads.Count;

        if (currentThreadCount == lastThreadCount) {
          stableCount++;

          if (stableCount >= 3)
            return;
        }
        else {
          stableCount = 0;
          lastThreadCount = currentThreadCount;
        }

        Thread.Sleep (100);
      }
    }

    private static int GetPositiveInt (string variableName, int defaultValue)
    {
      var value = Environment.GetEnvironmentVariable (variableName);
      int parsed;

      if (!Int32.TryParse (value, out parsed) || parsed < 1)
        return defaultValue;

      return parsed;
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

    private static void RunConnectStormRound (
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

          WaitFor (opened, timeout, "Not all lifecycle connect-storm clients opened.");
          AssertNoErrors (errors);

          WaitUntil (
            () => sessions.Count == clientCount,
            timeout,
            "The server did not observe the expected lifecycle connect-storm session count."
          );

          foreach (var client in clients)
            client.CloseAsync ();

          WaitFor (closed, timeout, "Not all lifecycle connect-storm clients closed.");

          WaitUntil (
            () => sessions.Count == 0,
            timeout,
            "The server kept lifecycle connect-storm sessions after close."
          );
        }
        finally {
          foreach (var client in clients)
            ((IDisposable) client).Dispose ();
        }
      }
    }

    private static void RunSlowHandshakeRound (
      StressLoopbackServer server,
      WebSocketSessionManager sessions,
      int silentClientCount,
      TimeSpan timeout
    )
    {
      var clients = new List<TcpClient> (silentClientCount);

      try {
        for (var i = 0; i < silentClientCount; i++)
          clients.Add (CreateSilentClient (server.Port));

        WaitUntil (
          () => clients.All (IsDisconnected),
          timeout,
          "The server did not disconnect every lifecycle silent handshake client."
        );

        Assert.That (sessions.Count, Is.EqualTo (0));
      }
      finally {
        foreach (var client in clients)
          client.Close ();
      }
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
