#region License
/*
 * WebSocket.cs
 *
 * This code is derived from WebSocket.java
 * (http://github.com/adamac/Java-WebSocket-client).
 *
 * The MIT License
 *
 * Copyright (c) 2009 Adam MacBeth
 * Copyright (c) 2010-2026 sta.blockhead
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 * THE SOFTWARE.
 */
#endregion

#region Contributors
/*
 * Contributors:
 * - Frank Razenberg <frank@zzattack.org>
 * - David Wood <dpwood@gmail.com>
 * - Liryna <liryna.stark@gmail.com>
 */
#endregion

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using WebSocketSharp.Net;
using WebSocketSharp.Net.WebSockets;

namespace WebSocketSharp
{
  /// <summary>
  /// Implements the WebSocket interface.
  /// </summary>
  /// <remarks>
  ///   <para>
  ///   This class provides a set of methods and properties for two-way
  ///   communication using the WebSocket protocol.
  ///   </para>
  ///   <para>
  ///   The WebSocket protocol is defined in
  ///   <see href="http://tools.ietf.org/html/rfc6455">RFC 6455</see>.
  ///   </para>
  /// </remarks>
  public class WebSocket : IDisposable
  {
    #region Private Fields

    private AuthenticationChallenge        _authChallenge;
    private string                         _base64Key;
    private bool                           _client;
    private TimeSpan                       _connectionTimeout;
    private Action                         _closeContext;
    private CompressionMethod              _compression;
    private WebSocketContext               _context;
    private CookieCollection               _cookies;
    private Uri                            _cookiesOrigin;
    private NetworkCredential              _credentials;
    private Uri                            _credentialsOrigin;
    private bool                           _allowInsecureRedirection;
    private bool                           _emitOnPing;
    private static readonly byte[]         _emptyBytes;
    private bool                           _enableRedirection;
    private Func<bool>                     _executorBeforeClose;
    private Func<bool>                     _executorBeforeOpen;
    private string                         _extensions;
    private object                         _forAsyncSendQueue;
    private object                         _forMessageEventQueue;
    private object                         _forPing;
    private object                         _forSend;
    private object                         _forState;
    private TimeSpan                       _frameReadTimeout;
    private MemoryStream                   _fragmentsBuffer;
    private bool                           _fragmentsCompressed;
    private Opcode                         _fragmentsOpcode;
    private const string                   _guid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
    private Func<WebSocketContext, string> _handshakeRequestChecker;
    private Action<WebSocketContext>       _handshakeRequestResponder;
    private CookieCollection               _handshakeResponseCookies;
    private NameValueCollection            _handshakeResponseHeaders;
    private bool                           _hasExtension;
    private bool                           _hasProtocol;
    private bool                           _ignoreExtensions;
    private bool                           _inContinuation;
    private volatile bool                  _inMessage;
    private volatile Logger                _log;
    private int                            _maxAsyncSendQueueLength;
    private long                           _maxFramePayloadLength;
    private int                            _maxMessageEventQueueLength;
    private long                           _maxMessagePayloadLength;
    private int                            _maxRedirections;
    private const int                     _maxRedirectionLimit = 100;
    private static readonly int            _maxRetryCountForConnect;
    private Action<MessageEventArgs>       _message;
    private Queue<MessageEventArgs>        _messageEventQueue;
    private bool                           _noDelay;
    private uint                           _nonceCount;
    private string                         _origin;
    private ManualResetEvent               _pongReceived;
    private bool                           _preAuth;
    private string                         _protocol;
    private string[]                       _protocols;
    private NetworkCredential              _proxyCredentials;
    private Uri                            _proxyUri;
    private volatile WebSocketState        _readyState;
    private ManualResetEvent               _receivingExited;
    private int                            _retryCountForConnect;
    private bool                           _secure;
    private Socket                         _socket;
    private ClientSslConfiguration         _sslConfig;
    private Uri                            _sslConfigOrigin;
    private Stream                         _stream;
    private TcpClient                      _tcpClient;
    private Uri                            _uri;
    private WebHeaderCollection            _userHeaders;
    private Uri                            _userHeadersOrigin;
    private const string                   _version = "13";
    private TimeSpan                       _waitTime;
    private int                            _asyncSendQueueLength;

    #endregion

    #region Public Fields

    /// <summary>
    /// Represents the default maximum payload length for a single received frame.
    /// </summary>
    public static readonly long DefaultMaxFramePayloadLength;

    /// <summary>
    /// Represents the default maximum payload length for an assembled received message.
    /// </summary>
    public static readonly long DefaultMaxMessagePayloadLength;

    /// <summary>
    /// Represents the default maximum number of queued received message events.
    /// </summary>
    public static readonly int DefaultMaxMessageEventQueueLength;

    /// <summary>
    /// Represents the default maximum number of queued asynchronous sends.
    /// </summary>
    public static readonly int DefaultMaxAsyncSendQueueLength;

    /// <summary>
    /// Represents the default timeout for a partial WebSocket frame read.
    /// </summary>
    public static readonly TimeSpan DefaultFrameReadTimeout;

    #endregion

    #region Internal Fields

    /// <summary>
    /// Represents the length used to determine whether the data should
    /// be fragmented in sending.
    /// </summary>
    /// <remarks>
    ///   <para>
    ///   The data will be fragmented if its length is greater than
    ///   the value of this field.
    ///   </para>
    ///   <para>
    ///   If you would like to change the value, you must set it to
    ///   a value between 125 and <c>Int32.MaxValue - 14</c> inclusive.
    ///   </para>
    /// </remarks>
    internal static readonly int FragmentLength;

    /// <summary>
    /// Represents the random number generator used internally.
    /// </summary>
    internal static readonly RandomNumberGenerator RandomNumber;

    #endregion

    #region Static Constructor

    static WebSocket ()
    {
      _emptyBytes = new byte[0];
      DefaultMaxFramePayloadLength = 16 * 1024 * 1024;
      DefaultMaxMessagePayloadLength = 64 * 1024 * 1024;
      DefaultMaxMessageEventQueueLength = 1024;
      DefaultMaxAsyncSendQueueLength = 256;
      DefaultFrameReadTimeout = TimeSpan.FromSeconds (10);
      _maxRetryCountForConnect = 10;

      FragmentLength = 1016;
      RandomNumber = new RNGCryptoServiceProvider ();
    }

    #endregion

    #region Internal Constructors

    // As server
    internal WebSocket (HttpListenerWebSocketContext context, string protocol)
    {
      _context = context;
      _protocol = protocol;

      _closeContext = context.Close;
      _log = context.Log;
      _message = messages;
      _secure = context.IsSecureConnection;
      _socket = context.Socket;
      _stream = context.Stream;
      _waitTime = TimeSpan.FromSeconds (1);

      init ();
    }

    // As server
    internal WebSocket (TcpListenerWebSocketContext context, string protocol)
    {
      _context = context;
      _protocol = protocol;

      _closeContext = context.Close;
      _log = context.Log;
      _message = messages;
      _secure = context.IsSecureConnection;
      _socket = context.Socket;
      _stream = context.Stream;
      _waitTime = TimeSpan.FromSeconds (1);

      init ();
    }

    #endregion

    #region Public Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="WebSocket"/> class with
    /// the specified URL and optionally subprotocols.
    /// </summary>
    /// <param name="url">
    ///   <para>
    ///   A <see cref="string"/> that specifies the URL to which to connect.
    ///   </para>
    ///   <para>
    ///   The scheme of the URL must be ws or wss.
    ///   </para>
    ///   <para>
    ///   The new instance uses a secure connection if the scheme is wss.
    ///   </para>
    /// </param>
    /// <param name="protocols">
    ///   <para>
    ///   An array of <see cref="string"/> that specifies the names of
    ///   the subprotocols if necessary.
    ///   </para>
    ///   <para>
    ///   Each value of the array must be a token defined in
    ///   <see href="http://tools.ietf.org/html/rfc2616#section-2.2">
    ///   RFC 2616</see>.
    ///   </para>
    /// </param>
    /// <exception cref="ArgumentException">
    ///   <para>
    ///   <paramref name="url"/> is an empty string.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   <paramref name="url"/> is an invalid WebSocket URL string.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   <paramref name="protocols"/> contains a value that is not a token.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   <paramref name="protocols"/> contains a value twice.
    ///   </para>
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="url"/> is <see langword="null"/>.
    /// </exception>
    public WebSocket (string url, params string[] protocols)
    {
      if (url == null)
        throw new ArgumentNullException ("url");

      if (url.Length == 0)
        throw new ArgumentException ("An empty string.", "url");

      string msg;

      if (!url.TryCreateWebSocketUri (out _uri, out msg))
        throw new ArgumentException (msg, "url");

      if (protocols != null && protocols.Length > 0) {
        if (!checkProtocols (protocols, out msg))
          throw new ArgumentException (msg, "protocols");

        _protocols = protocols;
        _hasProtocol = true;
      }

      _base64Key = CreateBase64Key ();
      _client = true;
      _log = new Logger ();
      _message = messagec;
      _retryCountForConnect = -1;
      _secure = _uri.Scheme == "wss";
      _connectionTimeout = TimeSpan.FromSeconds (10);
      _waitTime = TimeSpan.FromSeconds (5);

      init ();
    }

    #endregion

    #region Internal Properties

    internal CookieCollection Cookies {
      get {
        if (_cookies == null)
          _cookies = new CookieCollection ();

        return _cookies;
      }
    }

    // As server
    internal Func<WebSocketContext, string> CustomHandshakeRequestChecker {
      get {
        return _handshakeRequestChecker;
      }

      set {
        _handshakeRequestChecker = value;
      }
    }

    // As server
    internal Action<WebSocketContext> CustomHandshakeRequestResponder {
      get {
        return _handshakeRequestResponder;
      }

      set {
        _handshakeRequestResponder = value;
      }
    }

    // As server
    internal Func<bool> ExecutorBeforeClose {
      get {
        return _executorBeforeClose;
      }

      set {
        _executorBeforeClose = value;
      }
    }

    // As server
    internal Func<bool> ExecutorBeforeOpen {
      get {
        return _executorBeforeOpen;
      }

      set {
        _executorBeforeOpen = value;
      }
    }

    // As server
    internal bool IgnoreExtensions {
      get {
        return _ignoreExtensions;
      }

      set {
        _ignoreExtensions = value;
      }
    }

    internal WebHeaderCollection UserHeaders {
      get {
        if (_userHeaders == null) {
          var state = _client
                      ? HttpHeaderType.Request
                      : HttpHeaderType.Response;

          _userHeaders = new WebHeaderCollection (state, false);
        }

        return _userHeaders;
      }
    }

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets or sets a value indicating whether a secure client can follow a
    /// redirect from <c>wss</c> to <c>ws</c>.
    /// </summary>
    /// <remarks>
    /// The default value is <see langword="false"/>. The set operation is not
    /// available when the current state is neither New nor Closed.
    /// </remarks>
    public bool AllowInsecureRedirection {
      get {
        return _allowInsecureRedirection;
      }

      set {
        if (!_client) {
          var msg = "The interface is not for the client.";

          throw new InvalidOperationException (msg);
        }

        lock (_forState) {
          if (!canSet ()) {
            var msg = "The current state of the interface is neither New nor Closed.";

            throw new InvalidOperationException (msg);
          }

          _allowInsecureRedirection = value;
        }
      }
    }

    /// <summary>
    /// Gets or sets the compression method used to compress a message.
    /// </summary>
    /// <value>
    ///   <para>
    ///   One of the <see cref="CompressionMethod"/> enum values.
    ///   </para>
    ///   <para>
    ///   It indicates the compression method used to compress a message.
    ///   </para>
    ///   <para>
    ///   The default value is <see cref="CompressionMethod.None"/>.
    ///   </para>
    /// </value>
    /// <exception cref="InvalidOperationException">
    ///   <para>
    ///   The set operation is not available if the interface is not for
    ///   the client.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   The set operation is not available when the current state of
    ///   the interface is neither New nor Closed.
    ///   </para>
    /// </exception>
    public CompressionMethod Compression {
      get {
        return _compression;
      }

      set {
        if (!_client) {
          var msg = "The interface is not for the client.";

          throw new InvalidOperationException (msg);
        }

        lock (_forState) {
          if (!canSet ()) {
            var msg = "The current state of the interface is neither New nor Closed.";

            throw new InvalidOperationException (msg);
          }

          _compression = value;
        }
      }
    }

