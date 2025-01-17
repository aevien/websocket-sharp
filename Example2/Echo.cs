using System;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace Example2
{
  public class Echo : WebSocketServiceBehavior
  {
    protected override void OnMessage (MessageEventArgs e)
    {
      var name = Context.QueryString["name"];
      Send (!name.IsNullOrEmpty () ? String.Format ("\"{0}\" to {1}", e.Data, name) : e.Data);
    }
  }
}
