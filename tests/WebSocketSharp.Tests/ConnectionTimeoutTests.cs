using System;
using System.Diagnostics;
using NUnit.Framework;

namespace WebSocketSharp.Tests
{
  [TestFixture]
  [NonParallelizable]
  public sealed class ConnectionTimeoutTests
  {
    [Test]
    public void ConnectionTimeoutCanBeConfiguredBeforeConnecting ()
    {
      using (var client = new WebSocket ("ws://127.0.0.1/")) {
        client.ConnectionTimeout = TimeSpan.FromMilliseconds (500);

        Assert.That (
          client.ConnectionTimeout,
          Is.EqualTo (TimeSpan.FromMilliseconds (500))
        );
      }
    }

    [Test]
    public void ConnectUsesConnectionTimeoutForSilentHandshake ()
    {
      using (var server = SilentTcpServer.Start ())
      using (var client = new WebSocket (server.GetUrl ("/silent"))) {
        var elapsed = Stopwatch.StartNew ();

        client.ConnectionTimeout = TimeSpan.FromMilliseconds (250);
        client.Log.Output = (data, path) => { };

        client.Connect ();

        elapsed.Stop ();

        Assert.That (client.ReadyState, Is.Not.EqualTo (WebSocketState.Open));
        Assert.That (elapsed.Elapsed, Is.LessThan (TimeSpan.FromSeconds (3)));
      }
    }
  }
}
