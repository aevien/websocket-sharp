using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using NUnit.Framework;
using WebSocketSharp.Server;

namespace WebSocketSharp.Tests
{
  [TestFixture]
  [NonParallelizable]
  public sealed class AsyncSendQueueTests
  {
    private const int ImmediateOpenIterations = 100;
    private const int OrderedPairIterations = 1000;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds (10);

    [Test]
    public void ImmediateBinarySendFromOnOpenIsDelivered ()
    {
      using (var server = LoopbackServer.Start (
        s => s.AddWebSocketService<ImmediateBinaryBehavior> ("/immediate"))) {
        for (var i = 0; i < ImmediateOpenIterations; i++) {
          using (var client = new WebSocket (server.GetUrl ("/immediate")))
          using (var received = new ManualResetEventSlim ()) {
            var payload = default (byte[]);
            var error = default (Exception);

            client.OnMessage += (sender, e) => {
              payload = e.RawData;
              received.Set ();
            };
            client.OnError += (sender, e) => {
              error = e.Exception ?? new Exception (e.Message);
              received.Set ();
            };

            client.Connect ();

            WaitFor (received, "The immediate OnOpen message was not received.");
            AssertNoError (error);
            Assert.That (payload, Is.EqualTo (ImmediateBinaryBehavior.Payload));

            client.Close ();
          }
        }
      }
    }

    [Test]
    public void SequentialServerSendAsyncCallsPreserveFifoOrder ()
    {
      using (var sendsCompleted = new ManualResetEventSlim ())
      using (var received = new CountdownEvent (OrderedPairIterations * 2)) {
        var sendError = default (Exception);

        using (var server = LoopbackServer.Start (
          s => s.AddWebSocketService<OrderedPairBehavior> (
            "/ordered",
            behavior => behavior.Initialize (
              OrderedPairIterations,
              sendsCompleted,
              exception => sendError = exception
            )
          )))
        using (var client = new WebSocket (server.GetUrl ("/ordered"))) {
          var messages = new List<string> (OrderedPairIterations * 2);
          var receiveError = default (Exception);

          client.OnMessage += (sender, e) => {
            lock (messages)
              messages.Add (e.Data);

            received.Signal ();
          };
          client.OnError += (sender, e) => {
            receiveError = e.Exception ?? new Exception (e.Message);
          };

          client.Connect ();

          WaitFor (sendsCompleted, "The ordered server sends did not complete.");
          AssertNoError (sendError);
          WaitFor (received, "The ordered server messages were not all received.");
          AssertNoError (receiveError);

          lock (messages) {
            for (var i = 0; i < OrderedPairIterations; i++) {
              Assert.That (messages[i * 2], Is.EqualTo ("A:" + i));
              Assert.That (messages[(i * 2) + 1], Is.EqualTo ("B:" + i));
            }
          }

          client.Close ();
        }
      }
    }

    [Test]
    public void QueueLimitIncludesCallbackCurrentlyExecuting ()
    {
      using (var server = LoopbackServer.Start (
        s => s.AddWebSocketService<WebSocketLoopbackTests.EchoBehavior> ("/echo")))
      using (var client = new WebSocket (server.GetUrl ("/echo")))
      using (var firstCallbackEntered = new ManualResetEventSlim ())
      using (var releaseFirstCallback = new ManualResetEventSlim ())
      using (var firstCallbackCompleted = new ManualResetEventSlim ())
      using (var rejectedCallbackCompleted = new ManualResetEventSlim ()) {
        var firstSucceeded = false;
        var rejectedSucceeded = true;

        client.MaxAsyncSendQueueLength = 1;
        client.Log.Output = (data, path) => { };
        client.Connect ();

        client.SendAsync (
          "first",
          succeeded => {
            firstSucceeded = succeeded;
            firstCallbackEntered.Set ();
            releaseFirstCallback.Wait (Timeout);
            firstCallbackCompleted.Set ();
          }
        );

        WaitFor (firstCallbackEntered, "The first send callback did not start.");

        client.SendAsync (
          "rejected",
          succeeded => {
            rejectedSucceeded = succeeded;
            rejectedCallbackCompleted.Set ();
          }
        );

        WaitFor (rejectedCallbackCompleted, "The rejected send callback was not invoked.");
        Assert.That (rejectedSucceeded, Is.False);

        releaseFirstCallback.Set ();
        WaitFor (firstCallbackCompleted, "The first send callback did not finish.");
        Assert.That (firstSucceeded, Is.True);

        client.Close ();
      }
    }

