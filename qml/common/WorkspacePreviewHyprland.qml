import QtQuick
import Quickshell.Hyprland

QtObject {
  id: root
  readonly property var workspaces: Hyprland.workspaces.values
  signal invalidated()
  property double lastRefresh: 0
  property Connections events: Connections {
    target: Hyprland
    function onRawEvent(event) { root.invalidated() }
  }

  function refresh() {
    const now = Date.now()
    if (now - lastRefresh < 500) return
    lastRefresh = now
    // Pinned native IPC guards each request category against overlapping work.
    // There is no public batch completion signal: request once per open.
    Hyprland.refreshMonitors()
    Hyprland.refreshWorkspaces()
    Hyprland.refreshToplevels()
  }
}
