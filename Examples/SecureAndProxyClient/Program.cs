using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using WebSocketSharp;

namespace SecureAndProxyClient
{
  internal static class Program
  {
    private const string SampleHeaderName = "X-Client-Example";
    private const string SampleHeaderValue = "SecureAndProxyClient";

    private static int Main (string[] args)
    {
      ClientOptions options;

      if (!ClientOptions.TryParse (args, out options)) {
        PrintUsage ();
        return 1;
      }

      if (options.ShowHelp || options.ServerUri == null) {
        PrintUsage ();
        return 0;
      }

      using (var ws = new WebSocket (options.ServerUri.OriginalString)) {
        // Request permessage-deflate. The server may decline it during the
        // handshake; websocket-sharp then continues without compression.
        ws.Compression = options.UseCompression
                         ? CompressionMethod.Deflate
                         : CompressionMethod.None;

        // Bounds TCP connect, proxy CONNECT, WebSocket handshake, and for wss://
        // also the TLS handshake.
        ws.ConnectionTimeout = options.ConnectionTimeout;

        if (!String.IsNullOrEmpty (options.Origin))
          ws.Origin = options.Origin;

        // User headers are sent in the opening handshake.
        ws.SetUserHeader (SampleHeaderName, SampleHeaderValue);

        foreach (var header in options.UserHeaders)
          ws.SetUserHeader (header.Key, header.Value);

        if (!String.IsNullOrEmpty (options.ProxyUrl))
          ws.SetProxy (options.ProxyUrl, options.ProxyUsername, options.ProxyPassword);

        if (options.ServerUri.Scheme == "wss") {
          // Safe default: accept only certificates that pass platform validation.
          // The local/dev exception below is opt-in and limited to loopback/local
          // hosts, so it does not weaken production WSS connections by default.
          ws.SslConfiguration.ServerCertificateValidationCallback =
            (sender, certificate, chain, sslPolicyErrors) =>
              ValidateServerCertificate (
                options.ServerUri,
                options.AllowLocalDevCertificate,
                options.TrustedCertificateThumbprint,
                certificate,
                sslPolicyErrors
              );
        }

        ws.OnOpen += (sender, e) => Console.WriteLine ("Connected.");
        ws.OnMessage +=
          (sender, e) => Console.WriteLine ("Message: {0}", e.IsText ? e.Data : "<binary>");
        ws.OnError += (sender, e) => Console.WriteLine ("Error: {0}", e.Message);
        ws.OnClose +=
          (sender, e) => Console.WriteLine ("Closed: {0} {1}", e.Code, e.Reason);

        Console.WriteLine ("Connecting to {0}", options.ServerUri);
        ws.Connect ();

        if (ws.ReadyState == WebSocketState.Open) {
          ws.Send ("Hello from SecureAndProxyClient.");
          ws.Close ();
        }
      }

      return 0;
    }

    private static bool ValidateServerCertificate (
      Uri serverUri,
      bool allowLocalDevCertificate,
      string trustedCertificateThumbprint,
      X509Certificate certificate,
      SslPolicyErrors sslPolicyErrors
    )
    {
      if (sslPolicyErrors == SslPolicyErrors.None)
        return true;

      if (certificate == null)
        return false;

      if (!String.IsNullOrEmpty (trustedCertificateThumbprint) &&
          CertificateMatchesThumbprint (certificate, trustedCertificateThumbprint)) {
        Console.WriteLine (
          "Accepting certificate for {0} by explicit thumbprint pin.",
          serverUri.Host
        );

        return true;
      }

      if (!allowLocalDevCertificate || !IsLocalDevelopmentHost (serverUri))
        return false;

      const SslPolicyErrors allowedLocalDevErrors =
        SslPolicyErrors.RemoteCertificateChainErrors;

      if ((sslPolicyErrors & ~allowedLocalDevErrors) != 0)
        return false;

      Console.WriteLine (
        "Accepting local development certificate for {0}: {1}",
        serverUri.Host,
        sslPolicyErrors
      );

      return true;
    }

    private static bool CertificateMatchesThumbprint (
      X509Certificate certificate,
      string expectedThumbprint
    )
    {
      var certificate2 = certificate as X509Certificate2
                         ?? new X509Certificate2 (certificate);
      var shouldDispose = !(certificate is X509Certificate2);

      try {
        return String.Equals (
          NormalizeThumbprint (certificate2.Thumbprint),
          NormalizeThumbprint (expectedThumbprint),
          StringComparison.OrdinalIgnoreCase
        );
      }
      finally {
        if (shouldDispose)
          certificate2.Dispose ();
      }
    }

    private static string NormalizeThumbprint (string value)
    {
      if (String.IsNullOrEmpty (value))
        return String.Empty;

      var normalized = new StringBuilder (value.Length);

      foreach (var ch in value) {
        if (Uri.IsHexDigit (ch))
          normalized.Append (ch);
      }

      return normalized.ToString ();
    }