    [Test]
    public void BlockingCallbackDoesNotBlockFollowingPhysicalSend ()
    {
      using (var server = LoopbackServer.Start (
        s => s.AddWebSocketService<WebSocketLoopbackTests.EchoBehavior> ("/echo")))
      using (var client = new WebSocket (server.GetUrl ("/echo")))
      using (var received = new CountdownEvent (2))
      using (var firstCallbackEntered = new ManualResetEventSlim ())
      using (var releaseFirstCallback = new ManualResetEventSlim ())
      using (var secondCallbackCompleted = new ManualResetEventSlim ()) {
        var messages = new List<string> (2);
        var secondSucceeded = false;
        var error = default (Exception);

        client.OnMessage += (sender, e) => {
          lock (messages)
            messages.Add (e.Data);

          received.Signal ();
        };
        client.OnError += (sender, e) => error = e.Exception ?? new Exception (e.Message);
        client.Connect ();

        try {
          client.SendAsync (
            "first",
            succeeded => {
              firstCallbackEntered.Set ();
              releaseFirstCallback.Wait (Timeout);
            }
          );

          WaitFor (firstCallbackEntered, "The first callback did not start.");

          client.SendAsync (
            "second",
            succeeded => {
              secondSucceeded = succeeded;
              secondCallbackCompleted.Set ();
            }
          );

          WaitFor (secondCallbackCompleted, "The second callback was blocked by the first callback.");
          WaitFor (received, "The second physical send was blocked by the first callback.");
          Assert.That (secondSucceeded, Is.True);
          AssertNoError (error);

          lock (messages)
            Assert.That (messages, Is.EqualTo (new[] { "first", "second" }));
        }
        finally {
          releaseFirstCallback.Set ();
          client.Close ();
        }
      }
    }

    [Test]
    public void ThrowingCallbackDoesNotStopFollowingSend ()
    {
      using (var server = LoopbackServer.Start (
        s => s.AddWebSocketService<WebSocketLoopbackTests.EchoBehavior> ("/echo")))
      using (var client = new WebSocket (server.GetUrl ("/echo")))
      using (var received = new CountdownEvent (2))
      using (var callbackErrorRaised = new ManualResetEventSlim ())
      using (var secondCallbackCompleted = new ManualResetEventSlim ()) {
        var messages = new List<string> (2);
        var secondSucceeded = false;

        client.OnMessage += (sender, e) => {
          lock (messages)
            messages.Add (e.Data);

          received.Signal ();
        };
        client.OnError += (sender, e) => {
          if (e.Exception is InvalidOperationException)
            callbackErrorRaised.Set ();
        };
        client.Log.Output = (data, path) => { };
        client.Connect ();

        client.SendAsync (
          "first",
          succeeded => { throw new InvalidOperationException ("callback failure proof"); }
        );
        client.SendAsync (
          "second",
          succeeded => {
            secondSucceeded = succeeded;
            secondCallbackCompleted.Set ();
          }
        );

        WaitFor (callbackErrorRaised, "The callback exception was not reported.");
        WaitFor (secondCallbackCompleted, "The callback exception stopped the following callback.");
        WaitFor (received, "The callback exception stopped the following physical send.");
        Assert.That (secondSucceeded, Is.True);

        lock (messages)
          Assert.That (messages, Is.EqualTo (new[] { "first", "second" }));

        client.Close ();
      }
    }

    [Test]
    public void StaleCompressedSendDoesNotRunOrReachReconnectedSession ()
    {
      using (var server = LoopbackServer.Start (
        s => s.AddWebSocketService<WebSocketLoopbackTests.EchoBehavior> ("/echo")))
      using (var client = new WebSocket (server.GetUrl ("/echo")))
      using (var firstOpened = new ManualResetEventSlim ())
      using (var secondOpened = new ManualResetEventSlim ())
      using (var staleCallbackCompleted = new ManualResetEventSlim ()) {
        var messages = new BlockingCollection<string> ();
        var openCount = 0;
        var staleSucceeded = true;
        var error = default (Exception);
        var sendLock = GetPrivateField (client, "_forSend");
        var staleStream = new TrackingStream (new byte[1024 * 1024]);

        client.Compression = CompressionMethod.Deflate;
        client.Log.Output = (data, path) => { };
        client.OnOpen += (sender, e) => {
          if (Interlocked.Increment (ref openCount) == 1)
            firstOpened.Set ();
          else
            secondOpened.Set ();
        };
        client.OnMessage += (sender, e) => messages.Add (e.Data);
        client.OnError += (sender, e) => error = e.Exception ?? new Exception (e.Message);

        client.Connect ();
        WaitFor (firstOpened, "The first connection did not open.");

        Monitor.Enter (sendLock);

        try {
          InvokePrivateSendAsync (
            client,
            staleStream,
            succeeded => {
              staleSucceeded = succeeded;
              staleCallbackCompleted.Set ();
            }
          );

          WaitUntil (
            () => GetAsyncSendQueueCount (client) == 0,
            "The stale operation was not picked up by the old worker."
          );

          client.Close ();
          client.Connect ();
          WaitFor (secondOpened, "The reconnected session did not open.");
        }
        finally {
          Monitor.Exit (sendLock);
        }

        WaitFor (staleCallbackCompleted, "The stale send callback did not complete.");
        Assert.That (staleSucceeded, Is.False);
        Assert.That (staleStream.ReadCount, Is.EqualTo (0), "The stale payload was compressed after reconnect.");
        Assert.That (staleStream.IsDisposed, Is.True);

        client.Send ("fresh");
        Assert.That (TakeMessage (messages), Is.EqualTo ("fresh"));
        AssertNoError (error);
        Assert.That (messages.TryTake (out _, 200), Is.False, "A stale message reached the new session.");

        client.Close ();
      }
    }

