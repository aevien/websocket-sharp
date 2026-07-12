using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using NUnit.Framework;
using WebSocketSharp.Net;
using WebSocketSharp.Server;

namespace WebSocketSharp.Tests
{
  [TestFixture]
  [NonParallelizable]
  public sealed class RedirectPolicyTests
  {
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds (5);

    [Test]
    public void CrossOriginRedirectStripsCredentialsCookiesAndUserHeaders ()
    {
      var capture = new HeaderCapture ();

      using (
        var target = LoopbackServer.Start (
          server => server.AddWebSocketService<CaptureBehavior> (
            "/echo",
            behavior => behavior.Configure (capture)
          )
        )
      )
      using (var redirect = RedirectServer.StartSingle (target.GetUrl ("/echo"), 302))
      using (var client = CreateSensitiveClient (redirect.GetUrl ("/start"))) {
        client.Connect ();

        Assert.That (client.ReadyState, Is.EqualTo (WebSocketState.Open));
        Assert.That (capture.Opened.Wait (Timeout), Is.True);
        Assert.That (GetHeader (redirect.FirstRequest, "Authorization"), Is.Not.Null);
        Assert.That (GetHeader (redirect.FirstRequest, "Cookie"), Does.Contain ("cookie-secret"));
        Assert.That (GetHeader (redirect.FirstRequest, "X-MST-Secret"), Is.EqualTo ("header-secret"));
        Assert.That (capture.Authorization, Is.Null);
        Assert.That (capture.Cookie, Is.Null);
        Assert.That (capture.UserSecret, Is.Null);

        client.Close ();

        capture.Authorization = "not cleared";
        capture.Cookie = "not cleared";
        capture.UserSecret = "not cleared";

        client.Connect ();

        Assert.That (
          SpinWait.SpinUntil (() => capture.OpenCount == 2, Timeout),
          Is.True
        );
        Assert.That (capture.Authorization, Is.Null);
        Assert.That (capture.Cookie, Is.Null);
        Assert.That (capture.UserSecret, Is.Null);

        client.Close ();
      }
    }

    [Test]
    public void RelativeSameOriginRedirectPreservesSensitiveHeaders ()
    {
      using (var server = RedirectServer.StartRelativeThenAccept (307))
      using (var client = CreateSensitiveClient (server.GetUrl ("/start"))) {
        client.Connect ();

        Assert.That (client.ReadyState, Is.EqualTo (WebSocketState.Open));
        Assert.That (server.Completed.Wait (Timeout), Is.True);
        Assert.That (GetRequestTarget (server.SecondRequest), Is.EqualTo ("/final"));
        Assert.That (GetHeader (server.SecondRequest, "Authorization"), Is.Not.Null);
        Assert.That (GetHeader (server.SecondRequest, "Cookie"), Does.Contain ("cookie-secret"));
        Assert.That (GetHeader (server.SecondRequest, "X-MST-Secret"), Is.EqualTo ("header-secret"));

        client.Close ();
      }
    }

    [Test]
    public void RedirectLoopStopsAtConfiguredLimit ()
    {
      using (var server = RedirectServer.StartLoop (4))
      using (var client = new WebSocket (server.GetUrl ("/loop"))) {
        client.EnableRedirection = true;
        client.MaxRedirections = 3;
        client.ConnectionTimeout = Timeout;
        client.Log.Output = (data, path) => { };

        client.Connect ();

        Assert.That (client.ReadyState, Is.Not.EqualTo (WebSocketState.Open));
        Assert.That (server.Completed.Wait (Timeout), Is.True);
        Assert.That (server.RequestCount, Is.EqualTo (4));
      }
    }

    [Test]
    public void RelativeRedirectUsesRedirectedPathForDigestAuthentication ()
    {
      using (var server = RedirectServer.StartRelativeThenDigest ())
      using (var client = new WebSocket (server.GetUrl ("/start"))) {
        client.EnableRedirection = true;
        client.ConnectionTimeout = Timeout;
        client.Log.Output = (data, path) => { };
        client.SetCredentials ("digest-user", "digest-secret", false);

        client.Connect ();

        Assert.That (client.ReadyState, Is.EqualTo (WebSocketState.Open));
        Assert.That (server.Completed.Wait (Timeout), Is.True);
        Assert.That (
          GetHeader (server.ThirdRequest, "Authorization"),
          Does.Contain ("uri=\"/final\"")
        );

        client.Close ();
      }
    }

