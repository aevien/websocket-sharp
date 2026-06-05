using System;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
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

    public WebSocketServiceManager WebSocketServices {
      get {
        return _server.WebSocketServices;
      }
    }

    public static LoopbackServer Start (Action<WebSocketServer> configure)
    {
      return Start (false, null, configure);
    }

    public static LoopbackServer StartSecure (
      X509Certificate2 serverCertificate,
      Action<WebSocketServer> configure
    )
    {
      if (serverCertificate == null)
        throw new ArgumentNullException ("serverCertificate");

      return Start (true, serverCertificate, configure);
    }

    private static LoopbackServer Start (
      bool secure,
      X509Certificate2 serverCertificate,
      Action<WebSocketServer> configure
    )
    {
      var port = GetFreeTcpPort ();
      var server = new WebSocketServer (IPAddress.Loopback, port, secure);

      if (secure)
        server.SslConfiguration.ServerCertificate = serverCertificate;

      configure (server);
      server.Start ();

      Assert.That (server.IsListening, Is.True, "The loopback WebSocket server did not start.");

      return new LoopbackServer (server, port);
    }

    public string GetUrl (string path)
    {
      return String.Format ("ws://127.0.0.1:{0}{1}", Port, path);
    }

    public string GetSecureUrl (string path)
    {
      return String.Format ("wss://127.0.0.1:{0}{1}", Port, path);
    }

    public void Dispose ()
    {
      _server.Stop ();
    }

    internal static int GetFreeTcpPort ()
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
