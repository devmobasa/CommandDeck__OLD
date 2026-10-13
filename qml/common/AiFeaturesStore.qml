import QtQuick
import Quickshell.Io

// QML-only persistence for the AI master switch.
//
// Mirrors DisplayScaleStore: a tiny file under $XDG_STATE_HOME/commanddeck/
// records whether AI features are on. Off hides the assistant and the direct
// AI settings and keeps the launcher from starting any AI request. A missing
// or corrupted file reads as on. The daemon owns and creates the state
// directory, so the write needs no mkdir and a failed write is non-fatal.
// `active` is off in the test harness so specs never touch the real filesystem.
// QML must not spawn processes (only ProtocolClient may).
QtObject {
  id: root

  readonly property var serializedFlags: Object.freeze({ On: "on", Off: "off" })

  property bool active: true
  property string stateDir: ""
  property string fileName: "ai-features"
  readonly property string filePath: root.stateDir.length > 0
    ? root.stateDir + "/" + root.fileName
    : ""
  property bool enabled: true
  property bool loaded: false

  signal changed()

  // Only an explicit "off" disables AI: anything else, including an empty or
  // hand-edited file, keeps the features available.
  function parse(text) {
    const raw = String(text === null || text === undefined ? "" : text).trim().toLowerCase()
    return raw !== root.serializedFlags.Off && raw !== "false" && raw !== "0"
  }

  function load() {
    if (!root.active || root.filePath.length === 0) {
      root.enabled = true
      root.loaded = true
      root.changed()
      return
    }
    let existing = ""
    try {
      existing = file.text()
    } catch (error) {
      existing = ""
    }
    root.enabled = root.parse(existing)
    root.loaded = true
    root.changed()
  }

  function set(enabled) {
    const next = enabled === true
    if (root.loaded && next === root.enabled) return
    root.enabled = next
    root.changed()
    if (!root.active || root.filePath.length === 0) return
    try {
      file.setText((next ? root.serializedFlags.On : root.serializedFlags.Off) + "\n")
    } catch (error) {
      // A failed durable write still applies for this session; the switch
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
