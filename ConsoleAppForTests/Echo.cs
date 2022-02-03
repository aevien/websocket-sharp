using System;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace ConsoleAppForTests
{
    class Echo : WebSocketServiceBehavior
    {
        protected override void OnMessage(MessageEventArgs e)
        {
            var name = Context.QueryString["name"];
            Send(!name.IsNullOrEmpty() ? $"\"{e.Data}\" to {name}" : e.Data);
            Console.WriteLine(e.Data);
        }
    }
}