    [Test]
    public void CrossOriginRedirectUsesCurrentConnectTargetForDigestProxy ()
    {
      var capture = new HeaderCapture ();

      using (
        var target = LoopbackServer.Start (
          server => server.AddWebSocketService<CaptureBehavior> (
            "/echo",
            behavior => behavior.Configure (capture)
          )
        )
      )
      using (var redirect = RedirectServer.StartSingle (target.GetUrl ("/echo"), 302))
      using (var proxy = LoopbackProxyServer.StartDigestAuthTunnel ())
      using (var client = new WebSocket (redirect.GetUrl ("/start"))) {
        client.EnableRedirection = true;
        client.ConnectionTimeout = Timeout;
        client.Log.Output = (data, path) => { };
        client.SetProxy (proxy.GetUrl (), "proxy-user", "proxy-secret");

        client.Connect ();

        Assert.That (client.ReadyState, Is.EqualTo (WebSocketState.Open));
        Assert.That (capture.Opened.Wait (Timeout), Is.True);
        Assert.That (proxy.AuthorizedConnectCount, Is.EqualTo (2));
        Assert.That (proxy.TunnelCount, Is.EqualTo (2));
        Assert.That (
          proxy.LastProxyAuthorization,
          Does.Contain ("uri=\"" + proxy.LastConnectTarget + "\"")
        );

        client.Close ();
      }
    }

    [TestCase (301)]
    [TestCase (302)]
    [TestCase (303)]
    [TestCase (307)]
    [TestCase (308)]
    public void SupportedRedirectStatusIsFollowed (int statusCode)
    {
      var capture = new HeaderCapture ();

      using (
        var target = LoopbackServer.Start (
          server => server.AddWebSocketService<CaptureBehavior> (
            "/echo",
            behavior => behavior.Configure (capture)
          )
        )
      )
      using (var redirect = RedirectServer.StartSingle (target.GetUrl ("/echo"), statusCode))
      using (var client = new WebSocket (redirect.GetUrl ("/start"))) {
        client.EnableRedirection = true;
        client.ConnectionTimeout = Timeout;
        client.Log.Output = (data, path) => { };
        client.Connect ();

        Assert.That (client.ReadyState, Is.EqualTo (WebSocketState.Open));
        Assert.That (capture.Opened.Wait (Timeout), Is.True);

        client.Close ();
      }
    }

    [Test]
    public void RedirectSettingsHaveSafeDefaultsAndValidateValues ()
    {
      using (var client = new WebSocket ("ws://127.0.0.1:1/")) {
        Assert.That (client.AllowInsecureRedirection, Is.False);
        Assert.That (client.MaxRedirections, Is.EqualTo (5));

        client.AllowInsecureRedirection = true;
        client.MaxRedirections = 0;

        Assert.That (client.AllowInsecureRedirection, Is.True);
        Assert.That (client.MaxRedirections, Is.EqualTo (0));
        Assert.Throws<ArgumentOutOfRangeException> (() => client.MaxRedirections = -1);
        Assert.Throws<ArgumentOutOfRangeException> (() => client.MaxRedirections = 101);
      }
    }

