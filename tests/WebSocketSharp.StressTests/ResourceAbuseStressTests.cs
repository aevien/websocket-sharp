using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using NUnit.Framework;
using WebSocketSharp.Server;

namespace WebSocketSharp.StressTests
{
  [TestFixture]
  [NonParallelizable]
  [Category ("Stress")]
  public sealed class ResourceAbuseStressTests
  {
    private const int DefaultBadHandshakeClients = 50;
    private const int DefaultFragmentClients = 25;
    private const int DefaultTimeoutSeconds = 60;
    private const int OpcodeContinuation = 0x0;
    private const int OpcodeText = 0x1;
    private const int OpcodeClose = 0x8;

    [Test]
    public void RejectedHandshakesAndFragmentLimitViolationsDoNotBlockValidClients ()
    {
      var badHandshakeClients = GetPositiveInt (
        "WEBSOCKET_SHARP_RESOURCE_ABUSE_BAD_HANDSHAKES",
        DefaultBadHandshakeClients
      );
      var fragmentClients = GetPositiveInt (
        "WEBSOCKET_SHARP_RESOURCE_ABUSE_FRAGMENT_CLIENTS",
        DefaultFragmentClients
      );
      var timeout = TimeSpan.FromSeconds (
        GetPositiveInt ("WEBSOCKET_SHARP_RESOURCE_ABUSE_TIMEOUT_SECONDS", DefaultTimeoutSeconds)
      );
      var elapsed = Stopwatch.StartNew ();

      CountingBehavior.Reset ();

      using (var server = StressLoopbackServer.Start (
        s => {
          s.Log.Output = (data, outputPath) => { };
          s.AddWebSocketService<CountingBehavior> (
            "/count",
            b => b.MaxMessagePayloadLength = 8
          );
          s.AddWebSocketService<EchoBehavior> ("/echo");
        })) {
        var errors = new ConcurrentQueue<string> ();

        RunBadHandshakeClients (server.Port, badHandshakeClients, timeout, errors);
        AssertNoErrors (errors);
        Assert.That (server.WebSocketServices["/count"].Sessions.Count, Is.EqualTo (0));

        RunFragmentLimitClients (server.Port, fragmentClients, timeout, errors);
        AssertNoErrors (errors);

        WaitUntil (
          () => server.WebSocketServices["/count"].Sessions.Count == 0,
          timeout,
          "The server kept sessions after fragment limit violations."
        );
        Assert.That (CountingBehavior.MessageCount, Is.EqualTo (0));

        RunValidEchoClient (server.GetUrl ("/echo"), timeout);

        WaitUntil (
          () => server.WebSocketServices["/echo"].Sessions.Count == 0,
          timeout,
          "The server kept the valid echo session after resource-abuse stress."
        );
      }

      elapsed.Stop ();
      TestContext.WriteLine (
        "Completed resource-abuse stress with {0} rejected handshakes and {1} fragment-limit clients in {2}.",
        badHandshakeClients,
        fragmentClients,
        elapsed.Elapsed
      );
    }

    private static void AssertNoErrors (ConcurrentQueue<string> errors)
    {
      if (errors.IsEmpty)
        return;

      Assert.Fail (String.Join (Environment.NewLine, errors.Take (10).ToArray ()));
    }

