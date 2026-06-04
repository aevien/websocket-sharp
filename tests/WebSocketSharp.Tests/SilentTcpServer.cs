using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace WebSocketSharp.Tests
{
  internal sealed class SilentTcpServer : IDisposable
  {
    private readonly TcpListener _listener;
    private readonly Thread _thread;
    private volatile bool _disposed;
    private TcpClient _client;

    private SilentTcpServer (TcpListener listener)
    {
      _listener = listener;
      Port = ((IPEndPoint) listener.LocalEndpoint).Port;
      _thread = new Thread (AcceptOne);
      _thread.IsBackground = true;
      _thread.Start ();
    }

    public int Port { get; private set; }

    public static SilentTcpServer Start ()
    {
      var listener = new TcpListener (IPAddress.Loopback, 0);

      listener.Start ();

      return new SilentTcpServer (listener);
    }

    public string GetUrl (string path)
    {
      return String.Format ("ws://127.0.0.1:{0}{1}", Port, path);
    }

    public void Dispose ()
    {
      _disposed = true;

      _listener.Stop ();

      if (_client != null)
        _client.Close ();

      _thread.Join (1000);
    }

    private void AcceptOne ()
    {
      try {
        _client = _listener.AcceptTcpClient ();

        while (!_disposed)
          Thread.Sleep (10);
      }
      catch (SocketException) {
      }
      catch (ObjectDisposedException) {
      }
    }
  }
}