    [TestCase (false, false)]
    [TestCase (true, true)]
    public void SecureToInsecureRedirectRequiresExplicitOptIn (
      bool allowInsecure,
      bool shouldOpen
    )
    {
      var capture = new HeaderCapture ();

      using (var certificate = TestCertificates.CreateSelfSignedServerCertificate ())
      using (
        var target = LoopbackServer.Start (
          server => server.AddWebSocketService<CaptureBehavior> (
            "/echo",
            behavior => behavior.Configure (capture)
          )
        )
      )
      using (
        var redirect = SecureRedirectServer.Start (
          certificate,
          target.GetUrl ("/echo")
        )
      )
      using (var client = new WebSocket (redirect.GetUrl ())) {
        client.EnableRedirection = true;
        client.AllowInsecureRedirection = allowInsecure;
        client.ConnectionTimeout = Timeout;
        client.Log.Output = (data, path) => { };
        client.SslConfiguration.ServerCertificateValidationCallback =
          (sender, remoteCertificate, chain, errors) =>
            remoteCertificate != null
            && String.Equals (
                 remoteCertificate.GetCertHashString (),
                 certificate.Thumbprint,
                 StringComparison.OrdinalIgnoreCase
               );

        client.Connect ();

        Assert.That (
          client.ReadyState == WebSocketState.Open,
          Is.EqualTo (shouldOpen)
        );

        if (shouldOpen)
          Assert.That (capture.Opened.Wait (Timeout), Is.True);

        Assert.That (capture.OpenCount, Is.EqualTo (shouldOpen ? 1 : 0));

        if (shouldOpen)
          client.Close ();
      }
    }

    [Test]
    public void CrossOriginSecureRedirectRetargetsTlsConfiguration ()
    {
      var capture = new HeaderCapture ();

      using (var certificate = TestCertificates.CreateSelfSignedServerCertificate ())
      using (
        var target = LoopbackServer.StartSecure (
          certificate,
          server => server.AddWebSocketService<CaptureBehavior> (
            "/echo",
            behavior => behavior.Configure (capture)
          )
        )
      )
      using (
        var redirect = SecureRedirectServer.Start (
          certificate,
          target.GetSecureUrl ("/echo")
        )
      )
      using (var client = new WebSocket (redirect.GetUrl ("localhost"))) {
        client.EnableRedirection = true;
        client.ConnectionTimeout = Timeout;
        client.Log.Output = (data, path) => { };
        client.SslConfiguration.ServerCertificateValidationCallback =
          (sender, remoteCertificate, chain, errors) =>
            remoteCertificate != null
            && String.Equals (
                 remoteCertificate.GetCertHashString (),
                 certificate.Thumbprint,
                 StringComparison.OrdinalIgnoreCase
               );

        client.Connect ();

        Assert.That (client.ReadyState, Is.EqualTo (WebSocketState.Open));
        Assert.That (capture.Opened.Wait (Timeout), Is.True);
        Assert.That (client.SslConfiguration.TargetHost, Is.EqualTo ("localhost"));

        client.Close ();
      }
    }

    private static string ComputeAccept (string key)
    {
      var source = key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

      using (var sha1 = SHA1.Create ())
        return Convert.ToBase64String (sha1.ComputeHash (Encoding.ASCII.GetBytes (source)));
    }

    private static WebSocket CreateSensitiveClient (string url)
    {
      var client = new WebSocket (url);

      client.EnableRedirection = true;
      client.ConnectionTimeout = Timeout;
      client.Log.Output = (data, path) => { };
      client.SetCredentials ("redirect-user", "credential-secret", true);
      client.SetCookie (new WebSocketSharp.Net.Cookie ("session", "cookie-secret"));
      client.SetUserHeader ("X-MST-Secret", "header-secret");

      return client;
    }

    private static string GetHeader (string request, string name)
    {
      if (request == null)
        return null;

      var prefix = name + ":";
      var lines = request.Split (new[] { "\r\n" }, StringSplitOptions.None);

      foreach (var line in lines) {
        if (line.StartsWith (prefix, StringComparison.OrdinalIgnoreCase))
          return line.Substring (prefix.Length).Trim ();
      }

      return null;
    }

    private static string GetRequestTarget (string request)
    {
      var firstLineEnd = request.IndexOf ("\r\n", StringComparison.Ordinal);
      var firstLine = firstLineEnd >= 0 ? request.Substring (0, firstLineEnd) : request;
      var parts = firstLine.Split (' ');

      return parts.Length > 1 ? parts[1] : null;
    }

