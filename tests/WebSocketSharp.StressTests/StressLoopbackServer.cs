using System;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using NUnit.Framework;
using WebSocketSharp.Server;

namespace WebSocketSharp.StressTests
{
  internal sealed class StressLoopbackServer : IDisposable
  {
    private readonly WebSocketServer _server;

    private StressLoopbackServer (WebSocketServer server, int port)
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

    public static StressLoopbackServer Start (Action<WebSocketServer> configure)
    {
      return Start (false, null, configure);
    }

    public static StressLoopbackServer StartSecure (
      X509Certificate2 serverCertificate,
      Action<WebSocketServer> configure
    )
    {
      if (serverCertificate == null)
        throw new ArgumentNullException ("serverCertificate");

      return Start (true, serverCertificate, configure);
    }

    private static StressLoopbackServer Start (
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

      return new StressLoopbackServer (server, port);
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
