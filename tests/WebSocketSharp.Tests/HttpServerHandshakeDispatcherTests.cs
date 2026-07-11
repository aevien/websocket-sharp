using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using NUnit.Framework;
using WebSocketSharp.Server;

namespace WebSocketSharp.Tests
{
  [TestFixture]
  [NonParallelizable]
  public sealed class HttpServerHandshakeDispatcherTests
  {
    [Test]
    public void HttpServerDoesNotRestartUntilBlockedHandshakeWorkerStops ()
    {
      var gate = new HandshakeGate ();
      var port = LoopbackServer.GetFreeTcpPort ();
      var server = new HttpServer (IPAddress.Loopback, port);

      using (var clientSettled = new ManualResetEventSlim ())
      using (var firstStopCompleted = new ManualResetEventSlim ())
      using (var client = new WebSocket (GetUrl (port))) {
        try {
          server.MaxConcurrentHandshakes = 1;
          server.MaxPendingHandshakes = 1;
          server.Log.Output = (data, path) => { };
          server.AddWebSocketService<BlockingEchoBehavior> (
            "/echo",
            behavior => behavior.Configure (gate)
          );
          server.Start ();

          client.ConnectionTimeout = TimeSpan.FromSeconds (10);
          client.Log.Output = (data, path) => { };
          client.OnOpen += (sender, e) => clientSettled.Set ();
          client.OnError += (sender, e) => clientSettled.Set ();
          client.OnClose += (sender, e) => clientSettled.Set ();
          client.ConnectAsync ();

          WaitUntil (
            () => gate.ActiveCount == 1,
            TimeSpan.FromSeconds (5),
            "The test handshake callback did not start."
          );

          var firstStopElapsed = TimeSpan.Zero;
          var firstStop = new Thread (() => {
            var elapsed = System.Diagnostics.Stopwatch.StartNew ();

            server.Stop ();
            elapsed.Stop ();

            firstStopElapsed = elapsed.Elapsed;
            firstStopCompleted.Set ();
          });

          firstStop.IsBackground = true;
          firstStop.Start ();

          WaitUntil (
            () => !server.IsListening,
            TimeSpan.FromSeconds (2),
            "The first shutdown did not stop the listener."
          );

          var secondStopElapsed = System.Diagnostics.Stopwatch.StartNew ();

          server.Stop ();
          secondStopElapsed.Stop ();

          Assert.That (secondStopElapsed.Elapsed, Is.LessThan (TimeSpan.FromSeconds (1)));
          Assert.That (server.IsListening, Is.False);

          server.Start ();

          Assert.That (
            server.IsListening,
            Is.False,
            "The server restarted while an old handshake callback was still running."
          );

          Assert.That (
            firstStopCompleted.Wait (TimeSpan.FromSeconds (7)),
            Is.True,
            "The first shutdown did not return after its worker timeout."
          );
          Assert.That (firstStopElapsed, Is.LessThan (TimeSpan.FromSeconds (7)));

          gate.Release ();

          Assert.That (clientSettled.Wait (TimeSpan.FromSeconds (5)), Is.True);
          WaitUntil (
            () => gate.ActiveCount == 0,
            TimeSpan.FromSeconds (5),
            "The blocked handshake callback did not finish after release."
          );

          server.Stop ();
          server.Start ();

          Assert.That (server.IsListening, Is.True);
          RoundTrip (port, "after-blocked-shutdown");

          TestContext.WriteLine (
            "Blocked shutdown proof: first Stop={0}, concurrent Stop={1}, restart blocked until worker exit, second Start succeeded.",
            firstStopElapsed,
            secondStopElapsed.Elapsed
          );
        }
        finally {
          gate.Release ();
          server.Stop ();
        }
      }
    }