    private static string ReadHeader (Stream stream)
    {
      using (var data = new MemoryStream ()) {
        var current = new byte[1];
        var previous = new byte[3];

        while (data.Length <= 8192) {
          var read = stream.Read (current, 0, 1);

          if (read <= 0)
            throw new EndOfStreamException ();

          data.WriteByte (current[0]);

          if (previous[0] == '\r'
              && previous[1] == '\n'
              && previous[2] == '\r'
              && current[0] == '\n')
            return Encoding.ASCII.GetString (data.ToArray ());

          previous[0] = previous[1];
          previous[1] = previous[2];
          previous[2] = current[0];
        }
      }

      throw new InvalidDataException ("The request header is too large.");
    }

    private static void WriteRedirect (Stream stream, string location, int statusCode)
    {
      var reason = statusCode == 307 ? "Temporary Redirect" : "Found";
      var response = String.Format (
        "HTTP/1.1 {0} {1}\r\nLocation: {2}\r\nConnection: close\r\nContent-Length: 0\r\n\r\n",
        statusCode,
        reason,
        location
      );
      var bytes = Encoding.ASCII.GetBytes (response);

      stream.Write (bytes, 0, bytes.Length);
      stream.Flush ();
    }

    internal sealed class HeaderCapture
    {
      internal readonly ManualResetEventSlim Opened = new ManualResetEventSlim ();
      internal string Authorization;
      internal string Cookie;
      internal int OpenCount;
      internal string UserSecret;
    }

    public sealed class CaptureBehavior : WebSocketBehavior
    {
      private HeaderCapture _capture;

      internal void Configure (HeaderCapture capture)
      {
        _capture = capture;
      }

      protected override void OnOpen ()
      {
        _capture.Authorization = Headers["Authorization"];
        _capture.Cookie = Headers["Cookie"];
        _capture.UserSecret = Headers["X-MST-Secret"];
        Interlocked.Increment (ref _capture.OpenCount);
        _capture.Opened.Set ();
      }
    }

    private sealed class RedirectServer : IDisposable
    {
      private enum Mode
      {
        Single,
        RelativeThenAccept,
        RelativeThenDigest,
        Loop
      }

      private readonly int _expectedRequests;
      private readonly TcpListener _listener;
      private readonly string _location;
      private readonly Mode _mode;
      private readonly int _statusCode;
      private readonly Thread _thread;
      private volatile bool _disposed;

      private RedirectServer (
        Mode mode,
        string location,
        int statusCode,
        int expectedRequests
      )
      {
        _mode = mode;
        _location = location;
        _statusCode = statusCode;
        _expectedRequests = expectedRequests;
        _listener = new TcpListener (IPAddress.Loopback, 0);
        _listener.Start ();
        Port = ((IPEndPoint) _listener.LocalEndpoint).Port;
        Completed = new ManualResetEventSlim ();
        _thread = new Thread (Run);
        _thread.IsBackground = true;
        _thread.Start ();
      }

      internal ManualResetEventSlim Completed { get; private set; }

      internal string FirstRequest { get; private set; }

      private int Port { get; set; }

      internal int RequestCount { get; private set; }

      internal string SecondRequest { get; private set; }

      internal string ThirdRequest { get; private set; }

      internal static RedirectServer StartLoop (int expectedRequests)
      {
        return new RedirectServer (Mode.Loop, "/loop", 302, expectedRequests);
      }

      internal static RedirectServer StartRelativeThenAccept (int statusCode)
      {
        return new RedirectServer (
          Mode.RelativeThenAccept,
          "/final",
          statusCode,
          2
        );
      }

      internal static RedirectServer StartRelativeThenDigest ()
      {
        return new RedirectServer (
          Mode.RelativeThenDigest,
          "/final",
          307,
          3
        );
      }

      internal static RedirectServer StartSingle (string location, int statusCode)
      {
        return new RedirectServer (Mode.Single, location, statusCode, 1);
      }

      public void Dispose ()
      {
        _disposed = true;
        _listener.Stop ();
        _thread.Join (1000);
        Completed.Dispose ();
      }

