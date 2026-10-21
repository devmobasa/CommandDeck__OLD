import QtQuick
import Quickshell.Io

// QML-only persistence for the launcher layout.
//
// Mirrors TransparencyStore: a tiny file under $XDG_STATE_HOME/commanddeck/
// holds one `name=value` line per layout choice, with no C# backend
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
  // The result details and file preview beside the list.
  property bool preview: true
  property int widthPercent: 100
  property int heightPercent: 100
  property bool loaded: false

  signal changed()

  function normalize(value) {
    const candidate = String(value === null || value === undefined ? "" : value).trim()
    if (!/^[+-]?\d+$/.test(candidate)) return 100

    return Math.max(80, Math.min(140, Math.round(Number(candidate) / 5) * 5))
  }

  function parse(text) {
    const values = { compact: false, preview: true, widthPercent: 100, heightPercent: 100 }
    const lines = String(text === null || text === undefined ? "" : text).split("\n")
    for (let index = 0; index < lines.length; index++) {
      const parts = lines[index].trim().toLowerCase().split("=")
      if (parts.length !== 2) continue
      const name = parts[0].trim()
      const value = parts[1].trim()
      if (name === "compact") values.compact = value === "on"
      else if (name === "preview") values.preview = value !== "off"
      else if (name === "width") values.widthPercent = normalize(value)
      else if (name === "height") values.heightPercent = normalize(value)
    }
    return values
  }

  function serialize() {
    return "compact=" + (root.compact ? "on" : "off") + "\n"
      + "preview=" + (root.preview ? "on" : "off") + "\n"
      + "width=" + root.widthPercent + "\nheight=" + root.heightPercent + "\n"
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
    root.compact = parsed.compact
    root.preview = parsed.preview
    root.widthPercent = parsed.widthPercent
    root.heightPercent = parsed.heightPercent
    root.loaded = true
    root.changed()
  }

  function setCompact(enabled) {
    root.set(enabled === true, root.preview)
  }

  function setPreview(visible) {
    root.set(root.compact, visible === true)
  }

  function setSize(width, height) {
    root.set(root.compact, root.preview, width, height)
  }

  function set(compact, preview, width = root.widthPercent, height = root.heightPercent) {
    const nextWidth = root.normalize(width)
    const nextHeight = root.normalize(height)
    if (root.loaded && compact === root.compact && preview === root.preview
        && nextWidth === root.widthPercent && nextHeight === root.heightPercent) return
    root.compact = compact
    root.preview = preview
    root.widthPercent = nextWidth
    root.heightPercent = nextHeight
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
