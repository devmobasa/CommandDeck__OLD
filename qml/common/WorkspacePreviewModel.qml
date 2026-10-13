pragma ComponentBehavior: Bound
import QtQuick
import "UiConstants.js" as Ui
import "WorkspacePreviewGeometry.js" as Geometry
import "WorkspacePreviewPolicy.js" as Policy

Item {
  id: root
  property var backend: null
  property var result: null
  property bool eligible: false
  property int issuedRequestId: 0
  property int resultRequestId: 0
  // A removal invalidates this result batch, not just the current selection.
  // Keep one bounded, monotonic fence until a newer search response arrives.
  property int invalidBeforeRequestId: -1
  property var retainedWorkspace: null
  property var retainedMonitor: null
  property string retainedIdentity: ""
  property double monitorAcceptedAt: 0
  property string monitorShape: ""
  // Reported geometry is trusted this long. `now` moves only when geometry
  // arrives or the next one expires, so an idle preview does no work.
  readonly property int geometryLifetime: 2000
  property double now: Date.now()
  readonly property var identity: Policy.identity(result)
  readonly property var candidate: resolveWorkspace()
  property var workspace: null
  readonly property var monitor: workspace ? workspace.monitor : null
  readonly property var members: workspace && workspace.toplevels
    ? workspace.toplevels.values.slice(0, 64) : []
  readonly property int totalCount: workspace && workspace.toplevels ? workspace.toplevels.values.length : 0
  readonly property var monitorGeometry: monitor && monitorAcceptedAt > 0
    && now - monitorAcceptedAt < geometryLifetime && now >= monitorAcceptedAt
    ? Geometry.monitorRect(monitor.lastIpcObject) : null
  readonly property var descriptors: collect()
  readonly property var snapshotEntries: collectSnapshots()
  readonly property bool layoutAvailable: !!monitorGeometry && descriptors.length > 0
    && descriptors.every(value => value.rect !== null)
  readonly property int overflow: Math.max(0, totalCount - Math.min(24, descriptors.length))
  readonly property string status: !workspace ? "Workspace unavailable"
    : totalCount === 0 ? "No windows" : layoutAvailable ? "Layout preview" : "Layout unavailable"

  function resolveWorkspace() {
    if (!eligible || !backend || !identity || identity.kind !== Ui.WorkspaceIdentityKind.Ordinary) return null
    const values = backend.workspaces || []
    for (let index = 0; index < values.length; index++) {
      const value = values[index]
      if (value.id === identity.id && value.name === identity.name && value.monitor
          && value.monitor.name === identity.monitor) return value
    }
    return null
  }

  function collect() {
    const values = []
    for (let index = 0; index < observers.count; index++) {
      const item = observers.itemAt(index) as WorkspacePreviewMember
      if (item && item.descriptor && item.descriptor.address) values.push(item.descriptor)
    }
    return Geometry.order(values)
  }

  function collectSnapshots() {
    const values = []
    for (let index = 0; index < observers.count; index++) {
      const item = observers.itemAt(index) as WorkspacePreviewMember
      if (item && item.snapshotEntry) values.push(item.snapshotEntry)
    }
    return values
  }

  function clearGeometry() {
    monitorAcceptedAt = 0
    for (let index = 0; index < observers.count; index++) {
      const item = observers.itemAt(index) as WorkspacePreviewMember
      if (item) item.invalidate()
    }
    scheduleExpiry()
  }

  // Arms one timer for the earliest geometry that is still valid, instead
  // of polling the clock while a preview shows.
  function scheduleExpiry() {
    let next = 0
    const consider = acceptedAt => {
      const expiry = acceptedAt + geometryLifetime
      if (acceptedAt > 0 && expiry > now && (next === 0 || expiry < next)) next = expiry
    }
    consider(monitorAcceptedAt)
    for (let index = 0; index < observers.count; index++) {
      const item = observers.itemAt(index) as WorkspacePreviewMember
      if (item) consider(item.acceptedAt)
    }
    if (next === 0 || !eligible || !workspace) {
      expiry.stop()
      return
    }

    // A coarse timer may fire a little early; the handler then re-arms.
    expiry.interval = Math.max(1, next - Date.now())
    expiry.restart()
  }

  function begin() {
    clearGeometry()
    settle.stop()
    if (eligible && workspace) settle.restart()
  }

  function checkIdentity() {
    const key = JSON.stringify(identity)
    if (key !== retainedIdentity) {
      retainedIdentity = key
      retainedWorkspace = null
      retainedMonitor = null
    }
    if (retainedWorkspace && eligible
        && (candidate !== retainedWorkspace || !candidate || candidate.monitor !== retainedMonitor)) {
      invalidBeforeRequestId = Math.max(invalidBeforeRequestId, issuedRequestId)
      retainedWorkspace = null
      retainedMonitor = null
      clearGeometry()
    }
    workspace = eligible && resultRequestId > invalidBeforeRequestId ? candidate : null
    if (workspace) {
      retainedWorkspace = workspace
      retainedMonitor = workspace.monitor
    }
  }

  onEligibleChanged: {
    if (!eligible) {
      retainedWorkspace = null
      retainedMonitor = null
    }
    checkIdentity()
    begin()
  }
  onCandidateChanged: checkIdentity()
  onResultRequestIdChanged: checkIdentity()
  onWorkspaceChanged: begin()
  onMonitorChanged: {
    checkIdentity()
    clearGeometry()
  }

  Repeater {
    id: observers
    model: root.members
    delegate: WorkspacePreviewMember {
      required property var modelData
      source: modelData
      workspace: root.workspace
      now: root.now
      lifetime: root.geometryLifetime
      monitorRect: root.monitorGeometry
      onAcceptedAtChanged: root.scheduleExpiry()
    }
  }

  Connections {
    target: root.monitor
    function onLastIpcObjectChanged() {
      const value = root.monitor.lastIpcObject
      const shape = JSON.stringify([value.x, value.y, value.width, value.height, value.scale, value.transform])
      if (root.monitorShape.length > 0 && root.monitorShape !== shape) root.clearGeometry()
      root.monitorShape = shape
      root.monitorAcceptedAt = Date.now()
      root.now = root.monitorAcceptedAt
      root.scheduleExpiry()
    }
    function onNameChanged() { root.checkIdentity(); root.clearGeometry() }
    function onScaleChanged() { root.clearGeometry() }
    function onXChanged() { root.clearGeometry() }
    function onYChanged() { root.clearGeometry() }
    function onWidthChanged() { root.clearGeometry() }
    function onHeightChanged() { root.clearGeometry() }
  }

  Connections {
    target: root.backend
    function onInvalidated() {
      root.clearGeometry()
      if (!root.eligible) root.invalidBeforeRequestId = Math.max(root.invalidBeforeRequestId, root.issuedRequestId)
    }
    function onWorkspacesChanged() {
      // Inventory changes can remove an unselected workspace and reuse its ID.
      // Invalidate the whole batch, including additions, without retaining a
      // second inventory of native objects that may already have been destroyed.
      root.invalidBeforeRequestId = Math.max(root.invalidBeforeRequestId, root.issuedRequestId)
      root.checkIdentity()
    }
  }

  Timer {
    id: settle
    interval: 100
    onTriggered: {
      if (root.eligible && root.workspace && root.backend) root.backend.refresh()
    }
  }
  Timer {
    id: expiry
    onTriggered: {
      root.now = Date.now()
      root.scheduleExpiry()
    }
  }
}