      internal string GetUrl (string path)
      {
        return String.Format ("ws://127.0.0.1:{0}{1}", Port, path);
      }

      private void Run ()
      {
        try {
          for (var i = 0; i < _expectedRequests && !_disposed; i++) {
            using (var client = _listener.AcceptTcpClient ()) {
              client.ReceiveTimeout = 5000;
              client.SendTimeout = 5000;

              var stream = client.GetStream ();
              var request = ReadHeader (stream);

              RequestCount++;

              if (i == 0)
                FirstRequest = request;
              else if (i == 1)
                SecondRequest = request;
              else if (i == 2)
                ThirdRequest = request;

              if ((_mode == Mode.RelativeThenAccept && i == 1)
                  || (_mode == Mode.RelativeThenDigest && i == 2)) {
                WriteAccept (stream, request);
                Completed.Set ();

                while (!_disposed && !IsDisconnected (client))
                  Thread.Sleep (10);

                return;
              }

              if (_mode == Mode.RelativeThenDigest && i == 1) {
                WriteDigestChallenge (stream);

                continue;
              }

              WriteRedirect (stream, _location, _statusCode);
            }
          }
        }
        catch {
        }
        finally {
          Completed.Set ();
        }
      }

      private static bool IsDisconnected (TcpClient client)
      {
        try {
          return client.Client.Poll (0, SelectMode.SelectRead)
                 && client.Client.Available == 0;
        }
        catch {
          return true;
        }
      }

      private static void WriteAccept (Stream stream, string request)
      {
        var key = GetHeader (request, "Sec-WebSocket-Key");
        var response = "HTTP/1.1 101 Switching Protocols\r\n"
                       + "Upgrade: websocket\r\n"
                       + "Connection: Upgrade\r\n"
                       + "Sec-WebSocket-Accept: " + ComputeAccept (key) + "\r\n\r\n";
        var bytes = Encoding.ASCII.GetBytes (response);

        stream.Write (bytes, 0, bytes.Length);
        stream.Flush ();
      }

      private static void WriteDigestChallenge (Stream stream)
      {
        var response = "HTTP/1.1 401 Unauthorized\r\n"
                       + "WWW-Authenticate: Digest realm=\"redirect\", "
                       + "nonce=\"fixed-nonce\", algorithm=MD5, qop=\"auth\"\r\n"
                       + "Connection: close\r\n"
                       + "Content-Length: 0\r\n\r\n";
        var bytes = Encoding.ASCII.GetBytes (response);

        stream.Write (bytes, 0, bytes.Length);
        stream.Flush ();
      }
    }

    private sealed class SecureRedirectServer : IDisposable
    {
      private readonly X509Certificate2 _certificate;
      private readonly TcpListener _listener;
      private readonly string _location;
      private readonly Thread _thread;

      private SecureRedirectServer (X509Certificate2 certificate, string location)
      {
        _certificate = certificate;
        _location = location;
        _listener = new TcpListener (IPAddress.Loopback, 0);
        _listener.Start ();
        Port = ((IPEndPoint) _listener.LocalEndpoint).Port;
        _thread = new Thread (Run);
        _thread.IsBackground = true;
        _thread.Start ();
      }

      private int Port { get; set; }

      internal static SecureRedirectServer Start (
        X509Certificate2 certificate,
        string location
      )
      {
        return new SecureRedirectServer (certificate, location);
      }

      public void Dispose ()
      {
        _listener.Stop ();
        _thread.Join (1000);
      }

      internal string GetUrl ()
      {
        return GetUrl ("127.0.0.1");
      }

      internal string GetUrl (string host)
      {
        return String.Format ("wss://{0}:{1}/start", host, Port);
      }

      private void Run ()
      {
        try {
          using (var client = _listener.AcceptTcpClient ())
          using (var ssl = new SslStream (client.GetStream (), false)) {
            ssl.AuthenticateAsServer (
              _certificate,
              false,
              SslProtocols.Tls12,
              false
            );

            ReadHeader (ssl);
            WriteRedirect (ssl, _location, 302);
          }
        }
        catch {
        }
      }
    }
  }
}