    private static void AssertNoError (Exception error)
    {
      if (error != null)
        Assert.Fail (error.ToString ());
    }

    private static string TakeMessage (BlockingCollection<string> messages)
    {
      string message;

      Assert.That (messages.TryTake (out message, (int) Timeout.TotalMilliseconds), Is.True);

      return message;
    }

    private static object GetPrivateField (object target, string name)
    {
      var field = target.GetType ().GetField (
                    name,
                    BindingFlags.Instance | BindingFlags.NonPublic
                  );

      Assert.That (field, Is.Not.Null, "The expected private field was not found.");

      return field.GetValue (target);
    }

    private static int GetAsyncSendQueueCount (WebSocket client)
    {
      var dispatcher = GetPrivateField (client, "_asyncSendDispatcher");
      var queueProperty = dispatcher.GetType ().GetProperty (
                            "Queue",
                            BindingFlags.Instance | BindingFlags.NonPublic
                          );

      Assert.That (queueProperty, Is.Not.Null, "The async send queue property was not found.");

      return ((ICollection) queueProperty.GetValue (dispatcher, null)).Count;
    }

    private static void InvokePrivateSendAsync (
      WebSocket client,
      Stream stream,
      Action<bool> completed
    )
    {
      var method = typeof (WebSocket).GetMethod (
                     "sendAsync",
                     BindingFlags.Instance | BindingFlags.NonPublic
                   );

      Assert.That (method, Is.Not.Null, "The private async send method was not found.");

      var opcodeType = method.GetParameters ()[0].ParameterType;
      var binaryOpcode = Enum.ToObject (opcodeType, 2);

      method.Invoke (client, new object[] { binaryOpcode, stream, completed });
    }

    private static void WaitFor (ManualResetEventSlim signal, string message)
    {
      Assert.That (signal.Wait (Timeout), Is.True, message);
    }

    private static void WaitFor (CountdownEvent signal, string message)
    {
      Assert.That (
        signal.Wait (Timeout),
        Is.True,
        String.Format ("{0} Remaining: {1}.", message, signal.CurrentCount)
      );
    }

    private static void WaitUntil (Func<bool> predicate, string message)
    {
      var deadline = DateTime.UtcNow.Add (Timeout);

      while (DateTime.UtcNow < deadline) {
        if (predicate ())
          return;

        Thread.Sleep (5);
      }

      Assert.That (predicate (), Is.True, message);
    }

    public sealed class ImmediateBinaryBehavior : WebSocketBehavior
    {
      internal static readonly byte[] Payload = Encoding.UTF8.GetBytes ("first");

      protected override void OnOpen ()
      {
        SendAsync (Payload, null);
      }
    }

    public sealed class OrderedPairBehavior : WebSocketBehavior
    {
      private ManualResetEventSlim _completed;
      private Action<Exception> _error;
      private int _pairCount;

      internal void Initialize (
        int pairCount,
        ManualResetEventSlim completed,
        Action<Exception> error
      )
      {
        _pairCount = pairCount;
        _completed = completed;
        _error = error;
      }

      protected override void OnOpen ()
      {
        ThreadPool.QueueUserWorkItem (state => SendPairs ());
      }

      private void SendPairs ()
      {
        try {
          for (var i = 0; i < _pairCount; i++) {
            using (var pairCompleted = new CountdownEvent (2)) {
              var pair = i;
              var pairSucceeded = true;

              SendAsync ("A:" + pair, succeeded => {
                if (!succeeded)
                  pairSucceeded = false;

                pairCompleted.Signal ();
              });
              SendAsync ("B:" + pair, succeeded => {
                if (!succeeded)
                  pairSucceeded = false;

                pairCompleted.Signal ();
              });

              if (!pairCompleted.Wait (Timeout))
                throw new TimeoutException ("An ordered async send pair did not complete.");

              if (!pairSucceeded)
                throw new InvalidOperationException ("An ordered async send reported failure.");
            }
          }
        }
        catch (Exception exception) {
          _error (exception);
        }
        finally {
          _completed.Set ();
        }
      }
    }

    private sealed class TrackingStream : MemoryStream
    {
      internal TrackingStream (byte[] buffer)
        : base (buffer)
      {
      }

      internal bool IsDisposed { get; private set; }
      internal int ReadCount { get; private set; }

      public override int Read (byte[] buffer, int offset, int count)
      {
        ReadCount++;

        return base.Read (buffer, offset, count);
      }

      protected override void Dispose (bool disposing)
      {
        IsDisposed = true;
        base.Dispose (disposing);
      }
    }
  }
}
