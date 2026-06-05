using System;
using System.Net;
using System.Threading;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace ServerWithLimits
{
  internal static class Program
  {
    private const int Port = 4649;

    private static readonly ManualResetEventSlim ShutdownRequested =
      new ManualResetEventSlim (false);

    private static void Main (string[] args)
    {
      var server = new WebSocketServer (IPAddress.Loopback, Port) {
        // Bounds peers that connect and then do not finish the HTTP/WebSocket
        // handshake. On secure servers this also bounds the TLS handshake.
        HandshakeTimeout = TimeSpan.FromSeconds (5)
      };

      server.AddWebSocketService<Echo> (
        "/Echo",
        service => {
          // Keep a single WebSocket frame reasonably small. This prevents one
          // read from reserving too much memory before the message is complete.
          service.MaxFramePayloadLength = 1024 * 1024;

          // Bound the assembled message size across one or more fragments.
          // Fragmentation should not let a peer bypass the frame limit.
          service.MaxMessagePayloadLength = 4 * 1024 * 1024;

          // Once a peer starts a frame, require the rest of that frame to arrive
          // promptly. Idle open connections are not closed by this timeout.
          service.FrameReadTimeout = TimeSpan.FromSeconds (5);

          // Limit queued async sends so a slow client cannot grow server memory
          // without bound while the application keeps calling SendAsync.
          service.MaxAsyncSendQueueLength = 128;

          // Limit queued OnMessage events so a fast sender cannot outrun the
          // application message handler indefinitely.
          service.MaxMessageEventQueueLength = 512;
        }
      );

      Console.CancelKeyPress += (sender, e) => {
        e.Cancel = true;
        ShutdownRequested.Set ();
      };

      server.Start ();

      if (server.IsListening) {
        Console.WriteLine ("Listening on ws://localhost:{0}/Echo", server.Port);
        Console.WriteLine ("Press Enter or Ctrl+C to stop the server.");
      }

      WaitForShutdownRequest (server);
    }

    private static void WaitForShutdownRequest (WebSocketServer server)
    {
      ThreadPool.QueueUserWorkItem (_ => {
        Console.ReadLine ();
        ShutdownRequested.Set ();
      });

      ShutdownRequested.Wait ();

      // Stop performs the graceful server shutdown path: listeners stop
      // accepting new clients and existing sessions are closed.
      server.Stop ();
    }

    private sealed class Echo : WebSocketBehavior
    {
      protected override void OnMessage (MessageEventArgs e)
      {
        if (e.IsBinary)
          Send (e.RawData);
        else
          Send (e.Data);
      }
    }
  }
}
