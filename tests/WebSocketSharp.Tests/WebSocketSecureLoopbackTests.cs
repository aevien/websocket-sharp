using System;
using System.Net.Security;
using System.Threading;
using NUnit.Framework;
using WebSocketSharp.Server;

namespace WebSocketSharp.Tests
{
  [TestFixture]
  [NonParallelizable]
  public sealed class WebSocketSecureLoopbackTests
  {
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds (5);

    [Test]
    public void DefaultValidationRejectsSelfSignedServerCertificate ()
    {
      using (var certificate = TestCertificates.CreateSelfSignedServerCertificate ())
      using (var server = LoopbackServer.StartSecure (
        certificate,
        s => {
          s.Log.Output = (data, path) => { };
          s.AddWebSocketService<WebSocketLoopbackTests.EchoBehavior> ("/echo");
        }))
      using (var client = new WebSocket (server.GetSecureUrl ("/echo"))) {
        var opened = false;

        client.Log.Output = (data, path) => { };
        client.OnOpen += (sender, e) => opened = true;

        client.Connect ();

        Assert.That (opened, Is.False);
        Assert.That (client.ReadyState, Is.Not.EqualTo (WebSocketState.Open));
      }
    }

    [Test]
    public void CustomValidationAllowsSelfSignedServerCertificateAndEcho ()
    {
      using (var certificate = TestCertificates.CreateSelfSignedServerCertificate ())
      using (var server = LoopbackServer.StartSecure (
        certificate,
        s => s.AddWebSocketService<WebSocketLoopbackTests.EchoBehavior> ("/echo")))
      using (var client = new WebSocket (server.GetSecureUrl ("/echo"))) {
        var opened = new ManualResetEventSlim ();
        var received = new ManualResetEventSlim ();
        var callbackCalled = new ManualResetEventSlim ();
        var actual = default (string);
        var errors = SslPolicyErrors.None;
        var error = default (Exception);
        var expectedThumbprint = certificate.Thumbprint;

        client.SslConfiguration.ServerCertificateValidationCallback =
          (sender, remoteCertificate, chain, sslPolicyErrors) => {
            errors = sslPolicyErrors;
            callbackCalled.Set ();

            return remoteCertificate != null
                   && String.Equals (
                        remoteCertificate.GetCertHashString (),
                        expectedThumbprint,
                        StringComparison.OrdinalIgnoreCase
                      );
          };

        client.OnOpen += (sender, e) => opened.Set ();
        client.OnMessage += (sender, e) => {
          actual = e.Data;
          received.Set ();
        };
        client.OnError += (sender, e) => {
          error = e.Exception ?? new Exception (e.Message);
          received.Set ();
        };

        client.Connect ();

        WaitFor (callbackCalled, "The custom certificate validation callback was not called.");
        WaitFor (opened, "The secure client did not open.");
        AssertNoError (error);
        Assert.That (errors, Is.Not.EqualTo (SslPolicyErrors.None));
        Assert.That (client.IsSecure, Is.True);

        client.Send ("secure hello");

        WaitFor (received, "The secure text echo was not received.");
        AssertNoError (error);
        Assert.That (actual, Is.EqualTo ("secure hello"));

        client.Close ();
      }
    }

    private static void AssertNoError (Exception error)
    {
      if (error != null)
        Assert.Fail (error.ToString ());
    }

    private static void WaitFor (ManualResetEventSlim signal, string message)
    {
      Assert.That (signal.Wait (Timeout), Is.True, message);
    }
  }
}
