import QtQuick
import Quickshell.Io

// C10 persistence - durable "first-run coach seen" marker, written from QML.
//
// A tiny marker file under $XDG_STATE_HOME/commanddeck/ records that the coach
// has been dismissed. Reads are synchronous (blockLoading); a missing file just
// reads empty. The daemon owns and creates that state directory (the launcher
// only runs with the daemon), so the write needs no mkdir; a failed write is
// non-fatal. `active` is turned off in the test harness so specs never touch
// the real filesystem. QML must not spawn processes (only ProtocolClient may).
QtObject {
  id: root

  property bool active: true
  property string stateDir: ""
  property string markerName: "first-run-coach.seen"
  readonly property string markerPath: root.stateDir.length > 0
    ? root.stateDir + "/" + root.markerName
    : ""
  property bool seen: false
  property bool loaded: false

  signal changed()

  function load() {
    if (!root.active || root.markerPath.length === 0) {
      root.loaded = true
      return
    }
    let existing = ""
    try {
      existing = marker.text()
    } catch (error) {
      existing = ""
    }
    root.seen = existing !== null && String(existing).trim().length > 0
    root.loaded = true
    root.changed()
  }

  function markSeen() {
    if (root.seen) return
    root.seen = true
    root.changed()
    if (!root.active || root.markerPath.length === 0) return
    try {
      marker.setText("seen " + new Date().toISOString() + "\n")
    } catch (error) {
      // A failed durable write still leaves `seen` true for this session; the
      // coach simply reappears on the next launch rather than crashing.
    }
  }

  property FileView marker: FileView {
    path: root.active && root.markerPath.length > 0 ? root.markerPath : ""
    blockLoading: true
    printErrors: false
    atomicWrites: true
  }
}
