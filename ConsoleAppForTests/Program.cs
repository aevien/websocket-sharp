using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace ConsoleAppForTests
{
    class Program
    {
        static void Main(string[] args)
        {
            var wssv = new WebSocketServer(5000);

#if DEBUG
            // To change the logging level.
            wssv.Log.Level = LogLevel.Trace;

            // To change the wait time for the response to the WebSocket Ping or Close.
            //wssv.WaitTime = TimeSpan.FromSeconds (2);

            // Not to remove the inactive sessions periodically.
            //wssv.KeepClean = false;
#endif
            wssv.AddWebSocketService<Echo>("/echo");

            wssv.Start();

            if (wssv.IsListening)
            {
                Console.WriteLine("Listening on port {0}, and providing WebSocket services:", wssv.Port);

                foreach (var path in wssv.WebSocketServices.Paths)
                    Console.WriteLine("- {0}", path);
            }

            Console.WriteLine("\nPress Enter key to stop the server...");
            Console.ReadLine();

            wssv.Stop();
        }
    }
}
