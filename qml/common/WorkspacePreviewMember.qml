import QtQuick
import "WorkspacePreviewGeometry.js" as Geometry

Item {
  id: root
  required property var source
  required property var workspace
  property double acceptedAt: 0
  property double now: 0
  property int lifetime: 2000
  property var monitorRect: null
  readonly property var snapshot: source ? source.lastIpcObject : null
  readonly property bool member: !!source && source.workspace === workspace
    && !!snapshot && !!snapshot.workspace && snapshot.workspace.id === workspace.id
    && snapshot.workspace.name === workspace.name
    && source.monitor === workspace.monitor && snapshot.monitor === workspace.monitor.id
    && Geometry.address(snapshot.address) === Geometry.address(source.address)
  readonly property bool mapped: member && snapshot.mapped === true && snapshot.hidden === false
  readonly property var snapshotEntry: mapped && source.wayland && snapshot.size
    && workspace.monitor && workspace.monitor.scale > 0 ? ({
      source: source.wayland,
      label: String(snapshot.class || source.title || "Window").slice(0, 160),
      width: Math.ceil(snapshot.size[0] * workspace.monitor.scale),
      height: Math.ceil(snapshot.size[1] * workspace.monitor.scale)
    }) : null
  readonly property var descriptor: !mapped ? null : ({
    address: Geometry.address(source.address),
    label: String(snapshot.class || source.title || "Window").slice(0, 160),
    title: String(source.title || "").slice(0, 256),
    layer: snapshot.fullscreen > 0 ? 2 : snapshot.floating === true ? 1 : 0,
    rect: acceptedAt > 0 && Date.now() >= acceptedAt && now - acceptedAt < lifetime
      ? Geometry.windowRect(snapshot, monitorRect) : null
  })

  function invalidate() { acceptedAt = 0 }

  onSourceChanged: invalidate()
  onWorkspaceChanged: invalidate()
  Connections {
    target: root.source
    function onLastIpcObjectChanged() { root.acceptedAt = Date.now() }
    function onWorkspaceChanged() { root.invalidate() }
    function onMonitorChanged() { root.invalidate() }
    function onAddressChanged() { root.invalidate() }
  }
}
