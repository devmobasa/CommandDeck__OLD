import QtQuick

QtObject {
  property string integrationId: "generic"
  property DesktopCapabilities capabilities: DesktopCapabilities { }
  property var screens: []
  property var focusedMonitor: null
  property var focusedWorkspace: null
  property var activeWorkspace: null
  property var windowToplevels: []
  property var workspacePreviewBackend: null
  property var workspaceSnapshotLifecycle: null
  property bool workspaceSnapshotsEnabled: false

  signal windowsChanged()

  function refreshWindows() { }
  function refreshWorkspaces() { }
}
