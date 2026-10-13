import QtQuick
import Quickshell.Io

// QML-only persistence for launcher panel transparency.
//
// Mirrors DisplayScaleStore: a tiny file under $XDG_STATE_HOME/commanddeck/
// records whether transparency is on and how see-through the panel is, with
// no C# backend preference involved. Reads are synchronous (blockLoading);
// a missing file reads as off at 20%. The daemon owns and creates the state
// directory, so the write needs no mkdir and a failed write is non-fatal.
// `active` is off in the test harness so specs never touch the real filesystem.
// QML must not spawn processes (only ProtocolClient may).
QtObject {
  id: root

  readonly property var serializedFlags: Object.freeze({ On: "on", Off: "off" })


  property bool active: true
  property string stateDir: ""
  property string fileName: "panel-transparency"
  readonly property string filePath: root.stateDir.length > 0
    ? root.stateDir + "/" + root.fileName
    : ""
  property bool enabled: false
  property int percent: 20
  property bool loaded: false
  readonly property int maxPercent: 80
  readonly property int defaultPercent: 20

  signal changed()

  // Keep the panel readable: 0–80 inclusive. A corrupted or hand-edited file
  // degrades to the default amount rather than a disappeared launcher.
  function normalizePercent(value) {
    const n = Math.round(Number(value))
    if (!isFinite(n)) return root.defaultPercent
    if (n < 0) return 0
    if (n > root.maxPercent) return root.maxPercent
    return n
  }

  function parse(text) {
    const raw = String(text === null || text === undefined ? "" : text)
      .trim().toLowerCase()
    if (raw.length === 0) {
      return { enabled: false, percent: root.defaultPercent }
    }
    const parts = raw.split(":")
    const flag = String(parts[0] || "").trim()
    const amount = parts.length > 1 ? parts[1] : String(root.defaultPercent)
    const on = flag === root.serializedFlags.On || flag === "true" || flag === "1"
    return { enabled: on, percent: root.normalizePercent(amount) }
  }

  function serialize() {
    return (root.enabled ? root.serializedFlags.On : root.serializedFlags.Off) + ":" + String(root.percent) + "\n"
  }

  function load() {
    if (!root.active || root.filePath.length === 0) {
      root.enabled = false
      root.percent = root.defaultPercent
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
    const parsed = root.parse(existing)
    root.enabled = parsed.enabled
    root.percent = parsed.percent
    root.loaded = true
    root.changed()
  }

  function set(enabled, percent) {
    const nextEnabled = enabled === true
    const nextPercent = root.normalizePercent(percent)
    if (root.loaded && nextEnabled === root.enabled && nextPercent === root.percent) {
      return
    }
    root.enabled = nextEnabled
    root.percent = nextPercent
    root.changed()
    if (!root.active || root.filePath.length === 0) return
    try {
      file.setText(root.serialize())
    } catch (error) {
      // A failed durable write still applies for this session; the setting
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
