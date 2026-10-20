import QtQuick
import "UiConstants.js" as Ui
import Quickshell.Io

// Durable display-size percentage, written from QML only.
//
// Mirrors FirstRunStore: a tiny file under $XDG_STATE_HOME/commanddeck/ records
// the chosen display-size percentage so users can fix launcher sizing themselves,
// with no C# backend preference involved. Reads are synchronous (blockLoading);
// a missing percentage reads as 100%. The versioned profile preserves existing
// users' sizing; Recommended opts into logical workspace sizing. The daemon creates the
// state directory, so the write needs no mkdir and a failed write is non-fatal.
// `active` disables I/O in fixtures; durable specs use a temporary directory.
// QML must not spawn processes (only ProtocolClient may).
QtObject {
  id: root

  property bool active: true
  property string stateDir: ""
  property string fileName: "display-size"
  readonly property string filePath: root.stateDir.length > 0
    ? root.stateDir + "/" + root.fileName
    : ""
  property int percent: Ui.DisplaySizePercent.Default
  property bool legacy: false
  property int legacyPercent: Ui.DisplaySizePercent.Default
  property bool loaded: false

  signal changed()

  // Legacy presets keep their size. Other file contents must be integers;
  // clamp and snap them so a hand-edited file cannot break the scale.
  function normalize(value) {
    const candidate = String(value === null || value === undefined ? "" : value)
      .trim().toLowerCase()
    const legacy = { auto: 100, default: 100, small: 90, large: 115, xlarge: 130 }
    if (Object.prototype.hasOwnProperty.call(legacy, candidate)) return legacy[candidate]
    if (!/^[+-]?\d+$/.test(candidate)) return Ui.DisplaySizePercent.Default

    const rounded = Math.round(Number(candidate) / Ui.DisplaySizePercent.Step) * Ui.DisplaySizePercent.Step
    return Math.max(Ui.DisplaySizePercent.Minimum, Math.min(Ui.DisplaySizePercent.Maximum, rounded))
  }

  function load() {
    if (!root.active || root.filePath.length === 0) {
      root.percent = Ui.DisplaySizePercent.Default
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
    root.percent = root.normalize(existing)
    root.loadProfile()
    root.loaded = true
    root.changed()
  }

  function setPercent(value) {
    const next = root.normalize(value)
    if (root.loaded && next === root.percent) return

    if (root.legacy) root.persistProfile()
    root.percent = next
    root.changed()
    if (!root.active || root.filePath.length === 0) return
    try {
      file.setText(String(root.percent) + "\n")
    } catch (error) {
      // A failed durable write still applies for this session; the percentage
      // simply reverts on the next launch rather than crashing.
    }
  }

  function loadProfile() {
    let existing = ""
    try { existing = profile.text().trim() } catch (error) { existing = "" }
    root.legacy = existing !== "logical-v1"
    root.legacyPercent = existing.startsWith("legacy-v1:")
      ? root.normalize(existing.slice("legacy-v1:".length)) : root.percent
    // Upgrade only the known legacy marker; absent/unknown markers fail safe
    // without racing a daemon that is still initializing a fresh installation.
    if (existing === "legacy-v1") root.persistProfile()
  }

  function persistProfile(replaceUnknown = false) {
    if (!root.active || root.filePath.length === 0) return
    let existing = ""
    try { existing = profile.text().trim() } catch (error) { existing = "" }
    if (!replaceUnknown && existing.length > 0 && existing !== "logical-v1"
        && !/^legacy-v1(?::\d+)?$/.test(existing)) return

    try {
      profile.setText(root.legacy ? "legacy-v1:" + root.legacyPercent + "\n" : "logical-v1\n")
    } catch (error) {
      // As with the percentage, a read-only state directory applies in-session.
    }
  }

  function useRecommended() {
    root.legacy = false
    root.setPercent(Ui.DisplaySizePercent.Default)
    root.persistProfile(true)
    root.changed()
  }

  property FileView file: FileView {
    path: root.active && root.filePath.length > 0 ? root.filePath : ""
    blockLoading: true
    printErrors: false
    atomicWrites: true
  }

  property FileView profile: FileView {
    path: root.active && root.stateDir.length > 0 ? root.stateDir + "/display-sizing" : ""
    blockLoading: true
    printErrors: false
    atomicWrites: true
    watchChanges: true
    onFileChanged: { reload(); root.load() }
  }
}
