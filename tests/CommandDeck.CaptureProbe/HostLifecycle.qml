import QtQuick
import Quickshell
import Quickshell.Io
import "AuthServiceStore.js" as Store

ShellRoot {
  id: root
  property var peer: null
  property var source: null
  property int phase: 0
  property int ticks: 0
  Component {
    id: lockFactory
    QtObject {
      property bool locked: false
      property bool strandedLock: false
      property bool strandedLockResolved: true
      signal captureStateInvalidated()
      Component.onDestruction: captureStateInvalidated()
    }
  }
  Component {
    id: fakeCapture
    Item {
      property var source: null
      property bool permitted: false
      readonly property bool hasContent: permitted && !!source
      readonly property bool expired: false
    }
  }
  SocketServer {
    id: server
    path: Quickshell.env("XDG_RUNTIME_DIR") + "/hypr/host-probe/.socket2.sock"
    active: true
    handler: Socket { Component.onCompleted: root.peer = this }
  }
  PluginShellApi { id: api; pluginId: "local.commanddeck" }
  WorkspaceSnapshotLifecycle { id: lifecycle; enabled: server.active; capability: api.workspaceCaptureState }
  Theme { id: theme; loadShellTokens: false }
  WorkspaceSnapshotGrid {
    id: grid
    width: 640
    height: 360
    theme: theme
    permitted: lifecycle.permitted
    epoch: lifecycle.epoch
    captureComponent: fakeCapture
    entries: root.source ? [{ source: root.source, label: "synthetic", width: 640, height: 360 }] : []
  }
  function check(condition, message) {
    if (!condition) {
      console.log("HOST_CAPTURE_INTEGRATION_FAIL " + message)
      Qt.quit()
    }
  }
  Component.onCompleted: {
    Store.setCaptureStatePublisher(state => api._workspaceCaptureState = state)
    check(!lifecycle.permitted, "unknown startup")
    root.source = lockFactory.createObject(null)
    Store.put("omarchy.lock", root.source)
  }
  Timer {
    interval: 20
    repeat: true
    running: true
    onTriggered: {
      if (++root.ticks > 200) {
        root.check(false, "timeout at phase " + root.phase)
        return
      }
      if (root.phase === 0 && lifecycle.permitted && grid.count === 1) {
        root.source.locked = true
        root.check(!lifecycle.permitted && grid.count === 0, "lock clears immediately")
        root.source.locked = false
        root.check(!lifecycle.permitted, "unlock waits for stable state")
        root.phase = 1
      } else if (root.phase === 1 && lifecycle.permitted && grid.count === 1) {
        Store.destroy("omarchy.lock")
        root.check(!lifecycle.permitted && grid.count === 0, "service removal clears immediately")
        root.source = lockFactory.createObject(null)
        Store.put("omarchy.lock", root.source)
        root.phase = 2
      } else if (root.phase === 2 && lifecycle.permitted && grid.count === 1 && root.peer) {
        root.peer.connected = false
        root.phase = 3
      } else if (root.phase === 3 && !lifecycle.permitted && grid.count === 0) {
        console.log("HOST_CAPTURE_INTEGRATION_PASS")
        Qt.quit()
      }
    }
  }
}
