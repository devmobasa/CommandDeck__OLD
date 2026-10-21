import QtQuick
import Quickshell
import Quickshell.Hyprland

DesktopIntegration {
  id: root

  property var shell: null
  integrationId: "hyprland"
  screens: Quickshell.screens
  monitors: Hyprland.monitors.values
  focusedMonitor: Hyprland.focusedMonitor
  focusedWorkspace: Hyprland.focusedWorkspace
  activeWorkspace: Hyprland.focusedWorkspace
  windowToplevels: Hyprland.toplevels
    ? Hyprland.toplevels.values
    : []
  workspacePreviewBackend: WorkspacePreviewHyprland { }
  workspaceSnapshotLifecycle: WorkspaceSnapshotLifecycle {
    enabled: root.workspaceSnapshotsEnabled
    capability: root.shell && root.shell.workspaceCaptureState !== undefined
      ? root.shell.workspaceCaptureState
      : null
  }
  capabilities: DesktopCapabilities {
    globalShortcuts: true
    shortcutInventory: true
    windows: true
    windowPreviews: true
    workspaces: true
    workspaceLayouts: true
    workspaceSnapshots: root.workspaceSnapshotLifecycle.available
  }

  function refreshWindows() {
    Hyprland.refreshToplevels()
  }

  function refreshWorkspaces() {
    root.workspacePreviewBackend.refresh()
  }

  property Connections toplevelEvents: Connections {
    target: Hyprland.toplevels

    function onValuesChanged() {
      root.windowsChanged()
    }
  }
}
