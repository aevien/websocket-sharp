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