    private static bool IsLocalDevelopmentHost (Uri uri)
    {
      if (uri == null)
        return false;

      if (uri.IsLoopback)
        return true;

      return String.Equals (uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
             || uri.Host.EndsWith (".localhost", StringComparison.OrdinalIgnoreCase);
    }

    private static void PrintUsage ()
    {
      Console.WriteLine ("SecureAndProxyClient");
      Console.WriteLine ();
      Console.WriteLine ("Usage:");
      Console.WriteLine ("  SecureAndProxyClient.exe <ws-or-wss-url> [options]");
      Console.WriteLine ();
      Console.WriteLine ("No connection is opened unless a URL is supplied.");
      Console.WriteLine ();
      Console.WriteLine ("Options:");
      Console.WriteLine ("  --origin <origin>                 Send an Origin header.");
      Console.WriteLine ("  --header <name=value>             Send an additional user header.");
      Console.WriteLine ("  --proxy <http-url>                Connect through an HTTP proxy.");
      Console.WriteLine ("  --proxy-user <username>           Proxy authentication user.");
      Console.WriteLine ("  --proxy-password <password>       Proxy authentication password.");
      Console.WriteLine ("  --timeout <seconds>               Connection timeout. Default: 10.");
      Console.WriteLine ("  --no-compression                  Do not request permessage-deflate.");
      Console.WriteLine ("  --allow-local-dev-cert            For wss:// localhost/loopback only,");
      Console.WriteLine ("                                    allow certificate chain errors.");
      Console.WriteLine ("  --trusted-thumbprint <hex>        Pin a specific server certificate.");
      Console.WriteLine ("  --help                            Print this usage.");
      Console.WriteLine ();
      Console.WriteLine ("Examples:");
      Console.WriteLine ("  SecureAndProxyClient.exe wss://localhost:5963/Echo --allow-local-dev-cert");
      Console.WriteLine ("  SecureAndProxyClient.exe wss://localhost:5963/Echo --trusted-thumbprint <sha1>");
      Console.WriteLine ("  SecureAndProxyClient.exe wss://example.com/Echo --proxy http://localhost:3128");
      Console.WriteLine ("  SecureAndProxyClient.exe ws://localhost:4649/Chat --origin http://localhost:4649 --header RequestForID=ID");
    }
  }

  internal sealed class ClientOptions
  {
    public bool AllowLocalDevCertificate { get; private set; }
    public TimeSpan ConnectionTimeout { get; private set; }
    public string Origin { get; private set; }
    public string ProxyPassword { get; private set; }
    public string ProxyUrl { get; private set; }
    public string ProxyUsername { get; private set; }
    public Uri ServerUri { get; private set; }
    public bool ShowHelp { get; private set; }
    public string TrustedCertificateThumbprint { get; private set; }
    public bool UseCompression { get; private set; }
    public List<KeyValuePair<string, string>> UserHeaders { get; private set; }

    public static bool TryParse (string[] args, out ClientOptions options)
    {
      options = new ClientOptions {
        ConnectionTimeout = TimeSpan.FromSeconds (10),
        UseCompression = true,
        UserHeaders = new List<KeyValuePair<string, string>> ()
      };

      if (args == null || args.Length == 0)
        return true;

      for (var i = 0; i < args.Length; i++) {
        var arg = args[i];

        if (arg == "--help" || arg == "-h") {
          options.ShowHelp = true;
          continue;
        }

        if (arg == "--allow-local-dev-cert") {
          options.AllowLocalDevCertificate = true;
          continue;
        }

        if (arg == "--no-compression") {
          options.UseCompression = false;
          continue;
        }

        if (arg == "--origin") {
          string value;

          if (!TryReadValue (args, ref i, out value))
            return false;

          options.Origin = value;
          continue;
        }

        if (arg == "--header") {
          string value;

          if (!TryReadValue (args, ref i, out value))
            return false;

          var separator = value.IndexOf ('=');

          if (separator <= 0)
            return false;

          options.UserHeaders.Add (
            new KeyValuePair<string, string> (
              value.Substring (0, separator),
              value.Substring (separator + 1)
            )
          );

          continue;
        }

        if (arg == "--proxy") {
          string value;

          if (!TryReadValue (args, ref i, out value))
            return false;

          options.ProxyUrl = value;
          continue;
        }

        if (arg == "--proxy-user") {
          string value;

          if (!TryReadValue (args, ref i, out value))
            return false;

          options.ProxyUsername = value;
          continue;
        }

        if (arg == "--proxy-password") {
          string value;

          if (!TryReadValue (args, ref i, out value))
            return false;

          options.ProxyPassword = value;
          continue;
        }

        if (arg == "--timeout") {
          string value;
          double seconds;

          if (!TryReadValue (args, ref i, out value) ||
              !Double.TryParse (
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out seconds
              ) ||
              seconds <= 0)
            return false;

          options.ConnectionTimeout = TimeSpan.FromSeconds (seconds);
          continue;
        }

        if (arg == "--trusted-thumbprint") {
          string value;

          if (!TryReadValue (args, ref i, out value))
            return false;

          options.TrustedCertificateThumbprint = value;
          continue;
        }

        if (arg.StartsWith ("-", StringComparison.Ordinal))
          return false;

        if (options.ServerUri != null)
          return false;

        Uri serverUri;

        if (!Uri.TryCreate (arg, UriKind.Absolute, out serverUri))
          return false;

        if (serverUri.Scheme != "ws" && serverUri.Scheme != "wss")
          return false;

        options.ServerUri = serverUri;
      }

      return true;
    }

    private static bool TryReadValue (string[] args, ref int index, out string value)
    {
      value = null;

      if (index + 1 >= args.Length)
        return false;

      value = args[++index];
      return !String.IsNullOrEmpty (value);
    }
  }
}
