using System;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace WebSocketSharp.Tests
{
  internal static class TestCertificates
  {
    public static X509Certificate2 CreateSelfSignedServerCertificate ()
    {
      using (var key = RSA.Create (2048)) {
        var request = new CertificateRequest (
          "CN=localhost",
          key,
          HashAlgorithmName.SHA256,
          RSASignaturePadding.Pkcs1
        );

        var san = new SubjectAlternativeNameBuilder ();
        san.AddDnsName ("localhost");
        san.AddIpAddress (IPAddress.Loopback);
        request.CertificateExtensions.Add (san.Build ());
        request.CertificateExtensions.Add (
          new X509EnhancedKeyUsageExtension (
            new OidCollection {
              new Oid ("1.3.6.1.5.5.7.3.1")
            },
            false
          )
        );

        var notBefore = DateTimeOffset.UtcNow.AddMinutes (-5);
        var notAfter = DateTimeOffset.UtcNow.AddDays (1);

        using (var certificate = request.CreateSelfSigned (notBefore, notAfter)) {
          var pfx = certificate.Export (X509ContentType.Pfx, String.Empty);

          return new X509Certificate2 (
            pfx,
            String.Empty,
            X509KeyStorageFlags.Exportable
          );
        }
      }
    }
  }
}
