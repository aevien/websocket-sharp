using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using NUnit.Framework;
using WebSocketSharp.Server;

namespace WebSocketSharp.StressTests
{
  [TestFixture]
  [NonParallelizable]
  [Category ("Stress")]
  public sealed class SlowHandshakeStressTests
  {
    private const int DefaultSilentClientCount = 20;
    private const int DefaultHandshakeTimeoutMilliseconds = 250;
    private const int DefaultTestTimeoutSeconds = 10;

    [Test]
    public void SilentTcpClientsDoNotBlockValidWebSocketHandshakes ()
    {
      var silentClientCount = GetPositiveInt ("WEBSOCKET_SHARP_SLOW_HANDSHAKE_CLIENTS", DefaultSilentClientCount);
      var handshakeTimeout = TimeSpan.FromMilliseconds (
        GetPositiveInt ("WEBSOCKET_SHARP_SLOW_HANDSHAKE_TIMEOUT_MILLISECONDS", DefaultHandshakeTimeoutMilliseconds)
      );
      var testTimeout = TimeSpan.FromSeconds (
        GetPositiveInt ("WEBSOCKET_SHARP_SLOW_HANDSHAKE_TEST_TIMEOUT_SECONDS", DefaultTestTimeoutSeconds)
      );
      var elapsed = Stopwatch.StartNew ();

      using (
        var server = StressLoopbackServer.Start (
          s => {
            s.HandshakeTimeout = handshakeTimeout;
            s.Log.Output = (data, path) => { };
            s.AddWebSocketService<EchoBehavior> ("/echo");
          }
        )
      ) {
        var sessions = server.WebSocketServices["/echo"].Sessions;
        var silentClients = new List<TcpClient> (silentClientCount);

        try {
          for (var i = 0; i < silentClientCount; i++)
            silentClients.Add (CreateSilentClient (server.Port));

          Assert.That (sessions.Count, Is.EqualTo (0));

          RoundTripValidClient (server.GetUrl ("/echo"), testTimeout);

          WaitUntil (
            () => sessions.Count == 0,
            testTimeout,
            "The valid client session was not released after close."
          );

          WaitUntil (
            () => silentClients.All (IsDisconnected),
            testTimeout,
            "The server did not disconnect every silent handshake client."
          );
        }
        finally {
          foreach (var client in silentClients)
            client.Close ();
        }
      }

      elapsed.Stop ();
      TestContext.WriteLine (
        "Completed slow-handshake stress with {0} silent clients and {1} server timeout in {2}.",
        silentClientCount,
        handshakeTimeout,
        elapsed.Elapsed
      );
    }

    private static TcpClient CreateSilentClient (int port)
    {
      var client = new TcpClient ();

      client.Connect (IPAddress.Loopback, port);

      return client;
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
        var socket = client.Client;

        return socket.Poll (0, SelectMode.SelectRead) && socket.Available == 0;
      }
      catch {
        return true;
      }
    }

    private static void RoundTripValidClient (string url, TimeSpan timeout)
    {
      using (var client = new WebSocket (url))
      using (var opened = new ManualResetEventSlim ())
      using (var sent = new ManualResetEventSlim ())
      using (var received = new ManualResetEventSlim ())
      using (var closed = new ManualResetEventSlim ()) {
        var payload = "slow-handshake-valid-client";
        var actual = default (string);
        var sendSucceeded = false;
        var error = default (Exception);

        client.ConnectionTimeout = timeout;
        client.OnOpen += (sender, e) => opened.Set ();
        client.OnMessage += (sender, e) => {
          actual = e.Data;
          received.Set ();
        };
        client.OnError += (sender, e) => {
          error = e.Exception ?? new Exception (e.Message);
          received.Set ();
        };
        client.OnClose += (sender, e) => closed.Set ();

        client.ConnectAsync ();

        WaitFor (opened, timeout, "The valid client did not open while silent handshakes were connected.");
        AssertNoError (error);

        client.SendAsync (
          payload,
          succeeded => {
            sendSucceeded = succeeded;
            sent.Set ();
          }
        );

        WaitFor (sent, timeout, "The valid client send callback was not called.");
        Assert.That (sendSucceeded, Is.True);
        WaitFor (received, timeout, "The valid client echo was not received.");
        AssertNoError (error);
        Assert.That (actual, Is.EqualTo (payload));

        client.CloseAsync ();

        WaitFor (closed, timeout, "The valid client did not close.");
      }
    }

    private static void AssertNoError (Exception error)
    {
      if (error != null)
        Assert.Fail (error.ToString ());
    }

    private static void WaitFor (ManualResetEventSlim signal, TimeSpan timeout, string message)
    {
      Assert.That (signal.Wait (timeout), Is.True, message);
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
