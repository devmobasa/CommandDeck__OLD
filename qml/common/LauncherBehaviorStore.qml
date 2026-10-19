import QtQuick
import Quickshell.Io

// QML-only persistence for how the launcher closes and reopens.
//
// Mirrors LayoutStore: a tiny file under $XDG_STATE_HOME/commanddeck/ holds
// `escape=clear|close` and `keep=<seconds>` lines, with no C# backend
// preference involved. A missing file or an unknown value reads as the
// default: Escape closes, and a reopened launcher starts on Home. `active` is
// off in the test harness so specs never touch the real filesystem. QML must
// not spawn processes (only ProtocolClient may).
QtObject {
  id: root

  // The choices offered for keeping the last search, in seconds; 0 is off.
  readonly property var keepChoices: Object.freeze([0, 30, 120, 600])

  property bool active: true
  property string stateDir: ""
  property string fileName: "launcher-behavior"
  readonly property string filePath: root.stateDir.length > 0
    ? root.stateDir + "/" + root.fileName
    : ""
  // Escape clears a non-empty search before it closes the launcher.
  property bool escapeClears: false
  // A launcher reopened this soon after the user closed it shows the same search.
  property int keepSeconds: 0
  property bool loaded: false

  signal changed()

  function normalizeKeep(value) {
    const seconds = Math.round(Number(value))
    return root.keepChoices.indexOf(seconds) >= 0 ? seconds : 0
  }

  function parse(text) {
    const values = { escapeClears: false, keepSeconds: 0 }
    const lines = String(text === null || text === undefined ? "" : text).split("\n")
    for (let index = 0; index < lines.length; index++) {
      const parts = lines[index].trim().toLowerCase().split("=")
      if (parts.length !== 2) continue
      const name = parts[0].trim()
      const value = parts[1].trim()
      if (name === "escape") values.escapeClears = value === "clear"
      else if (name === "keep") values.keepSeconds = root.normalizeKeep(value)
    }
    return values
  }

  function serialize() {
    return "escape=" + (root.escapeClears ? "clear" : "close") + "\n"
      + "keep=" + String(root.keepSeconds) + "\n"
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
    const parsed = root.parse(existing)
    root.escapeClears = parsed.escapeClears
    root.keepSeconds = parsed.keepSeconds
    root.loaded = true
    root.changed()
  }

  function set(escapeClears, keepSeconds) {
    const nextEscape = escapeClears === true
    const nextKeep = root.normalizeKeep(keepSeconds)
    if (root.loaded && nextEscape === root.escapeClears && nextKeep === root.keepSeconds) return
    root.escapeClears = nextEscape
    root.keepSeconds = nextKeep
    root.changed()
    if (!root.active || root.filePath.length === 0) return
    try {
      file.setText(root.serialize())
    } catch (error) {
      // A failed durable write still applies for this session; the choice
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
