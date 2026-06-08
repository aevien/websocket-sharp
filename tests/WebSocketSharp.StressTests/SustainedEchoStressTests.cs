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
  public sealed class SustainedEchoStressTests
  {
    private const int DefaultClientCount = 100;
    private const int DefaultInFlightPerClient = 1;
    private const int DefaultMessagesPerClient = 1000;
    private const int DefaultTimeoutSeconds = 300;

    [Test]
    public void SustainedClientsCanContinuouslyEchoMessagesWithoutQueueOverflow ()
    {
      var clientCount = GetPositiveInt ("WEBSOCKET_SHARP_SUSTAINED_CCU", DefaultClientCount);
      var messagesPerClient = GetPositiveInt (
        "WEBSOCKET_SHARP_SUSTAINED_MESSAGES_PER_CLIENT",
        DefaultMessagesPerClient
      );
      var inFlightPerClient = Math.Min (
        GetPositiveInt (
          "WEBSOCKET_SHARP_SUSTAINED_IN_FLIGHT_PER_CLIENT",
          DefaultInFlightPerClient
        ),
        messagesPerClient
      );
      var timeout = TimeSpan.FromSeconds (
        GetPositiveInt ("WEBSOCKET_SHARP_SUSTAINED_TIMEOUT_SECONDS", DefaultTimeoutSeconds)
      );
      var expectedMessages = checked (clientCount * messagesPerClient);
      var elapsed = Stopwatch.StartNew ();

      using (var server = StressLoopbackServer.Start (s => s.AddWebSocketService<EchoBehavior> ("/echo")))
      using (var opened = new CountdownEvent (clientCount))
      using (var sendCallbacks = new CountdownEvent (expectedMessages))
      using (var received = new CountdownEvent (expectedMessages))
      using (var completedClients = new CountdownEvent (clientCount))
      using (var closed = new CountdownEvent (clientCount)) {
        var sessions = server.WebSocketServices["/echo"].Sessions;
        var clients = new List<WebSocket> (clientCount);
        var echoed = new ConcurrentDictionary<string, bool> (StringComparer.Ordinal);
        var errors = new ConcurrentQueue<string> ();
        var sentCounts = new int[clientCount];
        var receivedCounts = new int[clientCount];
        var sendLocks = new object[clientCount];
        var openSeen = new int[clientCount];
        var closeSeen = new int[clientCount];
        Action<int> sendNext = null;

        sendNext = clientIndex => {
          int messageIndex;

          lock (sendLocks[clientIndex]) {
            if (sentCounts[clientIndex] >= messagesPerClient)
              return;

            messageIndex = sentCounts[clientIndex]++;
          }

          var payload = FormatPayload (clientIndex, messageIndex);

          try {
            clients[clientIndex].SendAsync (
              payload,
              succeeded => {
                if (!succeeded)
                  errors.Enqueue ("SendAsync reported failure for payload: " + payload);

                sendCallbacks.Signal ();
              }
            );
          }
          catch (Exception ex) {
            errors.Enqueue (
              String.Format ("SendAsync threw for payload {0}: {1}", payload, ex)
            );

            sendCallbacks.Signal ();
          }
        };

        try {
          for (var i = 0; i < clientCount; i++) {
            sendLocks[i] = new object ();
            clients.Add (
              CreateClient (
                server.GetUrl ("/echo"),
                i,
                messagesPerClient,
                opened,
                received,
                completedClients,
                closed,
                echoed,
                errors,
                receivedCounts,
                openSeen,
                closeSeen,
                sendNext
              )
            );
          }

          for (var clientIndex = 0; clientIndex < clients.Count; clientIndex++) {
            var currentClient = clientIndex;

            clients[currentClient].ConnectAsync ();

            WaitUntil (
              () => openSeen[currentClient] != 0,
              timeout,
              String.Format ("The sustained stress client {0} did not open.", currentClient)
            );

            AssertNoErrors (errors);
          }

          Assert.That (opened.CurrentCount, Is.EqualTo (0));

          WaitUntil (
            () => sessions.Count == clientCount,
            timeout,
            "The sustained stress server did not observe the expected concurrent session count."
          );

          for (var clientIndex = 0; clientIndex < clients.Count; clientIndex++)
            for (var inFlightIndex = 0; inFlightIndex < inFlightPerClient; inFlightIndex++)
              sendNext (clientIndex);

          WaitFor (sendCallbacks, timeout, "Not all sustained stress send callbacks completed.");
          AssertNoErrors (errors);

          WaitFor (received, timeout, "Not all sustained stress echo messages were received.");
          WaitFor (completedClients, timeout, "Not all sustained stress clients completed.");
          AssertNoErrors (errors);
          Assert.That (echoed.Count, Is.EqualTo (expectedMessages));

          for (var clientIndex = 0; clientIndex < clientCount; clientIndex++) {
            Assert.That (
              sentCounts[clientIndex],
              Is.EqualTo (messagesPerClient),
              "The sustained stress client did not send the expected message count."
            );

            Assert.That (
              receivedCounts[clientIndex],
              Is.EqualTo (messagesPerClient),
              "The sustained stress client did not receive the expected message count."
            );
          }

          foreach (var client in clients)
            client.CloseAsync ();

          WaitFor (closed, timeout, "Not all sustained stress clients closed.");

          WaitUntil (
            () => sessions.Count == 0,
            timeout,
            "The sustained stress server kept sessions after clients closed."
          );
        }
        finally {
          foreach (var client in clients)
            ((IDisposable) client).Dispose ();
        }
      }

      elapsed.Stop ();

      var messagesPerSecond = expectedMessages / Math.Max (elapsed.Elapsed.TotalSeconds, 0.001);

      TestContext.WriteLine (
        "Completed sustained echo stress with {0} CCU x {1} messages ({2} total, {3} in-flight per client) in {4}; throughput {5:0.0} msg/s.",
        clientCount,
        messagesPerClient,
        expectedMessages,
        inFlightPerClient,
        elapsed.Elapsed,
        messagesPerSecond
      );
    }

    private static WebSocket CreateClient (
      string url,
      int clientIndex,
      int messagesPerClient,
      CountdownEvent opened,
      CountdownEvent received,
      CountdownEvent completedClients,
      CountdownEvent closed,
      ConcurrentDictionary<string, bool> echoed,
      ConcurrentQueue<string> errors,
      int[] receivedCounts,
      int[] openSeen,
      int[] closeSeen,
      Action<int> sendNext
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

        var prefix = String.Format ("sustained-stress-{0}-", clientIndex);

        if (!e.Data.StartsWith (prefix, StringComparison.Ordinal)) {
          errors.Enqueue (
            String.Format (
              "Client {0} received a payload for another client. Actual: {1}.",
              clientIndex,
              e.Data
            )
          );
        }
        else {
          int messageIndex;

          if (!Int32.TryParse (e.Data.Substring (prefix.Length), out messageIndex) ||
              messageIndex < 0 ||
              messageIndex >= messagesPerClient)
            errors.Enqueue (
              String.Format (
                "Client {0} received an out-of-range payload. Actual: {1}.",
                clientIndex,
                e.Data
              )
            );
        }

        if (!echoed.TryAdd (e.Data, true))
          errors.Enqueue ("Received a duplicate echo payload: " + e.Data);

        var receivedCount = Interlocked.Increment (ref receivedCounts[clientIndex]);

        received.Signal ();

        if (receivedCount == messagesPerClient) {
          completedClients.Signal ();
          return;
        }

        sendNext (clientIndex);
      };

      client.OnError += (sender, e) => {
        errors.Enqueue (
          String.Format ("Client {0} error: {1}", clientIndex, e.Exception ?? new Exception (e.Message))
        );
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
      return String.Format ("sustained-stress-{0}-{1}", clientIndex, messageIndex);
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