    [Test]
    public void HttpServerBoundsRejectsAndRecoversWebSocketHandshakes ()
    {
      const int clientCount = 20;
      const int maxConcurrentHandshakes = 2;
      const int maxPendingHandshakes = 1;

      var gate = new HandshakeGate ();
      var logs = new List<LogData> ();
      var port = LoopbackServer.GetFreeTcpPort ();
      var server = new HttpServer (IPAddress.Loopback, port);
      var clients = new List<WebSocket> (clientCount);
      var outcomes = new int[clientCount];
      var overloadWorkerThreadCount = 0;

      try {
        server.HandshakeTimeout = TimeSpan.FromSeconds (10);
        server.MaxConcurrentHandshakes = maxConcurrentHandshakes;
        server.MaxPendingHandshakes = maxPendingHandshakes;
        server.Log.Level = LogLevel.Warn;
        server.Log.Output = (data, path) => {
          lock (logs)
            logs.Add (data);
        };
        server.AddWebSocketService<BlockingEchoBehavior> (
          "/echo",
          behavior => behavior.Configure (gate)
        );
        server.Start ();

        Assert.That (server.IsListening, Is.True);

        server.MaxConcurrentHandshakes = 99;
        server.MaxPendingHandshakes = 99;

        Assert.That (server.MaxConcurrentHandshakes, Is.EqualTo (maxConcurrentHandshakes));
        Assert.That (server.MaxPendingHandshakes, Is.EqualTo (maxPendingHandshakes));

        using (var settled = new CountdownEvent (clientCount)) {
          for (var i = 0; i < clientCount; i++) {
            var index = i;
            var client = new WebSocket (GetUrl (port));

            client.ConnectionTimeout = TimeSpan.FromSeconds (10);
            client.Log.Output = (data, path) => { };
            client.OnOpen += (sender, e) => SetOutcome (outcomes, index, 1, settled);
            client.OnError += (sender, e) => SetOutcome (outcomes, index, 2, settled);
            client.OnClose += (sender, e) => SetOutcome (outcomes, index, 2, settled);

            clients.Add (client);
            client.ConnectAsync ();
          }

          WaitUntil (
            () => gate.MaxActiveCount == maxConcurrentHandshakes,
            TimeSpan.FromSeconds (5),
            "The HTTP server did not reach the configured handshake concurrency."
          );
          WaitUntil (
            () => ContainsQueueFullWarning (logs),
            TimeSpan.FromSeconds (5),
            "The HTTP server did not report a full handshake queue."
          );
          WaitUntil (
            () => outcomes.Count (outcome => outcome == 2)
                  == clientCount - maxConcurrentHandshakes - maxPendingHandshakes,
            TimeSpan.FromSeconds (5),
            "The HTTP server did not reject every request beyond the configured capacity."
          );

          Assert.That (gate.MaxActiveCount, Is.EqualTo (maxConcurrentHandshakes));
          Assert.That (gate.WorkerThreadCount, Is.EqualTo (maxConcurrentHandshakes));

          overloadWorkerThreadCount = gate.WorkerThreadCount;

          gate.Release ();

          Assert.That (
            settled.Wait (TimeSpan.FromSeconds (10)),
            Is.True,
            "Not every overload client reached an open or rejected state."
          );
        }

        Assert.That (
          outcomes.Count (outcome => outcome == 1),
          Is.EqualTo (maxConcurrentHandshakes + maxPendingHandshakes)
        );
        Assert.That (
          outcomes.Count (outcome => outcome == 2),
          Is.EqualTo (clientCount - maxConcurrentHandshakes - maxPendingHandshakes)
        );

        foreach (var client in clients)
          client.Close ();

        WaitUntil (
          () => server.WebSocketServices["/echo"].Sessions.Count == 0,
          TimeSpan.FromSeconds (10),
          "The overload sessions were not released."
        );

        RoundTrip (port, "after-overload");

        server.Stop ();
        server.Start ();

        Assert.That (server.IsListening, Is.True);
        RoundTrip (port, "after-restart");

        TestContext.WriteLine (
          "HTTP handshake proof: clients={0}, concurrent={1}, pending={2}, opened={3}, rejected={4}, workerThreads={5}.",
          clientCount,
          maxConcurrentHandshakes,
          maxPendingHandshakes,
          outcomes.Count (outcome => outcome == 1),
          outcomes.Count (outcome => outcome == 2),
          overloadWorkerThreadCount
        );
      }
      finally {
        gate.Release ();

        foreach (var client in clients)
          ((IDisposable) client).Dispose ();

        server.Stop ();
      }
    }

    private static bool ContainsQueueFullWarning (List<LogData> logs)
    {
      lock (logs)
        return logs.Any (data => data.Message.Contains ("handshake queue is full"));
    }

    private static string GetUrl (int port)
    {
      return String.Format ("ws://127.0.0.1:{0}/echo", port);
    }

    private static void RoundTrip (int port, string payload)
    {
      using (var received = new ManualResetEventSlim ())
      using (var client = new WebSocket (GetUrl (port))) {
        string actual = null;
        Exception error = null;

        client.ConnectionTimeout = TimeSpan.FromSeconds (5);
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

        Assert.That (received.Wait (TimeSpan.FromSeconds (5)), Is.True);
        Assert.That (error, Is.Null);
        Assert.That (actual, Is.EqualTo (payload));

        client.Close ();
      }
    }

    private static void SetOutcome (
      int[] outcomes,
      int index,
      int outcome,
      CountdownEvent settled
    )
    {
      if (Interlocked.CompareExchange (ref outcomes[index], outcome, 0) == 0)
        settled.Signal ();
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

    internal sealed class HandshakeGate
    {
      private readonly ManualResetEventSlim _release = new ManualResetEventSlim ();
      private readonly object _sync = new object ();
      private readonly HashSet<int> _workerThreadIds = new HashSet<int> ();
      private int _activeCount;
      private int _maxActiveCount;

      internal int MaxActiveCount {
        get {
          lock (_sync)
            return _maxActiveCount;
        }
      }

      internal int ActiveCount {
        get {
          lock (_sync)
            return _activeCount;
        }
      }

      internal int WorkerThreadCount {
        get {
          lock (_sync)
            return _workerThreadIds.Count;
        }
      }

      internal void Enter ()
      {
        lock (_sync) {
          _activeCount++;
          _maxActiveCount = Math.Max (_maxActiveCount, _activeCount);
          _workerThreadIds.Add (Thread.CurrentThread.ManagedThreadId);
        }

        try {
          _release.Wait (TimeSpan.FromSeconds (10));
        }
        finally {
          lock (_sync)
            _activeCount--;
        }
      }

      internal void Release ()
      {
        _release.Set ();
      }
    }

    public sealed class BlockingEchoBehavior : WebSocketBehavior
    {
      internal void Configure (HandshakeGate gate)
      {
        UserHeadersResponder = (request, response) => gate.Enter ();
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
