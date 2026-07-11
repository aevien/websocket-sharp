#region License
/*
 * WebSocketServer.cs
 *
 * The MIT License
 *
 * Copyright (c) 2012-2024 sta.blockhead
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
 * - Juan Manuel Lallana <juan.manuel.lallana@gmail.com>
 * - Jonas Hovgaard <j@jhovgaard.dk>
 * - Liryna <liryna.stark@gmail.com>
 * - Rohan Singh <rohan-singh@hotmail.com>
 */
#endregion

using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text;
using System.Threading;
using WebSocketSharp.Net;
using WebSocketSharp.Net.WebSockets;

namespace WebSocketSharp.Server
{
  /// <summary>
  /// Provides a WebSocket protocol server.
  /// </summary>
  /// <remarks>
  /// This class can provide multiple WebSocket services.
  /// </remarks>
  public class WebSocketServer
  {
    #region Private Fields

    private System.Net.IPAddress               _address;
    private AuthenticationSchemes              _authSchemes;
    private static readonly string             _defaultRealm;
    private BoundedOperationDispatcher         _handshakeDispatcher;
    private TimeSpan                           _handshakeTimeout;
    private string                             _hostname;
    private bool                               _isDnsStyle;
    private bool                               _isSecure;
    private TcpListener                        _listener;
    private Logger                             _log;
    private int                                _maxConcurrentHandshakes;
    private int                                _maxPendingHandshakes;
    private int                                _port;
    private int                                _rejectedHandshakeCount;
    private string                             _realm;
    private string                             _realmInUse;
    private Thread                             _receiveThread;
    private bool                               _reuseAddress;
    private WebSocketServiceManager            _services;
    private ServerSslConfiguration             _sslConfig;
    private ServerSslConfiguration             _sslConfigInUse;
    private volatile ServerState               _state;
    private bool                               _shutdownInProgress;
    private object                             _sync;
    private Func<IIdentity, NetworkCredential> _userCredFinder;

    #endregion

    #region Static Constructor

    static WebSocketServer ()
    {
      _defaultRealm = "SECRET AREA";
    }

    #endregion

