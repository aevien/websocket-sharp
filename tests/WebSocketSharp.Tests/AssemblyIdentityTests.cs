using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
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
      var assembly = typeof (WebSocket).Assembly;
      var name = assembly.GetName ();

      Assert.That (name.Name, Is.EqualTo ("websocket-sharp"));
      Assert.That (name.Version, Is.EqualTo (new Version (1, 0, 2, 32832)));
      Assert.That (GetPublicKeyToken (name.GetPublicKeyToken ()), Is.EqualTo ("5660b08a1845a91e"));
    }

    [Test]
    public void BuiltAssemblyKeepsExpectedReleaseMetadata ()
    {
      var assembly = typeof (WebSocket).Assembly;
      var fileVersion = GetCustomAttribute<AssemblyFileVersionAttribute> (assembly);
      var informationalVersion = GetCustomAttribute<AssemblyInformationalVersionAttribute> (assembly);
      var product = GetCustomAttribute<AssemblyProductAttribute> (assembly);
      var fileInfo = FileVersionInfo.GetVersionInfo (assembly.Location);

      Assert.That (fileVersion.Version, Is.EqualTo ("1.2.0.0"));
      Assert.That (informationalVersion.InformationalVersion, Is.EqualTo ("1.2.0.0"));
      Assert.That (product.Product, Is.EqualTo ("websocket-sharp.dll"));
      Assert.That (fileInfo.FileVersion, Is.EqualTo ("1.2.0.0"));
      Assert.That (fileInfo.ProductVersion, Is.EqualTo ("1.2.0.0"));
      Assert.That (fileInfo.ProductName, Is.EqualTo ("websocket-sharp.dll"));
    }

    private static string GetPublicKeyToken (byte[] token)
    {
      return String.Concat (token.Select (value => value.ToString ("x2")));
    }

    private static T GetCustomAttribute<T> (Assembly assembly)
      where T : Attribute
    {
      var attribute = assembly.GetCustomAttributes (typeof (T), false).SingleOrDefault ();

      Assert.That (attribute, Is.Not.Null, typeof (T).Name + " is missing.");

      return (T) attribute;
    }
  }
}
