using System;
using System.Net;
using System.Net.Sockets;
using NUnit.Framework;
using WebSocketSharp.Server;

namespace WebSocketSharp.Tests
{
  internal sealed class LoopbackServer : IDisposable
  {
    private readonly WebSocketServer _server;

    private LoopbackServer (WebSocketServer server, int port)
    {
      _server = server;
      Port = port;
    }

    public int Port { get; private set; }

    public static LoopbackServer Start (Action<WebSocketServer> configure)
    {
      var port = GetFreeTcpPort ();
      var server = new WebSocketServer (IPAddress.Loopback, port);

      configure (server);
      server.Start ();

      Assert.That (server.IsListening, Is.True, "The loopback WebSocket server did not start.");

      return new LoopbackServer (server, port);
    }

    public string GetUrl (string path)
    {
      return String.Format ("ws://127.0.0.1:{0}{1}", Port, path);
    }

    public void Dispose ()
    {
      _server.Stop ();
    }

    private static int GetFreeTcpPort ()
    {
      var listener = new TcpListener (IPAddress.Loopback, 0);
      listener.Start ();

      try {
        return ((IPEndPoint) listener.LocalEndpoint).Port;
      }
      finally {
        listener.Stop ();
      }
    }
  }
}
