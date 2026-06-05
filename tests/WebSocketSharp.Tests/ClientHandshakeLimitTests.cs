using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using NUnit.Framework;

namespace WebSocketSharp.Tests
{
  [TestFixture]
  [NonParallelizable]
  public sealed class ClientHandshakeLimitTests
  {
    [Test]
    public void ConnectRejectsResponseWithTooManyHeadersWithoutOpening ()
    {
      var response = "HTTP/1.1 101 Switching Protocols\r\n"
                     + "Upgrade: websocket\r\n"
                     + "Connection: Upgrade\r\n"
                     + "Sec-WebSocket-Accept: invalid\r\n"
                     + BuildExtraHeaders (65)
                     + "\r\n";

      AssertRejectedHandshakeResponse (response);
    }

    [TestCase (true)]
    [TestCase (false)]
    public void ConnectRejectsResponseWithTooLongLineWithoutOpening (bool statusLine)
    {
      var longValue = new string ('a', 2050);
      var response = statusLine
                     ? "HTTP/1.1 101 " + longValue + "\r\n"
                       + "Upgrade: websocket\r\n"
                       + "Connection: Upgrade\r\n"
                       + "Sec-WebSocket-Accept: invalid\r\n"
                       + "\r\n"
                     : "HTTP/1.1 101 Switching Protocols\r\n"
                       + "Upgrade: websocket\r\n"
                       + "Connection: Upgrade\r\n"
                       + "Sec-WebSocket-Accept: invalid\r\n"
                       + "X-Long: " + longValue + "\r\n"
                       + "\r\n";

      AssertRejectedHandshakeResponse (response);
    }

    [Test]
    public void ConnectRejectsInvalidResponseLineWithoutOpening ()
    {
      AssertRejectedHandshakeResponse ("NOTHTTP\r\n\r\n");
    }

    private static void AssertRejectedHandshakeResponse (string response)
    {
      using (var server = RawResponseServer.Start (response))
      using (var client = new WebSocket (server.GetUrl ("/echo"))) {
        var elapsed = Stopwatch.StartNew ();

        client.ConnectionTimeout = TimeSpan.FromSeconds (2);
        client.Log.Output = (data, path) => { };

        client.Connect ();

        elapsed.Stop ();

        Assert.That (client.ReadyState, Is.Not.EqualTo (WebSocketState.Open));
        Assert.That (elapsed.Elapsed, Is.LessThan (TimeSpan.FromSeconds (3)));
        Assert.That (server.Accepted, Is.True);
      }
    }

    private static string BuildExtraHeaders (int count)
    {
      var headers = new StringBuilder ();

      for (var i = 0; i < count; i++)
        headers.AppendFormat ("X-Client-Test-{0}: value\r\n", i);

      return headers.ToString ();
    }

    private sealed class RawResponseServer : IDisposable
    {
      private readonly TcpListener _listener;
      private readonly string _response;
      private readonly Thread _thread;
      private volatile bool _disposed;
      private TcpClient _client;

      private RawResponseServer (TcpListener listener, string response)
      {
        _listener = listener;
        _response = response;
        Port = ((IPEndPoint) listener.LocalEndpoint).Port;
        _thread = new Thread (AcceptOne);
        _thread.IsBackground = true;
        _thread.Start ();
      }

      public bool Accepted { get; private set; }

      public int Port { get; private set; }

      public static RawResponseServer Start (string response)
      {
        var listener = new TcpListener (IPAddress.Loopback, 0);

        listener.Start ();

        return new RawResponseServer (listener, response);
      }

      public void Dispose ()
      {
        _disposed = true;

        _listener.Stop ();

        if (_client != null)
          _client.Close ();

        _thread.Join (1000);
      }

      public string GetUrl (string path)
      {
        return String.Format ("ws://127.0.0.1:{0}{1}", Port, path);
      }

      private void AcceptOne ()
      {
        try {
          _client = _listener.AcceptTcpClient ();
          Accepted = true;

          _client.ReceiveTimeout = 1000;

          DrainRequest (_client.GetStream ());

          if (_disposed)
            return;

          var bytes = Encoding.ASCII.GetBytes (_response);

          _client.GetStream ().Write (bytes, 0, bytes.Length);
          _client.GetStream ().Flush ();
        }
        catch (IOException) {
        }
        catch (SocketException) {
        }
        catch (ObjectDisposedException) {
        }
      }

      private static void DrainRequest (NetworkStream stream)
      {
        var buffer = new byte[1];
        var previous = new byte[3];
        var deadline = DateTime.UtcNow.AddSeconds (2);

        while (DateTime.UtcNow < deadline) {
          var read = stream.Read (buffer, 0, 1);

          if (read <= 0)
            return;

          if (previous[0] == '\r' &&
              previous[1] == '\n' &&
              previous[2] == '\r' &&
              buffer[0] == '\n')
            return;

          previous[0] = previous[1];
          previous[1] = previous[2];
          previous[2] = buffer[0];
        }
      }
    }
  }
}
