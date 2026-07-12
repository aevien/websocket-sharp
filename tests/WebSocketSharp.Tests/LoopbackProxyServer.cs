using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace WebSocketSharp.Tests
{
  internal sealed class LoopbackProxyServer : IDisposable
  {
    private enum ProxyMode
    {
      Silent,
      Reject,
      Tunnel,
      BasicAuthTunnel,
      BasicAuthChunkedTunnel
    }

    private readonly object _sync = new object ();
    private readonly List<TcpClient> _clients;
    private readonly TcpListener _listener;
    private readonly ProxyMode _mode;
    private readonly Thread _thread;
    private volatile bool _disposed;

    private LoopbackProxyServer (ProxyMode mode)
    {
      _mode = mode;
      _clients = new List<TcpClient> ();
      _listener = new TcpListener (IPAddress.Loopback, 0);
      _listener.Start ();

      Port = ((IPEndPoint) _listener.LocalEndpoint).Port;
      _thread = new Thread (AcceptLoop);
      _thread.IsBackground = true;
      _thread.Start ();
    }

    public int AuthorizedConnectCount { get; private set; }

    public int ConnectionCount { get; private set; }

    public int ConnectRequestCount { get; private set; }

    public string LastProxyAuthorization { get; private set; }

    public int Port { get; private set; }

    public int TunnelCount { get; private set; }

    public static LoopbackProxyServer StartBasicAuthTunnel ()
    {
      return new LoopbackProxyServer (ProxyMode.BasicAuthTunnel);
    }

    public static LoopbackProxyServer StartBasicAuthChunkedTunnel ()
    {
      return new LoopbackProxyServer (ProxyMode.BasicAuthChunkedTunnel);
    }

    public static LoopbackProxyServer StartRejecting ()
    {
      return new LoopbackProxyServer (ProxyMode.Reject);
    }

    public static LoopbackProxyServer StartSilent ()
    {
      return new LoopbackProxyServer (ProxyMode.Silent);
    }

    public static LoopbackProxyServer StartTunnel ()
    {
      return new LoopbackProxyServer (ProxyMode.Tunnel);
    }

    public void Dispose ()
    {
      _disposed = true;

      try {
        _listener.Stop ();
      }
      catch {
      }

      lock (_sync) {
        foreach (var client in _clients)
          client.Close ();

        _clients.Clear ();
      }

      _thread.Join (1000);
    }

    public string GetUrl ()
    {
      return String.Format ("http://127.0.0.1:{0}", Port);
    }

    private void AcceptLoop ()
    {
      while (!_disposed) {
        TcpClient client = null;

        try {
          client = _listener.AcceptTcpClient ();

          lock (_sync) {
            _clients.Add (client);
            ConnectionCount++;
          }

          var accepted = client;
          var thread = new Thread (() => HandleClient (accepted));
          thread.IsBackground = true;
          thread.Start ();
        }
        catch (SocketException) {
          if (!_disposed && client != null)
            client.Close ();
        }
        catch (ObjectDisposedException) {
        }
      }
    }

    private static void CopyTunnel (
      Stream input,
      Stream output,
      TcpClient first,
      TcpClient second
    )
    {
      try {
        input.CopyTo (output, 8192);
      }
      catch {
      }
      finally {
        first.Close ();
        second.Close ();
      }
    }

    private void HandleClient (TcpClient client)
    {
      if (_mode == ProxyMode.Silent) {
        while (!_disposed && client.Connected)
          Thread.Sleep (10);

        return;
      }

      try {
        client.ReceiveTimeout = 5000;
        client.SendTimeout = 5000;

        var stream = client.GetStream ();
        var request = ReadHeader (stream);

        lock (_sync) {
          ConnectRequestCount++;
          LastProxyAuthorization = GetHeaderValue (request, "Proxy-Authorization");

          if (!String.IsNullOrEmpty (LastProxyAuthorization))
            AuthorizedConnectCount++;
        }

        if (_mode == ProxyMode.Reject) {
          WriteResponse (stream, "HTTP/1.1 502 Bad Gateway", "Connection: close");
          client.Close ();

          return;
        }

        if ((_mode == ProxyMode.BasicAuthTunnel
             || _mode == ProxyMode.BasicAuthChunkedTunnel)
            && String.IsNullOrEmpty (LastProxyAuthorization)) {
          if (_mode == ProxyMode.BasicAuthChunkedTunnel) {
            WriteResponse (
              stream,
              "HTTP/1.1 407 Proxy Authentication Required",
              "Proxy-Authenticate: Basic realm=\"loopback\"",
              "Transfer-Encoding: chunked"
            );

            var body = Encoding.ASCII.GetBytes ("5\r\nerror\r\n0\r\n\r\n");

            try {
              stream.Write (body, 0, body.Length);
              stream.Flush ();
            }
            catch {
            }

            while (!_disposed && !IsDisconnected (client))
              Thread.Sleep (10);

            client.Close ();
            return;
          }

          WriteResponse (
            stream,
            "HTTP/1.1 407 Proxy Authentication Required",
            "Proxy-Authenticate: Basic realm=\"loopback\"",
            "Connection: close"
          );

          client.Close ();

          return;
        }

        OpenTunnel (client, request);
      }
      catch {
        client.Close ();
      }
    }

    private static bool IsDisconnected (TcpClient client)
    {
      try {
        return client.Client.Poll (0, SelectMode.SelectRead)
               && client.Client.Available == 0;
      }
      catch {
        return true;
      }
    }

    private static string GetHeaderValue (string request, string name)
    {
      var prefix = name + ":";
      var lines = request.Split (
                    new[] { "\r\n" },
                    StringSplitOptions.RemoveEmptyEntries
                  );

      foreach (var line in lines) {
        if (!line.StartsWith (prefix, StringComparison.OrdinalIgnoreCase))
          continue;

        return line.Substring (prefix.Length).Trim ();
      }

      return null;
    }

    private static void ParseConnectTarget (
      string request,
      out string host,
      out int port
    )
    {
      var requestLine = request.Split (new[] { "\r\n" }, StringSplitOptions.None)[0];
      var parts = requestLine.Split (new[] { ' ' }, 3);

      if (parts.Length != 3 || parts[0] != "CONNECT")
        throw new InvalidOperationException ("The proxy request is not CONNECT.");

      var authority = parts[1];
      var separator = authority.LastIndexOf (':');

      if (separator < 1)
        throw new InvalidOperationException ("The CONNECT target has no port.");

      host = authority.Substring (0, separator);
      port = Int32.Parse (authority.Substring (separator + 1));
    }

    private void OpenTunnel (TcpClient client, string request)
    {
      string host;
      int port;

      ParseConnectTarget (request, out host, out port);

      var upstream = new TcpClient ();

      upstream.Connect (host, port);

      lock (_sync) {
        _clients.Add (upstream);
        TunnelCount++;
      }

      var clientStream = client.GetStream ();
      var upstreamStream = upstream.GetStream ();

      WriteResponse (clientStream, "HTTP/1.1 200 Connection Established");

      var toUpstream =
        new Thread (() => CopyTunnel (clientStream, upstreamStream, client, upstream));
      var toClient =
        new Thread (() => CopyTunnel (upstreamStream, clientStream, upstream, client));

      toUpstream.IsBackground = true;
      toClient.IsBackground = true;
      toUpstream.Start ();
      toClient.Start ();
    }

    private static string ReadHeader (Stream stream)
    {
      var data = new List<byte> ();
      var previous = new Queue<byte> (4);

      while (true) {
        var value = stream.ReadByte ();

        if (value < 0)
          throw new EndOfStreamException ();

        var b = (byte) value;

        data.Add (b);
        previous.Enqueue (b);

        if (previous.Count > 4)
          previous.Dequeue ();

        if (previous.Count == 4) {
          var end = previous.ToArray ();

          if (end[0] == '\r'
              && end[1] == '\n'
              && end[2] == '\r'
              && end[3] == '\n')
            return Encoding.ASCII.GetString (data.ToArray ());
        }

        if (data.Count > 8192)
          throw new InvalidOperationException ("The proxy request header is too large.");
      }
    }

    private static void WriteResponse (
      Stream stream,
      string statusLine,
      params string[] headers
    )
    {
      var response = new StringBuilder ();

      response.Append (statusLine).Append ("\r\n");

      foreach (var header in headers)
        response.Append (header).Append ("\r\n");

      response.Append ("\r\n");

      var data = Encoding.ASCII.GetBytes (response.ToString ());

      stream.Write (data, 0, data.Length);
      stream.Flush ();
    }
  }
}
