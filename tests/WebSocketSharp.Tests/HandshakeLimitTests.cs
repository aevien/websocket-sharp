using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using NUnit.Framework;
using WebSocketSharp.Server;

namespace WebSocketSharp.Tests
{
  [TestFixture]
  [NonParallelizable]
  public sealed class HandshakeLimitTests
  {
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds (5);

    [Test]
    public void HandshakeWithMaximumHeaderCountStillOpens ()
    {
      using (var server = StartEchoServer ())
      using (var client = ConnectRawWebSocket (server.Port, "/echo", BuildExtraHeaders (59))) {
        Assert.That (client.Connected, Is.True);

        CloseRawWebSocket (client);
        WaitUntil (
          () => server.WebSocketServices["/echo"].Sessions.Count == 0,
          "The server kept a session after a max-header-count client closed."
        );
      }
    }

    [Test]
    public void HandshakeWithTooManyHeadersIsRejectedBeforeSessionStarts ()
    {
      using (var server = StartEchoServer ())
      using (var client = SendRawHandshake (server.Port, "/echo", BuildExtraHeaders (65))) {
        WaitUntil (
          () => IsDisconnected (client),
          "The server did not disconnect a handshake with too many headers."
        );

        Assert.That (server.WebSocketServices["/echo"].Sessions.Count, Is.EqualTo (0));
      }
    }

    [TestCase (true)]
    [TestCase (false)]
    public void HandshakeWithTooLongLineIsRejectedBeforeSessionStarts (bool requestLine)
    {
      using (var server = StartEchoServer ())
      using (var client = SendRawHandshakeWithLongLine (server.Port, requestLine)) {
        WaitUntil (
          () => IsDisconnected (client),
          "The server did not disconnect a handshake with a too-long line."
        );

        Assert.That (server.WebSocketServices["/echo"].Sessions.Count, Is.EqualTo (0));
      }
    }

    private static string BuildExtraHeaders (int count)
    {
      var headers = new StringBuilder ();

      for (var i = 0; i < count; i++)
        headers.AppendFormat ("X-Test-{0}: value\r\n", i);

      return headers.ToString ();
    }

    private static void CloseRawWebSocket (TcpClient client)
    {
      if (client == null || !client.Connected)
        return;

      try {
        var stream = client.GetStream ();
        var frame = new byte[] { 0x88, 0x82, 0x11, 0x22, 0x33, 0x44, 0x12, 0xca };

        stream.Write (frame, 0, frame.Length);
      }
      catch (IOException) {
      }
      catch (ObjectDisposedException) {
      }
    }

    private static TcpClient ConnectRawWebSocket (
      int port,
      string path,
      string extraHeaders
    )
    {
      var client = SendRawHandshake (port, path, extraHeaders);
      var response = ReadHttpResponseHeader (client.GetStream ());

      if (!response.StartsWith ("HTTP/1.1 101 ", StringComparison.Ordinal))
        Assert.Fail ("The raw WebSocket handshake did not receive a 101 response: " + response);

      return client;
    }

    private static string CreateWebSocketKey ()
    {
      var bytes = new byte[16];

      using (var rng = RandomNumberGenerator.Create ())
        rng.GetBytes (bytes);

      return Convert.ToBase64String (bytes);
    }

    private static bool IsDisconnected (TcpClient client)
    {
      try {
        return !client.Connected ||
               (client.Client.Poll (0, SelectMode.SelectRead) && client.Client.Available == 0);
      }
      catch (ObjectDisposedException) {
        return true;
      }
      catch (SocketException) {
        return true;
      }
    }

    private static string ReadHttpResponseHeader (NetworkStream stream)
    {
      var buffer = new byte[1];
      var data = new MemoryStream ();
      var previous = new byte[3];
      var deadline = DateTime.UtcNow.Add (Timeout);

      while (DateTime.UtcNow < deadline) {
        if (!stream.DataAvailable) {
          Thread.Sleep (10);
          continue;
        }

        var read = stream.Read (buffer, 0, 1);

        if (read <= 0)
          break;

        data.Write (buffer, 0, read);

        if (previous[0] == '\r' &&
            previous[1] == '\n' &&
            previous[2] == '\r' &&
            buffer[0] == '\n')
          return Encoding.ASCII.GetString (data.ToArray ());

        previous[0] = previous[1];
        previous[1] = previous[2];
        previous[2] = buffer[0];
      }

      Assert.Fail ("Timed out while reading the raw WebSocket handshake response.");
      return null;
    }

    private static TcpClient SendRawHandshake (
      int port,
      string path,
      string extraHeaders
    )
    {
      var request = String.Format (
        "GET {0} HTTP/1.1\r\nHost: 127.0.0.1:{1}\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Key: {2}\r\nSec-WebSocket-Version: 13\r\n{3}\r\n",
        path,
        port,
        CreateWebSocketKey (),
        extraHeaders ?? String.Empty
      );

      return SendRawRequest (port, request);
    }

    private static TcpClient SendRawHandshakeWithLongLine (
      int port,
      bool requestLine
    )
    {
      var longValue = new string ('a', 2050);
      var path = requestLine ? "/" + longValue : "/echo";
      var extraHeaders = requestLine
                         ? String.Empty
                         : "X-Long: " + longValue + "\r\n";

      return SendRawHandshake (port, path, extraHeaders);
    }

    private static LoopbackServer StartEchoServer ()
    {
      return LoopbackServer.Start (
        s => {
          s.Log.Output = (data, outputPath) => { };
          s.AddWebSocketService<EchoBehavior> ("/echo");
        }
      );
    }

    private static TcpClient SendRawRequest (int port, string request)
    {
      var client = new TcpClient ();

      client.Connect (IPAddress.Loopback, port);

      var stream = client.GetStream ();
      var bytes = Encoding.ASCII.GetBytes (request);

      stream.Write (bytes, 0, bytes.Length);

      return client;
    }

    private static void WaitUntil (Func<bool> predicate, string message)
    {
      var deadline = DateTime.UtcNow.Add (Timeout);

      while (DateTime.UtcNow < deadline) {
        if (predicate ())
          return;

        Thread.Sleep (10);
      }

      Assert.That (predicate (), Is.True, message);
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
