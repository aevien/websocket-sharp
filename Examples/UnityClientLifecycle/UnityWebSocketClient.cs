#if UNITY_WEBGL && !UNITY_EDITOR
using UnityEngine;

public sealed class UnityWebSocketClient : MonoBehaviour
{
  private void Awake ()
  {
    Debug.LogError (
      "websocket-sharp uses managed sockets and is not supported on Unity WebGL. " +
      "Use the browser JavaScript WebSocket layer for WebGL builds."
    );

    enabled = false;
  }
}
#else
using System;
using System.Collections.Concurrent;
using UnityEngine;
using WebSocketSharp;

public sealed class UnityWebSocketClient : MonoBehaviour
{
  [SerializeField]
  private string url = "ws://localhost:4649/Echo";

  [SerializeField]
  private bool connectOnEnable = true;

  [SerializeField]
  private int maxMainThreadActionsPerFrame = 64;

  private readonly ConcurrentQueue<Action> mainThreadActions =
    new ConcurrentQueue<Action> ();

  private WebSocket socket;
  private int generation;

  private void OnEnable ()
  {
    if (connectOnEnable)
      Connect ();
  }

  private void Update ()
  {
    var limit = Math.Max (1, maxMainThreadActionsPerFrame);

    for (var i = 0; i < limit; i++) {
      Action action;

      if (!mainThreadActions.TryDequeue (out action))
        return;

      action ();
    }
  }

  private void OnDisable ()
  {
    Close ();
  }

  private void OnDestroy ()
  {
    Close ();
  }

  public void Connect ()
  {
    if (socket != null)
      return;

    var currentGeneration = ++generation;
    var ws = new WebSocket (url);

    // Configure current fork guardrails before connecting.
    ws.ConnectionTimeout = TimeSpan.FromSeconds (10);
    ws.FrameReadTimeout = TimeSpan.FromSeconds (5);
    ws.MaxFramePayloadLength = 1024 * 1024;
    ws.MaxMessagePayloadLength = 4 * 1024 * 1024;
    ws.MaxMessageEventQueueLength = 256;
    ws.MaxAsyncSendQueueLength = 64;

    // websocket-sharp callbacks are not Unity main-thread callbacks. Do not
    // touch GameObjects, components, UI, scenes, or ScriptableObjects directly
    // here. Enqueue Unity work and run it from Update().
    ws.OnOpen += (sender, e) =>
      Enqueue (currentGeneration, () => Debug.Log ("WebSocket opened: " + url));

    ws.OnMessage += (sender, e) =>
      Enqueue (
        currentGeneration,
        () => Debug.Log (e.IsText ? "WebSocket message: " + e.Data : "WebSocket binary message")
      );

    ws.OnError += (sender, e) =>
      Enqueue (currentGeneration, () => Debug.LogError ("WebSocket error: " + e.Message));

    ws.OnClose += (sender, e) =>
      Enqueue (
        currentGeneration,
        () => Debug.Log ("WebSocket closed: " + e.Code + " " + e.Reason)
      );

    socket = ws;
    ws.ConnectAsync ();
  }

  public void SendText (string message)
  {
    var ws = socket;

    if (ws == null || ws.ReadyState != WebSocketState.Open)
      return;

    var currentGeneration = generation;

    ws.SendAsync (
      message,
      completed =>
        Enqueue (
          currentGeneration,
          () => Debug.Log (completed ? "WebSocket send completed." : "WebSocket send failed.")
        )
    );
  }

  public void Close ()
  {
    var ws = socket;

    if (ws == null)
      return;

    socket = null;
    generation++;

    EventHandler<CloseEventArgs> disposeOnClose = null;
    disposeOnClose = (sender, e) => {
      ws.OnClose -= disposeOnClose;
      ws.Dispose ();
    };

    ws.OnClose += disposeOnClose;

    try {
      if (ws.ReadyState == WebSocketState.Closed) {
        ws.OnClose -= disposeOnClose;
        ws.Dispose ();
        return;
      }

      ws.CloseAsync (CloseStatusCode.Normal, "Unity lifecycle ended");
    }
    catch (Exception ex) {
      ws.OnClose -= disposeOnClose;
      ws.Dispose ();
      Debug.LogWarning ("WebSocket close failed: " + ex.Message);
    }
  }

  private void Enqueue (int callbackGeneration, Action action)
  {
    if (callbackGeneration != generation)
      return;

    mainThreadActions.Enqueue (action);
  }
}
#endif
