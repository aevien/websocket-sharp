/*
 * echotest.js
 *
 * Derived from Echo Test of WebSocket.org (http://www.websocket.org/echo.html).
 *
 * Copyright (c) 2012 Kaazing Corporation.
 */

var url = "ws://localhost:4649/Echo";
//var url = "wss://localhost:5963/Echo";
var output;
var websocket;

function init () {
  output = document.getElementById ("output");
  doWebSocket ();
}

function doWebSocket () {
  websocket = new WebSocket (url);

  websocket.onopen = function (e) {
    onOpen (e);
  };

  websocket.onmessage = function (e) {
    onMessage (e);
  };

  websocket.onerror = function (e) {
    onError (e);
  };

  websocket.onclose = function (e) {
    onClose (e);
  };
}

function onOpen (event) {
  writeToScreen ("CONNECTED");
  send ("WebSocket rocks");
}

function onMessage (event) {
  writeToScreen ("RESPONSE: " + event.data, "blue");
  websocket.close ();
}

function onError (event) {
  writeToScreen ("ERROR: " + event.data, "red");
}

function onClose (event) {
  writeToScreen ("DISCONNECTED");
}

function send (message) {
  writeToScreen ("SENT: " + message);
  websocket.send (message);
}

function writeToScreen (message, color) {
  var pre = document.createElement ("p");
  pre.style.wordWrap = "break-word";
  if (color)
    pre.style.color = color;

  pre.textContent = message;
  output.appendChild (pre);
}

window.addEventListener ("load", init, false);