    private static string BuildExtraHeaders (int count)
    {
      var headers = new StringBuilder ();

      for (var i = 0; i < count; i++)
        headers.AppendFormat ("X-Abuse-{0}: value\r\n", i);

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

    private static TcpClient ConnectRawWebSocket (int port, string path)
    {
      var client = SendRawHandshake (port, path, null);
      var response = ReadHttpResponseHeader (client.GetStream (), TimeSpan.FromSeconds (5));

      if (!response.StartsWith ("HTTP/1.1 101 ", StringComparison.Ordinal))
        throw new InvalidOperationException (
          "The raw WebSocket handshake did not receive a 101 response: " + response
        );

      return client;
    }

    private static string CreateWebSocketKey ()
    {
      var bytes = new byte[16];

      using (var rng = RandomNumberGenerator.Create ())
        rng.GetBytes (bytes);

      return Convert.ToBase64String (bytes);
    }

    private static int GetPositiveInt (string variableName, int defaultValue)
    {
      var value = Environment.GetEnvironmentVariable (variableName);
      int parsed;

      if (!Int32.TryParse (value, out parsed) || parsed < 1)
        return defaultValue;

      return parsed;
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

    private static string ReadHttpResponseHeader (NetworkStream stream, TimeSpan timeout)
    {
      var buffer = new byte[1];
      var data = new MemoryStream ();
      var previous = new byte[3];
      var deadline = DateTime.UtcNow.Add (timeout);

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

      throw new TimeoutException ("Timed out while reading the raw WebSocket handshake response.");
    }

    private static byte[] ReadExactly (
      NetworkStream stream,
      int length,
      TimeSpan timeout
    )
    {
      var buffer = new byte[length];
      var offset = 0;
      var deadline = DateTime.UtcNow.Add (timeout);

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
        throw new TimeoutException ("Timed out while reading a WebSocket frame.");

      return buffer;
    }

    private static FrameData ReadServerFrame (NetworkStream stream, TimeSpan timeout)
    {
      var header = ReadExactly (stream, 2, timeout);
      var opcode = header[0] & 0x0f;
      var masked = (header[1] & 0x80) == 0x80;
      var payloadLength = (ulong) (header[1] & 0x7f);

      if (payloadLength == 126)
        payloadLength = ToUInt16BigEndian (ReadExactly (stream, 2, timeout));
      else if (payloadLength == 127)
        payloadLength = ToUInt64BigEndian (ReadExactly (stream, 8, timeout));

      if (payloadLength > Int32.MaxValue)
        throw new InvalidOperationException ("The test helper does not support oversized server frames.");

      var maskingKey = masked ? ReadExactly (stream, 4, timeout) : null;
      var payload = ReadExactly (stream, (int) payloadLength, timeout);

      if (masked)
        Mask (payload, maskingKey);

      return new FrameData (opcode, payload);
    }

    private static ushort ReadServerCloseCode (NetworkStream stream, TimeSpan timeout)
    {
      var frame = ReadServerFrame (stream, timeout);

      if (frame.Opcode != OpcodeClose)
        throw new InvalidOperationException ("The server did not send a close frame.");

      if (frame.Payload.Length < 2)
        throw new InvalidOperationException ("The close frame did not include a close code.");

      return (ushort) ((frame.Payload[0] << 8) | frame.Payload[1]);
    }

    private static void RunBadHandshakeClients (
      int port,
      int clientCount,
      TimeSpan timeout,
      ConcurrentQueue<string> errors
    )
    {
      using (var completed = new CountdownEvent (clientCount)) {
        var extraHeaders = BuildExtraHeaders (65);

        for (var i = 0; i < clientCount; i++) {
          var clientIndex = i;

          ThreadPool.QueueUserWorkItem (
            state => {
              try {
                using (var client = SendRawHandshake (port, "/count", extraHeaders)) {
                  WaitUntil (
                    () => IsDisconnected (client),
                    timeout,
                    "The server did not disconnect a bad handshake client."
                  );
                }
              }
              catch (Exception ex) {
                errors.Enqueue (String.Format ("Bad handshake client {0}: {1}", clientIndex, ex));
              }
              finally {
                completed.Signal ();
              }
            }
          );
        }

        WaitFor (completed, timeout, "Not all bad handshake clients completed.");
      }
    }

    private static void RunFragmentLimitClients (
      int port,
      int clientCount,
      TimeSpan timeout,
      ConcurrentQueue<string> errors
    )
    {
      using (var completed = new CountdownEvent (clientCount)) {
        for (var i = 0; i < clientCount; i++) {
          var clientIndex = i;

          ThreadPool.QueueUserWorkItem (
            state => {
              try {
                using (var client = ConnectRawWebSocket (port, "/count")) {
                  var stream = client.GetStream ();

                  for (var fragmentIndex = 0; fragmentIndex < 9; fragmentIndex++) {
                    var opcode = fragmentIndex == 0 ? OpcodeText : OpcodeContinuation;

                    WriteClientFrame (stream, opcode, Encoding.ASCII.GetBytes ("a"), false, true);
                  }

                  var closeCode = ReadServerCloseCode (stream, timeout);

                  if (closeCode != (ushort) CloseStatusCode.TooBig)
                    errors.Enqueue (
                      String.Format (
                        "Fragment client {0} received close code {1}.",
                        clientIndex,
                        closeCode
                      )
                    );
                }
              }
              catch (Exception ex) {
                errors.Enqueue (String.Format ("Fragment client {0}: {1}", clientIndex, ex));
              }
              finally {
                completed.Signal ();
              }
            }
          );
        }

        WaitFor (completed, timeout, "Not all fragment-limit clients completed.");
      }
    }

    private static void RunValidEchoClient (string url, TimeSpan timeout)
    {
      using (var opened = new ManualResetEvent (false))
      using (var received = new ManualResetEvent (false))
      using (var closed = new ManualResetEvent (false))
      using (var client = new WebSocket (url)) {
        var error = default (Exception);
        var message = default (string);

        client.OnOpen += (sender, e) => opened.Set ();
        client.OnMessage += (sender, e) => {
          message = e.Data;
          received.Set ();
        };
        client.OnError += (sender, e) => {
          error = e.Exception ?? new Exception (e.Message);
          received.Set ();
        };
        client.OnClose += (sender, e) => closed.Set ();

        client.Connect ();

        if (!opened.WaitOne (timeout))
          Assert.Fail ("The valid echo client did not open after resource-abuse stress.");

        if (error != null)
          Assert.Fail (error.ToString ());

        client.Send ("ok");

        if (!received.WaitOne (timeout))
          Assert.Fail ("The valid echo client did not receive a response after resource-abuse stress.");

        if (error != null)
          Assert.Fail (error.ToString ());

        Assert.That (message, Is.EqualTo ("ok"));

        client.Close ();

        if (!closed.WaitOne (timeout))
          Assert.Fail ("The valid echo client did not close after resource-abuse stress.");
      }
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

      var client = new TcpClient ();

      client.Connect (IPAddress.Loopback, port);

      var stream = client.GetStream ();
      var bytes = Encoding.ASCII.GetBytes (request);

      stream.Write (bytes, 0, bytes.Length);

      return client;
    }

    private static ushort ToUInt16BigEndian (byte[] bytes)
    {
      return (ushort) ((bytes[0] << 8) | bytes[1]);
    }

    private static ulong ToUInt64BigEndian (byte[] bytes)
    {
      var value = 0UL;

      for (var i = 0; i < bytes.Length; i++)
        value = (value << 8) | bytes[i];

      return value;
    }

    private static void WaitFor (CountdownEvent signal, TimeSpan timeout, string message)
    {
      Assert.That (
        signal.Wait (timeout),
        Is.True,
        String.Format ("{0} Remaining: {1}.", message, signal.CurrentCount)
      );
    }

    private static void WaitUntil (
      Func<bool> predicate,
      TimeSpan timeout,
      string message
    )
    {
      var deadline = DateTime.UtcNow.Add (timeout);

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
        WriteUInt16BigEndian (frame, (ushort) payload.Length);
      }
      else {
        frame.WriteByte ((byte) ((mask ? 0x80 : 0x00) | 127));
        WriteUInt64BigEndian (frame, (ulong) payload.Length);
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

    private static void WriteUInt16BigEndian (Stream stream, ushort value)
    {
      stream.WriteByte ((byte) ((value >> 8) & 0xff));
      stream.WriteByte ((byte) (value & 0xff));
    }

    private static void WriteUInt64BigEndian (Stream stream, ulong value)
    {
      for (var shift = 56; shift >= 0; shift -= 8)
        stream.WriteByte ((byte) ((value >> shift) & 0xff));
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
      public FrameData (int opcode, byte[] payload)
      {
        Opcode = opcode;
        Payload = payload;
      }

      public int Opcode { get; private set; }

      public byte[] Payload { get; private set; }
    }
  }
}
