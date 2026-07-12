using System;
using System.Collections.Specialized;
using System.Text;
using WebSocketSharp.Net;
using WebSocketSharp.Net.WebSockets;

namespace WebSocketSharp
{
  internal static class HandshakeLogFormatter
  {
    internal static string FormatRequest (HttpRequest request)
    {
      return formatRequest (
               request.HttpMethod,
               request.RequestTarget,
               request.ProtocolVersion,
               request.Headers
             );
    }

    internal static string FormatRequest (HttpListenerRequest request)
    {
      return formatRequest (
               request.HttpMethod,
               request.RawUrl,
               request.ProtocolVersion,
               request.Headers
             );
    }

    internal static string FormatRequest (WebSocketContext context)
    {
      var tcpContext = context as TcpListenerWebSocketContext;

      if (tcpContext != null)
        return FormatRequest (tcpContext.Request);

      var httpContext = context as HttpListenerWebSocketContext;

      if (httpContext != null)
        return FormatRequest (httpContext.Request);

      var uri = context.RequestUri;
      var target = uri != null ? uri.PathAndQuery : "/";

      return formatRequest (
               "GET",
               target,
               HttpVersion.Version11,
               context.Headers
             );
    }

    internal static string FormatResponse (HttpResponse response)
    {
      var buff = new StringBuilder (128);

      buff.AppendFormat (
        "HTTP/{0} {1} {2}\r\n",
        response.ProtocolVersion,
        response.StatusCode,
        response.StatusCode.GetStatusDescription ()
      );
      appendHeaders (buff, response.Headers);
      appendOmittedBody (buff, response.MessageBodyData);

      return buff.ToString ();
    }

    private static void appendHeaders (
      StringBuilder buffer,
      NameValueCollection headers
    )
    {
      foreach (var name in headers.AllKeys) {
        if (name == null)
          continue;

        var displayedValue = formatHeaderValue (name, headers[name]);

        buffer.AppendFormat ("{0}: {1}\r\n", name, displayedValue);
      }

      buffer.Append ("\r\n");
    }

    private static void appendOmittedBody (StringBuilder buffer, byte[] body)
    {
      if (body == null)
        return;

      buffer.AppendFormat ("Body: <omitted; {0} bytes>", body.LongLength);
    }

    private static string formatRequest (
      string method,
      string target,
      Version version,
      NameValueCollection headers
    )
    {
      var buff = new StringBuilder (128);

      buff.AppendFormat (
        "{0} {1} HTTP/{2}\r\n",
        formatMethod (method),
        summarizeTarget (target),
        version
      );
      appendHeaders (buff, headers);

      return buff.ToString ();
    }

