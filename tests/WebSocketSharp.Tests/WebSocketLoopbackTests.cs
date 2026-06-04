using System;
using System.Threading;
using NUnit.Framework;
using WebSocketSharp.Server;

namespace WebSocketSharp.Tests
{
  [TestFixture]
  [NonParallelizable]
  public sealed class WebSocketLoopbackTests
  {
    private const int StressIterations = 25;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds (5);

    [Test]
    public void EchoRoundTripsTextMessage ()
    {
      using (var server = LoopbackServer.Start (s => s.AddWebSocketService<EchoBehavior> ("/echo")))
      using (var client = new WebSocket (server.GetUrl ("/echo"))) {
        var opened = new ManualResetEventSlim ();
        var received = new ManualResetEventSlim ();
        var closed = new ManualResetEventSlim ();
        var closeArgs = default (CloseEventArgs);
        var actual = default (string);
        var wasText = false;
        var error = default (Exception);

        client.OnOpen += (sender, e) => opened.Set ();
        client.OnMessage += (sender, e) => {
          wasText = e.IsText;
          actual = e.Data;
          received.Set ();
        };
        client.OnError += (sender, e) => {
          error = e.Exception ?? new Exception (e.Message);
          received.Set ();
        };
        client.OnClose += (sender, e) => {
          closeArgs = e;
          closed.Set ();
        };

        client.Connect ();

        WaitFor (opened, "The client did not open.");
        AssertNoError (error);

        client.Send ("hello from tests");

        WaitFor (received, "The text echo was not received.");
        AssertNoError (error);
        Assert.That (wasText, Is.True);
        Assert.That (actual, Is.EqualTo ("hello from tests"));

        client.Close (CloseStatusCode.Normal, "done");

        WaitFor (closed, "The client did not close.");
        Assert.That (closeArgs.Code, Is.EqualTo ((ushort) CloseStatusCode.Normal));
      }
    }

    [Test]
    public void EchoRoundTripsBinaryMessage ()
    {
      using (var server = LoopbackServer.Start (s => s.AddWebSocketService<EchoBehavior> ("/echo")))
      using (var client = new WebSocket (server.GetUrl ("/echo"))) {
        var opened = new ManualResetEventSlim ();
        var received = new ManualResetEventSlim ();
        var expected = new byte[] { 1, 2, 3, 4, 5 };
        var actual = default (byte[]);
        var wasBinary = false;
        var error = default (Exception);

        client.OnOpen += (sender, e) => opened.Set ();
        client.OnMessage += (sender, e) => {
          wasBinary = e.IsBinary;
          actual = e.RawData;
          received.Set ();
        };
        client.OnError += (sender, e) => {
          error = e.Exception ?? new Exception (e.Message);
          received.Set ();
        };

        client.Connect ();

        WaitFor (opened, "The client did not open.");
        AssertNoError (error);

        client.Send (expected);

        WaitFor (received, "The binary echo was not received.");
        AssertNoError (error);
        Assert.That (wasBinary, Is.True);
        Assert.That (actual, Is.EqualTo (expected));

        client.Close ();
      }
    }

    [Test]
    public void PingReturnsTrueForOpenConnection ()
    {
      using (var server = LoopbackServer.Start (s => s.AddWebSocketService<EchoBehavior> ("/echo")))
      using (var client = new WebSocket (server.GetUrl ("/echo"))) {
        var opened = new ManualResetEventSlim ();
        var error = default (Exception);

        client.OnOpen += (sender, e) => opened.Set ();
        client.OnError += (sender, e) => error = e.Exception ?? new Exception (e.Message);

        client.Connect ();

        WaitFor (opened, "The client did not open.");
        AssertNoError (error);
        Assert.That (client.Ping ("health"), Is.True);

        client.Close ();
      }
    }

    [Test]
    public void AsyncClientApisOpenSendAndClose ()
    {
      using (var server = LoopbackServer.Start (s => s.AddWebSocketService<EchoBehavior> ("/echo")))
      using (var client = new WebSocket (server.GetUrl ("/echo"))) {
        var opened = new ManualResetEventSlim ();
        var sent = new ManualResetEventSlim ();
        var received = new ManualResetEventSlim ();
        var closed = new ManualResetEventSlim ();
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

        WaitFor (opened, "The async client did not open.");
        AssertNoError (error);

        client.SendAsync (
          "async hello",
          succeeded => {
            sendSucceeded = succeeded;
            sent.Set ();
          }
        );

        WaitFor (sent, "The async send callback was not called.");
        Assert.That (sendSucceeded, Is.True);
        WaitFor (received, "The async text echo was not received.");
        AssertNoError (error);
        Assert.That (actual, Is.EqualTo ("async hello"));

        client.CloseAsync ();

        WaitFor (closed, "The async client did not close.");
      }
    }

    [Test]
    public void AsyncClientApisCanRepeatWithoutStrandedSessions ()
    {
      using (var server = LoopbackServer.Start (s => s.AddWebSocketService<EchoBehavior> ("/echo"))) {
        var sessions = server.WebSocketServices["/echo"].Sessions;

        for (var i = 0; i < StressIterations; i++) {
          using (var client = new WebSocket (server.GetUrl ("/echo"))) {
            var opened = new ManualResetEventSlim ();
            var sent = new ManualResetEventSlim ();
            var received = new ManualResetEventSlim ();
            var closed = new ManualResetEventSlim ();
            var payload = String.Format ("stress-{0}", i);
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
      }
    }

    [Test]
    public void OriginValidatorRejectsUnexpectedOrigin ()
    {
      using (var server = LoopbackServer.Start (
        s => {
          s.Log.Output = (data, path) => { };
          s.AddWebSocketService<EchoBehavior> (
            "/origin",
            behavior => behavior.OriginValidator = origin => origin == "https://allowed.example"
          );
        }))
      using (var client = new WebSocket (server.GetUrl ("/origin"))) {
        var finished = new ManualResetEventSlim ();
        var opened = false;

        client.Log.Output = (data, path) => { };
        client.Origin = "https://blocked.example";
        client.OnOpen += (sender, e) => {
          opened = true;
          finished.Set ();
        };
        client.OnError += (sender, e) => finished.Set ();
        client.OnClose += (sender, e) => finished.Set ();

        client.Connect ();

        Assert.That (finished.Wait (Timeout) || client.ReadyState != WebSocketState.Connecting, Is.True);
        Assert.That (opened, Is.False);
        Assert.That (client.ReadyState, Is.Not.EqualTo (WebSocketState.Open));
      }
    }

    private static void AssertNoError (Exception error)
    {
      if (error != null)
        Assert.Fail (error.ToString ());
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
