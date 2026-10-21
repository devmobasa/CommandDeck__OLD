import QtQuick
import "UiConstants.js" as Ui
import Quickshell.Io

// Durable display-size percentage, written from QML only.
//
// Mirrors FirstRunStore: a tiny file under $XDG_STATE_HOME/commanddeck/ records
// the chosen display-size percentage so users can fix launcher sizing themselves,
// with no C# backend preference involved. Completed reads apply asynchronously;
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

  // A completed read may trail a local write; keep the session choice until
  // a read observes its bytes, including after an explicit refresh.
  property bool percentReady: false
  property bool profileReady: false
  property string percentText: ""
  property string profileText: ""
  property int pendingPercent: -1
  property string pendingProfile: ""

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
    root.percentReady = false
    root.profileReady = false
    file.reload()
    profile.reload()
  }

  function applyReads() {
    if (!root.active || !root.percentReady || !root.profileReady) return

    const next = root.normalize(root.percentText)
    if (root.pendingPercent < 0 || next === root.pendingPercent) {
      root.percent = next
      root.pendingPercent = -1
    }
    if (root.pendingProfile.length === 0 || root.profileText === root.pendingProfile) {
      root.pendingProfile = ""
      root.legacy = root.profileText !== "logical-v1"
      root.legacyPercent = root.profileText.startsWith("legacy-v1:")
        ? root.normalize(root.profileText.slice("legacy-v1:".length)) : root.percent
      if (root.profileText === "legacy-v1") root.persistProfile()
    }
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
    root.pendingPercent = next
    try {
      file.setText(String(root.percent) + "\n")
    } catch (error) {
      // A failed durable write still applies for this session; the percentage
      // simply reverts on the next launch rather than crashing.
    }
  }

  function persistProfile(replaceUnknown = false) {
    if (!root.active || root.filePath.length === 0) return
    const existing = root.profileText
    if (!replaceUnknown && existing.length > 0 && existing !== "logical-v1"
        && !/^legacy-v1(?::\d+)?$/.test(existing)) return

    root.pendingProfile = root.legacy ? "legacy-v1:" + root.legacyPercent : "logical-v1"
    try {
      profile.setText(root.pendingProfile + "\n")
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
    blockLoading: false
    printErrors: false
    atomicWrites: true
    watchChanges: true
    onFileChanged: reload()
    onLoaded: { root.percentText = text(); root.percentReady = true; root.applyReads() }
    onLoadFailed: { root.percentText = ""; root.percentReady = true; root.applyReads() }
    onSaved: reload()
  }

  property FileView profile: FileView {
    path: root.active && root.stateDir.length > 0 ? root.stateDir + "/display-sizing" : ""
    blockLoading: false
    printErrors: false
    atomicWrites: true
    watchChanges: true
    onFileChanged: reload()
    onLoaded: { root.profileText = text().trim(); root.profileReady = true; root.applyReads() }
    onLoadFailed: { root.profileText = ""; root.profileReady = true; root.applyReads() }
    onSaved: reload()
  }
}
