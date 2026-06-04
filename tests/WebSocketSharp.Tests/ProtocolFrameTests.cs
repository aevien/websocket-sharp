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
  public sealed class ProtocolFrameTests
  {
    private const int OpcodeContinuation = 0x0;
    private const int OpcodeText = 0x1;
    private const int OpcodeClose = 0x8;
    private const int OpcodePing = 0x9;
    private const int OpcodePong = 0xa;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds (5);

    [TestCase (125)]
    [TestCase (126)]
    [TestCase (66000)]
    public void TextPayloadLengthBoundariesRoundTrip (int payloadLength)
    {
      using (var server = LoopbackServer.Start (s => s.AddWebSocketService<EchoBehavior> ("/echo")))
      using (var client = ConnectRawWebSocket (server.Port, "/echo")) {
        var expected = new string ('a', payloadLength);

        WriteClientFrame (client.GetStream (), OpcodeText, Encoding.UTF8.GetBytes (expected), true, true);

        var actual = ReadServerMessage (client.GetStream ());

        Assert.That (actual.Opcode, Is.EqualTo (OpcodeText));
        Assert.That (Encoding.UTF8.GetString (actual.Payload), Is.EqualTo (expected));

        CloseRawWebSocket (client);
        WaitUntil (
          () => server.WebSocketServices["/echo"].Sessions.Count == 0,
          "The server kept a session after a boundary payload client closed."
        );
      }
    }

    [Test]
    public void FragmentedTextCanReceiveInterleavedPingAndThenRoundTrip ()
    {
      using (var server = LoopbackServer.Start (s => s.AddWebSocketService<EchoBehavior> ("/echo")))
      using (var client = ConnectRawWebSocket (server.Port, "/echo")) {
        var stream = client.GetStream ();

        WriteClientFrame (stream, OpcodeText, Encoding.UTF8.GetBytes ("hel"), false, true);
        WriteClientFrame (stream, OpcodePing, Encoding.UTF8.GetBytes ("q"), true, true);

        var pong = ReadServerFrame (stream);

        Assert.That (pong.Opcode, Is.EqualTo (OpcodePong));
        Assert.That (Encoding.UTF8.GetString (pong.Payload), Is.EqualTo ("q"));

        WriteClientFrame (stream, OpcodeContinuation, Encoding.UTF8.GetBytes ("lo"), true, true);

        var actual = ReadServerMessage (stream);

        Assert.That (actual.Opcode, Is.EqualTo (OpcodeText));
        Assert.That (Encoding.UTF8.GetString (actual.Payload), Is.EqualTo ("hello"));

        CloseRawWebSocket (client);
        WaitUntil (
          () => server.WebSocketServices["/echo"].Sessions.Count == 0,
          "The server kept a session after fragmented text client closed."
        );
      }
    }

    [Test]
    public void UnmaskedClientFrameClosesConnectionWithoutDeliveringMessage ()
    {
      using (var server = StartCountingServer ("/count"))
      using (var client = ConnectRawWebSocket (server.Port, "/count")) {
        WriteClientFrame (client.GetStream (), OpcodeText, Encoding.UTF8.GetBytes ("bad"), true, false);

        WaitForProtocolClose (server, "/count", client);

        Assert.That (CountingBehavior.MessageCount, Is.EqualTo (0));
      }
    }

    [Test]
    public void FragmentedControlFrameClosesConnectionWithoutDeliveringMessage ()
    {
      using (var server = StartCountingServer ("/count"))
      using (var client = ConnectRawWebSocket (server.Port, "/count")) {
        WriteClientFrame (client.GetStream (), OpcodePing, Encoding.UTF8.GetBytes ("bad"), false, true);

        WaitForProtocolClose (server, "/count", client);

        Assert.That (CountingBehavior.MessageCount, Is.EqualTo (0));
      }
    }

    [Test]
    public void UnexpectedContinuationFrameClosesConnectionWithoutDeliveringMessage ()
    {
      using (var server = StartCountingServer ("/count"))
      using (var client = ConnectRawWebSocket (server.Port, "/count")) {
        WriteClientFrame (client.GetStream (), OpcodeContinuation, Encoding.UTF8.GetBytes ("bad"), true, true);

        WaitForProtocolClose (server, "/count", client);

        Assert.That (CountingBehavior.MessageCount, Is.EqualTo (0));
      }
    }

    [Test]
    public void InvalidUtf8TextFrameClosesConnectionWithoutDeliveringMessage ()
    {
      using (var server = StartCountingServer ("/count"))
      using (var client = ConnectRawWebSocket (server.Port, "/count")) {
        WriteClientFrame (client.GetStream (), OpcodeText, new byte[] { 0xc3, 0x28 }, true, true);

        WaitForProtocolClose (server, "/count", client);

        Assert.That (CountingBehavior.MessageCount, Is.EqualTo (0));
      }
    }

    private static void CloseRawWebSocket (TcpClient client)
    {
      if (client == null || !client.Connected)
        return;

      try {
        WriteClientFrame (client.GetStream (), OpcodeClose, new byte[] { 0x03, 0xe8 }, true, true);
      }
      catch (IOException) {
      }
      catch (ObjectDisposedException) {
      }
    }

    private static TcpClient ConnectRawWebSocket (int port, string path)
    {
      var client = new TcpClient ();
      var key = CreateWebSocketKey ();

      client.Connect (IPAddress.Loopback, port);

      var request = String.Format (
        "GET {0} HTTP/1.1\r\nHost: 127.0.0.1:{1}\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Key: {2}\r\nSec-WebSocket-Version: 13\r\n\r\n",
        path,
        port,
        key
      );
      var stream = client.GetStream ();
      var bytes = Encoding.ASCII.GetBytes (request);

      stream.Write (bytes, 0, bytes.Length);

      var response = ReadHttpResponseHeader (stream);

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

    private static FrameData ReadServerFrame (NetworkStream stream)
    {
      var header = ReadExactly (stream, 2);
      var opcode = header[0] & 0x0f;
      var fin = (header[0] & 0x80) == 0x80;
      var masked = (header[1] & 0x80) == 0x80;
      var payloadLength = (ulong) (header[1] & 0x7f);

      if (payloadLength == 126)
        payloadLength = ReadExactly (stream, 2).ToUInt16BigEndian ();
      else if (payloadLength == 127)
        payloadLength = ReadExactly (stream, 8).ToUInt64BigEndian ();

      var maskingKey = masked ? ReadExactly (stream, 4) : null;
      var payload = ReadExactly (stream, payloadLength);

      if (masked)
        Mask (payload, maskingKey);

      return new FrameData (fin, opcode, payload);
    }

    private static FrameData ReadServerMessage (NetworkStream stream)
    {
      var opcode = -1;
      var payload = new MemoryStream ();

      while (true) {
        var frame = ReadServerFrame (stream);

        if (frame.Opcode == OpcodePong)
          continue;

        if (frame.Opcode == OpcodeText) {
          opcode = frame.Opcode;
          payload.Write (frame.Payload, 0, frame.Payload.Length);
        }
        else if (frame.Opcode == OpcodeContinuation) {
          payload.Write (frame.Payload, 0, frame.Payload.Length);
        }
        else {
          Assert.Fail ("Unexpected server frame opcode while reading a message: " + frame.Opcode);
        }

        if (frame.Fin)
          return new FrameData (true, opcode, payload.ToArray ());
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

    private static byte[] ReadExactly (NetworkStream stream, ulong length)
    {
      if (length > Int32.MaxValue)
        Assert.Fail ("The test helper does not support server frame payloads larger than Int32.MaxValue.");

      return ReadExactly (stream, (int) length);
    }

    private static byte[] ReadExactly (NetworkStream stream, int length)
    {
      var buffer = new byte[length];
      var offset = 0;
      var deadline = DateTime.UtcNow.Add (Timeout);

      while (offset < length && DateTime.UtcNow < deadline) {
        if (!stream.DataAvailable) {
          Thread.Sleep (10);
          continue;
        }

        var read = stream.Read (buffer, offset, length - offset);

        if (read <= 0)
          break;

        offset += read;
      }

      if (offset != length)
        Assert.Fail ("Timed out while reading a WebSocket frame.");

      return buffer;
    }

    private static LoopbackServer StartCountingServer (string path)
    {
      CountingBehavior.Reset ();

      return LoopbackServer.Start (
        s => {
          s.Log.Output = (data, outputPath) => { };
          s.AddWebSocketService<CountingBehavior> (path);
        }
      );
    }

    private static void WaitForProtocolClose (LoopbackServer server, string path, TcpClient client)
    {
      WaitUntil (
        () => server.WebSocketServices[path].Sessions.Count == 0 && IsDisconnected (client),
        "The server did not close and remove the protocol-error session."
      );
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

    private static void WriteClientFrame (
      NetworkStream stream,
      int opcode,
      byte[] payload,
      bool fin,
      bool mask
    )
    {
      var frame = new MemoryStream ();
      var firstByte = (byte) opcode;

      if (fin)
        firstByte |= 0x80;

      frame.WriteByte (firstByte);

      if (payload.Length <= 125) {
        frame.WriteByte ((byte) ((mask ? 0x80 : 0x00) | payload.Length));
      }
      else if (payload.Length <= UInt16.MaxValue) {
        frame.WriteByte ((byte) ((mask ? 0x80 : 0x00) | 126));
        frame.WriteUInt16BigEndian ((ushort) payload.Length);
      }
      else {
        frame.WriteByte ((byte) ((mask ? 0x80 : 0x00) | 127));
        frame.WriteUInt64BigEndian ((ulong) payload.Length);
      }

      var data = (byte[]) payload.Clone ();

      if (mask) {
        var maskingKey = new byte[] { 0x11, 0x22, 0x33, 0x44 };

        frame.Write (maskingKey, 0, maskingKey.Length);
        Mask (data, maskingKey);
      }

      frame.Write (data, 0, data.Length);

      var bytes = frame.ToArray ();

      stream.Write (bytes, 0, bytes.Length);
    }

    private static void Mask (byte[] payload, byte[] maskingKey)
    {
      for (var i = 0; i < payload.Length; i++)
        payload[i] = (byte) (payload[i] ^ maskingKey[i % 4]);
    }

    public sealed class CountingBehavior : WebSocketBehavior
    {
      private static int _messageCount;

      public static int MessageCount {
        get {
          return Thread.VolatileRead (ref _messageCount);
        }
      }

      public static void Reset ()
      {
        Thread.VolatileWrite (ref _messageCount, 0);
      }

      protected override void OnMessage (MessageEventArgs e)
      {
        Interlocked.Increment (ref _messageCount);
      }
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

    private sealed class FrameData
    {
      public FrameData (bool fin, int opcode, byte[] payload)
      {
        Fin = fin;
        Opcode = opcode;
        Payload = payload;
      }

      public bool Fin { get; private set; }

      public int Opcode { get; private set; }

      public byte[] Payload { get; private set; }
    }
  }

  internal static class ProtocolFrameTestExtensions
  {
    public static ushort ToUInt16BigEndian (this byte[] bytes)
    {
      return (ushort) ((bytes[0] << 8) | bytes[1]);
    }

    public static ulong ToUInt64BigEndian (this byte[] bytes)
    {
      var value = 0UL;

      for (var i = 0; i < bytes.Length; i++)
        value = (value << 8) | bytes[i];

      return value;
    }

    public static void WriteUInt16BigEndian (this Stream stream, ushort value)
    {
      stream.WriteByte ((byte) ((value >> 8) & 0xff));
      stream.WriteByte ((byte) (value & 0xff));
    }

    public static void WriteUInt64BigEndian (this Stream stream, ulong value)
    {
      for (var shift = 56; shift >= 0; shift -= 8)
        stream.WriteByte ((byte) ((value >> shift) & 0xff));
    }
  }
}
