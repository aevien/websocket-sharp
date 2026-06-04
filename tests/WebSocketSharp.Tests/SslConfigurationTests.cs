using System.Net.Security;
using NUnit.Framework;
using WebSocketSharp.Net;

namespace WebSocketSharp.Tests
{
  [TestFixture]
  public sealed class SslConfigurationTests
  {
    [Test]
    public void ClientDefaultServerCertificateValidationRejectsPolicyErrors ()
    {
      var configuration = new ClientSslConfiguration ("localhost");
      var callback = configuration.ServerCertificateValidationCallback;

      Assert.That (
        callback (null, null, null, SslPolicyErrors.RemoteCertificateNameMismatch),
        Is.False
      );
      Assert.That (
        callback (null, null, null, SslPolicyErrors.RemoteCertificateChainErrors),
        Is.False
      );
      Assert.That (
        callback (null, null, null, SslPolicyErrors.RemoteCertificateNotAvailable),
        Is.False
      );
    }

    [Test]
    public void ClientDefaultServerCertificateValidationAcceptsNoPolicyErrors ()
    {
      var configuration = new ClientSslConfiguration ("localhost");

      Assert.That (
        configuration.ServerCertificateValidationCallback (
          null,
          null,
          null,
          SslPolicyErrors.None
        ),
        Is.True
      );
    }

    [Test]
    public void ClientCustomServerCertificateValidationIsPreserved ()
    {
      var configuration = new ClientSslConfiguration ("localhost");

      configuration.ServerCertificateValidationCallback = (sender, certificate, chain, errors) => true;

      Assert.That (
        configuration.ServerCertificateValidationCallback (
          null,
          null,
          null,
          SslPolicyErrors.RemoteCertificateChainErrors
        ),
        Is.True
      );
    }

    [Test]
    public void ClientCopyPreservesCustomServerCertificateValidation ()
    {
      var source = new ClientSslConfiguration ("localhost");

      source.ServerCertificateValidationCallback = (sender, certificate, chain, errors) => true;

      var copy = new ClientSslConfiguration (source);

      Assert.That (
        copy.ServerCertificateValidationCallback (
          null,
          null,
          null,
          SslPolicyErrors.RemoteCertificateChainErrors
        ),
        Is.True
      );
    }

    [Test]
    public void ServerDefaultClientCertificateValidationRejectsPolicyErrors ()
    {
      var configuration = new ServerSslConfiguration ();
      var callback = configuration.ClientCertificateValidationCallback;

      Assert.That (
        callback (null, null, null, SslPolicyErrors.RemoteCertificateNameMismatch),
        Is.False
      );
      Assert.That (
        callback (null, null, null, SslPolicyErrors.RemoteCertificateChainErrors),
        Is.False
      );
      Assert.That (
        callback (null, null, null, SslPolicyErrors.RemoteCertificateNotAvailable),
        Is.False
      );
    }

    [Test]
    public void ServerDefaultClientCertificateValidationAcceptsNoPolicyErrors ()
    {
      var configuration = new ServerSslConfiguration ();

      Assert.That (
        configuration.ClientCertificateValidationCallback (
          null,
          null,
          null,
          SslPolicyErrors.None
        ),
        Is.True
      );
    }

    [Test]
    public void ServerCustomClientCertificateValidationIsPreserved ()
    {
      var configuration = new ServerSslConfiguration ();

      configuration.ClientCertificateValidationCallback = (sender, certificate, chain, errors) => true;

      Assert.That (
        configuration.ClientCertificateValidationCallback (
          null,
          null,
          null,
          SslPolicyErrors.RemoteCertificateChainErrors
        ),
        Is.True
      );
    }

    [Test]
    public void ServerCopyPreservesCustomClientCertificateValidation ()
    {
      var source = new ServerSslConfiguration ();

      source.ClientCertificateValidationCallback = (sender, certificate, chain, errors) => true;

      var copy = new ServerSslConfiguration (source);

      Assert.That (
        copy.ClientCertificateValidationCallback (
          null,
          null,
          null,
          SslPolicyErrors.RemoteCertificateChainErrors
        ),
        Is.True
      );
    }
  }
}
