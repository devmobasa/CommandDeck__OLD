import QtQuick
import Quickshell
import "plugin" as CommandDeck

// Real controller/transport/daemon lifecycle without a surface or desktop input.
ShellRoot {
  id: root
  property int requestId: 0
  property int completed: 0
  property bool started: false
  property bool finishing: false
  readonly property var command: ({ id: Quickshell.env("COMMANDDECK_TEST_COMMAND"), title: "UUID Tools" })
  Component.onCompleted: Qt.callLater(function() {
    root.started = true
    session.open(root.command)
  })

  function advance() {
    if (!started) return
    if (session.failed || session.error) {
      console.error("Lifecycle failed: " + session.error)
      Qt.exit(1)
    } else if (finishing && !session.closingRequestId) {
      console.log("PASS connected controller: 20 immediate close/reopen cycles")
      Qt.quit()
    } else if (session.opened && session.sessionId && !session.busy) {
      completed++
      session.close()
      if (completed === 20) finishing = true
      else session.open(root.command)
    }
  }
  CommandDeck.ExtensionSessionController {
    id: session
    nextRequestId: function() { return ++root.requestId }
    onRequest: value => bridge.send(value)
    onBusyChanged: Qt.callLater(root.advance)
    onClosingRequestIdChanged: Qt.callLater(root.advance)
  }
  CommandDeck.ProtocolClient {
    id: bridge
    executable: Quickshell.env("COMMANDDECK_TEST_HOST")
    socketPath: Quickshell.env("COMMANDDECK_TEST_SOCKET")
    onMessage: value => {
      session.handleMessage(value)
      Qt.callLater(root.advance)
    }
    onDisconnected: {
      if (root.started) {
        console.error("Lifecycle daemon disconnected")
        Qt.exit(1)
      }
    }
  }
  Timer {
    interval: 60000
    running: true
    onTriggered: { console.error("Lifecycle timed out"); Qt.exit(1) }
  }
}