    #region Public Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="WebSocketServer"/> class.
    /// </summary>
    /// <remarks>
    /// The new instance listens for incoming handshake requests on
    /// <see cref="System.Net.IPAddress.Any"/> and port 80.
    /// </remarks>
    public WebSocketServer ()
    {
      var addr = System.Net.IPAddress.Any;

      init (addr.ToString (), addr, 80, false);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WebSocketServer"/> class
    /// with the specified port.
    /// </summary>
    /// <remarks>
    ///   <para>
    ///   The new instance listens for incoming handshake requests on
    ///   <see cref="System.Net.IPAddress.Any"/> and <paramref name="port"/>.
    ///   </para>
    ///   <para>
    ///   It provides secure connections if <paramref name="port"/> is 443.
    ///   </para>
    /// </remarks>
    /// <param name="port">
    /// An <see cref="int"/> that specifies the number of the port on which
    /// to listen.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="port"/> is less than 1 or greater than 65535.
    /// </exception>
    public WebSocketServer (int port)
      : this (port, port == 443)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WebSocketServer"/> class
    /// with the specified URL.
    /// </summary>
    /// <remarks>
    ///   <para>
    ///   The new instance listens for incoming handshake requests on
    ///   the IP address and port of <paramref name="url"/>.
    ///   </para>
    ///   <para>
    ///   Either port 80 or 443 is used if <paramref name="url"/> includes
    ///   no port. Port 443 is used if the scheme of <paramref name="url"/>
    ///   is wss; otherwise, port 80 is used.
    ///   </para>
    ///   <para>
    ///   The new instance provides secure connections if the scheme of
    ///   <paramref name="url"/> is wss.
    ///   </para>
    /// </remarks>
    /// <param name="url">
    /// A <see cref="string"/> that specifies the WebSocket URL of the server.
    /// </param>
    /// <exception cref="ArgumentException">
    ///   <para>
    ///   <paramref name="url"/> is an empty string.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   <paramref name="url"/> is invalid.
    ///   </para>
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="url"/> is <see langword="null"/>.
    /// </exception>
    public WebSocketServer (string url)
    {
      if (url == null)
        throw new ArgumentNullException ("url");

      if (url.Length == 0)
        throw new ArgumentException ("An empty string.", "url");

      Uri uri;
      string msg;

      if (!tryCreateUri (url, out uri, out msg))
        throw new ArgumentException (msg, "url");

      var host = uri.DnsSafeHost;
      var addr = host.ToIPAddress ();

      if (addr == null) {
        msg = "The host part could not be converted to an IP address.";

        throw new ArgumentException (msg, "url");
      }

      if (!addr.IsLocal ()) {
        msg = "The IP address of the host is not a local IP address.";

        throw new ArgumentException (msg, "url");
      }

      init (host, addr, uri.Port, uri.Scheme == "wss");
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WebSocketServer"/> class
    /// with the specified port and boolean if secure or not.
    /// </summary>
    /// <remarks>
    /// The new instance listens for incoming handshake requests on
    /// <see cref="System.Net.IPAddress.Any"/> and <paramref name="port"/>.
    /// </remarks>
    /// <param name="port">
    /// An <see cref="int"/> that specifies the number of the port on which
    /// to listen.
    /// </param>
    /// <param name="secure">
    /// A <see cref="bool"/>: <c>true</c> if the new instance provides
    /// secure connections; otherwise, <c>false</c>.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="port"/> is less than 1 or greater than 65535.
    /// </exception>
    public WebSocketServer (int port, bool secure)
    {
      if (!port.IsPortNumber ()) {
        var msg = "Less than 1 or greater than 65535.";

        throw new ArgumentOutOfRangeException ("port", msg);
      }

      var addr = System.Net.IPAddress.Any;

      init (addr.ToString (), addr, port, secure);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WebSocketServer"/> class
    /// with the specified IP address and port.
    /// </summary>
    /// <remarks>
    ///   <para>
    ///   The new instance listens for incoming handshake requests on
    ///   <paramref name="address"/> and <paramref name="port"/>.
    ///   </para>
    ///   <para>
    ///   It provides secure connections if <paramref name="port"/> is 443.
    ///   </para>
    /// </remarks>
    /// <param name="address">
    /// A <see cref="System.Net.IPAddress"/> that specifies the local IP
    /// address on which to listen.
    /// </param>
    /// <param name="port">
    /// An <see cref="int"/> that specifies the number of the port on which
    /// to listen.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="address"/> is not a local IP address.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="address"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="port"/> is less than 1 or greater than 65535.
    /// </exception>
    public WebSocketServer (System.Net.IPAddress address, int port)
      : this (address, port, port == 443)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WebSocketServer"/> class
    /// with the specified IP address, port, and boolean if secure or not.
    /// </summary>
    /// <remarks>
    /// The new instance listens for incoming handshake requests on
    /// <paramref name="address"/> and <paramref name="port"/>.
    /// </remarks>
    /// <param name="address">
    /// A <see cref="System.Net.IPAddress"/> that specifies the local IP
    /// address on which to listen.
    /// </param>
    /// <param name="port">
    /// An <see cref="int"/> that specifies the number of the port on which
    /// to listen.
    /// </param>
    /// <param name="secure">
    /// A <see cref="bool"/>: <c>true</c> if the new instance provides
    /// secure connections; otherwise, <c>false</c>.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="address"/> is not a local IP address.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="address"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="port"/> is less than 1 or greater than 65535.
    /// </exception>
    public WebSocketServer (System.Net.IPAddress address, int port, bool secure)
    {
      if (address == null)
        throw new ArgumentNullException ("address");

      if (!address.IsLocal ()) {
        var msg = "Not a local IP address.";

        throw new ArgumentException (msg, "address");
      }

      if (!port.IsPortNumber ()) {
        var msg = "Less than 1 or greater than 65535.";

        throw new ArgumentOutOfRangeException ("port", msg);
      }

      init (address.ToString (), address, port, secure);
    }

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets the IP address of the server.
    /// </summary>
    /// <value>
    /// A <see cref="System.Net.IPAddress"/> that represents the local IP
    /// address on which to listen for incoming handshake requests.
    /// </value>
    public System.Net.IPAddress Address {
      get {
        return _address;
      }
    }

    /// <summary>
    /// Gets or sets the scheme used to authenticate the clients.
    /// </summary>
    /// <remarks>
    /// The set operation works if the current state of the server is
    /// Ready or Stop.
    /// </remarks>
    /// <value>
    ///   <para>
    ///   One of the <see cref="WebSocketSharp.Net.AuthenticationSchemes"/>
    ///   enum values.
    ///   </para>
    ///   <para>
    ///   It represents the scheme used to authenticate the clients.
    ///   </para>
    ///   <para>
    ///   The default value is
    ///   <see cref="WebSocketSharp.Net.AuthenticationSchemes.Anonymous"/>.
    ///   </para>
    /// </value>
    public AuthenticationSchemes AuthenticationSchemes {
      get {
        return _authSchemes;
      }

      set {
        lock (_sync) {
          if (!canSet ())
            return;

          _authSchemes = value;
        }
      }
    }

    /// <summary>
    /// Gets a value indicating whether the server has started.
    /// </summary>
    /// <value>
    /// <c>true</c> if the server has started; otherwise, <c>false</c>.
    /// </value>
    public bool IsListening {
      get {
        return _state == ServerState.Start;
      }
    }

    /// <summary>
    /// Gets a value indicating whether the server provides secure connections.
    /// </summary>
    /// <value>
    /// <c>true</c> if the server provides secure connections; otherwise,
    /// <c>false</c>.
    /// </value>
    public bool IsSecure {
      get {
        return _isSecure;
      }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the server cleans up
    /// the inactive sessions periodically.
    /// </summary>
    /// <remarks>
    /// The set operation works if the current state of the server is
    /// Ready or Stop.
    /// </remarks>
    /// <value>
    ///   <para>
    ///   <c>true</c> if the server cleans up the inactive sessions
    ///   every 60 seconds; otherwise, <c>false</c>.
    ///   </para>
    ///   <para>
    ///   The default value is <c>false</c>.
    ///   </para>
    /// </value>
    public bool KeepClean {
      get {
        return _services.KeepClean;
      }

      set {
        _services.KeepClean = value;
      }
    }

    /// <summary>
    /// Gets the logging function for the server.
    /// </summary>
    /// <remarks>
    /// The default logging level is <see cref="LogLevel.Error"/>.
    /// </remarks>
    /// <value>
    /// A <see cref="Logger"/> that provides the logging function.
    /// </value>
    public Logger Log {
      get {
        return _log;
      }
    }

    /// <summary>
    /// Gets the port of the server.
    /// </summary>
    /// <value>
    /// An <see cref="int"/> that represents the number of the port on which
    /// to listen for incoming handshake requests.
    /// </value>
    public int Port {
      get {
        return _port;
      }
    }

    /// <summary>
    /// Gets or sets the name of the realm associated with the server.
    /// </summary>
    /// <remarks>
    /// The set operation works if the current state of the server is
    /// Ready or Stop.
    /// </remarks>
    /// <value>
    ///   <para>
    ///   A <see cref="string"/> that represents the name of the realm.
    ///   </para>
    ///   <para>
    ///   "SECRET AREA" is used as the name of the realm if the value is
    ///   <see langword="null"/> or an empty string.
    ///   </para>
    ///   <para>
    ///   The default value is <see langword="null"/>.
    ///   </para>
    /// </value>
    public string Realm {
      get {
        return _realm;
      }

      set {
        lock (_sync) {
          if (!canSet ())
            return;

          _realm = value;
        }
      }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the server is allowed to
    /// be bound to an address that is already in use.
    /// </summary>
    /// <remarks>
    ///   <para>
    ///   You should set this property to <c>true</c> if you would like to
    ///   resolve to wait for socket in TIME_WAIT state.
    ///   </para>
    ///   <para>
    ///   The set operation works if the current state of the server is
    ///   Ready or Stop.
    ///   </para>
    /// </remarks>
    /// <value>
    ///   <para>
    ///   <c>true</c> if the server is allowed to be bound to an address
    ///   that is already in use; otherwise, <c>false</c>.
    ///   </para>
    ///   <para>
    ///   The default value is <c>false</c>.
    ///   </para>
    /// </value>
    public bool ReuseAddress {
      get {
        return _reuseAddress;
      }

      set {
        lock (_sync) {
          if (!canSet ())
            return;

          _reuseAddress = value;
        }
      }
    }

    /// <summary>
    /// Gets the configuration for secure connection.
    /// </summary>
    /// <remarks>
    /// The configuration is used when the server attempts to start,
    /// so it must be configured before the start method is called.
    /// </remarks>
    /// <value>
    /// A <see cref="ServerSslConfiguration"/> that represents the
    /// configuration used to provide secure connections.
    /// </value>
    /// <exception cref="InvalidOperationException">
    /// The server does not provide secure connections.
    /// </exception>
    public ServerSslConfiguration SslConfiguration {
      get {
        if (!_isSecure) {
          var msg = "The server does not provide secure connections.";

          throw new InvalidOperationException (msg);
        }

        return getSslConfiguration ();
      }
    }

    /// <summary>
    /// Gets or sets the delegate called to find the credentials for
    /// an identity used to authenticate a client.
    /// </summary>
    /// <remarks>
    /// The set operation works if the current state of the server is
    /// Ready or Stop.
    /// </remarks>
    /// <value>
    ///   <para>
    ///   A <see cref="T:System.Func{IIdentity, NetworkCredential}"/>
    ///   delegate.
    ///   </para>
    ///   <para>
    ///   It represents the delegate called when the server finds
    ///   the credentials used to authenticate a client.
    ///   </para>
    ///   <para>
    ///   It must return <see langword="null"/> if the credentials
    ///   are not found.
    ///   </para>
    ///   <para>
    ///   <see langword="null"/> if not necessary.
    ///   </para>
    ///   <para>
    ///   The default value is <see langword="null"/>.
    ///   </para>
    /// </value>
    public Func<IIdentity, NetworkCredential> UserCredentialsFinder {
      get {
        return _userCredFinder;
      }

      set {
        lock (_sync) {
          if (!canSet ())
            return;

          _userCredFinder = value;
        }
      }
    }

    /// <summary>
    /// Gets or sets the time to wait for a client WebSocket handshake request.
    /// </summary>
    /// <remarks>
    /// The set operation works if the current state of the server is
    /// Ready or Stop.
    /// </remarks>
    /// <value>
    /// A <see cref="TimeSpan"/> that represents the time to wait.
    /// The default value is the same as 10 seconds.
    /// </value>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The value specified for a set operation is zero or less.
    /// </exception>
    public TimeSpan HandshakeTimeout {
      get {
        return _handshakeTimeout;
      }

      set {
        if (value <= TimeSpan.Zero) {
          var msg = "Zero or less.";

          throw new ArgumentOutOfRangeException ("value", msg);
        }

        lock (_sync) {
          if (!canSet ())
            return;

          _handshakeTimeout = value;
        }
      }
    }

    /// <summary>
    /// Gets or sets the maximum number of handshake operations processed at once.
    /// </summary>
    /// <remarks>
    /// The set operation works if the current state of the server is Ready or Stop.
    /// The default value is 128.
    /// </remarks>
    public int MaxConcurrentHandshakes {
      get {
        return _maxConcurrentHandshakes;
      }

      set {
        if (value < 1) {
          var msg = "Less than 1.";

          throw new ArgumentOutOfRangeException ("value", msg);
        }

        lock (_sync) {
          if (!canSet ())
            return;

          _maxConcurrentHandshakes = value;
        }
      }
    }

    /// <summary>
    /// Gets or sets the maximum number of handshake operations waiting to run.
    /// </summary>
    /// <remarks>
    /// The set operation works if the current state of the server is Ready or Stop.
    /// The default value is 4096.
    /// </remarks>
    public int MaxPendingHandshakes {
      get {
        return _maxPendingHandshakes;
      }

      set {
        if (value < 1) {
          var msg = "Less than 1.";

          throw new ArgumentOutOfRangeException ("value", msg);
        }

        lock (_sync) {
          if (!canSet ())
            return;

          _maxPendingHandshakes = value;
        }
      }
    }

    /// <summary>
    /// Gets or sets the time to wait for the response to the WebSocket
    /// Ping or Close.
    /// </summary>
    /// <remarks>
    /// The set operation works if the current state of the server is
    /// Ready or Stop.
    /// </remarks>
    /// <value>
    ///   <para>
    ///   A <see cref="TimeSpan"/> that represents the time to wait for
    ///   the response.
    ///   </para>
    ///   <para>
    ///   The default value is the same as 1 second.
    ///   </para>
    /// </value>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The value specified for a set operation is zero or less.
    /// </exception>
    public TimeSpan WaitTime {
      get {
        return _services.WaitTime;
      }

      set {
        _services.WaitTime = value;
      }
    }

    /// <summary>
    /// Gets the management function for the WebSocket services provided by
    /// the server.
    /// </summary>
    /// <value>
    /// A <see cref="WebSocketServiceManager"/> that manages the WebSocket
    /// services provided by the server.
    /// </value>
    public WebSocketServiceManager WebSocketServices {
      get {
        return _services;
      }
    }

    #endregion

    #region Private Methods

    private void abort ()
    {
      if (!tryBeginShutdown ())
        return;

      var completed = false;

      try {
        try {
          _listener.Stop ();
        }
        catch (Exception ex) {
          _log.Fatal (ex.Message);
          _log.Debug (ex.ToString ());
        }

        var receivingStopped = stopHandshakeDispatcher (5000);

        if (!receivingStopped)
          return;

        try {
          _services.Stop (1006, String.Empty);
        }
        catch (Exception ex) {
          _log.Fatal (ex.Message);
          _log.Debug (ex.ToString ());
        }

        completed = true;
      }
      finally {
        endShutdown (completed);
      }
    }

    private bool authenticateClient (TcpListenerWebSocketContext context)
    {
      if (_authSchemes == AuthenticationSchemes.Anonymous)
        return true;

      if (_authSchemes == AuthenticationSchemes.None)
        return false;

      var chal = new AuthenticationChallenge (_authSchemes, _realmInUse)
                 .ToString ();

      var retry = -1;
      Func<bool> auth = null;
      auth =
        () => {
          retry++;

          if (retry > 99)
            return false;

          if (context.SetUser (_authSchemes, _realmInUse, _userCredFinder))
            return true;

          context.SendAuthenticationChallenge (chal);

          return auth ();
        };

      return auth ();
    }

    private bool canSet ()
    {
      return _state == ServerState.Ready || _state == ServerState.Stop;
    }

    private bool checkHostNameForRequest (string name)
    {
      return !_isDnsStyle
             || Uri.CheckHostName (name) != UriHostNameType.Dns
             || name == _hostname;
    }

    private string getRealm ()
    {
      var realm = _realm;

      return realm != null && realm.Length > 0 ? realm : _defaultRealm;
    }

    private ServerSslConfiguration getSslConfiguration ()
    {
      if (_sslConfig == null)
        _sslConfig = new ServerSslConfiguration ();

      return _sslConfig;
    }

    private int getHandshakeTimeoutMilliseconds ()
    {
      var milliseconds = _handshakeTimeout.TotalMilliseconds;

      if (milliseconds > Int32.MaxValue)
        return Int32.MaxValue;

      return Math.Max (1, (int) milliseconds);
    }

    private void init (
      string hostname,
      System.Net.IPAddress address,
      int port,
      bool secure
    )
    {
      _hostname = hostname;
      _address = address;
      _port = port;
      _isSecure = secure;

      _authSchemes = AuthenticationSchemes.Anonymous;
      _handshakeTimeout = TimeSpan.FromSeconds (10);
      _isDnsStyle = Uri.CheckHostName (hostname) == UriHostNameType.Dns;
      _listener = new TcpListener (address, port);
      _log = new Logger ();
      _maxConcurrentHandshakes = 128;
      _maxPendingHandshakes = 4096;
      _services = new WebSocketServiceManager (_log);
      _sync = new object ();
    }

    private void processRequest (TcpListenerWebSocketContext context)
    {
      if (!authenticateClient (context)) {
        context.Close (HttpStatusCode.Forbidden);

        return;
      }

      var uri = context.RequestUri;

      if (uri == null) {
        context.Close (HttpStatusCode.BadRequest);

        return;
      }

      var name = uri.DnsSafeHost;

      if (!checkHostNameForRequest (name)) {
        context.Close (HttpStatusCode.NotFound);

        return;
      }

      var path = uri.AbsolutePath;

      if (path.IndexOfAny (new[] { '%', '+' }) > -1)
        path = HttpUtility.UrlDecode (path, Encoding.UTF8);

      WebSocketServiceHost host;

      if (!_services.InternalTryGetServiceHost (path, out host)) {
        context.Close (HttpStatusCode.NotImplemented);

        return;
      }

      host.StartSession (context);
    }

    private void receiveRequest ()
    {
      while (true) {
        TcpClient cl = null;

        try {
          cl = _listener.AcceptTcpClient ();
          var accepted = cl;

          var dispatcher = _handshakeDispatcher;

          if (dispatcher == null) {
            accepted.Close ();
            cl = null;
            continue;
          }

          var queued = dispatcher.TryEnqueue (
            () => {
              try {
                var ctx = new TcpListenerWebSocketContext (
                            accepted,
                            null,
                            _isSecure,
                            _sslConfigInUse,
                            getHandshakeTimeoutMilliseconds (),
                            _log
                          );

                processRequest (ctx);
              }
              catch (Exception ex) {
                _log.Error (ex.Message);
                _log.Debug (ex.ToString ());

                accepted.Close ();
              }
            },
            accepted.Close
          );

          if (!queued)
            logHandshakeRejection ();

          cl = null;
        }
        catch (SocketException ex) {
          if (_state == ServerState.ShuttingDown)
            return;

          _log.Fatal (ex.Message);
          _log.Debug (ex.ToString ());

          break;
        }
        catch (InvalidOperationException ex) {
          if (_state == ServerState.ShuttingDown)
            return;

          _log.Fatal (ex.Message);
          _log.Debug (ex.ToString ());

          break;
        }
        catch (Exception ex) {
          _log.Fatal (ex.Message);
          _log.Debug (ex.ToString ());

          if (cl != null)
            cl.Close ();

          if (_state == ServerState.ShuttingDown)
            return;

          break;
        }
      }

      abort ();
    }

    private void start ()
    {
      lock (_sync) {
        if (_state == ServerState.Start || _state == ServerState.ShuttingDown)
          return;

        if (_isSecure) {
          var src = getSslConfiguration ();
          var conf = new ServerSslConfiguration (src);

          if (conf.ServerCertificate == null) {
            var msg = "There is no server certificate for secure connection.";

            throw new InvalidOperationException (msg);
          }

          _sslConfigInUse = conf;
        }

        _realmInUse = getRealm ();

        _services.Start ();

        try {
          startReceiving ();
        }
        catch {
          _services.Stop (1011, String.Empty);

          throw;
        }

        _state = ServerState.Start;
      }
    }

    private void startReceiving ()
    {
      Interlocked.Exchange (ref _rejectedHandshakeCount, 0);

      var dispatcher = new BoundedOperationDispatcher (
                         _maxConcurrentHandshakes,
                         _maxPendingHandshakes,
                         "WebSocketHandshake",
                         ex => {
                           _log.Error (ex.Message);
                           _log.Debug (ex.ToString ());
                         }
                       );

      if (_reuseAddress) {
        _listener.Server.SetSocketOption (
          SocketOptionLevel.Socket,
          SocketOptionName.ReuseAddress,
          true
        );
      }

      try {
        _listener.Start ();
      }
      catch (Exception ex) {
        dispatcher.Stop (5000);

        var msg = "The underlying listener has failed to start.";

        throw new InvalidOperationException (msg, ex);
      }

      var receiver = new ThreadStart (receiveRequest);
      _receiveThread = new Thread (receiver);
      _receiveThread.IsBackground = true;

      _handshakeDispatcher = dispatcher;

      try {
        _receiveThread.Start ();
      }
      catch {
        _handshakeDispatcher = null;
        _listener.Stop ();
        dispatcher.Stop (5000);
        throw;
      }
    }

    private void stop (ushort code, string reason)
    {
      if (!tryBeginShutdown ())
        return;

      var receivingStopped = false;
      var completed = false;

      try {
        try {
          var timeout = 5000;

          receivingStopped = stopReceiving (timeout);
        }
        catch (Exception ex) {
          _log.Fatal (ex.Message);
          _log.Debug (ex.ToString ());
        }

        if (!receivingStopped) {
          _log.Warn ("The server is still waiting for handshake workers to stop.");
          return;
        }

        try {
          _services.Stop (code, reason);
        }
        catch (Exception ex) {
          _log.Fatal (ex.Message);
          _log.Debug (ex.ToString ());
        }

        completed = true;
      }
      finally {
        endShutdown (completed);
      }
    }

    private bool stopReceiving (int millisecondsTimeout)
    {
      _listener.Stop ();

      var dispatcherStopped = stopHandshakeDispatcher (millisecondsTimeout);
      var receiverStopped = _receiveThread.Join (millisecondsTimeout);

      return dispatcherStopped && receiverStopped;
    }

    private void logHandshakeRejection ()
    {
      var count = Interlocked.Increment (ref _rejectedHandshakeCount);

      if (count != 1 && count % 256 != 0)
        return;

      var msg = String.Format (
                  "The pending WebSocket handshake queue is full; connections rejected since start: {0}.",
                  count
                );

      _log.Warn (msg);
    }

    private bool stopHandshakeDispatcher (int millisecondsTimeout)
    {
      var dispatcher = _handshakeDispatcher;

      if (dispatcher == null)
        return true;

      if (!dispatcher.Stop (millisecondsTimeout))
        return false;

      if (ReferenceEquals (_handshakeDispatcher, dispatcher))
        _handshakeDispatcher = null;

      return true;
    }

    private void endShutdown (bool completed)
    {
      lock (_sync) {
        if (completed)
          _state = ServerState.Stop;

        _shutdownInProgress = false;
      }
    }

    private bool tryBeginShutdown ()
    {
      lock (_sync) {
        if (_state == ServerState.Start)
          _state = ServerState.ShuttingDown;
        else if (_state != ServerState.ShuttingDown)
          return false;

        if (_shutdownInProgress)
          return false;

        _shutdownInProgress = true;
        return true;
      }
    }

    private static bool tryCreateUri (
      string uriString,
      out Uri result,
      out string message
    )
    {
      if (!uriString.TryCreateWebSocketUri (out result, out message))
        return false;

      if (result.PathAndQuery != "/") {
        result = null;
        message = "It includes either or both path and query components.";

        return false;
      }

      return true;
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Adds a WebSocket service with the specified behavior and path.
    /// </summary>
    /// <param name="path">
    ///   <para>
    ///   A <see cref="string"/> that specifies an absolute path to
    ///   the service to add.
    ///   </para>
    ///   <para>
    ///   / is trimmed from the end of the string if present.
    ///   </para>
    /// </param>
    /// <typeparam name="TBehavior">
    ///   <para>
    ///   The type of the behavior for the service.
    ///   </para>
    ///   <para>
    ///   It must inherit the <see cref="WebSocketBehavior"/> class.
    ///   </para>
    ///   <para>
    ///   Also it must have a public parameterless constructor.
    ///   </para>
    /// </typeparam>
    /// <exception cref="ArgumentException">
    ///   <para>
    ///   <paramref name="path"/> is an empty string.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   <paramref name="path"/> is not an absolute path.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   <paramref name="path"/> includes either or both
    ///   query and fragment components.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   <paramref name="path"/> is already in use.
    ///   </para>
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="path"/> is <see langword="null"/>.
    /// </exception>
    public void AddWebSocketService<TBehavior> (string path)
      where TBehavior : WebSocketBehavior, new ()
    {
      _services.AddService<TBehavior> (path, null);
    }

    /// <summary>
    /// Adds a WebSocket service with the specified behavior, path,
    /// and initializer.
    /// </summary>
    /// <param name="path">
    ///   <para>
    ///   A <see cref="string"/> that specifies an absolute path to
    ///   the service to add.
    ///   </para>
    ///   <para>
    ///   / is trimmed from the end of the string if present.
    ///   </para>
    /// </param>
    /// <param name="initializer">
    ///   <para>
    ///   An <see cref="T:System.Action{TBehavior}"/> delegate.
    ///   </para>
    ///   <para>
    ///   It specifies the delegate called when the service initializes
    ///   a new session instance.
    ///   </para>
    ///   <para>
    ///   <see langword="null"/> if not necessary.
    ///   </para>
    /// </param>
    /// <typeparam name="TBehavior">
    ///   <para>
    ///   The type of the behavior for the service.
    ///   </para>
    ///   <para>
    ///   It must inherit the <see cref="WebSocketBehavior"/> class.
    ///   </para>
    ///   <para>
    ///   Also it must have a public parameterless constructor.
    ///   </para>
    /// </typeparam>
    /// <exception cref="ArgumentException">
    ///   <para>
    ///   <paramref name="path"/> is an empty string.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   <paramref name="path"/> is not an absolute path.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   <paramref name="path"/> includes either or both
    ///   query and fragment components.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   <paramref name="path"/> is already in use.
    ///   </para>
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="path"/> is <see langword="null"/>.
    /// </exception>
    public void AddWebSocketService<TBehavior> (
      string path,
      Action<TBehavior> initializer
    )
      where TBehavior : WebSocketBehavior, new ()
    {
      _services.AddService<TBehavior> (path, initializer);
    }

    /// <summary>
    /// Removes a WebSocket service with the specified path.
    /// </summary>
    /// <remarks>
    /// The service is stopped with close status 1001 (going away)
    /// if the current state of the service is Start.
    /// </remarks>
    /// <returns>
    /// <c>true</c> if the service is successfully found and removed;
    /// otherwise, <c>false</c>.
    /// </returns>
    /// <param name="path">
    ///   <para>
    ///   A <see cref="string"/> that specifies an absolute path to
    ///   the service to remove.
    ///   </para>
    ///   <para>
    ///   / is trimmed from the end of the string if present.
    ///   </para>
    /// </param>
    /// <exception cref="ArgumentException">
    ///   <para>
    ///   <paramref name="path"/> is an empty string.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   <paramref name="path"/> is not an absolute path.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   <paramref name="path"/> includes either or both
    ///   query and fragment components.
    ///   </para>
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="path"/> is <see langword="null"/>.
    /// </exception>
    public bool RemoveWebSocketService (string path)
    {
      return _services.RemoveService (path);
    }

    /// <summary>
    /// Starts receiving incoming handshake requests.
    /// </summary>
    /// <remarks>
    /// This method works if the current state of the server is Ready or Stop.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    ///   <para>
    ///   There is no server certificate for secure connection.
    ///   </para>
    ///   <para>
    ///   -or-
    ///   </para>
    ///   <para>
    ///   The underlying <see cref="TcpListener"/> has failed to start.
    ///   </para>
    /// </exception>
    public void Start ()
    {
      if (_state == ServerState.Start || _state == ServerState.ShuttingDown)
        return;

      start ();
    }

    /// <summary>
    /// Stops receiving incoming handshake requests.
    /// </summary>
    /// <remarks>
    /// This method works if the current state of the server is Start or if a
    /// previous shutdown is still waiting for handshake workers to finish.
    /// </remarks>
    public void Stop ()
    {
      if (_state != ServerState.Start && _state != ServerState.ShuttingDown)
        return;

      stop (1001, String.Empty);
    }

    #endregion
  }
}
