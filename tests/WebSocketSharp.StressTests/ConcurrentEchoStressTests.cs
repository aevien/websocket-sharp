using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using WebSocketSharp.Server;

namespace WebSocketSharp.StressTests
{
  [TestFixture]
  [NonParallelizable]
  [Category ("Stress")]
  public sealed class ConcurrentEchoStressTests
  {
    private const int DefaultClientCount = 50;
    private const int DefaultMessagesPerClient = 100;
    private const int DefaultTimeoutSeconds = 60;

    [Test]
    public void ConcurrentClientsCanEchoManyMessagesWithoutStrandedSessions ()
    {
      var clientCount = GetPositiveInt ("WEBSOCKET_SHARP_STRESS_CCU", DefaultClientCount);
      var messagesPerClient = GetPositiveInt ("WEBSOCKET_SHARP_STRESS_MESSAGES_PER_CLIENT", DefaultMessagesPerClient);
      var timeout = TimeSpan.FromSeconds (
        GetPositiveInt ("WEBSOCKET_SHARP_STRESS_TIMEOUT_SECONDS", DefaultTimeoutSeconds)
      );
      var expectedMessages = checked (clientCount * messagesPerClient);
      var elapsed = Stopwatch.StartNew ();

      using (var server = StressLoopbackServer.Start (s => s.AddWebSocketService<EchoBehavior> ("/echo")))
      using (var opened = new CountdownEvent (clientCount))
      using (var sendCallbacks = new CountdownEvent (expectedMessages))
      using (var received = new CountdownEvent (expectedMessages))
      using (var closed = new CountdownEvent (clientCount)) {
        var sessions = server.WebSocketServices["/echo"].Sessions;
        var clients = new List<WebSocket> (clientCount);
        var echoed = new ConcurrentDictionary<string, bool> (StringComparer.Ordinal);
        var errors = new ConcurrentQueue<string> ();
        var openSeen = new int[clientCount];
        var closeSeen = new int[clientCount];
        var nextMessageIndex = new int[clientCount];

        try {
          for (var i = 0; i < clientCount; i++)
            clients.Add (CreateClient (
              server.GetUrl ("/echo"),
              i,
              opened,
              received,
              closed,
              echoed,
              errors,
              openSeen,
              closeSeen,
              nextMessageIndex
            ));

          for (var clientIndex = 0; clientIndex < clients.Count; clientIndex++) {
            var currentClient = clientIndex;

            clients[currentClient].ConnectAsync ();

            WaitUntil (
              () => openSeen[currentClient] != 0,
              timeout,
              String.Format ("The concurrent stress client {0} did not open.", currentClient)
            );

            AssertNoErrors (errors);
          }

          Assert.That (opened.CurrentCount, Is.EqualTo (0));

          WaitUntil (
            () => sessions.Count == clientCount,
            timeout,
            "The stress server did not observe the expected concurrent session count."
          );

          for (var clientIndex = 0; clientIndex < clients.Count; clientIndex++) {
            var client = clients[clientIndex];
            var currentClient = clientIndex;

            for (var messageIndex = 0; messageIndex < messagesPerClient; messageIndex++) {
              var payload = FormatPayload (currentClient, messageIndex);

              client.SendAsync (
                payload,
                succeeded => {
                  if (!succeeded)
                    errors.Enqueue ("SendAsync reported failure for payload: " + payload);

                  sendCallbacks.Signal ();
                }
              );
            }
          }

          WaitFor (sendCallbacks, timeout, "Not all concurrent stress send callbacks completed.");
          AssertNoErrors (errors);

          WaitFor (received, timeout, "Not all concurrent stress echo messages were received.");
          AssertNoErrors (errors);
          Assert.That (echoed.Count, Is.EqualTo (expectedMessages));

          for (var clientIndex = 0; clientIndex < clientCount; clientIndex++)
            for (var messageIndex = 0; messageIndex < messagesPerClient; messageIndex++)
              Assert.That (
                echoed.ContainsKey (FormatPayload (clientIndex, messageIndex)),
                Is.True,
                "The concurrent stress echo set is missing an expected payload."
              );

          foreach (var client in clients)
            client.CloseAsync ();

          WaitFor (closed, timeout, "Not all concurrent stress clients closed.");

          WaitUntil (
            () => sessions.Count == 0,
            timeout,
            "The stress server kept sessions after concurrent clients closed."
          );
        }
        finally {
          foreach (var client in clients)
            ((IDisposable) client).Dispose ();
        }
      }

      elapsed.Stop ();
      TestContext.WriteLine (
        "Completed concurrent echo stress with {0} CCU x {1} messages in {2}.",
        clientCount,
        messagesPerClient,
        elapsed.Elapsed
      );
    }

    private static WebSocket CreateClient (
      string url,
      int clientIndex,
      CountdownEvent opened,
      CountdownEvent received,
      CountdownEvent closed,
      ConcurrentDictionary<string, bool> echoed,
      ConcurrentQueue<string> errors,
      int[] openSeen,
      int[] closeSeen,
      int[] nextMessageIndex
    )
    {
      var client = new WebSocket (url);

      client.OnOpen += (sender, e) => {
        if (Interlocked.Exchange (ref openSeen[clientIndex], 1) == 0)
          opened.Signal ();
      };

      client.OnMessage += (sender, e) => {
        if (!e.IsText) {
          errors.Enqueue ("Received a non-text echo for client: " + clientIndex);
          return;
        }

        if (!echoed.TryAdd (e.Data, true)) {
          errors.Enqueue ("Received a duplicate echo payload: " + e.Data);
          return;
        }

        var expectedIndex = Interlocked.Increment (ref nextMessageIndex[clientIndex]) - 1;
        var expectedPayload = FormatPayload (clientIndex, expectedIndex);

        if (!String.Equals (e.Data, expectedPayload, StringComparison.Ordinal)) {
          errors.Enqueue (
            String.Format (
              "Client {0} received an out-of-order echo. Expected: {1}; actual: {2}.",
              clientIndex,
              expectedPayload,
              e.Data
            )
          );
        }

        received.Signal ();
      };

      client.OnError += (sender, e) => {
        errors.Enqueue (String.Format ("Client {0} error: {1}", clientIndex, e.Exception ?? new Exception (e.Message)));
      };

      client.OnClose += (sender, e) => {
        if (Interlocked.Exchange (ref closeSeen[clientIndex], 1) == 0)
          closed.Signal ();
      };

      return client;
    }

    private static void AssertNoErrors (ConcurrentQueue<string> errors)
    {
      if (errors.IsEmpty)
        return;

      Assert.Fail (String.Join (Environment.NewLine, errors.Take (10).ToArray ()));
    }

    private static string FormatPayload (int clientIndex, int messageIndex)
    {
      return String.Format ("concurrent-stress-{0}-{1}", clientIndex, messageIndex);
    }

    private static int GetPositiveInt (string variableName, int defaultValue)
    {
      var value = Environment.GetEnvironmentVariable (variableName);
      int parsed;

      if (!Int32.TryParse (value, out parsed) || parsed < 1)
        return defaultValue;

      return parsed;
    }

    private static void WaitFor (CountdownEvent signal, TimeSpan timeout, string message)
    {
      Assert.That (
        signal.Wait (timeout),
        Is.True,
        String.Format ("{0} Remaining: {1}.", message, signal.CurrentCount)
      );
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
