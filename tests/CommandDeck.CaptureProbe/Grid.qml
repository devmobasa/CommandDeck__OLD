import QtQuick
import QtQuick.Window
import Quickshell
import Quickshell.Hyprland
import Quickshell.Wayland

Window {
  id: root
  width: 640
  height: 360
  visible: true
  title: "CommandDeck synthetic snapshot grid"
  property int phase: 0
  property double startedAt: Date.now()
  property bool enabled: true
  property bool lockRequested: false
  property int generation: 1
  readonly property var targets: Hyprland.toplevels.values
    .filter(value => value.title.indexOf("CommandDeck synthetic counter ") === 0)
    .sort((a, b) => a.title.localeCompare(b.title))
  Theme { id: theme; loadShellTokens: false }
  QtObject {
    id: syntheticCapability
    readonly property int version: 1
    readonly property bool known: true
    readonly property bool locked: root.lockRequested || lock.locked || lock.secure
    readonly property int generation: root.generation
  }
  WorkspaceSnapshotLifecycle {
    id: lifecycle
    enabled: root.enabled
    capability: syntheticCapability
  }
  WlSessionLock {
    id: lock
    locked: false
    WlSessionLockSurface { color: "#402020" }
  }
  WorkspaceSnapshotGrid {
    id: grid
    anchors.fill: parent
    theme: theme
    permitted: lifecycle.permitted
    epoch: lifecycle.epoch
    selectionKey: String(root.generation)
    entries: root.targets.map(value => ({ source: value.wayland, label: value.title,
      width: value.lastIpcObject.size ? value.lastIpcObject.size[0] : 0,
      height: value.lastIpcObject.size ? value.lastIpcObject.size[1] : 0 }))
  }
  function report(event) {
    const frames = []
    for (const child of grid.children) {
      if (child.objectName.indexOf("workspaceSnapshotCell") !== 0) continue
      frames.push({ label: child.modelData.label, content: child.capture ? child.capture.hasContent : false,
        allocated: child.capture ? child.capture.allocated : false,
        size: child.capture ? String(child.capture.frameSize) : "",
        x: child.x, y: child.y, width: child.width, height: child.height })
    }
    console.log("PROBE " + JSON.stringify({ event: event, time: Date.now(), elapsed: Date.now() - startedAt,
      count: grid.count, locked: syntheticCapability.locked, permitted: lifecycle.permitted, frames: frames }))
  }
  Timer {
    interval: 50
    repeat: true
    running: root.phase === 0
    onTriggered: {
      for (const child of grid.children) {
        if (child.objectName.indexOf("workspaceSnapshotCell") === 0 && child.capture && child.capture.hasContent) {
          root.report("first-content")
          stop()
          return
        }
      }
    }
  }
  Timer {
    interval: 2000
    repeat: true
    running: true
    onTriggered: {
      root.phase++
      if (root.phase === 1 || root.phase === 2) {
        root.report("sample-" + root.phase)
        grid.grabToImage(result => result.saveToFile(Quickshell.env("CD_PROBE_OUTPUT") + "/grid-" + root.phase + ".png"))
      } else if (root.phase === 3) {
        root.lockRequested = true
        lock.locked = true
        root.report("lock-requested")
      } else if (root.phase === 4) {
        root.report("locked")
        lock.locked = false
        root.lockRequested = false
      } else if (root.phase === 5) {
        root.report("unlocked")
        root.generation++
        root.report("selection-cleared")
      } else if (root.phase === 6) {
        root.enabled = false
        root.report("dismissed")
      } else if (root.phase === 7) {
        root.report("teardown")
        Qt.quit()
      }
    }
  }
  Component.onDestruction: lock.locked = false
}
