using System;
using System.Diagnostics;
using System.Threading;
using NUnit.Framework;
using WebSocketSharp.Server;

namespace WebSocketSharp.StressTests
{
  [TestFixture]
  [NonParallelizable]
  [Category ("Stress")]
  public sealed class AsyncLifecycleLongStressTests
  {
    private const int DefaultCycles = 500;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds (5);

    [Test]
    public void AsyncLifecycleCanRepeatManyTimesWithoutStrandedSessions ()
    {
      var cycles = GetCycleCount ();
      var elapsed = Stopwatch.StartNew ();

      using (var server = StressLoopbackServer.Start (s => s.AddWebSocketService<EchoBehavior> ("/echo"))) {
        var sessions = server.WebSocketServices["/echo"].Sessions;

        for (var i = 0; i < cycles; i++) {
          using (var client = new WebSocket (server.GetUrl ("/echo")))
          using (var opened = new ManualResetEventSlim ())
          using (var sent = new ManualResetEventSlim ())
          using (var received = new ManualResetEventSlim ())
          using (var closed = new ManualResetEventSlim ()) {
            var payload = String.Format ("long-stress-{0}", i);
            var actual = default (string);
            var sendSucceeded = false;
            var error = default (Exception);

            client.OnOpen += (sender, e) => opened.Set ();
            client.OnMessage += (sender, e) => {
              actual = e.Data;
              received.Set ();
            };
            client.OnError += (sender, e) => {
              error = e.Exception ?? new Exception (e.Message);
              received.Set ();
            };
            client.OnClose += (sender, e) => closed.Set ();

            client.ConnectAsync ();

            WaitFor (opened, "The stress client did not open.");
            AssertNoError (error);
            Assert.That (client.ReadyState, Is.EqualTo (WebSocketState.Open));

            client.SendAsync (
              payload,
              succeeded => {
                sendSucceeded = succeeded;
                sent.Set ();
              }
            );

            WaitFor (sent, "The stress async send callback was not called.");
            Assert.That (sendSucceeded, Is.True);
            WaitFor (received, "The stress text echo was not received.");
            AssertNoError (error);
            Assert.That (actual, Is.EqualTo (payload));

            client.CloseAsync ();

            WaitFor (closed, "The stress client did not close.");
          }

          WaitUntil (
            () => sessions.Count == 0,
            "The stress server kept a session after the client closed."
          );
        }

        Assert.That (sessions.Count, Is.EqualTo (0));
      }

      elapsed.Stop ();
      TestContext.WriteLine ("Completed {0} async lifecycle cycles in {1}.", cycles, elapsed.Elapsed);
    }

    private static void AssertNoError (Exception error)
    {
      if (error != null)
        Assert.Fail (error.ToString ());
    }

    private static int GetCycleCount ()
    {
      var value = Environment.GetEnvironmentVariable ("WEBSOCKET_SHARP_STRESS_CYCLES");
      int cycles;

      if (!Int32.TryParse (value, out cycles) || cycles < 1)
        return DefaultCycles;

      return cycles;
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
