using System;
using System.Linq;
using NUnit.Framework;
using WebSocketSharp;

namespace WebSocketSharp.Tests
{
  [TestFixture]
  public sealed class AssemblyIdentityTests
  {
    [Test]
    public void BuiltAssemblyKeepsUnityPluginIdentity ()
    {
      var name = typeof (WebSocket).Assembly.GetName ();

      Assert.That (name.Name, Is.EqualTo ("websocket-sharp"));
      Assert.That (name.Version, Is.EqualTo (new Version (1, 0, 2, 32832)));
      Assert.That (GetPublicKeyToken (name.GetPublicKeyToken ()), Is.EqualTo ("5660b08a1845a91e"));
    }

    private static string GetPublicKeyToken (byte[] token)
    {
      return String.Concat (token.Select (value => value.ToString ("x2")));
    }
  }
}
