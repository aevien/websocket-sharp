using System;
using System.Collections.Concurrent;
using System.Threading;
using WebSocketSharp;

namespace ClientLifecycle
{
  internal static class Program
  {
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds (10);
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds (5);
    private static readonly TimeSpan MessageTimeout = TimeSpan.FromSeconds (5);
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds (5);

    private static int Main (string[] args)
    {
      var serverUrl = args.Length > 0 ? args[0] : "ws://localhost:4649/Echo";
      var mainThreadQueue = new ConcurrentQueue<Action> ();

      using (var opened = new ManualResetEvent (false))
      using (var failed = new ManualResetEvent (false))
      using (var messageReceived = new ManualResetEvent (false))
      using (var closed = new ManualResetEvent (false))
      using (var pendingSends = new CountdownEvent (1))
      using (var ws = new WebSocket (serverUrl))
      {
        var errorSeen = 0;

        // websocket-sharp raises lifecycle callbacks on worker threads.
        // In Unity, do not touch GameObjects, components, UI, or other
        // main-thread-only state in these handlers. Enqueue work instead:
        //
        //   dispatchToMainThread (() => { /* update Unity state */ });
        //
        // Then drain that queue from MonoBehaviour.Update(). This console
        // sample uses the same idea without depending on UnityEngine.
        Action<Action> dispatchToMainThread = action => mainThreadQueue.Enqueue (action);

        ws.OnOpen += (sender, e) => {
          dispatchToMainThread (() => Console.WriteLine ("[open] Connected to " + serverUrl));
          opened.Set ();
        };

        ws.OnMessage += (sender, e) => {
          dispatchToMainThread (() => {
            if (e.IsPing) {
              Console.WriteLine ("[message/ping] " + e.Data);
              return;
            }

            if (e.IsText) {
              Console.WriteLine ("[message/text] " + e.Data);
              return;
            }

            Console.WriteLine ("[message/binary] " + e.RawData.Length + " bytes");
          });

          messageReceived.Set ();
        };

        ws.OnError += (sender, e) => {
          Interlocked.Exchange (ref errorSeen, 1);
          dispatchToMainThread (() => Console.WriteLine ("[error] " + e.Message));
          failed.Set ();
        };

        ws.OnClose += (sender, e) => {
          dispatchToMainThread (
            () => Console.WriteLine ("[close] Code=" + e.Code + ", Reason=\"" + e.Reason + "\"")
          );

          closed.Set ();
        };

        Console.WriteLine ("Connecting with ConnectAsync...");
        ws.ConnectAsync ();

        if (!WaitForOpenOrFailure (opened, failed, closed, mainThreadQueue, ConnectTimeout)) {
          DrainMainThreadQueue (mainThreadQueue);

          if (failed.WaitOne (0) || closed.WaitOne (0))
            Console.WriteLine ("Connection ended before OnOpen.");
          else
            Console.WriteLine ("Timed out waiting for OnOpen.");

          return 1;
        }

        if (Interlocked.CompareExchange (ref errorSeen, 0, 0) != 0 || ws.ReadyState != WebSocketState.Open) {
          DrainMainThreadQueue (mainThreadQueue);
          return 1;
        }

        var exitCode = 0;

        try {
          SendTextAsync (ws, pendingSends, mainThreadQueue, "Hello from ClientLifecycle.");
        }
        catch (Exception ex) {
          Console.WriteLine ("SendAsync failed before the completion callback: " + ex.Message);
          exitCode = 1;
        }

        pendingSends.Signal ();

        if (!WaitForSends (pendingSends, mainThreadQueue, SendTimeout)) {
          Console.WriteLine ("Timed out waiting for SendAsync completion.");
          exitCode = 1;
        }

        if (!WaitForSignal (messageReceived, mainThreadQueue, MessageTimeout))
          Console.WriteLine ("No message arrived before the sample timeout.");

        Console.WriteLine ("Closing with CloseAsync...");
        ws.CloseAsync (CloseStatusCode.Normal, "sample complete");

        if (!WaitForSignal (closed, mainThreadQueue, CloseTimeout))
          Console.WriteLine ("Timed out waiting for OnClose.");

        DrainMainThreadQueue (mainThreadQueue);
        return Interlocked.CompareExchange (ref errorSeen, 0, 0) == 0 ? exitCode : 1;
      }
    }

    private static void SendTextAsync (
      WebSocket ws,
      CountdownEvent pendingSends,
      ConcurrentQueue<Action> mainThreadQueue,
      string message
    )
    {
      pendingSends.AddCount ();

      try {
        // SendAsync completion can also arrive on a websocket-sharp worker
        // thread, so this sample routes its output through the same queue.
        ws.SendAsync (
          message,
          completed => {
            mainThreadQueue.Enqueue (
              () => Console.WriteLine (completed ? "[send] Completed." : "[send] Failed.")
            );

            pendingSends.Signal ();
          }
        );
      }
      catch {
        pendingSends.Signal ();
        throw;
      }
    }

    private static bool WaitForOpenOrFailure (
      ManualResetEvent opened,
      ManualResetEvent failed,
      ManualResetEvent closed,
      ConcurrentQueue<Action> mainThreadQueue,
      TimeSpan timeout
    )
    {
      var deadline = DateTime.UtcNow + timeout;

      while (DateTime.UtcNow < deadline) {
        DrainMainThreadQueue (mainThreadQueue);

        if (opened.WaitOne (50))
          return true;

        if (failed.WaitOne (0) || closed.WaitOne (0))
          return false;
      }

      DrainMainThreadQueue (mainThreadQueue);
      return false;
    }

    private static bool WaitForSends (
      CountdownEvent pendingSends,
      ConcurrentQueue<Action> mainThreadQueue,
      TimeSpan timeout
    )
    {
      var deadline = DateTime.UtcNow + timeout;

      while (DateTime.UtcNow < deadline) {
        DrainMainThreadQueue (mainThreadQueue);

        if (pendingSends.Wait (50))
          return true;
      }

      DrainMainThreadQueue (mainThreadQueue);
      return false;
    }

    private static bool WaitForSignal (
      ManualResetEvent signal,
      ConcurrentQueue<Action> mainThreadQueue,
      TimeSpan timeout
    )
    {
      var deadline = DateTime.UtcNow + timeout;

      while (DateTime.UtcNow < deadline) {
        DrainMainThreadQueue (mainThreadQueue);

        if (signal.WaitOne (50))
          return true;
      }

      DrainMainThreadQueue (mainThreadQueue);
      return false;
    }

    private static void DrainMainThreadQueue (ConcurrentQueue<Action> mainThreadQueue)
    {
      Action action;

      while (mainThreadQueue.TryDequeue (out action))
        action ();
    }
  }
}
