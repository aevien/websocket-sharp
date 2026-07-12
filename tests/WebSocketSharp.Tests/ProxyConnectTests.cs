using System;
using System.Diagnostics;
using System.Threading;
using NUnit.Framework;

namespace WebSocketSharp.Tests
{
  [TestFixture]
  [NonParallelizable]
  public sealed class ProxyConnectTests
  {
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds (5);

    [Test]
    public void ConnectThroughHttpProxyTunnelEchoes ()
    {
      using (var server = LoopbackServer.Start (s => s.AddWebSocketService<WebSocketLoopbackTests.EchoBehavior> ("/echo")))
      using (var proxy = LoopbackProxyServer.StartTunnel ())
      using (var client = new WebSocket (server.GetUrl ("/echo"))) {
        client.SetProxy (proxy.GetUrl (), null, null);

        RoundTrip (client, "proxied hello");

        Assert.That (proxy.ConnectRequestCount, Is.EqualTo (1));
        Assert.That (proxy.TunnelCount, Is.EqualTo (1));
      }
    }

    [Test]
    public void ProxyAuthenticationRequiredWithoutCredentialsDoesNotOpen ()
    {
      using (var proxy = LoopbackProxyServer.StartBasicAuthTunnel ())
      using (var client = new WebSocket ("ws://127.0.0.1:1/echo")) {
        client.ConnectionTimeout = TimeSpan.FromMilliseconds (500);
        client.Log.Output = (data, path) => { };
        client.SetProxy (proxy.GetUrl (), null, null);

        client.Connect ();

        Assert.That (client.ReadyState, Is.Not.EqualTo (WebSocketState.Open));
        Assert.That (proxy.ConnectRequestCount, Is.EqualTo (1));
        Assert.That (proxy.TunnelCount, Is.EqualTo (0));
      }
    }

    [Test]
    public void ProxyBasicAuthenticationReconnectsAfterClosedChallenge ()
    {
      using (var server = LoopbackServer.Start (s => s.AddWebSocketService<WebSocketLoopbackTests.EchoBehavior> ("/echo")))
      using (var proxy = LoopbackProxyServer.StartBasicAuthTunnel ())
      using (var client = new WebSocket (server.GetUrl ("/echo"))) {
        client.SetProxy (proxy.GetUrl (), "user", "pass");

        RoundTrip (client, "authenticated proxy hello");

        Assert.That (proxy.ConnectionCount, Is.GreaterThanOrEqualTo (2));
        Assert.That (proxy.ConnectRequestCount, Is.EqualTo (2));
        Assert.That (proxy.AuthorizedConnectCount, Is.EqualTo (1));
        Assert.That (proxy.LastProxyAuthorization, Does.StartWith ("Basic "));
        Assert.That (proxy.TunnelCount, Is.EqualTo (1));
      }
    }

    [Test]
    public void ProxyBasicAuthenticationReconnectsAfterChunkedChallenge ()
    {
      using (var server = LoopbackServer.Start (s => s.AddWebSocketService<WebSocketLoopbackTests.EchoBehavior> ("/echo")))
      using (var proxy = LoopbackProxyServer.StartBasicAuthChunkedTunnel ())
      using (var client = new WebSocket (server.GetUrl ("/echo"))) {
        client.SetProxy (proxy.GetUrl (), "user", "pass");

        RoundTrip (client, "chunked authenticated proxy hello");

        Assert.That (proxy.ConnectionCount, Is.GreaterThanOrEqualTo (2));
        Assert.That (proxy.ConnectRequestCount, Is.EqualTo (2));
        Assert.That (proxy.AuthorizedConnectCount, Is.EqualTo (1));
        Assert.That (proxy.LastProxyAuthorization, Does.StartWith ("Basic "));
        Assert.That (proxy.TunnelCount, Is.EqualTo (1));
      }
    }

    [Test]
    public void ProxyConnectFailureDoesNotOpen ()
    {
      using (var proxy = LoopbackProxyServer.StartRejecting ())
      using (var client = new WebSocket ("ws://127.0.0.1:1/echo")) {
        client.ConnectionTimeout = TimeSpan.FromMilliseconds (500);
        client.Log.Output = (data, path) => { };
        client.SetProxy (proxy.GetUrl (), null, null);

        client.Connect ();

        Assert.That (client.ReadyState, Is.Not.EqualTo (WebSocketState.Open));
        Assert.That (proxy.ConnectRequestCount, Is.EqualTo (1));
        Assert.That (proxy.TunnelCount, Is.EqualTo (0));
      }
    }

    [Test]
    public void SilentProxyConnectUsesConnectionTimeout ()
    {
      using (var proxy = LoopbackProxyServer.StartSilent ())
      using (var client = new WebSocket ("ws://127.0.0.1:1/echo")) {
        var elapsed = Stopwatch.StartNew ();

        client.ConnectionTimeout = TimeSpan.FromMilliseconds (250);
        client.Log.Output = (data, path) => { };
        client.SetProxy (proxy.GetUrl (), null, null);

        client.Connect ();

        elapsed.Stop ();

        Assert.That (client.ReadyState, Is.Not.EqualTo (WebSocketState.Open));
        Assert.That (elapsed.Elapsed, Is.LessThan (TimeSpan.FromSeconds (3)));
        Assert.That (proxy.ConnectionCount, Is.EqualTo (1));
        Assert.That (proxy.TunnelCount, Is.EqualTo (0));
      }
    }

    private static void RoundTrip (WebSocket client, string payload)
    {
      using (var opened = new ManualResetEventSlim ())
      using (var received = new ManualResetEventSlim ())
      using (var closed = new ManualResetEventSlim ()) {
        var actual = default (string);
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

        client.Connect ();

        if (!opened.Wait (Timeout)) {
          AssertNoError (error);
          Assert.Fail ("The proxied client did not open.");
        }

        AssertNoError (error);

        client.Send (payload);

        WaitFor (received, "The proxied echo was not received.");
        AssertNoError (error);
        Assert.That (actual, Is.EqualTo (payload));

        client.Close ();

        WaitFor (closed, "The proxied client did not close.");
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
  }
}
