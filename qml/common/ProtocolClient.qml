pragma ComponentBehavior: Bound
import QtQuick
import "Protocol.js" as Protocol
import Quickshell.Io

QtObject {
  id: root

  property string executable: "commanddeck"
  property string socketPath: ""
  property bool enabled: true
  property bool awaitingReconnect: false
  property bool suppressReconnectDone: false
  readonly property int reconnectProbeRequestId: 2147483000
  property bool ready: !enabled || (bridge.running && !awaitingReconnect)
  property string lastError: ""
  // A bridge that keeps failing is retried after a wait that doubles up to the
  // cap, so a stopped daemon costs a few spawns a minute. A verified
  // reconnection resets the wait, and reconnectNow() skips it.
  property int reconnectInitialDelay: 500
  property int reconnectMaximumDelay: 30000
  // The wait before the scheduled attempt; 0 while connected.
  property int reconnectDelay: 0

  signal message(var value)
  signal disconnected()
  signal requestSent(var value)
  signal reconnected()

  function send(value) {
    requestSent(value)
    if (!enabled) return
    if (!bridge.running) {
      attemptConnection()
    }

    const encoded = JSON.stringify(value) + "\n"
    bridge.write(encoded)
  }

  function restart() {
    if (!enabled) return
    awaitingReconnect = true
    bridge.running = false
    bridge.running = true
    reconnectProbe.restart()
  }

  // Opening the launcher retries a failed bridge now and restarts the backoff.
  // Nothing sent before the failure is replayed.
  function reconnectNow() {
    if (!enabled || !awaitingReconnect || bridge.running) return
    reconnectDelay = 0
    attemptConnection()
  }

  function attemptConnection() {
    reconnectTimer.stop()
    bridge.running = true
    if (awaitingReconnect) reconnectProbe.restart()
  }

  function scheduleReconnect() {
    reconnectDelay = reconnectDelay > 0
      ? Math.min(reconnectDelay * 2, reconnectMaximumDelay)
      : reconnectInitialDelay
    reconnectTimer.interval = reconnectDelay
    reconnectTimer.restart()
  }

  function handleBridgeMessage(value) {
    if (awaitingReconnect && value.requestId === reconnectProbeRequestId
        && value.type === Protocol.Messages.Pong) {
      awaitingReconnect = false
      suppressReconnectDone = true
      reconnectDelay = 0
      reconnectProbe.stop()
      reconnectProbeTimeout.stop()
      reconnected()
      return
    }
    if (awaitingReconnect) {
      return
    }
    if (suppressReconnectDone && value.requestId === reconnectProbeRequestId
        && value.type === Protocol.Messages.Done) {
      suppressReconnectDone = false
      return
    }
    message(value)
  }

  property Timer reconnectTimer: Timer {
    repeat: false
    onTriggered: {
      if (!root.enabled) return
      root.attemptConnection()
    }
  }

  property Timer reconnectProbe: Timer {
    interval: 100
    repeat: false
    onTriggered: {
      if (!root.enabled || !root.awaitingReconnect || !root.bridge.running) return
      root.bridge.write(JSON.stringify({
        version: Protocol.Version,
        requestId: root.reconnectProbeRequestId,
        operation: Protocol.Operations.Ping
      }) + "\n")
      root.reconnectProbeTimeout.restart()
    }
  }

  property Timer reconnectProbeTimeout: Timer {
    interval: 2000
    repeat: false
    onTriggered: {
      if (!root.awaitingReconnect || !root.bridge.running) return
      root.lastError = "commanddeck bridge reconnect probe timed out"
      root.bridge.running = false
    }
  }

  property Process bridge: Process {
    command: root.socketPath.length > 0
      ? [root.executable, "bridge", "--socket", root.socketPath]
      : [root.executable, "bridge"]
    running: root.enabled
    stdinEnabled: true

    stdout: SplitParser {
      splitMarker: "\n"
      onRead: data => {
        if (data.trim().length === 0) {
          return
        }

        try {
          root.handleBridgeMessage(JSON.parse(data))
        } catch (error) {
          root.lastError = "Invalid bridge response: " + error
        }
      }
    }

    stderr: SplitParser {
      splitMarker: "\n"
      onRead: data => {
        if (data.trim().length > 0) {
          root.lastError = data.trim()
        }
      }
    }

    // The installed Quickshell qmltypes names the second signal parameter as
    // QProcess::ExitStatus without exporting that C++ enum to qmllint.
    // qmllint disable signal-handler-parameters
    onExited: exitCode => {
      root.awaitingReconnect = true
      root.reconnectProbe.stop()
      root.reconnectProbeTimeout.stop()
      root.lastError = "commanddeck bridge exited with code " + exitCode
      if (root.enabled) root.scheduleReconnect()
      root.disconnected()
    }
    // qmllint enable signal-handler-parameters
  }
}
