import QtQuick
import "UiConstants.js" as Ui
import "ColorThemeFormat.js" as Format
import Quickshell.Io

// QML-only persistence for the launcher colours.
//
// Mirrors TransparencyStore: a small JSON file under $XDG_STATE_HOME/commanddeck/
// records whether CommandDeck's own palette replaces the Omarchy theme and that
// palette's accent, background, and text. Reads are synchronous; a missing or
// corrupted file reads as the Omarchy theme with the default palette. The
// daemon owns and creates the state directory, so a failed write only lasts
// for this session. `active` is off in the test harness so specs never touch
// the real filesystem. QML must not spawn processes (only ProtocolClient may).
QtObject {
  id: root

  property bool active: true
  property string stateDir: ""
  property string fileName: "color-theme"
  readonly property string filePath: root.stateDir.length > 0
    ? root.stateDir + "/" + root.fileName
    : ""
  property string source: Ui.ColorThemeSource.Omarchy
  property var colors: Format.defaultColors()
  property bool loaded: false
  readonly property bool overridesHost: root.source === Ui.ColorThemeSource.CommandDeck

  signal changed()

  function current() {
    return { source: root.source, colors: root.colors }
  }

  function apply(preference) {
    root.source = preference.source
    root.colors = preference.colors
    root.loaded = true
    root.changed()
  }

  function load() {
    if (!root.active || root.filePath.length === 0) {
      root.apply(Format.defaults())
      return
    }

    let existing = ""
    try {
      existing = file.text()
    } catch (error) {
      existing = ""
    }

    root.apply(Format.parse(existing))
  }

  // Takes any of { source, colors: { accent, background, text } }; a colour
  // that is not #rrggbb keeps its current value.
  function set(change) {
    const next = Format.merge(root.current(), change)
    if (root.loaded && Format.equal(next, root.current())) return

    root.apply(next)
    if (!root.active || root.filePath.length === 0) return

    try {
      file.setText(Format.serialize(next))
    } catch (error) {
      // A failed durable write still applies for this session.
    }
  }

  function resetColors() {
    root.set({ colors: Format.defaultColors() })
  }

  property FileView file: FileView {
    path: root.active && root.filePath.length > 0 ? root.filePath : ""
    blockLoading: true
    printErrors: false
    atomicWrites: true
  }
}
