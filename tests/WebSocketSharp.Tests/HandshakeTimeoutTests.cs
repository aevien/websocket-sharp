using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using NUnit.Framework;
using WebSocketSharp.Server;

namespace WebSocketSharp.Tests
{
  [TestFixture]
  [NonParallelizable]
  public sealed class HandshakeTimeoutTests
  {
    [Test]
    public void ServerHandshakeTimeoutCanBeConfiguredBeforeStarting ()
    {
      var timeout = TimeSpan.FromMilliseconds (500);
      var webSocketServer = new WebSocketServer (IPAddress.Loopback, 1);
      var httpServer = new HttpServer (IPAddress.Loopback, 1);

      webSocketServer.HandshakeTimeout = timeout;
      httpServer.HandshakeTimeout = timeout;

      Assert.That (webSocketServer.HandshakeTimeout, Is.EqualTo (timeout));
      Assert.That (httpServer.HandshakeTimeout, Is.EqualTo (timeout));
      Assert.Throws<ArgumentOutOfRangeException> (
        () => webSocketServer.HandshakeTimeout = TimeSpan.Zero
      );
      Assert.Throws<ArgumentOutOfRangeException> (
        () => httpServer.HandshakeTimeout = TimeSpan.Zero
      );
    }

    [Test]
    public void SilentTcpHandshakeUsesServerHandshakeTimeout ()
    {
      using (
        var server = LoopbackServer.Start (
          s => {
            s.HandshakeTimeout = TimeSpan.FromMilliseconds (250);
            s.Log.Output = (data, path) => { };
            s.AddWebSocketService<EchoBehavior> ("/echo");
          }
        )
      )
      using (var client = new TcpClient ()) {
        var elapsed = Stopwatch.StartNew ();

        client.Connect (IPAddress.Loopback, server.Port);

        WaitUntil (
          () => IsDisconnected (client),
          TimeSpan.FromSeconds (3),
          "The server did not disconnect a silent handshake client."
        );

        elapsed.Stop ();

        Assert.That (elapsed.Elapsed, Is.LessThan (TimeSpan.FromSeconds (3)));
        Assert.That (server.WebSocketServices["/echo"].Sessions.Count, Is.EqualTo (0));
      }
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
    }
  }
}
