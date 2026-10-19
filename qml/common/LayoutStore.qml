import QtQuick
import Quickshell.Io

// QML-only persistence for the launcher layout.
//
// Mirrors TransparencyStore: a tiny file under $XDG_STATE_HOME/commanddeck/
// holds one `name=on|off` line per layout choice, with no C# backend
// preference involved. A missing file or an unknown value reads as the
// default layout. `active` is off in the test harness so specs never touch
// the real filesystem. QML must not spawn processes (only ProtocolClient may).
QtObject {
  id: root

  property bool active: true
  property string stateDir: ""
  property string fileName: "launcher-layout"
  readonly property string filePath: root.stateDir.length > 0
    ? root.stateDir + "/" + root.fileName
    : ""
  // One line per result, Ctrl+number badges only while Ctrl is held, and a
  // footer with just the primary action, Actions and Help.
  property bool compact: false
  property bool loaded: false

  signal changed()

  function parse(text) {
    const values = { compact: false }
    const lines = String(text === null || text === undefined ? "" : text).split("\n")
    for (let index = 0; index < lines.length; index++) {
      const parts = lines[index].trim().toLowerCase().split("=")
      if (parts.length === 2 && parts[0].trim() === "compact") values.compact = parts[1].trim() === "on"
    }
    return values
  }

  function serialize() {
    return "compact=" + (root.compact ? "on" : "off") + "\n"
  }

  function load() {
    let existing = ""
    if (root.active && root.filePath.length > 0) {
      try {
        existing = file.text()
      } catch (error) {
        existing = ""
      }
    }
    root.compact = root.parse(existing).compact
    root.loaded = true
    root.changed()
  }

  function setCompact(enabled) {
    const next = enabled === true
    if (root.loaded && next === root.compact) return
    root.compact = next
    root.changed()
    if (!root.active || root.filePath.length === 0) return
    try {
      file.setText(root.serialize())
    } catch (error) {
      // A failed durable write still applies for this session; the layout
      // simply reverts on the next launch rather than crashing.
    }
  }

  property FileView file: FileView {
    path: root.active && root.filePath.length > 0 ? root.filePath : ""
    blockLoading: true
    printErrors: false
    atomicWrites: true
  }
}