    private static string formatHeaderValue (string name, string value)
    {
      if (String.IsNullOrEmpty (value))
        return String.Empty;

      if (name.Equals ("Connection", StringComparison.OrdinalIgnoreCase))
        return String.Format (
          "upgrade={0}; close={1}; tokens={2}",
          formatBoolean (containsToken (value, "upgrade")),
          formatBoolean (containsToken (value, "close")),
          countTokens (value)
        );

      if (name.Equals ("Upgrade", StringComparison.OrdinalIgnoreCase))
        return String.Format (
          "websocket={0}; tokens={1}",
          formatBoolean (containsToken (value, "websocket")),
          countTokens (value)
        );

      if (name.Equals ("Content-Length", StringComparison.OrdinalIgnoreCase)) {
        long length;

        return Int64.TryParse (value, out length) && length >= 0
               ? length.ToString ()
               : "<invalid>";
      }

      if (name.Equals ("Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
        return String.Format (
          "chunked={0}; tokens={1}",
          formatBoolean (containsToken (value, "chunked")),
          countTokens (value)
        );

      if (name.Equals ("Sec-WebSocket-Version", StringComparison.OrdinalIgnoreCase)) {
        int version;

        return Int32.TryParse (value, out version) && version >= 0 && version <= 255
               ? version.ToString ()
               : "<invalid>";
      }

      if (name.Equals ("Sec-WebSocket-Protocol", StringComparison.OrdinalIgnoreCase)
          || name.Equals ("Sec-WebSocket-Extensions", StringComparison.OrdinalIgnoreCase))
        return String.Format ("tokens={0}; values=<redacted>", countTokens (value));

      if (name.Equals ("Origin", StringComparison.OrdinalIgnoreCase)
          || name.Equals ("Location", StringComparison.OrdinalIgnoreCase))
        return summarizeUri (value);

      if (name.Equals ("WWW-Authenticate", StringComparison.OrdinalIgnoreCase)
          || name.Equals ("Proxy-Authenticate", StringComparison.OrdinalIgnoreCase))
        return String.Format (
          "scheme={0}; parameters=<redacted>",
          getAuthenticationScheme (value)
        );

      if (name.Equals ("Host", StringComparison.OrdinalIgnoreCase))
        return "<present>";

      return "<redacted>";
    }

    private static string formatMethod (string method)
    {
      return String.Equals (method, "GET", StringComparison.Ordinal)
             || String.Equals (method, "CONNECT", StringComparison.Ordinal)
             ? method
             : "<other>";
    }

    private static string formatBoolean (bool value)
    {
      return value ? "true" : "false";
    }

    private static string getAuthenticationScheme (string value)
    {
      var separator = value.IndexOf (' ');
      var scheme = separator > 0 ? value.Substring (0, separator) : value;

      if (scheme.Equals ("Basic", StringComparison.OrdinalIgnoreCase))
        return "Basic";

      if (scheme.Equals ("Digest", StringComparison.OrdinalIgnoreCase))
        return "Digest";

      return "<other>";
    }

    private static bool containsToken (string value, string expected)
    {
      foreach (var token in value.Split (',')) {
        if (String.Equals (token.Trim (), expected, StringComparison.OrdinalIgnoreCase))
          return true;
      }

      return false;
    }

    private static int countTokens (string value)
    {
      var count = 0;

      foreach (var token in value.Split (',')) {
        if (token.Trim ().Length > 0)
          count++;
      }

      return count;
    }

    private static string summarizeTarget (string target)
    {
      if (String.IsNullOrEmpty (target))
        return "<empty>";

      var queryStart = target.IndexOf ('?');
      var path = queryStart >= 0 ? target.Substring (0, queryStart) : target;
      var segments = countPathSegments (path);

      return String.Format (
        "<path; segments={0}; query={1}>",
        segments,
        formatBoolean (queryStart >= 0)
      );
    }

    private static string summarizeUri (string value)
    {
      Uri uri;

      if (!Uri.TryCreate (value, UriKind.RelativeOrAbsolute, out uri))
        return "<invalid>";

      if (!uri.IsAbsoluteUri)
        return String.Format (
          "relative=true; segments={0}; query={1}",
          countPathSegments (value),
          formatBoolean (value.IndexOf ('?') >= 0)
        );

      return String.Format (
        "scheme={0}; host=<redacted>; port={1}; segments={2}; query={3}",
        formatScheme (uri.Scheme),
        uri.Port,
        countPathSegments (uri.AbsolutePath),
        formatBoolean (uri.Query.Length > 0)
      );
    }

    private static int countPathSegments (string path)
    {
      var count = 0;
      var queryStart = path.IndexOf ('?');
      var value = queryStart >= 0 ? path.Substring (0, queryStart) : path;

      foreach (var segment in value.Split ('/')) {
        if (segment.Length > 0)
          count++;
      }

      return count;
    }

    private static string formatScheme (string scheme)
    {
      if (scheme.Equals ("ws", StringComparison.OrdinalIgnoreCase)
          || scheme.Equals ("wss", StringComparison.OrdinalIgnoreCase)
          || scheme.Equals ("http", StringComparison.OrdinalIgnoreCase)
          || scheme.Equals ("https", StringComparison.OrdinalIgnoreCase))
        return scheme.ToLowerInvariant ();

      return "<other>";
    }
  }
}
