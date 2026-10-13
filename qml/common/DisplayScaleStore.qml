import QtQuick
import "UiConstants.js" as Ui
import Quickshell.Io

// D13 persistence - durable "display size" preset, written from QML only.
//
// Mirrors FirstRunStore: a tiny file under $XDG_STATE_HOME/commanddeck/ records
// the chosen display-size preset so users can fix launcher sizing themselves,
// with no C# backend preference involved. Reads are synchronous (blockLoading);
// a missing file reads as the default "auto". The daemon owns and creates the
// state directory, so the write needs no mkdir and a failed write is non-fatal.
// `active` is off in the test harness so specs never touch the real filesystem.
// QML must not spawn processes (only ProtocolClient may).
QtObject {
  id: root

  property bool active: true
  property string stateDir: ""
  property string fileName: "display-size"
  readonly property string filePath: root.stateDir.length > 0
    ? root.stateDir + "/" + root.fileName
    : ""
  property string preset: Ui.DisplaySize.Auto
  property bool loaded: false

  signal changed()

  // Only ever return a preset the launcher understands, so a corrupted or
  // hand-edited file degrades to Auto rather than a broken scale.
  function normalize(value) {
    const candidate = String(value === null || value === undefined ? "" : value)
      .trim().toLowerCase()
    switch (candidate) {
      case Ui.DisplaySize.Small:
      case Ui.DisplaySize.Default:
      case Ui.DisplaySize.Large:
      case Ui.DisplaySize.ExtraLarge:
      case Ui.DisplaySize.Auto:
        return candidate
      default:
        return Ui.DisplaySize.Auto
    }
  }

  function load() {
    if (!root.active || root.filePath.length === 0) {
      root.preset = Ui.DisplaySize.Auto
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
    root.preset = root.normalize(existing)
    root.loaded = true
    root.changed()
  }

  function setPreset(value) {
    const next = root.normalize(value)
    if (root.loaded && next === root.preset) return
    root.preset = next
    root.changed()
    if (!root.active || root.filePath.length === 0) return
    try {
      file.setText(next + "\n")
    } catch (error) {
      // A failed durable write still applies for this session; the preset
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