    /// <summary>
    /// Gets the credentials for the HTTP authentication (Basic/Digest).
    /// </summary>
    /// <value>
    ///   <para>
    ///   A <see cref="NetworkCredential"/> that represents the credentials
    ///   used to authenticate the client.
    ///   </para>
    ///   <para>
    ///   The default value is <see langword="null"/>.
    ///   </para>
    /// </value>
    public NetworkCredential Credentials {
      get {
        return _credentials;
      }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the interface emits
    /// the message event when it receives a ping.
    /// </summary>
    /// <value>
    ///   <para>
    ///   <c>true</c> if the interface emits the message event when
    ///   it receives a ping; otherwise, <c>false</c>.
    ///   </para>
    ///   <para>
    ///   The default value is <c>false</c>.
    ///   </para>
    /// </value>
    /// <exception cref="InvalidOperationException">
    /// The set operation is not available when the current state of
    /// the interface is neither New nor Closed.
    /// </exception>
    public bool EmitOnPing {
      get {
        return _emitOnPing;
      }

      set {
        lock (_forState) {
          if (!canSet ()) {
            var msg = "The current state of the interface is neither New nor Closed.";

            throw new InvalidOperationException (msg);
          }

          _emitOnPing = value;
        }
      }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the URL redirection for
    /// the handshake request is allowed.
    /// </summary>
    /// <value>
    ///   <para>
    ///   <c>true</c> if the interface allows the URL redirection for
    ///   the handshake request; otherwise, <c>false</c>.
    ///   </para>
    ///   <para>
    ///   The default value is <c>false</c>.
    ///   </para>
    /// </value>
    /// <exception cref="InvalidOperationException">
    ///   <para>
    ///   The set operation is not available if the interface is not for
    ///   the client.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   The set operation is not available when the current state of
    ///   the interface is neither New nor Closed.
    ///   </para>
    /// </exception>
    public bool EnableRedirection {
      get {
        return _enableRedirection;
      }

      set {
        if (!_client) {
          var msg = "The interface is not for the client.";

          throw new InvalidOperationException (msg);
        }

        lock (_forState) {
          if (!canSet ()) {
            var msg = "The current state of the interface is neither New nor Closed.";

            throw new InvalidOperationException (msg);
          }

          _enableRedirection = value;
        }
      }
    }

    /// <summary>
    /// Gets the extensions selected by the server.
    /// </summary>
    /// <value>
    ///   <para>
    ///   A <see cref="string"/> that represents a list of the extensions
    ///   negotiated between the client and server.
    ///   </para>
    ///   <para>
    ///   An empty string if not specified or selected.
    ///   </para>
    /// </value>
    public string Extensions {
      get {
        return _extensions ?? String.Empty;
      }
    }

    /// <summary>
    /// Gets the HTTP cookies included in the handshake response.
    /// </summary>
    /// <value>
    ///   <para>
    ///   A <see cref="CookieCollection"/> that contains the cookies
    ///   included in the handshake response if any.
    ///   </para>
    ///   <para>
    ///   <see langword="null"/> if the interface could not receive
    ///   the handshake response.
    ///   </para>
    /// </value>
    /// <exception cref="InvalidOperationException">
    ///   <para>
    ///   The get operation is not available if the interface is not for
    ///   the client.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   The get operation is not available when the current state of
    ///   the interface is New or Connecting.
    ///   </para>
    /// </exception>
    public CookieCollection HandshakeResponseCookies {
      get {
        if (!_client) {
          var msg = "The interface is not for the client.";

          throw new InvalidOperationException (msg);
        }

        lock (_forState) {
          var canGet = _readyState > WebSocketState.Connecting;

          if (!canGet) {
            var msg = "The current state of the interface is New or Connecting.";

            throw new InvalidOperationException (msg);
          }

          return _handshakeResponseCookies;
        }
      }
    }

    /// <summary>
    /// Gets the HTTP headers included in the handshake response.
    /// </summary>
    /// <value>
    ///   <para>
    ///   A <see cref="NameValueCollection"/> that contains the headers
    ///   included in the handshake response.
    ///   </para>
    ///   <para>
    ///   <see langword="null"/> if the interface could not receive
    ///   the handshake response.
    ///   </para>
    /// </value>
    /// <exception cref="InvalidOperationException">
    ///   <para>
    ///   The get operation is not available if the interface is not for
    ///   the client.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   The get operation is not available when the current state of
    ///   the interface is New or Connecting.
    ///   </para>
    /// </exception>
    public NameValueCollection HandshakeResponseHeaders {
      get {
        if (!_client) {
          var msg = "The interface is not for the client.";

          throw new InvalidOperationException (msg);
        }

        lock (_forState) {
          var canGet = _readyState > WebSocketState.Connecting;

          if (!canGet) {
            var msg = "The current state of the interface is New or Connecting.";

            throw new InvalidOperationException (msg);
          }

          return _handshakeResponseHeaders;
        }
      }
    }

    /// <summary>
    /// Gets a value indicating whether the communication is possible.
    /// </summary>
    /// <value>
    /// <c>true</c> if the communication is possible; otherwise, <c>false</c>.
    /// </value>
    public bool IsAlive {
      get {
        return ping (_emptyBytes);
      }
    }

    /// <summary>
    /// Gets a value indicating whether the connection is secure.
    /// </summary>
    /// <value>
    /// <c>true</c> if the connection is secure; otherwise, <c>false</c>.
    /// </value>
    public bool IsSecure {
      get {
        return _secure;
      }
    }

    /// <summary>
    /// Gets or sets the time to wait for a client connection handshake.
    /// </summary>
    /// <remarks>
    /// This timeout applies to TCP connect, proxy connect, TLS handshake,
    /// and WebSocket handshake response reads.
    /// </remarks>
    /// <value>
    /// A <see cref="TimeSpan"/> that represents the time to wait.
    /// The default value is the same as 10 seconds.
    /// </value>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The value specified for a set operation is zero or less.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    ///   <para>
    ///   The set operation is not available if the interface is not for
    ///   the client.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   The set operation is not available when the current state of
    ///   the interface is neither New nor Closed.
    ///   </para>
    /// </exception>
    public TimeSpan ConnectionTimeout {
      get {
        if (!_client) {
          var msg = "The interface is not for the client.";

          throw new InvalidOperationException (msg);
        }

        return _connectionTimeout;
      }

      set {
        if (!_client) {
          var msg = "The interface is not for the client.";

          throw new InvalidOperationException (msg);
        }

        if (value <= TimeSpan.Zero) {
          var msg = "Zero or less.";

          throw new ArgumentOutOfRangeException ("value", msg);
        }

        lock (_forState) {
          if (!canSet ()) {
            var msg = "The current state of the interface is neither New nor Closed.";

            throw new InvalidOperationException (msg);
          }

          _connectionTimeout = value;
        }
      }
    }

    /// <summary>
    /// Gets the logging function.
    /// </summary>
    /// <remarks>
    /// The default logging level is <see cref="LogLevel.Error"/>.
    /// </remarks>
    /// <value>
    /// A <see cref="Logger"/> that provides the logging function.
    /// </value>
    /// <exception cref="InvalidOperationException">
    /// The get operation is not available if the interface is not for
    /// the client.
    /// </exception>
    public Logger Log {
      get {
        if (!_client) {
          var msg = "The interface is not for the client.";

          throw new InvalidOperationException (msg);
        }

        return _log;
      }

      internal set {
        _log = value;
      }
    }

    /// <summary>
    /// Gets or sets the maximum payload length for a single received frame.
    /// </summary>
    /// <remarks>
    /// The default value is 16 MiB.
    /// </remarks>
    public long MaxFramePayloadLength {
      get {
        return _maxFramePayloadLength;
      }

      set {
        CheckMaxFramePayloadLength (value, "value");

        lock (_forState) {
          if (!canSet ()) {
            var msg = "The current state of the interface is neither New nor Closed.";

            throw new InvalidOperationException (msg);
          }

          _maxFramePayloadLength = value;
        }
      }
    }

    /// <summary>
    /// Gets or sets the maximum payload length for an assembled received message.
    /// </summary>
    /// <remarks>
    /// The default value is 64 MiB.
    /// </remarks>
    public long MaxMessagePayloadLength {
      get {
        return _maxMessagePayloadLength;
      }

      set {
        CheckPositiveLength (value, "value");

        lock (_forState) {
          if (!canSet ()) {
            var msg = "The current state of the interface is neither New nor Closed.";

            throw new InvalidOperationException (msg);
          }

          _maxMessagePayloadLength = value;
        }
      }
    }

    /// <summary>
    /// Gets or sets the maximum number of redirects followed by the client.
    /// </summary>
    /// <remarks>
    /// The default value is 5. Valid values are from 0 through 100. The set
    /// operation is not available when the current state is neither New nor
    /// Closed.
    /// </remarks>
    public int MaxRedirections {
      get {
        return _maxRedirections;
      }

      set {
        if (!_client) {
          var msg = "The interface is not for the client.";

          throw new InvalidOperationException (msg);
        }

        if (value < 0 || value > _maxRedirectionLimit) {
          var msg = String.Format (
                      "Not between 0 and {0}.",
                      _maxRedirectionLimit
                    );

          throw new ArgumentOutOfRangeException ("value", msg);
        }

        lock (_forState) {
          if (!canSet ()) {
            var msg = "The current state of the interface is neither New nor Closed.";

            throw new InvalidOperationException (msg);
          }

          _maxRedirections = value;
        }
      }
    }

    /// <summary>
    /// Gets or sets the maximum number of received message events queued for dispatch.
    /// </summary>
    /// <remarks>
    /// The default value is 1024.
    /// </remarks>
    public int MaxMessageEventQueueLength {
      get {
        return _maxMessageEventQueueLength;
      }

      set {
        CheckPositiveCount (value, "value");

        lock (_forState) {
          if (!canSet ()) {
            var msg = "The current state of the interface is neither New nor Closed.";

            throw new InvalidOperationException (msg);
          }

          _maxMessageEventQueueLength = value;
        }
      }
    }

    /// <summary>
    /// Gets or sets the maximum number of asynchronous sends queued for execution.
    /// </summary>
    /// <remarks>
    /// The default value is 256.
    /// </remarks>
    public int MaxAsyncSendQueueLength {
      get {
        return _maxAsyncSendQueueLength;
      }

      set {
        CheckPositiveCount (value, "value");

        lock (_forState) {
          if (!canSet ()) {
            var msg = "The current state of the interface is neither New nor Closed.";

            throw new InvalidOperationException (msg);
          }

          _maxAsyncSendQueueLength = value;
        }
      }
    }

    /// <summary>
    /// Gets or sets the timeout for a partial WebSocket frame read.
    /// </summary>
    /// <remarks>
    /// The timeout starts only after a peer begins a frame or when the
    /// implementation is waiting for the rest of an already-started frame.
    /// An idle open connection with no incoming bytes is not closed by this
    /// timeout. The default value is 10 seconds.
    /// </remarks>
    public TimeSpan FrameReadTimeout {
      get {
        return _frameReadTimeout;
      }

      set {
        if (value <= TimeSpan.Zero) {
          var msg = "Zero or less.";

          throw new ArgumentOutOfRangeException ("value", msg);
        }

        lock (_forState) {
          if (!canSet ()) {
            var msg = "The current state of the interface is neither New nor Closed.";

            throw new InvalidOperationException (msg);
          }

          _frameReadTimeout = value;
        }
      }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the underlying TCP socket
    /// disables a delay when send or receive buffer is not full.
    /// </summary>
    /// <value>
    ///   <para>
    ///   <c>true</c> if the delay is disabled; otherwise, <c>false</c>.
    ///   </para>
    ///   <para>
    ///   The default value is <c>false</c>.
    ///   </para>
    /// </value>
    /// <seealso cref="Socket.NoDelay"/>
    /// <exception cref="InvalidOperationException">
    /// The set operation is not available when the current state of
    /// the interface is neither New nor Closed.
    /// </exception>
    public bool NoDelay {
      get {
        return _noDelay;
      }

      set {
        lock (_forState) {
          if (!canSet ()) {
            var msg = "The current state of the interface is neither New nor Closed.";

            throw new InvalidOperationException (msg);
          }

          _noDelay = value;
        }
      }
    }

    /// <summary>
    /// Gets or sets the value of the HTTP Origin header to send with
    /// the handshake request.
    /// </summary>
    /// <remarks>
    ///   <para>
    ///   The HTTP Origin header is defined in
    ///   <see href="http://tools.ietf.org/html/rfc6454#section-7">
    ///   Section 7 of RFC 6454</see>.
    ///   </para>
    ///   <para>
    ///   The interface sends the Origin header if this property has any.
    ///   </para>
    /// </remarks>
    /// <value>
    ///   <para>
    ///   A <see cref="string"/> that represents the value of the Origin
    ///   header to send.
    ///   </para>
    ///   <para>
    ///   The syntax is &lt;scheme&gt;://&lt;host&gt;[:&lt;port&gt;].
    ///   </para>
    ///   <para>
    ///   The default value is <see langword="null"/>.
    ///   </para>
    /// </value>
    /// <exception cref="ArgumentException">
    ///   <para>
    ///   The value specified for a set operation is not an absolute URI string.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   The value specified for a set operation includes the path segments.
    ///   </para>
    /// </exception>
    /// <exception cref="InvalidOperationException">
    ///   <para>
    ///   The set operation is not available if the interface is not for
    ///   the client.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   The set operation is not available when the current state of
    ///   the interface is neither New nor Closed.
    ///   </para>
    /// </exception>
    public string Origin {
      get {
        return _origin;
      }

      set {
        if (!_client) {
          var msg = "The interface is not for the client.";

          throw new InvalidOperationException (msg);
        }

        var hasValue = !value.IsNullOrEmpty ();

        if (hasValue) {
          Uri uri;

          if (!Uri.TryCreate (value, UriKind.Absolute, out uri)) {
            var msg = "Not an absolute URI string.";

            throw new ArgumentException (msg, "value");
          }

          if (uri.Segments.Length > 1) {
            var msg = "It includes the path segments.";

            throw new ArgumentException (msg, "value");
          }
        }

        lock (_forState) {
          if (!canSet ()) {
            var msg = "The current state of the interface is neither New nor Closed.";

            throw new InvalidOperationException (msg);
          }

          _origin = hasValue ? value.TrimEnd ('/') : value;
        }
      }
    }

    /// <summary>
    /// Gets the name of subprotocol selected by the server.
    /// </summary>
    /// <value>
    ///   <para>
    ///   A <see cref="string"/> that will be one of the names of
    ///   subprotocols specified by client.
    ///   </para>
    ///   <para>
    ///   An empty string if not specified or selected.
    ///   </para>
    /// </value>
    public string Protocol {
      get {
        return _protocol ?? String.Empty;
      }

      internal set {
        _protocol = value;
      }
    }

    /// <summary>
    /// Gets the current state of the interface.
    /// </summary>
    /// <value>
    ///   <para>
    ///   One of the <see cref="WebSocketState"/> enum values.
    ///   </para>
    ///   <para>
    ///   It indicates the current state of the interface.
    ///   </para>
    ///   <para>
    ///   The default value is <see cref="WebSocketState.New"/>.
    ///   </para>
    /// </value>
    public WebSocketState ReadyState {
      get {
        return _readyState;
      }
    }

    /// <summary>
    /// Gets the configuration for secure connection.
    /// </summary>
    /// <remarks>
    /// The configuration is used when the interface attempts to connect,
    /// so it must be configured before any connect method is called.
    /// </remarks>
    /// <value>
    /// A <see cref="ClientSslConfiguration"/> that represents the
    /// configuration used to establish a secure connection.
    /// </value>
    /// <exception cref="InvalidOperationException">
    ///   <para>
    ///   The get operation is not available if the interface is not for
    ///   the client.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   The get operation is not available if the interface does not use
    ///   a secure connection.
    ///   </para>
    /// </exception>
    public ClientSslConfiguration SslConfiguration {
      get {
        if (!_client) {
          var msg = "The interface is not for the client.";

          throw new InvalidOperationException (msg);
        }

        if (!_secure) {
          var msg = "The interface does not use a secure connection.";

          throw new InvalidOperationException (msg);
        }

        return getSslConfiguration ();
      }
    }

    /// <summary>
    /// Gets the URL to which to connect.
    /// </summary>
    /// <value>
    ///   <para>
    ///   A <see cref="Uri"/> that represents the URL to which to connect.
    ///   </para>
    ///   <para>
    ///   Also it represents the URL requested by the client if the interface
    ///   is for the server.
    ///   </para>
    /// </value>
    public Uri Url {
      get {
        return _client ? _uri : _context.RequestUri;
      }
    }

    /// <summary>
    /// Gets or sets the time to wait for the response to the ping or close.
    /// </summary>
    /// <value>
    ///   <para>
    ///   A <see cref="TimeSpan"/> that represents the time to wait for
    ///   the response.
    ///   </para>
    ///   <para>
    ///   The default value is the same as 5 seconds if the interface is
    ///   for the client.
    ///   </para>
    /// </value>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The value specified for a set operation is zero or less.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The set operation is not available when the current state of
    /// the interface is neither New nor Closed.
    /// </exception>
    public TimeSpan WaitTime {
      get {
        return _waitTime;
      }

      set {
        if (value <= TimeSpan.Zero) {
          var msg = "Zero or less.";

          throw new ArgumentOutOfRangeException ("value", msg);
        }

        lock (_forState) {
          if (!canSet ()) {
            var msg = "The current state of the interface is neither New nor Closed.";

            throw new InvalidOperationException (msg);
          }

          _waitTime = value;
        }
      }
    }

    #endregion

    #region Public Events

    /// <summary>
    /// Occurs when the connection has been closed.
    /// </summary>
    public event EventHandler<CloseEventArgs> OnClose;

    /// <summary>
    /// Occurs when the interface gets an error.
    /// </summary>
    public event EventHandler<ErrorEventArgs> OnError;

    /// <summary>
    /// Occurs when the interface receives a message.
    /// </summary>
    public event EventHandler<MessageEventArgs> OnMessage;

    /// <summary>
    /// Occurs when the connection has been established.
    /// </summary>
    public event EventHandler OnOpen;

    #endregion

    #region Private Methods

    private void abort (string reason, Exception exception)
    {
      var code = exception is WebSocketException
                 ? ((WebSocketException) exception).Code
                 : (ushort) 1006;

      abort (code, reason);
    }

    private void abort (ushort code, string reason)
    {
      var data = new PayloadData (code, reason);

      close (data, false, false);
    }

    // As server
    private bool accept ()
    {
      lock (_forState) {
        if (_readyState == WebSocketState.Open) {
          _log.Trace ("The connection has already been established.");

          return false;
        }

        if (_readyState == WebSocketState.Closing) {
          _log.Error ("The close process is in progress.");

          error ("An error has occurred before accepting.", null);

          return false;
        }

        if (_readyState == WebSocketState.Closed) {
          _log.Error ("The connection has been closed.");

          error ("An error has occurred before accepting.", null);

          return false;
        }

        _readyState = WebSocketState.Connecting;

        var accepted = false;

        try {
          accepted = acceptHandshake ();
        }
        catch (Exception ex) {
          _log.Fatal (ex.Message);
          _log.Debug (ex.ToString ());

          abort (1011, "An exception has occurred while accepting.");
        }

        if (!accepted)
          return false;

        _readyState = WebSocketState.Open;

        return true;
      }
    }

    // As server
    private bool acceptHandshake ()
    {
      string msg;

      if (!checkHandshakeRequest (_context, out msg)) {
        _log.Error (msg);
        _log.Debug (HandshakeLogFormatter.FormatRequest (_context));

        refuseHandshake (1002, "A handshake error has occurred.");

        return false;
      }

      if (!customCheckHandshakeRequest (_context, out msg)) {
        _log.Error (msg);
        _log.Debug (HandshakeLogFormatter.FormatRequest (_context));

        refuseHandshake (1002, "A handshake error has occurred.");

        return false;
      }

      _base64Key = _context.Headers["Sec-WebSocket-Key"];

      if (_protocol != null) {
        var matched = _context
                      .SecWebSocketProtocols
                      .Contains (p => p == _protocol);

        if (!matched)
          _protocol = null;
      }

      if (!_ignoreExtensions) {
        var val = _context.Headers["Sec-WebSocket-Extensions"];

        processSecWebSocketExtensionsClientHeader (val);
      }

      customRespondToHandshakeRequest (_context);

      if (_noDelay)
        _socket.NoDelay = true;

      createHandshakeResponse ().WriteTo (_stream);

      return true;
    }

    private bool canSet ()
    {
      return _readyState == WebSocketState.New
             || _readyState == WebSocketState.Closed;
    }

    internal static void CheckMaxFramePayloadLength (long value, string paramName)
    {
      if (value < 125) {
        var msg = "Less than 125.";

        throw new ArgumentOutOfRangeException (paramName, msg);
      }
    }

    internal static void CheckPositiveCount (int value, string paramName)
    {
      if (value <= 0) {
        var msg = "Zero or less.";

        throw new ArgumentOutOfRangeException (paramName, msg);
      }
    }

    internal static void CheckPositiveLength (long value, string paramName)
    {
      if (value <= 0) {
        var msg = "Zero or less.";

        throw new ArgumentOutOfRangeException (paramName, msg);
      }
    }

    // As server
    private bool checkHandshakeRequest (
      WebSocketContext context,
      out string message
    )
    {
      message = null;

      if (!context.IsWebSocketRequest) {
        message = "Not a WebSocket handshake request.";

        return false;
      }

      var headers = context.Headers;

      var key = headers["Sec-WebSocket-Key"];

      if (key == null) {
        message = "The Sec-WebSocket-Key header is non-existent.";

        return false;
      }

      if (key.Length == 0) {
        message = "The Sec-WebSocket-Key header is invalid.";

        return false;
      }

      var ver = headers["Sec-WebSocket-Version"];

      if (ver == null) {
        message = "The Sec-WebSocket-Version header is non-existent.";

        return false;
      }

      if (ver != _version) {
        message = "The Sec-WebSocket-Version header is invalid.";

        return false;
      }

      var subps = headers["Sec-WebSocket-Protocol"];

      if (subps != null) {
        if (subps.Length == 0) {
          message = "The Sec-WebSocket-Protocol header is invalid.";

          return false;
        }
      }

      if (!_ignoreExtensions) {
        var exts = headers["Sec-WebSocket-Extensions"];

        if (exts != null) {
          if (exts.Length == 0) {
            message = "The Sec-WebSocket-Extensions header is invalid.";

            return false;
          }
        }
      }

      return true;
    }

    // As client
    private bool checkHandshakeResponse (
      HttpResponse response,
      out string message
    )
    {
      message = null;

      if (response.IsRedirect) {
        message = "The redirection is indicated.";

        return false;
      }

      if (response.IsUnauthorized) {
        message = "The authentication is required.";

        return false;
      }

      if (!response.IsWebSocketResponse) {
        message = "Not a WebSocket handshake response.";

        return false;
      }

      var headers = response.Headers;

      var key = headers["Sec-WebSocket-Accept"];

      if (key == null) {
        message = "The Sec-WebSocket-Accept header is non-existent.";

        return false;
      }

      if (key != CreateResponseKey (_base64Key)) {
        message = "The Sec-WebSocket-Accept header is invalid.";

        return false;
      }

      var ver = headers["Sec-WebSocket-Version"];

      if (ver != null) {
        if (ver != _version) {
          message = "The Sec-WebSocket-Version header is invalid.";

          return false;
        }
      }

      var subp = headers["Sec-WebSocket-Protocol"];

      if (subp == null) {
        if (_hasProtocol) {
          message = "The Sec-WebSocket-Protocol header is non-existent.";

          return false;
        }
      }
      else {
        var isValid = _hasProtocol
                      && subp.Length > 0
                      && _protocols.Contains (p => p == subp);

        if (!isValid) {
          message = "The Sec-WebSocket-Protocol header is invalid.";

          return false;
        }
      }

      var exts = headers["Sec-WebSocket-Extensions"];

      if (exts != null) {
        if (!validateSecWebSocketExtensionsServerHeader (exts)) {
          message = "The Sec-WebSocket-Extensions header is invalid.";

          return false;
        }
      }

      return true;
    }

    private static bool checkProtocols (string[] protocols, out string message)
    {
      message = null;

      Func<string, bool> cond = p => p.IsNullOrEmpty () || !p.IsToken ();

      if (protocols.Contains (cond)) {
        message = "It contains a value that is not a token.";

        return false;
      }

      if (protocols.ContainsTwice ()) {
        message = "It contains a value twice.";

        return false;
      }

      return true;
    }

    // As client
    private bool checkProxyConnectResponse (
      HttpResponse response,
      out string message
    )
    {
      message = null;

      if (response.IsProxyAuthenticationRequired) {
        message = "The proxy authentication is required.";

        return false;
      }

      if (!response.IsSuccess) {
        message = "The proxy has failed a connection to the requested URL.";

        return false;
      }

      return true;
    }

    private bool checkReceivedFrame (WebSocketFrame frame, out string message)
    {
      message = null;

      if (frame.IsMasked) {
        if (_client) {
          message = "A frame from the server is masked.";

          return false;
        }
      }
      else {
        if (!_client) {
          message = "A frame from a client is not masked.";

          return false;
        }
      }

      if (frame.IsCompressed) {
        if (_compression == CompressionMethod.None) {
          message = "A frame is compressed without any agreement for it.";

          return false;
        }

        if (!frame.IsData) {
          message = "A non data frame is compressed.";

          return false;
        }
      }

      if (frame.IsData) {
        if (_inContinuation) {
          message = "A data frame was received while receiving continuation frames.";

          return false;
        }
      }
      else if (frame.IsContinuation) {
        if (!_inContinuation) {
          message = "A continuation frame was received while not receiving continuation frames.";

          return false;
        }
      }

      if (frame.IsControl) {
        if (frame.Fin == Fin.More) {
          message = "A control frame is fragmented.";

          return false;
        }

        if (frame.PayloadLength > 125) {
          message = "The payload length of a control frame is greater than 125.";

          return false;
        }
      }

      if (frame.Rsv2 == Rsv.On) {
        message = "The RSV2 of a frame is non-zero without any negotiation for it.";

        return false;
      }

      if (frame.Rsv3 == Rsv.On) {
        message = "The RSV3 of a frame is non-zero without any negotiation for it.";

        return false;
      }

      return true;
    }

    private void close (ushort code, string reason)
    {
      if (_readyState == WebSocketState.Closing) {
        _log.Trace ("The close process is already in progress.");

        return;
      }

      if (_readyState == WebSocketState.Closed) {
        _log.Trace ("The connection has already been closed.");

        return;
      }

      if (code == 1005) {
        close (PayloadData.Empty, true, false);

        return;
      }

      var data = new PayloadData (code, reason);
      var send = !code.IsReservedStatusCode ();

      close (data, send, false);
    }

    private void close (PayloadData payloadData, bool send, bool received)
    {
      lock (_forState) {
        if (_readyState == WebSocketState.Closing) {
          _log.Trace ("The close process is already in progress.");

          return;
        }

        if (_readyState == WebSocketState.Closed) {
          _log.Trace ("The connection has already been closed.");

          return;
        }

        send = send && _readyState == WebSocketState.Open;

        _readyState = WebSocketState.Closing;
      }

      _log.Trace ("Begin closing the connection.");

      var res = closeHandshake (payloadData, send, received);

      releaseResources ();

      _log.Trace ("End closing the connection.");

      _readyState = WebSocketState.Closed;

      var canEmit = executeBeforeClose ();

      if (!canEmit)
        return;

      var e = new CloseEventArgs (payloadData, res);

      try {
        OnClose.Emit (this, e);
      }
      catch (Exception ex) {
        _log.Error (ex.Message);
        _log.Debug (ex.ToString ());
      }
    }

    private void closeAsync (ushort code, string reason)
    {
      if (_readyState == WebSocketState.Closing) {
        _log.Trace ("The close process is already in progress.");

        return;
      }

      if (_readyState == WebSocketState.Closed) {
        _log.Trace ("The connection has already been closed.");

        return;
      }

      if (code == 1005) {
        closeAsync (PayloadData.Empty, true, false);

        return;
      }

      var data = new PayloadData (code, reason);
      var send = !code.IsReservedStatusCode ();

      closeAsync (data, send, false);
    }

    private void closeAsync (PayloadData payloadData, bool send, bool received)
    {
      AsyncHelper.Queue (() => close (payloadData, send, received));
    }

    private bool closeHandshake (
      PayloadData payloadData,
      bool send,
      bool received
    )
    {
      var sent = false;

      if (send) {
        var frame = WebSocketFrame.CreateCloseFrame (payloadData, _client);
        var bytes = frame.ToArray ();

        sent = sendBytes (bytes);

        if (_client)
          frame.Unmask ();
      }

      var wait = !received && sent && _receivingExited != null;

      if (wait)
        received = _receivingExited.WaitOne (_waitTime);

      var ret = sent && received;

      var msg = String.Format (
                  "The closing was clean? {0} (sent: {1} received: {2})",
                  ret,
                  sent,
                  received
                );

      _log.Debug (msg);

      return ret;
    }

    // As client
    private bool connect ()
    {
      if (_readyState == WebSocketState.Connecting) {
        _log.Trace ("The connect process is in progress.");

        return false;
      }

      lock (_forState) {
        if (_readyState == WebSocketState.Open) {
          _log.Trace ("The connection has already been established.");

          return false;
        }

        if (_readyState == WebSocketState.Closing) {
          _log.Error ("The close process is in progress.");

          error ("An error has occurred before connecting.", null);

          return false;
        }

        if (_retryCountForConnect >= _maxRetryCountForConnect) {
          _log.Error ("An opportunity for reconnecting has been lost.");

          error ("An error has occurred before connecting.", null);

          return false;
        }

        if (_readyState == WebSocketState.Closed)
          initr ();

        _retryCountForConnect++;

        _readyState = WebSocketState.Connecting;

        var done = false;

        try {
          done = doHandshake ();
        }
        catch (Exception ex) {
          _log.Fatal (ex.Message);
          _log.Debug (ex.ToString ());

          abort ("An exception has occurred while connecting.", ex);
        }

        if (!done)
          return false;

        _retryCountForConnect = -1;

        _readyState = WebSocketState.Open;

        return true;
      }
    }
    
    // As client
    private AuthenticationResponse createAuthenticationResponse ()
    {
      if (_credentials == null)
        return null;

      var credentials = getCredentialsForCurrentUri ();

      if (_authChallenge == null)
        return _preAuth ? new AuthenticationResponse (credentials) : null;

      var ret = new AuthenticationResponse (
                  _authChallenge,
                  credentials,
                  _nonceCount
                );

      _nonceCount = ret.NonceCount;

      return ret;
    }

    // As client
    private string createExtensions ()
    {
      var buff = new StringBuilder (80);

      if (_compression != CompressionMethod.None) {
        var str = _compression.ToExtensionString (
                    "server_no_context_takeover",
                    "client_no_context_takeover"
                  );

        buff.AppendFormat ("{0}, ", str);
      }

      var len = buff.Length;

      if (len <= 2)
        return null;

      buff.Length = len - 2;

      return buff.ToString ();
    }

    // As server
    private HttpResponse createHandshakeFailureResponse ()
    {
      var ret = HttpResponse.CreateCloseResponse (HttpStatusCode.BadRequest);

      ret.Headers["Sec-WebSocket-Version"] = _version;

      return ret;
    }

    // As client
    private HttpRequest createHandshakeRequest (bool includeSensitiveHeaders)
    {
      var ret = HttpRequest.CreateWebSocketHandshakeRequest (_uri);

      var headers = ret.Headers;

      headers["Sec-WebSocket-Key"] = _base64Key;
      headers["Sec-WebSocket-Version"] = _version;

      if (!_origin.IsNullOrEmpty ())
        headers["Origin"] = _origin;

      if (_hasProtocol)
        headers["Sec-WebSocket-Protocol"] = _protocols.ToString (", ");

      var exts = createExtensions ();

      _hasExtension = exts != null;

      if (_hasExtension)
        headers["Sec-WebSocket-Extensions"] = exts;

      var includeCredentials = canSendSensitiveData (
                                 includeSensitiveHeaders,
                                 _credentialsOrigin
                               );
      var ares = includeCredentials ? createAuthenticationResponse () : null;

      if (ares != null)
        headers["Authorization"] = ares.ToString ();

      var hasUserHeader = _userHeaders != null && _userHeaders.Count > 0;

      var includeUserHeaders = canSendSensitiveData (
                                 includeSensitiveHeaders,
                                 _userHeadersOrigin
                               );

      if (includeUserHeaders && hasUserHeader)
        headers.Add (_userHeaders);

      var hasCookie = _cookies != null && _cookies.Count > 0;

      var includeCookies = canSendSensitiveData (
                             includeSensitiveHeaders,
                             _cookiesOrigin
                           );

      if (includeCookies && hasCookie)
        ret.SetCookies (_cookies);

      return ret;
    }

    // As server
    private HttpResponse createHandshakeResponse ()
    {
      var ret = HttpResponse.CreateWebSocketHandshakeResponse ();

      var headers = ret.Headers;

      headers["Sec-WebSocket-Accept"] = CreateResponseKey (_base64Key);

      if (_protocol != null)
        headers["Sec-WebSocket-Protocol"] = _protocol;

      if (_extensions != null)
        headers["Sec-WebSocket-Extensions"] = _extensions;

      var hasUserHeader = _userHeaders != null && _userHeaders.Count > 0;

      if (hasUserHeader)
        headers.Add (_userHeaders);

      var hasCookie = _cookies != null && _cookies.Count > 0;

      if (hasCookie)
        ret.SetCookies (_cookies);

      return ret;
    }

    // As client
    private TcpClient createTcpClient (string hostname, int port)
    {
      var ret = new TcpClient ();
      var timeout = getConnectionTimeoutMilliseconds ();
      var ar = ret.BeginConnect (hostname, port, null, null);
      var waitHandle = ar.AsyncWaitHandle;

      try {
        if (!waitHandle.WaitOne (timeout)) {
          ret.Close ();

          var fmt = "A connection to {0}:{1} has timed out.";
          var msg = String.Format (fmt, hostname, port);

          throw new TimeoutException (msg);
        }

        ret.EndConnect (ar);
      }
      catch {
        ret.Close ();

        throw;
      }
      finally {
        waitHandle.Close ();
      }

      if (_noDelay)
        ret.NoDelay = true;

      return ret;
    }

    // As server
    private bool customCheckHandshakeRequest (
      WebSocketContext context,
      out string message
    )
    {
      message = null;

      if (_handshakeRequestChecker == null)
        return true;

      message = _handshakeRequestChecker (context);

      return message == null;
    }

    // As server
    private void customRespondToHandshakeRequest (WebSocketContext context)
    {
      if (_handshakeRequestResponder == null)
        return;

      _handshakeRequestResponder (context);
    }

    private MessageEventArgs dequeueFromMessageEventQueue ()
    {
      lock (_forMessageEventQueue) {
        return _messageEventQueue.Count > 0
               ? _messageEventQueue.Dequeue ()
               : null;
      }
    }

    // As client
    private bool doHandshake ()
    {
      setClientStream (canSendSensitiveData (true, _sslConfigOrigin));

      var res = sendHandshakeRequest (0, true);

      _log.Debug (HandshakeLogFormatter.FormatResponse (res));

      _handshakeResponseHeaders = res.Headers;
      _handshakeResponseCookies = res.Cookies;

      string msg;

      if (!checkHandshakeResponse (res, out msg)) {
        _log.Error (msg);

        abort (1002, "A handshake error has occurred.");

        return false;
      }

      if (_hasProtocol)
        _protocol = _handshakeResponseHeaders["Sec-WebSocket-Protocol"];

      if (_hasExtension) {
        var exts = _handshakeResponseHeaders["Sec-WebSocket-Extensions"];

        if (exts != null)
          _extensions = exts;
        else
          _compression = CompressionMethod.None;
      }

      if (_handshakeResponseCookies.Count > 0
          && (_cookiesOrigin == null || isSameOrigin (_cookiesOrigin, _uri))) {
        _cookiesOrigin = _uri;
        Cookies.SetOrRemove (_handshakeResponseCookies);
      }

      return true;
    }

    private bool enqueueToMessageEventQueue (MessageEventArgs e)
    {
      lock (_forMessageEventQueue) {
        if (_messageEventQueue.Count >= _maxMessageEventQueueLength)
          return false;

        _messageEventQueue.Enqueue (e);

        return true;
      }
    }

    private void error (string message, Exception exception)
    {
      var e = new ErrorEventArgs (message, exception);

      try {
        OnError.Emit (this, e);
      }
      catch (Exception ex) {
        _log.Error (ex.Message);
        _log.Debug (ex.ToString ());
      }
    }

    private bool executeBeforeClose ()
    {
      return _executorBeforeClose != null ? _executorBeforeClose () : true;
    }

    private bool executeBeforeOpen ()
    {
      return _executorBeforeOpen != null ? _executorBeforeOpen () : true;
    }

    private ClientSslConfiguration getSslConfiguration ()
    {
      if (_sslConfig == null) {
        _sslConfig = new ClientSslConfiguration (_uri.DnsSafeHost);
        _sslConfigOrigin = _uri;
      }

      return _sslConfig;
    }

    // As client
    private NetworkCredential getCredentialsForCurrentUri ()
    {
      return new NetworkCredential (
               _credentials.Username,
               _credentials.Password,
               _uri.PathAndQuery
             );
    }

    // As client
    private int getConnectionTimeoutMilliseconds ()
    {
      var milliseconds = _connectionTimeout.TotalMilliseconds;

      if (milliseconds > Int32.MaxValue)
        return Int32.MaxValue;

      return Math.Max (1, (int) milliseconds);
    }

    private void init ()
    {
      _compression = CompressionMethod.None;
      _frameReadTimeout = DefaultFrameReadTimeout;
      _forAsyncSendQueue = new object ();
      _forPing = new object ();
      _forSend = new object ();
      _forState = new object ();
      _messageEventQueue = new Queue<MessageEventArgs> ();
      _forMessageEventQueue = ((ICollection) _messageEventQueue).SyncRoot;
      _maxAsyncSendQueueLength = DefaultMaxAsyncSendQueueLength;
      _maxFramePayloadLength = DefaultMaxFramePayloadLength;
      _maxMessageEventQueueLength = DefaultMaxMessageEventQueueLength;
      _maxMessagePayloadLength = DefaultMaxMessagePayloadLength;
      _maxRedirections = 5;
      _readyState = WebSocketState.New;
    }

    // As client
    private void initr ()
    {
      _handshakeResponseCookies = null;
      _handshakeResponseHeaders = null;
    }

    private void message ()
    {
      MessageEventArgs e = null;

      lock (_forMessageEventQueue) {
        if (_inMessage)
          return;

        if (_messageEventQueue.Count == 0)
          return;

        if (_readyState != WebSocketState.Open)
          return;

        e = _messageEventQueue.Dequeue ();

        _inMessage = true;
      }

      _message (e);
    }

    private void messagec (MessageEventArgs e)
    {
      do {
        try {
          OnMessage.Emit (this, e);
        }
        catch (Exception ex) {
          _log.Error (ex.Message);
          _log.Debug (ex.ToString ());

          error ("An exception has occurred during an OnMessage event.", ex);
        }

        lock (_forMessageEventQueue) {
          if (_messageEventQueue.Count == 0) {
            _inMessage = false;

            break;
          }

          if (_readyState != WebSocketState.Open) {
            _inMessage = false;

            break;
          }

          e = _messageEventQueue.Dequeue ();
        }
      }
      while (true);
    }

    private void messages (MessageEventArgs e)
    {
      try {
        OnMessage.Emit (this, e);
      }
      catch (Exception ex) {
        _log.Error (ex.Message);
        _log.Debug (ex.ToString ());

        error ("An exception has occurred during an OnMessage event.", ex);
      }

      lock (_forMessageEventQueue) {
        if (_messageEventQueue.Count == 0) {
          _inMessage = false;

          return;
        }

        if (_readyState != WebSocketState.Open) {
          _inMessage = false;

          return;
        }

        e = _messageEventQueue.Dequeue ();
      }

      ThreadPool.QueueUserWorkItem (state => messages (e));
    }

    private void open ()
    {
      _inMessage = true;

      startReceiving ();

      var canEmit = executeBeforeOpen ();

      try {
        if (canEmit)
          OnOpen.Emit (this, EventArgs.Empty);
      }
      catch (Exception ex) {
        _log.Error (ex.Message);
        _log.Debug (ex.ToString ());

        error ("An exception has occurred during the OnOpen event.", ex);
      }

      MessageEventArgs e = null;

      lock (_forMessageEventQueue) {
        if (_messageEventQueue.Count == 0) {
          _inMessage = false;

          return;
        }

        if (_readyState != WebSocketState.Open) {
          _inMessage = false;

          return;
        }

        e = _messageEventQueue.Dequeue ();
      }

      AsyncHelper.Queue (() => _message (e));
    }

    private bool ping (byte[] data)
    {
      if (_readyState != WebSocketState.Open)
        return false;

      var received = _pongReceived;

      if (received == null)
        return false;

      lock (_forPing) {
        try {
          received.Reset ();

          var sent = send (Fin.Final, Opcode.Ping, data, false);

          if (!sent)
            return false;

          return received.WaitOne (_waitTime);
        }
        catch (ObjectDisposedException) {
          return false;
        }
      }
    }

    private bool processCloseFrame (WebSocketFrame frame)
    {
      var data = frame.PayloadData;

      ushort code;
      string message;

      if (!checkClosePayload (data, out code, out message)) {
        var closeCode = code == (ushort) CloseStatusCode.InvalidData
                        ? CloseStatusCode.InvalidData
                        : CloseStatusCode.ProtocolError;

        _log.Error (message);
        close (new PayloadData ((ushort) closeCode, message), true, true);

        return false;
      }

      close (data, true, true);

      return false;
    }

    private bool processDataFrame (WebSocketFrame frame)
    {
      byte[] data;

      try {
        data = frame.IsCompressed
               ? frame.PayloadData.ApplicationData.Decompress (
                   _compression,
                   _maxMessagePayloadLength
                 )
               : frame.PayloadData.ApplicationData;
      }
      catch (WebSocketException ex) {
        _log.Error (ex.Message);
        close (new PayloadData (ex.Code, ex.Message), true, false);

        return false;
      }
      catch (Exception ex) {
        var msg = "The compressed payload data is invalid.";

        _log.Error (msg);
        _log.Debug (ex.ToString ());
        close (new PayloadData ((ushort) CloseStatusCode.ProtocolError, msg), true, false);

        return false;
      }

      if (data.LongLength > _maxMessagePayloadLength) {
        var msg = "The payload data of a message is too big.";

        _log.Error (msg);
        close (new PayloadData ((ushort) CloseStatusCode.TooBig, msg), true, false);

        return false;
      }

      if (frame.IsText && !isValidTextPayload (data)) {
        var msg = "A text frame contains invalid UTF-8.";

        _log.Error (msg);
        abort ((ushort) CloseStatusCode.InvalidData, msg);

        return false;
      }

      var e = new MessageEventArgs (frame.Opcode, data);

      if (!enqueueToMessageEventQueue (e))
        return closeDueToMessageEventQueueOverflow ();

      return true;
    }

    private bool processFragmentFrame (WebSocketFrame frame)
    {
      if (!_inContinuation) {
        if (frame.IsContinuation)
          return true;

        _fragmentsOpcode = frame.Opcode;
        _fragmentsCompressed = frame.IsCompressed;
        _fragmentsBuffer = new MemoryStream ();
        _inContinuation = true;
      }

      var payload = frame.PayloadData.ApplicationData;

      if (_fragmentsBuffer.Length + payload.LongLength > _maxMessagePayloadLength) {
        var msg = "The payload data of a fragmented message is too big.";

        _log.Error (msg);
        releaseFragmentsBuffer ();
        close (new PayloadData ((ushort) CloseStatusCode.TooBig, msg), true, false);

        return false;
      }

      _fragmentsBuffer.WriteBytes (payload, 1024);

      if (frame.IsFinal) {
        var opcode = _fragmentsOpcode;
        byte[] data;

        try {
          using (_fragmentsBuffer) {
            data = _fragmentsCompressed
                   ? _fragmentsBuffer.DecompressToArray (
                       _compression,
                       _maxMessagePayloadLength
                     )
                   : _fragmentsBuffer.ToByteArray (_maxMessagePayloadLength);
          }
        }
        catch (WebSocketException ex) {
          _log.Error (ex.Message);
          _fragmentsBuffer = null;
          _inContinuation = false;
          close (new PayloadData (ex.Code, ex.Message), true, false);

          return false;
        }
        catch (Exception ex) {
          var msg = "The compressed payload data is invalid.";

          _log.Error (msg);
          _log.Debug (ex.ToString ());
          _fragmentsBuffer = null;
          _inContinuation = false;
          close (new PayloadData ((ushort) CloseStatusCode.ProtocolError, msg), true, false);

          return false;
        }

        _fragmentsBuffer = null;
        _inContinuation = false;

        if (opcode == Opcode.Text && !isValidTextPayload (data)) {
          var msg = "A fragmented text message contains invalid UTF-8.";

          _log.Error (msg);
          abort ((ushort) CloseStatusCode.InvalidData, msg);

          return false;
        }

        var e = new MessageEventArgs (opcode, data);

        if (!enqueueToMessageEventQueue (e))
          return closeDueToMessageEventQueueOverflow ();
      }

      return true;
    }

    private bool closeDueToMessageEventQueueOverflow ()
    {
      var msg = "The message event queue is full.";

      _log.Error (msg);
      close (new PayloadData ((ushort) CloseStatusCode.PolicyViolation, msg), true, false);

      return false;
    }

    private static bool isValidTextPayload (byte[] data)
    {
      try {
        new UTF8Encoding (false, true).GetString (data);

        return true;
      }
      catch (ArgumentException) {
        return false;
      }
    }

    private static bool checkClosePayload (
      PayloadData payloadData,
      out ushort code,
      out string message
    )
    {
      var data = payloadData.ToArray ();
      var len = data.LongLength;

      code = (ushort) CloseStatusCode.ProtocolError;
      message = null;

      if (len == 0)
        return true;

      if (len == 1) {
        message = "A close frame has a one-byte payload.";

        return false;
      }

      code = data.SubArray (0, 2).ToUInt16 (ByteOrder.Big);

      if (!code.IsCloseStatusCode () || code.IsReservedStatusCode ()) {
        message = "A close frame has an invalid status code.";

        return false;
      }

      if (len <= 2)
        return true;

      if (isValidTextPayload (data.SubArray (2, len - 2)))
        return true;

      code = (ushort) CloseStatusCode.InvalidData;
      message = "A close frame reason contains invalid UTF-8.";

      return false;
    }

    private bool processPingFrame (WebSocketFrame frame)
    {
      _log.Trace ("A ping was received.");

      var pong = WebSocketFrame.CreatePongFrame (frame.PayloadData, _client);

      lock (_forState) {
        if (_readyState != WebSocketState.Open) {
          _log.Trace ("A pong to this ping cannot be sent.");

          return true;
        }

        var bytes = pong.ToArray ();
        var sent = sendBytes (bytes);

        if (!sent)
          return false;
      }

      _log.Trace ("A pong to this ping has been sent.");

      if (_emitOnPing) {
        if (_client)
          pong.Unmask ();

        var e = new MessageEventArgs (frame);

        if (!enqueueToMessageEventQueue (e))
          return closeDueToMessageEventQueueOverflow ();
      }

      return true;
    }

    private bool processPongFrame (WebSocketFrame frame)
    {
      _log.Trace ("A pong was received.");

      try {
        _pongReceived.Set ();
      }
      catch (NullReferenceException) {
        return false;
      }
      catch (ObjectDisposedException) {
        return false;
      }

      _log.Trace ("It has been signaled.");

      return true;
    }

    private bool processReceivedFrame (WebSocketFrame frame)
    {
      string msg;

      if (!checkReceivedFrame (frame, out msg)) {
        _log.Error (msg);
        _log.Debug (frame.ToString (false));

        close (new PayloadData ((ushort) CloseStatusCode.ProtocolError, msg), true, false);

        return false;
      }

      frame.Unmask ();

      return frame.IsFragment
             ? processFragmentFrame (frame)
             : frame.IsData
               ? processDataFrame (frame)
               : frame.IsPing
                 ? processPingFrame (frame)
                 : frame.IsPong
                   ? processPongFrame (frame)
                   : frame.IsClose
                      ? processCloseFrame (frame)
                      : processUnsupportedFrame (frame);
    }

    private void releaseFragmentsBuffer ()
    {
      if (_fragmentsBuffer != null) {
        _fragmentsBuffer.Dispose ();
        _fragmentsBuffer = null;
      }

      _inContinuation = false;
    }

    // As server
    private void processSecWebSocketExtensionsClientHeader (string value)
    {
      if (value == null)
        return;

      var buff = new StringBuilder (80);

      var compRequested = false;

      foreach (var elm in value.SplitHeaderValue (',')) {
        var ext = elm.Trim ();

        if (ext.Length == 0)
          continue;

        if (!compRequested) {
          if (ext.IsCompressionExtension (CompressionMethod.Deflate)) {
            _compression = CompressionMethod.Deflate;

            var str = _compression.ToExtensionString (
                        "client_no_context_takeover",
                        "server_no_context_takeover"
                      );

            buff.AppendFormat ("{0}, ", str);

            compRequested = true;
          }
        }
      }

      var len = buff.Length;

      if (len <= 2)
        return;

      buff.Length = len - 2;

      _extensions = buff.ToString ();
    }

    private bool processUnsupportedFrame (WebSocketFrame frame)
    {
      _log.Fatal ("An unsupported frame was received.");
      _log.Debug (frame.ToString (false));

      abort (1003, "There is no way to handle it.");

      return false;
    }

    // As server
    private void refuseHandshake (ushort code, string reason)
    {
      createHandshakeFailureResponse ().WriteTo (_stream);

      abort (code, reason);
    }

    // As client
    private void releaseClientResources ()
    {
      if (_stream != null) {
        _stream.Dispose ();

        _stream = null;
      }

      if (_tcpClient != null) {
        _tcpClient.Close ();

        _tcpClient = null;
      }
    }

    private void releaseCommonResources ()
    {
      if (_fragmentsBuffer != null) {
        _fragmentsBuffer.Dispose ();

        _fragmentsBuffer = null;
        _inContinuation = false;
      }

      if (_pongReceived != null) {
        _pongReceived.Close ();

        _pongReceived = null;
      }

      if (_receivingExited != null) {
        _receivingExited.Close ();

        _receivingExited = null;
      }
    }

    private void releaseResources ()
    {
      if (_client)
        releaseClientResources ();
      else
        releaseServerResources ();

      releaseCommonResources ();
    }

    // As server
    private void releaseServerResources ()
    {
      if (_closeContext != null) {
        _closeContext ();

        _closeContext = null;
      }

      _stream = null;
      _context = null;
    }

    private bool send (byte[] rawFrame)
    {
      lock (_forState) {
        if (_readyState != WebSocketState.Open) {
          _log.Error ("The current state of the interface is not Open.");

          return false;
        }

        return sendBytes (rawFrame);
      }
    }

    private bool send (Opcode opcode, Stream sourceStream)
    {
      lock (_forSend) {
        var dataStream = sourceStream;
        var compressed = false;
        var sent = false;

        try {
          if (_compression != CompressionMethod.None) {
            dataStream = sourceStream.Compress (_compression);
            compressed = true;
          }

          sent = send (opcode, dataStream, compressed);

          if (!sent)
            error ("A send has failed.", null);
        }
        catch (Exception ex) {
          _log.Error (ex.Message);
          _log.Debug (ex.ToString ());

          error ("An exception has occurred during a send.", ex);
        }
        finally {
          if (compressed)
            dataStream.Dispose ();

          sourceStream.Dispose ();
        }

        return sent;
      }
    }

    private bool send (Opcode opcode, Stream dataStream, bool compressed)
    {
      var len = dataStream.Length;

      if (len == 0)
        return send (Fin.Final, opcode, _emptyBytes, false);

      var quo = len / FragmentLength;
      var rem = (int) (len % FragmentLength);

      byte[] buff = null;

      if (quo == 0) {
        buff = new byte[rem];

        return dataStream.Read (buff, 0, rem) == rem
               && send (Fin.Final, opcode, buff, compressed);
      }

      if (quo == 1 && rem == 0) {
        buff = new byte[FragmentLength];

        return dataStream.Read (buff, 0, FragmentLength) == FragmentLength
               && send (Fin.Final, opcode, buff, compressed);
      }

      /* Send fragments */

      // Begin

      buff = new byte[FragmentLength];

      var sent = dataStream.Read (buff, 0, FragmentLength) == FragmentLength
                 && send (Fin.More, opcode, buff, compressed);

      if (!sent)
        return false;

      // Continue

      var n = rem == 0 ? quo - 2 : quo - 1;

      for (long i = 0; i < n; i++) {
        sent = dataStream.Read (buff, 0, FragmentLength) == FragmentLength
               && send (Fin.More, Opcode.Cont, buff, false);

        if (!sent)
          return false;
      }

      // End

      if (rem == 0)
        rem = FragmentLength;
      else
        buff = new byte[rem];

      return dataStream.Read (buff, 0, rem) == rem
             && send (Fin.Final, Opcode.Cont, buff, false);
    }

    private bool send (Fin fin, Opcode opcode, byte[] data, bool compressed)
    {
      var frame = new WebSocketFrame (fin, opcode, data, compressed, _client);
      var rawFrame = frame.ToArray ();

      return send (rawFrame);
    }

    private void sendAsync (
      Opcode opcode,
      Stream sourceStream,
      Action<bool> completed
    )
    {
      if (!tryIncrementAsyncSendQueue ()) {
        var msg = "The async send queue is full.";

        _log.Error (msg);
        sourceStream.Dispose ();

        if (completed != null) {
          try {
            completed (false);
          }
          catch (Exception ex) {
            _log.Error (ex.Message);
            _log.Debug (ex.ToString ());

            error (
              "An exception has occurred during the callback for an async send.",
              ex
            );
          }
        }

        return;
      }

      AsyncHelper.Queue (
        () => {
          try {
            var sent = send (opcode, sourceStream);

            if (completed != null)
              completed (sent);
          }
          catch (Exception ex) {
            _log.Error (ex.Message);
            _log.Debug (ex.ToString ());

            error (
              "An exception has occurred during the callback for an async send.",
              ex
            );
          }
          finally {
            decrementAsyncSendQueue ();
          }
        }
      );
    }

    private void decrementAsyncSendQueue ()
    {
      lock (_forAsyncSendQueue)
        _asyncSendQueueLength--;
    }

    private bool tryIncrementAsyncSendQueue ()
    {
      lock (_forAsyncSendQueue) {
        if (_asyncSendQueueLength >= _maxAsyncSendQueueLength)
          return false;

        _asyncSendQueueLength++;

        return true;
      }
    }

    private bool sendBytes (byte[] bytes)
    {
      try {
        _stream.Write (bytes, 0, bytes.Length);
      }
      catch (Exception ex) {
        _log.Error (ex.Message);
        _log.Debug (ex.ToString ());

        return false;
      }

      return true;
    }

    // As client
    private HttpResponse sendHandshakeRequest (
      int redirectionCount,
      bool includeSensitiveHeaders
    )
    {
      var req = createHandshakeRequest (includeSensitiveHeaders);

      _log.Debug (HandshakeLogFormatter.FormatRequest (req));

      var timeout = getConnectionTimeoutMilliseconds ();
      var res = req.GetResponse (_stream, timeout);

      if (res.IsUnauthorized) {
        var val = res.Headers["WWW-Authenticate"];

        if (val.IsNullOrEmpty ()) {
          _log.Debug ("No authentication challenge is specified.");

          return res;
        }

        var achal = AuthenticationChallenge.Parse (val);

        if (achal == null) {
          _log.Debug ("An invalid authentication challenge is specified.");

          return res;
        }

        _authChallenge = achal;

        if (_credentials == null
            || !canSendSensitiveData (
                  includeSensitiveHeaders,
                  _credentialsOrigin
                ))
          return res;

        var ares = new AuthenticationResponse (
                     _authChallenge,
                     getCredentialsForCurrentUri (),
                     _nonceCount
                   );

        _nonceCount = ares.NonceCount;

        req.Headers["Authorization"] = ares.ToString ();

        if (res.CloseConnection) {
          releaseClientResources ();
          setClientStream (
            canSendSensitiveData (includeSensitiveHeaders, _sslConfigOrigin)
          );
        }

        _log.Debug (HandshakeLogFormatter.FormatRequest (req));

        res = req.GetResponse (_stream, timeout);
      }

      if (res.IsRedirect) {
        if (!_enableRedirection)
          return res;

        if (redirectionCount >= _maxRedirections) {
          _log.Debug ("The maximum number of redirects has been reached.");

          return res;
        }

        var val = res.Headers["Location"];

        if (val.IsNullOrEmpty ()) {
          _log.Debug ("No URL to redirect is located.");

          return res;
        }

        Uri uri;
        string msg;

        if (!tryCreateRedirectUri (_uri, val, out uri, out msg)) {
          _log.Debug ("An invalid URL to redirect is located.");

          return res;
        }

        if (_secure && uri.Scheme == "ws" && !_allowInsecureRedirection) {
          _log.Debug ("A redirect from wss to ws has been rejected.");

          return res;
        }

        var sameOrigin = isSameOrigin (_uri, uri);

        if (!sameOrigin) {
          _authChallenge = null;
          _nonceCount = 0;
        }

        releaseClientResources ();

        _uri = uri;
        _secure = uri.Scheme == "wss";

        var includeSensitiveHeadersOnRedirect =
          includeSensitiveHeaders && sameOrigin;

        setClientStream (
          canSendSensitiveData (
            includeSensitiveHeadersOnRedirect,
            _sslConfigOrigin
          )
        );

        return sendHandshakeRequest (
                 redirectionCount + 1,
                 includeSensitiveHeadersOnRedirect
               );
      }

      return res;
    }

    private bool canSendSensitiveData (bool allowedByRedirect, Uri origin)
    {
      return allowedByRedirect
             && (origin == null || isSameOrigin (origin, _uri));
    }

    private static bool isSameOrigin (Uri first, Uri second)
    {
      return String.Equals (first.Scheme, second.Scheme, StringComparison.OrdinalIgnoreCase)
             && String.Equals (
                  first.DnsSafeHost,
                  second.DnsSafeHost,
                  StringComparison.OrdinalIgnoreCase
                )
             && first.Port == second.Port;
    }

    private static bool tryCreateRedirectUri (
      Uri currentUri,
      string location,
      out Uri result,
      out string message
    )
    {
      result = null;
      message = null;

      Uri uri;

      if (!Uri.TryCreate (currentUri, location, out uri)) {
        message = "An invalid redirect URL.";

        return false;
      }

      return uri.ToString ().TryCreateWebSocketUri (out result, out message);
    }

    // As client
    private HttpResponse sendProxyConnectRequest ()
    {
      var req = HttpRequest.CreateConnectRequest (_uri);

      var timeout = getConnectionTimeoutMilliseconds ();
      var res = req.GetResponse (_stream, timeout);

      if (res.IsProxyAuthenticationRequired) {
        if (_proxyCredentials == null)
          return res;

        var val = res.Headers["Proxy-Authenticate"];

        if (val.IsNullOrEmpty ()) {
          _log.Debug ("No proxy authentication challenge is specified.");

          return res;
        }

        var achal = AuthenticationChallenge.Parse (val);

        if (achal == null) {
          _log.Debug ("An invalid proxy authentication challenge is specified.");

          return res;
        }

        var credentials = new NetworkCredential (
                            _proxyCredentials.Username,
                            _proxyCredentials.Password,
                            String.Format (
                              "{0}:{1}",
                              _uri.DnsSafeHost,
                              _uri.Port
                            )
                          );
        var ares = new AuthenticationResponse (achal, credentials, 0);

        req.Headers["Proxy-Authorization"] = ares.ToString ();

        if (res.CloseConnection) {
          releaseClientResources ();

          _tcpClient = createTcpClient (_proxyUri.DnsSafeHost, _proxyUri.Port);
          _stream = _tcpClient.GetStream ();
        }

        res = req.GetResponse (_stream, timeout);
      }

      return res;
    }

    // As client
    private void setClientStream (bool includeSensitiveCredentials)
    {
      if (_proxyUri != null) {
        _tcpClient = createTcpClient (_proxyUri.DnsSafeHost, _proxyUri.Port);
        _stream = _tcpClient.GetStream ();

        var res = sendProxyConnectRequest ();

        string msg;

        if (!checkProxyConnectResponse (res, out msg))
          throw new WebSocketException (msg);
      }
      else {
        _tcpClient = createTcpClient (_uri.DnsSafeHost, _uri.Port);
        _stream = _tcpClient.GetStream ();
      }

      if (_secure) {
        var conf = getSslConfiguration ();
        var host = _uri.DnsSafeHost;

        if (includeSensitiveCredentials
            && !String.Equals (
                  conf.TargetHost,
                  host,
                  StringComparison.OrdinalIgnoreCase
                )) {
          var msg = "An invalid host name is specified.";

          throw new WebSocketException (
                  CloseStatusCode.TlsHandshakeFailure,
                  msg
                );
        }

        if (!includeSensitiveCredentials) {
          conf = new ClientSslConfiguration (conf);
          conf.TargetHost = host;
          conf.ClientCertificates = null;
          conf.ClientCertificateSelectionCallback = null;
        }

        try {
          var sslStream = new SslStream (
                            _stream,
                            false,
                            conf.ServerCertificateValidationCallback,
                            conf.ClientCertificateSelectionCallback
                          );

          sslStream.AuthenticateAsClientWithTimeout (
            host,
            conf.ClientCertificates,
            conf.EnabledSslProtocols,
            conf.CheckCertificateRevocation,
            getConnectionTimeoutMilliseconds ()
          );

          _stream = sslStream;
        }
        catch (Exception ex) {
          throw new WebSocketException (
                  CloseStatusCode.TlsHandshakeFailure,
                  ex
                );
        }
      }
    }

    private void startReceiving ()
    {
      if (_messageEventQueue.Count > 0)
        _messageEventQueue.Clear ();

      _pongReceived = new ManualResetEvent (false);
      _receivingExited = new ManualResetEvent (false);

      Action receive = null;
      receive =
        () => WebSocketFrame.ReadFrameAsync (
                _stream,
                false,
                (ulong) _maxFramePayloadLength,
                _frameReadTimeout,
                frame => {
                  var doNext = processReceivedFrame (frame)
                               && _readyState != WebSocketState.Closed;

                  if (!doNext) {
                    var exited = _receivingExited;

                    if (exited != null)
                      exited.Set ();

                    return;
                  }

                  receive ();

                  if (_inMessage)
                    return;

                  message ();
                },
                ex => {
                  _log.Fatal (ex.Message);
                  _log.Debug (ex.ToString ());

                  var wsEx = ex as WebSocketException;

                  if (wsEx != null &&
                      wsEx.Code.IsCloseStatusCode () &&
                      !wsEx.Code.IsReservedStatusCode ()) {
                    close (new PayloadData (wsEx.Code, wsEx.Message), true, false);

                    return;
                  }

                  abort ("An exception has occurred while receiving.", ex);
                }
              );

      receive ();
    }

    // As client
    private bool validateSecWebSocketExtensionsServerHeader (string value)
    {
      if (!_hasExtension)
        return false;

      if (value.Length == 0)
        return false;

      var compRequested = _compression != CompressionMethod.None;

      foreach (var elm in value.SplitHeaderValue (',')) {
        var ext = elm.Trim ();

        if (compRequested && ext.IsCompressionExtension (_compression)) {
          var param1 = "server_no_context_takeover";
          var param2 = "client_no_context_takeover";

          if (!ext.Contains (param1)) {
            // The server did not send back "server_no_context_takeover".

            return false;
          }

          var name = _compression.ToExtensionString ();

          var isInvalid = ext.SplitHeaderValue (';').Contains (
                            t => {
                              t = t.Trim ();

                              var isValid = t == name
                                            || t == param1
                                            || t == param2;

                              return !isValid;
                            }
                          );

          if (isInvalid)
            return false;

          compRequested = false;
        }
        else {
          return false;
        }
      }

      return true;
    }

    #endregion

    #region Internal Methods

    // As server
    internal void Accept ()
    {
      var accepted = accept ();

      if (!accepted)
        return;

      open ();
    }

    // As server
    internal void AcceptAsync ()
    {
      AsyncHelper.QueueBlocking (
        () => {
          var accepted = accept ();

          if (!accepted)
            return;

          open ();
        }
      );
    }

    // As server
    internal void Close (PayloadData payloadData, byte[] rawFrame)
    {
      lock (_forState) {
        if (_readyState == WebSocketState.Closing) {
          _log.Trace ("The close process is already in progress.");

          return;
        }

        if (_readyState == WebSocketState.Closed) {
          _log.Trace ("The connection has already been closed.");

          return;
        }

        _readyState = WebSocketState.Closing;
      }

      _log.Trace ("Begin closing the connection.");

      var sent = rawFrame != null && sendBytes (rawFrame);
      var received = sent && _receivingExited != null
                     ? _receivingExited.WaitOne (_waitTime)
                     : false;

      var res = sent && received;

      var msg = String.Format (
                  "The closing was clean? {0} (sent: {1} received: {2})",
                  res,
                  sent,
                  received
                );

      _log.Debug (msg);

      releaseServerResources ();
      releaseCommonResources ();

      _log.Trace ("End closing the connection.");

      _readyState = WebSocketState.Closed;

      var canEmit = executeBeforeClose ();

      if (!canEmit)
        return;

      var e = new CloseEventArgs (payloadData, res);

      try {
        OnClose.Emit (this, e);
      }
      catch (Exception ex) {
        _log.Error (ex.Message);
        _log.Debug (ex.ToString ());
      }
    }

    // As client
    internal static string CreateBase64Key ()
    {
      var key = new byte[16];

      RandomNumber.GetBytes (key);

      return Convert.ToBase64String (key);
    }

    internal static string CreateResponseKey (string base64Key)
    {
      SHA1 sha1 = new SHA1CryptoServiceProvider ();

      var src = base64Key + _guid;
      var bytes = src.GetUTF8EncodedBytes ();
      var key = sha1.ComputeHash (bytes);

      return Convert.ToBase64String (key);
    }

    // As server
    internal bool Ping (byte[] rawFrame)
    {
      if (_readyState != WebSocketState.Open)
        return false;

      var received = _pongReceived;

      if (received == null)
        return false;

      lock (_forPing) {
        try {
          received.Reset ();

          var sent = send (rawFrame);

          if (!sent)
            return false;

          return received.WaitOne (_waitTime);
        }
        catch (ObjectDisposedException) {
          return false;
        }
      }
    }

    // As server
    internal void Send (
      Opcode opcode,
      byte[] data,
      Dictionary<CompressionMethod, byte[]> cache
    )
    {
      lock (_forSend) {
        byte[] found;

        if (!cache.TryGetValue (_compression, out found)) {
          found = new WebSocketFrame (
                    Fin.Final,
                    opcode,
                    data.Compress (_compression),
                    _compression != CompressionMethod.None,
                    false
                  )
                  .ToArray ();

          cache.Add (_compression, found);
        }

        send (found);
      }
    }

    // As server
    internal void Send (
      Opcode opcode,
      Stream sourceStream,
      Dictionary<CompressionMethod, Stream> cache
    )
    {
      lock (_forSend) {
        Stream found;

        if (!cache.TryGetValue (_compression, out found)) {
          found = sourceStream.Compress (_compression);

          cache.Add (_compression, found);
        }
        else {
          found.Position = 0;
        }

        send (opcode, found, _compression != CompressionMethod.None);
      }
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Closes the connection.
    /// </summary>
    /// <remarks>
    /// This method does nothing if the current state of the interface is
    /// Closing or Closed.
    /// </remarks>
    public void Close ()
    {
      close (1005, String.Empty);
    }

    /// <summary>
    /// Closes the connection with the specified status code.
    /// </summary>
    /// <remarks>
    /// This method does nothing if the current state of the interface is
    /// Closing or Closed.
    /// </remarks>
    /// <param name="code">
    ///   <para>
    ///   A <see cref="ushort"/> that specifies the status code indicating
    ///   the reason for the close.
    ///   </para>
    ///   <para>
    ///   The status codes are defined in
    ///   <see href="http://tools.ietf.org/html/rfc6455#section-7.4">
    ///   Section 7.4</see> of RFC 6455.
    ///   </para>
    /// </param>
    /// <exception cref="ArgumentException">
    ///   <para>
    ///   <paramref name="code"/> is 1011 (server error).
    ///   It cannot be used by a client.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   <paramref name="code"/> is 1010 (mandatory extension).
    ///   It cannot be used by a server.
    ///   </para>
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="code"/> is less than 1000 or greater than 4999.
    /// </exception>
    public void Close (ushort code)
    {
      Close (code, String.Empty);
    }

    /// <summary>
    /// Closes the connection with the specified status code.
    /// </summary>
    /// <remarks>
    /// This method does nothing if the current state of the interface is
    /// Closing or Closed.
    /// </remarks>
    /// <param name="code">
    ///   <para>
    ///   One of the <see cref="CloseStatusCode"/> enum values.
    ///   </para>
    ///   <para>
    ///   It specifies the status code indicating the reason for the close.
    ///   </para>
    /// </param>
    /// <exception cref="ArgumentException">
    ///   <para>
    ///   <paramref name="code"/> is an undefined enum value.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   <paramref name="code"/> is <see cref="CloseStatusCode.ServerError"/>.
    ///   It cannot be used by a client.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   <paramref name="code"/> is <see cref="CloseStatusCode.MandatoryExtension"/>.
    ///   It cannot be used by a server.
    ///   </para>
    /// </exception>
    public void Close (CloseStatusCode code)
    {
      Close (code, String.Empty);
    }

    /// <summary>
    /// Closes the connection with the specified status code and reason.
    /// </summary>
    /// <remarks>
    /// This method does nothing if the current state of the interface is
    /// Closing or Closed.
    /// </remarks>
    /// <param name="code">
    ///   <para>
    ///   A <see cref="ushort"/> that specifies the status code indicating
    ///   the reason for the close.
    ///   </para>
    ///   <para>
    ///   The status codes are defined in
    ///   <see href="http://tools.ietf.org/html/rfc6455#section-7.4">
    ///   Section 7.4</see> of RFC 6455.
    ///   </para>
    /// </param>
    /// <param name="reason">
    ///   <para>
    ///   A <see cref="string"/> that specifies the reason for the close.
    ///   </para>
    ///   <para>
    ///   Its size must be 123 bytes or less in UTF-8.
    ///   </para>
    /// </param>
    /// <exception cref="ArgumentException">
    ///   <para>
    ///   <paramref name="code"/> is 1011 (server error).
    ///   It cannot be used by a client.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   <paramref name="code"/> is 1010 (mandatory extension).
    ///   It cannot be used by a server.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   <paramref name="code"/> is 1005 (no status) and
    ///   <paramref name="reason"/> is specified.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   <paramref name="reason"/> could not be UTF-8-encoded.
    ///   </para>
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///   <para>
    ///   <paramref name="code"/> is less than 1000 or greater than 4999.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   The size of <paramref name="reason"/> is greater than 123 bytes.
    ///   </para>
    /// </exception>
    public void Close (ushort code, string reason)
    {
      if (!code.IsCloseStatusCode ()) {
        var msg = "Less than 1000 or greater than 4999.";

        throw new ArgumentOutOfRangeException ("code", msg);
      }

      if (_client) {
        if (code == 1011) {
          var msg = "1011 cannot be used.";

          throw new ArgumentException (msg, "code");
        }
      }
      else {
        if (code == 1010) {
          var msg = "1010 cannot be used.";

          throw new ArgumentException (msg, "code");
        }
      }

      if (reason.IsNullOrEmpty ()) {
        close (code, String.Empty);

        return;
      }

      if (code == 1005) {
        var msg = "1005 cannot be used.";

        throw new ArgumentException (msg, "code");
      }

      byte[] bytes;

      if (!reason.TryGetUTF8EncodedBytes (out bytes)) {
        var msg = "It could not be UTF-8-encoded.";

        throw new ArgumentException (msg, "reason");
      }

      if (bytes.Length > 123) {
        var msg = "Its size is greater than 123 bytes.";

        throw new ArgumentOutOfRangeException ("reason", msg);
      }

      close (code, reason);
    }

    /// <summary>
    /// Closes the connection with the specified status code and reason.
    /// </summary>
    /// <remarks>
    /// This method does nothing if the current state of the interface is
    /// Closing or Closed.
    /// </remarks>
    /// <param name="code">
    ///   <para>
    ///   One of the <see cref="CloseStatusCode"/> enum values.
    ///   </para>
    ///   <para>
    ///   It specifies the status code indicating the reason for the close.
    ///   </para>
    /// </param>
    /// <param name="reason">
    ///   <para>
    ///   A <see cref="string"/> that specifies the reason for the close.
    ///   </para>
    ///   <para>
    ///   Its size must be 123 bytes or less in UTF-8.
    ///   </para>
    /// </param>
    /// <exception cref="ArgumentException">
    ///   <para>
    ///   <paramref name="code"/> is an undefined enum value.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   <paramref name="code"/> is <see cref="CloseStatusCode.ServerError"/>.
    ///   It cannot be used by a client.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   <paramref name="code"/> is <see cref="CloseStatusCode.MandatoryExtension"/>.
    ///   It cannot be used by a server.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   <paramref name="code"/> is <see cref="CloseStatusCode.NoStatus"/> and
    ///   <paramref name="reason"/> is specified.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   <paramref name="reason"/> could not be UTF-8-encoded.
    ///   </para>
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The size of <paramref name="reason"/> is greater than 123 bytes.
    /// </exception>
    public void Close (CloseStatusCode code, string reason)
    {
      if (!code.IsDefined ()) {
        var msg = "An undefined enum value.";

        throw new ArgumentException (msg, "code");
      }

      if (_client) {
        if (code == CloseStatusCode.ServerError) {
          var msg = "ServerError cannot be used.";

          throw new ArgumentException (msg, "code");
        }
      }
      else {
        if (code == CloseStatusCode.MandatoryExtension) {
          var msg = "MandatoryExtension cannot be used.";

          throw new ArgumentException (msg, "code");
        }
      }

      if (reason.IsNullOrEmpty ()) {
        close ((ushort) code, String.Empty);

        return;
      }

      if (code == CloseStatusCode.NoStatus) {
        var msg = "NoStatus cannot be used.";

        throw new ArgumentException (msg, "code");
      }

      byte[] bytes;

      if (!reason.TryGetUTF8EncodedBytes (out bytes)) {
        var msg = "It could not be UTF-8-encoded.";

        throw new ArgumentException (msg, "reason");
      }

      if (bytes.Length > 123) {
        var msg = "Its size is greater than 123 bytes.";

        throw new ArgumentOutOfRangeException ("reason", msg);
      }

      close ((ushort) code, reason);
    }

    /// <summary>
    /// Closes the connection asynchronously.
    /// </summary>
    /// <remarks>
    ///   <para>
    ///   This method does not wait for the close to be complete.
    ///   </para>
    ///   <para>
    ///   This method does nothing if the current state of the interface is
    ///   Closing or Closed.
    ///   </para>
    /// </remarks>
    public void CloseAsync ()
    {
      closeAsync (1005, String.Empty);
    }

    /// <summary>
    /// Closes the connection asynchronously with the specified status code.
    /// </summary>
    /// <remarks>
    ///   <para>
    ///   This method does not wait for the close to be complete.
    ///   </para>
    ///   <para>
    ///   This method does nothing if the current state of the interface is
    ///   Closing or Closed.
    ///   </para>
    /// </remarks>
    /// <param name="code">
    ///   <para>
    ///   A <see cref="ushort"/> that specifies the status code indicating
    ///   the reason for the close.
    ///   </para>
    ///   <para>
    ///   The status codes are defined in
    ///   <see href="http://tools.ietf.org/html/rfc6455#section-7.4">
    ///   Section 7.4</see> of RFC 6455.
    ///   </para>
    /// </param>
    /// <exception cref="ArgumentException">
    ///   <para>
    ///   <paramref name="code"/> is 1011 (server error).
    ///   It cannot be used by a client.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   <paramref name="code"/> is 1010 (mandatory extension).
    ///   It cannot be used by a server.
    ///   </para>
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="code"/> is less than 1000 or greater than 4999.
    /// </exception>
    public void CloseAsync (ushort code)
    {
      CloseAsync (code, String.Empty);
    }

    /// <summary>
    /// Closes the connection asynchronously with the specified status code.
    /// </summary>
    /// <remarks>
    ///   <para>
    ///   This method does not wait for the close to be complete.
    ///   </para>
    ///   <para>
    ///   This method does nothing if the current state of the interface is
    ///   Closing or Closed.
    ///   </para>
    /// </remarks>
    /// <param name="code">
    ///   <para>
    ///   One of the <see cref="CloseStatusCode"/> enum values.
    ///   </para>
    ///   <para>
    ///   It specifies the status code indicating the reason for the close.
    ///   </para>
    /// </param>
    /// <exception cref="ArgumentException">
    ///   <para>
    ///   <paramref name="code"/> is an undefined enum value.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   <paramref name="code"/> is <see cref="CloseStatusCode.ServerError"/>.
    ///   It cannot be used by a client.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   <paramref name="code"/> is <see cref="CloseStatusCode.MandatoryExtension"/>.
    ///   It cannot be used by a server.
    ///   </para>
    /// </exception>
    public void CloseAsync (CloseStatusCode code)
    {
      CloseAsync (code, String.Empty);
    }

    /// <summary>
    /// Closes the connection asynchronously with the specified status code and
    /// reason.
    /// </summary>
    /// <remarks>
    ///   <para>
    ///   This method does not wait for the close to be complete.
    ///   </para>
    ///   <para>
    ///   This method does nothing if the current state of the interface is
    ///   Closing or Closed.
    ///   </para>
    /// </remarks>
    /// <param name="code">
    ///   <para>
    ///   A <see cref="ushort"/> that specifies the status code indicating
    ///   the reason for the close.
    ///   </para>
    ///   <para>
    ///   The status codes are defined in
    ///   <see href="http://tools.ietf.org/html/rfc6455#section-7.4">
    ///   Section 7.4</see> of RFC 6455.
    ///   </para>
    /// </param>
    /// <param name="reason">
    ///   <para>
    ///   A <see cref="string"/> that specifies the reason for the close.
    ///   </para>
    ///   <para>
    ///   Its size must be 123 bytes or less in UTF-8.
    ///   </para>
    /// </param>
    /// <exception cref="ArgumentException">
    ///   <para>
    ///   <paramref name="code"/> is 1011 (server error).
    ///   It cannot be used by a client.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   <paramref name="code"/> is 1010 (mandatory extension).
    ///   It cannot be used by a server.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   <paramref name="code"/> is 1005 (no status) and
    ///   <paramref name="reason"/> is specified.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   <paramref name="reason"/> could not be UTF-8-encoded.
    ///   </para>
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///   <para>
    ///   <paramref name="code"/> is less than 1000 or greater than 4999.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   The size of <paramref name="reason"/> is greater than 123 bytes.
    ///   </para>
    /// </exception>
    public void CloseAsync (ushort code, string reason)
    {
      if (!code.IsCloseStatusCode ()) {
        var msg = "Less than 1000 or greater than 4999.";

        throw new ArgumentOutOfRangeException ("code", msg);
      }

      if (_client) {
        if (code == 1011) {
          var msg = "1011 cannot be used.";

          throw new ArgumentException (msg, "code");
        }
      }
      else {
        if (code == 1010) {
          var msg = "1010 cannot be used.";

          throw new ArgumentException (msg, "code");
        }
      }

      if (reason.IsNullOrEmpty ()) {
        closeAsync (code, String.Empty);

        return;
      }

      if (code == 1005) {
        var msg = "1005 cannot be used.";

        throw new ArgumentException (msg, "code");
      }

      byte[] bytes;

      if (!reason.TryGetUTF8EncodedBytes (out bytes)) {
        var msg = "It could not be UTF-8-encoded.";

        throw new ArgumentException (msg, "reason");
      }

      if (bytes.Length > 123) {
        var msg = "Its size is greater than 123 bytes.";

        throw new ArgumentOutOfRangeException ("reason", msg);
      }

      closeAsync (code, reason);
    }

    /// <summary>
    /// Closes the connection asynchronously with the specified status code and
    /// reason.
    /// </summary>
    /// <remarks>
    ///   <para>
    ///   This method does not wait for the close to be complete.
    ///   </para>
    ///   <para>
    ///   This method does nothing if the current state of the interface is
    ///   Closing or Closed.
    ///   </para>
    /// </remarks>
    /// <param name="code">
    ///   <para>
    ///   One of the <see cref="CloseStatusCode"/> enum values.
    ///   </para>
    ///   <para>
    ///   It specifies the status code indicating the reason for the close.
    ///   </para>
    /// </param>
    /// <param name="reason">
    ///   <para>
    ///   A <see cref="string"/> that specifies the reason for the close.
    ///   </para>
    ///   <para>
    ///   Its size must be 123 bytes or less in UTF-8.
    ///   </para>
    /// </param>
    /// <exception cref="ArgumentException">
    ///   <para>
    ///   <paramref name="code"/> is an undefined enum value.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   <paramref name="code"/> is <see cref="CloseStatusCode.ServerError"/>.
    ///   It cannot be used by a client.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   <paramref name="code"/> is <see cref="CloseStatusCode.MandatoryExtension"/>.
    ///   It cannot be used by a server.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   <paramref name="code"/> is <see cref="CloseStatusCode.NoStatus"/> and
    ///   <paramref name="reason"/> is specified.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   <paramref name="reason"/> could not be UTF-8-encoded.
    ///   </para>
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The size of <paramref name="reason"/> is greater than 123 bytes.
    /// </exception>
    public void CloseAsync (CloseStatusCode code, string reason)
    {
      if (!code.IsDefined ()) {
        var msg = "An undefined enum value.";

        throw new ArgumentException (msg, "code");
      }

      if (_client) {
        if (code == CloseStatusCode.ServerError) {
          var msg = "ServerError cannot be used.";

          throw new ArgumentException (msg, "code");
        }
      }
      else {
        if (code == CloseStatusCode.MandatoryExtension) {
          var msg = "MandatoryExtension cannot be used.";

          throw new ArgumentException (msg, "code");
        }
      }

      if (reason.IsNullOrEmpty ()) {
        closeAsync ((ushort) code, String.Empty);

        return;
      }

      if (code == CloseStatusCode.NoStatus) {
        var msg = "NoStatus cannot be used.";

        throw new ArgumentException (msg, "code");
      }

      byte[] bytes;

      if (!reason.TryGetUTF8EncodedBytes (out bytes)) {
        var msg = "It could not be UTF-8-encoded.";

        throw new ArgumentException (msg, "reason");
      }

      if (bytes.Length > 123) {
        var msg = "Its size is greater than 123 bytes.";

        throw new ArgumentOutOfRangeException ("reason", msg);
      }

      closeAsync ((ushort) code, reason);
    }

    /// <summary>
    /// Establishes a connection.
    /// </summary>
    /// <remarks>
    /// This method does nothing when the current state of the interface is
    /// Connecting or Open.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    ///   <para>
    ///   This method is not available if the interface is not for the client.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   This method is not available if reconnection attempts have failed.
    ///   </para>
    /// </exception>
    public void Connect ()
    {
      if (!_client) {
        var msg = "The interface is not for the client.";

        throw new InvalidOperationException (msg);
      }

      if (_retryCountForConnect >= _maxRetryCountForConnect) {
        var msg = "Reconnection attempts have failed.";

        throw new InvalidOperationException (msg);
      }

      var connected = connect ();

      if (!connected)
        return;

      open ();
    }

    /// <summary>
    /// Establishes a connection asynchronously.
    /// </summary>
    /// <remarks>
    ///   <para>
    ///   This method does not wait for the connect process to be complete.
    ///   </para>
    ///   <para>
    ///   This method does nothing when the current state of the interface is
    ///   Connecting or Open.
    ///   </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    ///   <para>
    ///   This method is not available if the interface is not for the client.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   This method is not available if reconnection attempts have failed.
    ///   </para>
    /// </exception>
    public void ConnectAsync ()
    {
      if (!_client) {
        var msg = "The interface is not for the client.";

        throw new InvalidOperationException (msg);
      }

      if (_retryCountForConnect >= _maxRetryCountForConnect) {
        var msg = "Reconnection attempts have failed.";

        throw new InvalidOperationException (msg);
      }

      AsyncHelper.QueueBlocking (
        () => {
          var connected = connect ();

          if (!connected)
            return;

          open ();
        }
      );
    }

    /// <summary>
    /// Sends a ping to the remote endpoint.
    /// </summary>
    /// <returns>
    /// <c>true</c> if the send has successfully done and a pong has been
    /// received within a time; otherwise, <c>false</c>.
    /// </returns>
    public bool Ping ()
    {
      return ping (_emptyBytes);
    }

    /// <summary>
    /// Sends a ping with the specified message to the remote endpoint.
    /// </summary>
    /// <returns>
    /// <c>true</c> if the send has successfully done and a pong has been
    /// received within a time; otherwise, <c>false</c>.
    /// </returns>
    /// <param name="message">
    ///   <para>
    ///   A <see cref="string"/> that specifies the message to send.
    ///   </para>
    ///   <para>
    ///   Its size must be 125 bytes or less in UTF-8.
    ///   </para>
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="message"/> could not be UTF-8-encoded.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The size of <paramref name="message"/> is greater than 125 bytes.
    /// </exception>
    public bool Ping (string message)
    {
      if (message.IsNullOrEmpty ())
        return ping (_emptyBytes);

      byte[] bytes;

      if (!message.TryGetUTF8EncodedBytes (out bytes)) {
        var msg = "It could not be UTF-8-encoded.";

        throw new ArgumentException (msg, "message");
      }

      if (bytes.Length > 125) {
        var msg = "Its size is greater than 125 bytes.";

        throw new ArgumentOutOfRangeException ("message", msg);
      }

      return ping (bytes);
    }

    /// <summary>
    /// Sends the specified data to the remote endpoint.
    /// </summary>
    /// <param name="data">
    /// An array of <see cref="byte"/> that specifies the binary data to send.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="data"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// This method is not available when the current state of the interface
    /// is not Open.
    /// </exception>
    public void Send (byte[] data)
    {
      if (_readyState != WebSocketState.Open) {
        var msg = "The current state of the interface is not Open.";

        throw new InvalidOperationException (msg);
      }

      if (data == null)
        throw new ArgumentNullException ("data");

      send (Opcode.Binary, new MemoryStream (data));
    }

    /// <summary>
    /// Sends the specified file to the remote endpoint.
    /// </summary>
    /// <param name="fileInfo">
    ///   <para>
    ///   A <see cref="FileInfo"/> that specifies the file to send.
    ///   </para>
    ///   <para>
    ///   The file is sent as the binary data.
    ///   </para>
    /// </param>
    /// <exception cref="ArgumentException">
    ///   <para>
    ///   The file does not exist.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   The file could not be opened.
    ///   </para>
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="fileInfo"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// This method is not available when the current state of the interface
    /// is not Open.
    /// </exception>
    public void Send (FileInfo fileInfo)
    {
      if (_readyState != WebSocketState.Open) {
        var msg = "The current state of the interface is not Open.";

        throw new InvalidOperationException (msg);
      }

      if (fileInfo == null)
        throw new ArgumentNullException ("fileInfo");

      if (!fileInfo.Exists) {
        var msg = "The file does not exist.";

        throw new ArgumentException (msg, "fileInfo");
      }

      FileStream stream;

      if (!fileInfo.TryOpenRead (out stream)) {
        var msg = "The file could not be opened.";

        throw new ArgumentException (msg, "fileInfo");
      }

      send (Opcode.Binary, stream);
    }

    /// <summary>
    /// Sends the specified data to the remote endpoint.
    /// </summary>
    /// <param name="data">
    /// A <see cref="string"/> that specifies the text data to send.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="data"/> could not be UTF-8-encoded.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="data"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// This method is not available when the current state of the interface
    /// is not Open.
    /// </exception>
    public void Send (string data)
    {
      if (_readyState != WebSocketState.Open) {
        var msg = "The current state of the interface is not Open.";

        throw new InvalidOperationException (msg);
      }

      if (data == null)
        throw new ArgumentNullException ("data");

      byte[] bytes;

      if (!data.TryGetUTF8EncodedBytes (out bytes)) {
        var msg = "It could not be UTF-8-encoded.";

        throw new ArgumentException (msg, "data");
      }

      send (Opcode.Text, new MemoryStream (bytes));
    }

    /// <summary>
    /// Sends the data from the specified stream instance to the remote endpoint.
    /// </summary>
    /// <param name="stream">
    ///   <para>
    ///   A <see cref="Stream"/> instance from which to read the data to send.
    ///   </para>
    ///   <para>
    ///   The data is sent as the binary data.
    ///   </para>
    /// </param>
    /// <param name="length">
    /// An <see cref="int"/> that specifies the number of bytes to send.
    /// </param>
    /// <exception cref="ArgumentException">
    ///   <para>
    ///   <paramref name="stream"/> cannot be read.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   <paramref name="length"/> is less than 1.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   No data could be read from <paramref name="stream"/>.
    ///   </para>
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="stream"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// This method is not available when the current state of the interface
    /// is not Open.
    /// </exception>
    public void Send (Stream stream, int length)
    {
      if (_readyState != WebSocketState.Open) {
        var msg = "The current state of the interface is not Open.";

        throw new InvalidOperationException (msg);
      }

      if (stream == null)
        throw new ArgumentNullException ("stream");

      if (!stream.CanRead) {
        var msg = "It cannot be read.";

        throw new ArgumentException (msg, "stream");
      }

      if (length < 1) {
        var msg = "Less than 1.";

        throw new ArgumentException (msg, "length");
      }

      var bytes = stream.ReadBytes (length);
      var len = bytes.Length;

      if (len == 0) {
        var msg = "No data could be read from it.";

        throw new ArgumentException (msg, "stream");
      }

      if (len < length) {
        var fmt = "Only {0} byte(s) of data could be read from the stream.";
        var msg = String.Format (fmt, len);

        _log.Warn (msg);
      }

      send (Opcode.Binary, new MemoryStream (bytes));
    }

    /// <summary>
    /// Sends the specified data to the remote endpoint asynchronously.
    /// </summary>
    /// <remarks>
    /// This method does not wait for the send to be complete.
    /// </remarks>
    /// <param name="data">
    /// An array of <see cref="byte"/> that specifies the binary data to send.
    /// </param>
    /// <param name="completed">
    ///   <para>
    ///   An <see cref="T:System.Action{bool}"/> delegate.
    ///   </para>
    ///   <para>
    ///   It specifies the delegate called when the send is complete.
    ///   </para>
    ///   <para>
    ///   The <see cref="bool"/> parameter passed to the delegate is <c>true</c>
    ///   if the send has successfully done; otherwise, <c>false</c>.
    ///   </para>
    ///   <para>
    ///   <see langword="null"/> if not necessary.
    ///   </para>
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="data"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// This method is not available when the current state of the interface
    /// is not Open.
    /// </exception>
    public void SendAsync (byte[] data, Action<bool> completed)
    {
      if (_readyState != WebSocketState.Open) {
        var msg = "The current state of the interface is not Open.";

        throw new InvalidOperationException (msg);
      }

      if (data == null)
        throw new ArgumentNullException ("data");

      sendAsync (Opcode.Binary, new MemoryStream (data), completed);
    }

    /// <summary>
    /// Sends the specified file to the remote endpoint asynchronously.
    /// </summary>
    /// <remarks>
    /// This method does not wait for the send to be complete.
    /// </remarks>
    /// <param name="fileInfo">
    ///   <para>
    ///   A <see cref="FileInfo"/> that specifies the file to send.
    ///   </para>
    ///   <para>
    ///   The file is sent as the binary data.
    ///   </para>
    /// </param>
    /// <param name="completed">
    ///   <para>
    ///   An <see cref="T:System.Action{bool}"/> delegate.
    ///   </para>
    ///   <para>
    ///   It specifies the delegate called when the send is complete.
    ///   </para>
    ///   <para>
    ///   The <see cref="bool"/> parameter passed to the delegate is <c>true</c>
    ///   if the send has successfully done; otherwise, <c>false</c>.
    ///   </para>
    ///   <para>
    ///   <see langword="null"/> if not necessary.
    ///   </para>
    /// </param>
    /// <exception cref="ArgumentException">
    ///   <para>
    ///   The file does not exist.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   The file could not be opened.
    ///   </para>
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="fileInfo"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// This method is not available when the current state of the interface
    /// is not Open.
    /// </exception>
    public void SendAsync (FileInfo fileInfo, Action<bool> completed)
    {
      if (_readyState != WebSocketState.Open) {
        var msg = "The current state of the interface is not Open.";

        throw new InvalidOperationException (msg);
      }

      if (fileInfo == null)
        throw new ArgumentNullException ("fileInfo");

      if (!fileInfo.Exists) {
        var msg = "The file does not exist.";

        throw new ArgumentException (msg, "fileInfo");
      }

      FileStream stream;

      if (!fileInfo.TryOpenRead (out stream)) {
        var msg = "The file could not be opened.";

        throw new ArgumentException (msg, "fileInfo");
      }

      sendAsync (Opcode.Binary, stream, completed);
    }

    /// <summary>
    /// Sends the specified data to the remote endpoint asynchronously.
    /// </summary>
    /// <remarks>
    /// This method does not wait for the send to be complete.
    /// </remarks>
    /// <param name="data">
    /// A <see cref="string"/> that specifies the text data to send.
    /// </param>
    /// <param name="completed">
    ///   <para>
    ///   An <see cref="T:System.Action{bool}"/> delegate.
    ///   </para>
    ///   <para>
    ///   It specifies the delegate called when the send is complete.
    ///   </para>
    ///   <para>
    ///   The <see cref="bool"/> parameter passed to the delegate is <c>true</c>
    ///   if the send has successfully done; otherwise, <c>false</c>.
    ///   </para>
    ///   <para>
    ///   <see langword="null"/> if not necessary.
    ///   </para>
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="data"/> could not be UTF-8-encoded.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="data"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// This method is not available when the current state of the interface
    /// is not Open.
    /// </exception>
    public void SendAsync (string data, Action<bool> completed)
    {
      if (_readyState != WebSocketState.Open) {
        var msg = "The current state of the interface is not Open.";

        throw new InvalidOperationException (msg);
      }

      if (data == null)
        throw new ArgumentNullException ("data");

      byte[] bytes;

      if (!data.TryGetUTF8EncodedBytes (out bytes)) {
        var msg = "It could not be UTF-8-encoded.";

        throw new ArgumentException (msg, "data");
      }

      sendAsync (Opcode.Text, new MemoryStream (bytes), completed);
    }

    /// <summary>
    /// Sends the data from the specified stream instance to the remote
    /// endpoint asynchronously.
    /// </summary>
    /// <remarks>
    /// This method does not wait for the send to be complete.
    /// </remarks>
    /// <param name="stream">
    ///   <para>
    ///   A <see cref="Stream"/> instance from which to read the data to send.
    ///   </para>
    ///   <para>
    ///   The data is sent as the binary data.
    ///   </para>
    /// </param>
    /// <param name="length">
    /// An <see cref="int"/> that specifies the number of bytes to send.
    /// </param>
    /// <param name="completed">
    ///   <para>
    ///   An <see cref="T:System.Action{bool}"/> delegate.
    ///   </para>
    ///   <para>
    ///   It specifies the delegate called when the send is complete.
    ///   </para>
    ///   <para>
    ///   The <see cref="bool"/> parameter passed to the delegate is <c>true</c>
    ///   if the send has successfully done; otherwise, <c>false</c>.
    ///   </para>
    ///   <para>
    ///   <see langword="null"/> if not necessary.
    ///   </para>
    /// </param>
    /// <exception cref="ArgumentException">
    ///   <para>
    ///   <paramref name="stream"/> cannot be read.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   <paramref name="length"/> is less than 1.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   No data could be read from <paramref name="stream"/>.
    ///   </para>
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="stream"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// This method is not available when the current state of the interface
    /// is not Open.
    /// </exception>
    public void SendAsync (Stream stream, int length, Action<bool> completed)
    {
      if (_readyState != WebSocketState.Open) {
        var msg = "The current state of the interface is not Open.";

        throw new InvalidOperationException (msg);
      }

      if (stream == null)
        throw new ArgumentNullException ("stream");

      if (!stream.CanRead) {
        var msg = "It cannot be read.";

        throw new ArgumentException (msg, "stream");
      }

      if (length < 1) {
        var msg = "Less than 1.";

        throw new ArgumentException (msg, "length");
      }

      var bytes = stream.ReadBytes (length);
      var len = bytes.Length;

      if (len == 0) {
        var msg = "No data could be read from it.";

        throw new ArgumentException (msg, "stream");
      }

      if (len < length) {
        var fmt = "Only {0} byte(s) of data could be read from the stream.";
        var msg = String.Format (fmt, len);

        _log.Warn (msg);
      }

      sendAsync (Opcode.Binary, new MemoryStream (bytes), completed);
    }

    /// <summary>
    /// Sets an HTTP cookie to send with the handshake request or response.
    /// </summary>
    /// <param name="cookie">
    /// A <see cref="Cookie"/> that specifies the cookie to send.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="cookie"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// This method is not available when the current state of the interface
    /// is neither New nor Closed.
    /// </exception>
    public void SetCookie (Cookie cookie)
    {
      if (cookie == null)
        throw new ArgumentNullException ("cookie");

      lock (_forState) {
        if (!canSet ()) {
          var msg = "The current state of the interface is neither New nor Closed.";

          throw new InvalidOperationException (msg);
        }

        if (_cookiesOrigin != null && !isSameOrigin (_cookiesOrigin, _uri))
          _cookies = null;

        _cookiesOrigin = _uri;
        Cookies.SetOrRemove (cookie);
      }
    }

    /// <summary>
    /// Sets the credentials for the HTTP authentication (Basic/Digest).
    /// </summary>
    /// <param name="username">
    ///   <para>
    ///   A <see cref="string"/> that specifies the username associated
    ///   with the credentials.
    ///   </para>
    ///   <para>
    ///   <see langword="null"/> or an empty string if the credentials need
    ///   to be initialized.
    ///   </para>
    /// </param>
    /// <param name="password">
    ///   <para>
    ///   A <see cref="string"/> that specifies the password for the
    ///   username associated with the credentials.
    ///   </para>
    ///   <para>
    ///   <see langword="null"/> or an empty string if not necessary.
    ///   </para>
    /// </param>
    /// <param name="preAuth">
    /// A <see cref="bool"/>: <c>true</c> if the interface sends the
    /// credentials for the Basic authentication in advance with
    /// the first handshake request; otherwise, <c>false</c>.
    /// </param>
    /// <exception cref="ArgumentException">
    ///   <para>
    ///   <paramref name="username"/> contains an invalid character.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   <paramref name="password"/> contains an invalid character.
    ///   </para>
    /// </exception>
    /// <exception cref="InvalidOperationException">
    ///   <para>
    ///   This method is not available if the interface is not for the client.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   This method is not available when the current state of the interface
    ///   is neither New nor Closed.
    ///   </para>
    /// </exception>
    public void SetCredentials (string username, string password, bool preAuth)
    {
      if (!_client) {
        var msg = "The interface is not for the client.";

        throw new InvalidOperationException (msg);
      }

      if (!username.IsNullOrEmpty ()) {
        if (username.Contains (':') || !username.IsText ()) {
          var msg = "It contains an invalid character.";

          throw new ArgumentException (msg, "username");
        }
      }

      if (!password.IsNullOrEmpty ()) {
        if (!password.IsText ()) {
          var msg = "It contains an invalid character.";

          throw new ArgumentException (msg, "password");
        }
      }

      lock (_forState) {
        if (!canSet ()) {
          var msg = "The current state of the interface is neither New nor Closed.";

          throw new InvalidOperationException (msg);
        }

        if (username.IsNullOrEmpty ()) {
          _credentials = null;
          _credentialsOrigin = null;
          _preAuth = false;

          return;
        }

        _credentials = new NetworkCredential (
                         username,
                         password,
                         _uri.PathAndQuery
                       );

        _credentialsOrigin = _uri;
        _authChallenge = null;
        _nonceCount = 0;
        _preAuth = preAuth;
      }
    }

    /// <summary>
    /// Sets the URL of the HTTP proxy server through which to connect and
    /// the credentials for the HTTP proxy authentication (Basic/Digest).
    /// </summary>
    /// <param name="url">
    ///   <para>
    ///   A <see cref="string"/> that specifies the URL of the proxy
    ///   server through which to connect.
    ///   </para>
    ///   <para>
    ///   The syntax is http://&lt;host&gt;[:&lt;port&gt;].
    ///   </para>
    ///   <para>
    ///   <see langword="null"/> or an empty string if the URL and
    ///   the credentials need to be initialized.
    ///   </para>
    /// </param>
    /// <param name="username">
    ///   <para>
    ///   A <see cref="string"/> that specifies the username associated
    ///   with the credentials.
    ///   </para>
    ///   <para>
    ///   <see langword="null"/> or an empty string if the credentials
    ///   are not necessary.
    ///   </para>
    /// </param>
    /// <param name="password">
    ///   <para>
    ///   A <see cref="string"/> that specifies the password for the
    ///   username associated with the credentials.
    ///   </para>
    ///   <para>
    ///   <see langword="null"/> or an empty string if not necessary.
    ///   </para>
    /// </param>
    /// <exception cref="ArgumentException">
    ///   <para>
    ///   <paramref name="url"/> is not an absolute URI string.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   The scheme of <paramref name="url"/> is not http.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   <paramref name="url"/> includes the path segments.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   <paramref name="username"/> contains an invalid character.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   <paramref name="password"/> contains an invalid character.
    ///   </para>
    /// </exception>
    /// <exception cref="InvalidOperationException">
    ///   <para>
    ///   This method is not available if the interface is not for the client.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   This method is not available when the current state of the interface
    ///   is neither New nor Closed.
    ///   </para>
    /// </exception>
    public void SetProxy (string url, string username, string password)
    {
      if (!_client) {
        var msg = "The interface is not for the client.";

        throw new InvalidOperationException (msg);
      }

      Uri uri = null;

      if (!url.IsNullOrEmpty ()) {
        if (!Uri.TryCreate (url, UriKind.Absolute, out uri)) {
          var msg = "Not an absolute URI string.";

          throw new ArgumentException (msg, "url");
        }

        if (uri.Scheme != "http") {
          var msg = "The scheme part is not http.";

          throw new ArgumentException (msg, "url");
        }

        if (uri.Segments.Length > 1) {
          var msg = "It includes the path segments.";

          throw new ArgumentException (msg, "url");
        }
      }

      if (!username.IsNullOrEmpty ()) {
        if (username.Contains (':') || !username.IsText ()) {
          var msg = "It contains an invalid character.";

          throw new ArgumentException (msg, "username");
        }
      }

      if (!password.IsNullOrEmpty ()) {
        if (!password.IsText ()) {
          var msg = "It contains an invalid character.";

          throw new ArgumentException (msg, "password");
        }
      }

      lock (_forState) {
        if (!canSet ()) {
          var msg = "The current state of the interface is neither New nor Closed.";

          throw new InvalidOperationException (msg);
        }

        if (url.IsNullOrEmpty ()) {
          _proxyUri = null;
          _proxyCredentials = null;

          return;
        }

        _proxyUri = uri;

        if (username.IsNullOrEmpty ()) {
          _proxyCredentials = null;

          return;
        }

        var domain = String.Format ("{0}:{1}", _uri.DnsSafeHost, _uri.Port);

        _proxyCredentials = new NetworkCredential (username, password, domain);
      }
    }

    /// <summary>
    /// Sets a user header to send with the handshake request or response.
    /// </summary>
    /// <param name="name">
    /// A <see cref="string"/> that specifies the name of the header to set.
    /// </param>
    /// <param name="value">
    /// A <see cref="string"/> that specifies the value of the header to set.
    /// </param>
    /// <exception cref="ArgumentException">
    ///   <para>
    ///   <paramref name="name"/> is an empty string.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   <paramref name="name"/> is a string of spaces.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   <paramref name="name"/> contains an invalid character.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   <paramref name="value"/> contains an invalid character.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   <paramref name="name"/> is a restricted header name.
    ///   </para>
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="name"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The length of <paramref name="value"/> is greater than 65,535
    /// characters.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    ///   <para>
    ///   This method is not available when the current state of the interface
    ///   is neither New nor Closed.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   This method is not available if the interface does not allow
    ///   that header type.
    ///   </para>
    /// </exception>
    public void SetUserHeader (string name, string value)
    {
      lock (_forState) {
        if (!canSet ()) {
          var msg = "The current state of the interface is neither New nor Closed.";

          throw new InvalidOperationException (msg);
        }

        if (_userHeadersOrigin != null
            && !isSameOrigin (_userHeadersOrigin, _uri))
          _userHeaders = null;

        _userHeadersOrigin = _uri;
        UserHeaders.Set (name, value);
      }
    }

    #endregion

    #region Explicit Interface Implementations

    /// <summary>
    /// Closes the connection and releases all associated resources.
    /// </summary>
    /// <remarks>
    ///   <para>
    ///   This method closes the connection with close status 1001 (going away).
    ///   </para>
    ///   <para>
    ///   This method does nothing if the current state of the interface is
    ///   Closing or Closed.
    ///   </para>
    /// </remarks>
    void IDisposable.Dispose ()
    {
      close (1001, String.Empty);
    }

    #endregion
  }
}
