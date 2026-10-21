import QtQuick
import QtTest
import "../../qml/common" as Deck
import "../../qml/common/WorkspacePreviewGeometry.js" as Geometry
import "../../qml/common/WorkspacePreviewPolicy.js" as Policy
import "../../qml/common/Protocol.js" as Protocol

Item {
  id: scene
  width: 700
  height: 500
  readonly property var testMonitor: monitor
  readonly property var testWorkspace: workspace
  Deck.Theme { id: theme; loadShellTokens: false }
  ListModel { id: rows }
  Deck.SearchNavigationController { id: navigation; rows: rows }
  Deck.CommandDeckSearchField {
    id: search
    width: parent.width
    theme: theme
    controller: navigation
    surfaceVisible: preview.eligible
    onSurfaceVisibleChanged: if (surfaceVisible) navigation.focusRevision++
  }
  Deck.WorkspacePreview {
    id: preview
    y: 100
    width: 500
    theme: theme
    backend: backend
    issuedRequestId: 1
    resultRequestId: 1
  }
  QtObject {
    id: backend
    property var workspaces: [workspace]
    property int requests: 0
    signal invalidated()
    function refresh() { requests++ }
  }
  QtObject {
    id: monitor
    property int id: 0
    property string name: "DP-1"
    property real scale: 1.25
    property int x: -1536
    property int y: 0
    property int width: 1920
    property int height: 1080
    property var lastIpcObject: ({})
  }
  QtObject {
    id: workspace
    property int id: -1337
    property string name: "named"
    property var monitor: scene.testMonitor
    property var toplevels: ({ values: [client] })
  }
  QtObject {
    id: otherWorkspace
    property int id: 2
    property string name: "2"
    property var monitor: scene.testMonitor
    property var toplevels: ({ values: [] })
  }
  QtObject {
    id: replacement
    property int id: -1337
    property string name: "named"
    property var monitor: scene.testMonitor
    property var toplevels: ({ values: [client] })
  }
  QtObject {
    id: client
    property string address: "0x123"
    property string title: "<b>Synthetic title</b>"
    property var workspace: scene.testWorkspace
    property var monitor: scene.testMonitor
    property var lastIpcObject: ({})
  }

  DeadlineTestCase {
    name: "WorkspacePreview"
    when: windowShown

    function result() {
      return { id: "workspace:-1337", providerId: Protocol.ProviderIds.Hyprland, sourceId: "windows-workspaces",
        kind: "workspace", metadata: { workspaceId: "-1337", workspaceName: "named",
          monitorName: "DP-1", workspaceKind: "ordinary" } }
    }
    function preferences(mode, home, searchEnabled) {
      return { workspacePreviewMode: mode, sources: [{ sourceId: "windows-workspaces",
        enabledOnHome: home, enabledInSearch: searchEnabled }] }
    }
    function snapshot() {
      return { workspace: { id: -1337, name: "named" }, monitor: 0, address: "0x123", class: "Synthetic",
        mapped: true, hidden: false, at: [-1536, 0], size: [768, 432], fullscreen: 0, floating: false }
    }
    function refreshMetadata() {
      monitor.lastIpcObject = { x: -1536, y: 0, width: 1920, height: 1080, scale: 1.25, transform: 0 }
      client.lastIpcObject = snapshot()
    }
    function init() {
      preview.eligible = false
      backend.workspaces = [workspace]
      backend.requests = 0
      workspace.monitor = monitor
      workspace.toplevels = { values: [client] }
      client.workspace = workspace
      monitor.name = "DP-1"
      preview.issuedRequestId += 10
      preview.resultRequestId = preview.issuedRequestId
      preview.result = result()
      preview.mode = "layout"
      navigation.clear()
      preview.eligible = true
      waitForValue(search.inputControl, "activeFocus", true)
      waitForValue(preview.model, "totalCount", 1)
      refreshMetadata()
      waitForValue(preview.model, "layoutAvailable", true)
    }
    function cleanup() { preview.eligible = false }

    function test_fallback_uses_available_width_for_one_or_two_windows() {
      backend.invalidated()
      compare(preview.model.layoutAvailable, false)
      const canvas = findChild(preview, "workspacePreviewCanvas")
      const first = findChild(preview, "workspacePreviewOutline0")
      verify(canvas !== null)
      verify(first !== null)
      compare(first.width, canvas.width)

      workspace.toplevels = { values: [client, client] }
      waitForValue(preview.model, "totalCount", 2)
      compare(findChild(preview, "workspacePreviewOutline0").width, canvas.width / 2)
      const second = findChild(preview, "workspacePreviewOutline1")
      verify(second !== null)
      compare(second.x + second.width, canvas.width)
    }

    function test_logical_geometry_and_clipping() {
      for (let transform = 0; transform < 8; transform++) {
        const rect = Geometry.monitorRect({ x: -1536, y: -100, width: 1920, height: 1080, scale: 1.25, transform: transform })
        compare(rect.width, transform % 2 ? 864 : 1536)
        compare(rect.height, transform % 2 ? 1536 : 864)
      }
      const monitorRect = Geometry.monitorRect({ x: -100, y: 0, width: 1000, height: 1000, scale: 1, transform: 0 })
      const clipped = Geometry.windowRect({ at: [-200, -100], size: [600, 600] }, monitorRect)
      compare(clipped.x, 0)
      compare(clipped.width, 0.5)
      compare(clipped.height, 0.5)
      compare(Geometry.windowRect({ at: [10000, 0], size: [5, 5] }, monitorRect), null)
      compare(Geometry.monitorRect({ width: Infinity }), null)
      compare(Geometry.windowRect({ at: [0, 0], size: [-1, 5] }, monitorRect), null)
    }

    function test_expiry_without_notification_and_one_refresh_per_open() {
      waitForValue(backend, "requests", 1)
      waitForValue(preview.model, "layoutAvailable", false, 2300)
      compare(preview.model.status, "Layout unavailable")
      compare(preview.model.descriptors.length, 1)
      compare(backend.requests, 1)
      refreshMetadata()
      waitForValue(preview.model, "layoutAvailable", true)
      compare(backend.requests, 1)
      backend.invalidated()
      compare(preview.model.layoutAvailable, false)
    }

    function test_idle_layout_recomputes_only_when_it_expires() {
      // Capture the transition times rather than requiring the test thread to
      // wake before expiry on a busy machine.
      refreshMetadata()
      verify(preview.model.layoutAvailable)
      const started = Date.now()
      const recomputations = []
      let invalidatedAt = 0
      const count = () => recomputations.push(Date.now() - started)
      const invalidated = () => { if (!preview.model.layoutAvailable) invalidatedAt = Date.now() - started }
      preview.model.descriptorsChanged.connect(count)
      preview.model.layoutAvailableChanged.connect(invalidated)
      try {
        waitForValue(preview.model, "layoutAvailable", false)
        verify(invalidatedAt >= 700, "geometry stays valid for at least 700 ms; expired at " + invalidatedAt)
        compare(recomputations.filter(elapsed => elapsed < 700).length, 0,
          "an idle layout preview does not recompute its windows before expiry")
        compare(recomputations.length, 1, "an idle layout recomputes once at expiry, without polling")
        compare(preview.model.status, "Layout unavailable")
      } finally {
        preview.model.descriptorsChanged.disconnect(count)
        preview.model.layoutAvailableChanged.disconnect(invalidated)
      }
    }

    function test_recycled_workspace_requires_search_started_after_removal() {
      backend.workspaces = []
      waitForValue(preview.model, "workspace", null)
      backend.workspaces = [replacement]
      client.workspace = replacement
      preview.result = result()
      compare(preview.model.workspace, null)
      preview.issuedRequestId++
      compare(preview.model.workspace, null)
      preview.resultRequestId = preview.issuedRequestId
      waitForValue(preview.model, "workspace", replacement)
      refreshMetadata()
      waitForValue(preview.model, "layoutAvailable", true)
    }

    function test_recycled_workspace_fence_survives_navigation_and_reopen() {
      const oldResult = result()
      const oldRequest = preview.resultRequestId
      const pendingRequest = ++preview.issuedRequestId
      backend.workspaces = []
      waitForValue(preview.model, "workspace", null)
      backend.workspaces = [replacement]
      client.workspace = replacement

      const otherResult = result()
      otherResult.id = "workspace:2"
      otherResult.metadata.workspaceId = "2"
      otherResult.metadata.workspaceName = "2"
      preview.result = otherResult
      preview.result = oldResult
      compare(preview.resultRequestId, oldRequest)
      compare(preview.model.workspace, null)
      compare(preview.model.descriptors.length, 0)

      preview.eligible = false
      preview.result = { id: "app:notes", kind: "application" }
      preview.result = oldResult
      preview.eligible = true
      refreshMetadata()
      compare(preview.model.workspace, null)
      compare(preview.model.status, "Workspace unavailable")

      preview.resultRequestId = pendingRequest
      compare(preview.model.workspace, null)

      preview.issuedRequestId++
      preview.result = result()
      compare(preview.model.workspace, null)
      preview.resultRequestId = preview.issuedRequestId
      waitForValue(preview.model, "workspace", replacement)
      refreshMetadata()
      waitForValue(preview.model, "layoutAvailable", true)
    }

    function test_unselected_workspace_removal_invalidates_old_results() {
      backend.workspaces = [workspace, otherWorkspace]
      preview.issuedRequestId++
      preview.resultRequestId = preview.issuedRequestId
      const otherResult = result()
      otherResult.id = "workspace:2"
      otherResult.metadata.workspaceId = "2"
      otherResult.metadata.workspaceName = "2"
      preview.result = otherResult
      waitForValue(preview.model, "workspace", otherWorkspace)

      backend.workspaces = [otherWorkspace]
      backend.workspaces = [replacement, otherWorkspace]
      client.workspace = replacement
      preview.result = result()
      compare(preview.model.workspace, null)
      compare(preview.model.descriptors.length, 0)

      preview.issuedRequestId++
      preview.resultRequestId = preview.issuedRequestId
      waitForValue(preview.model, "workspace", replacement)
    }

    function test_special_missing_and_conflicting_identity() {
      const special = result()
      special.metadata.workspaceKind = "special"
      preview.result = special
      compare(preview.model.workspace, null)
      compare(preview.model.status, "Workspace unavailable")
      preview.result = { kind: "workspace", metadata: {} }
      compare(preview.model.workspace, null)
      const wrong = result()
      wrong.metadata.monitorName = "DP-2"
      preview.result = wrong
      compare(preview.model.workspace, null)
    }

    function test_inventory_bounds_hidden_members_resize_and_monitor_move() {
      workspace.toplevels = { values: Array(100).fill(client) }
      waitForValue(preview.model, "totalCount", 100)
      compare(preview.model.descriptors.length, 64)
      compare(preview.model.overflow, 76)
      const hidden = snapshot()
      hidden.hidden = true
      client.lastIpcObject = hidden
      waitForValue(preview.model, "descriptors", [])
      workspace.toplevels = { values: [client] }
      refreshMetadata()
      waitForValue(preview.model, "layoutAvailable", true)
      const resized = snapshot()
      resized.size = [384, 216]
      client.lastIpcObject = resized
      compare(preview.model.descriptors[0].rect.width, 0.25)
      monitor.name = "DP-2"
      waitForValue(preview.model, "workspace", null)
      compare(preview.model.status, "Workspace unavailable")
    }

    function test_policy_uses_acknowledged_consent_and_route() {
      const snapshots = preferences("snapshots", true, true)
      const layout = preferences("layout", true, true)
      compare(Policy.displayMode(snapshots, snapshots), "snapshots")
      compare(Policy.displayMode(snapshots, layout), "layout")
      compare(Policy.displayMode(layout, snapshots), "layout")
      compare(Policy.displayMode(snapshots, preferences("off", true, true)), "off")
      const homeOnly = preferences("layout", true, false)
      const searchOnly = preferences("layout", false, true)
      verify(Policy.eligible(result(), homeOnly, homeOnly, true))
      verify(!Policy.eligible(result(), homeOnly, homeOnly, false))
      verify(Policy.eligible(result(), searchOnly, searchOnly, false))
      verify(!Policy.eligible(result(), searchOnly, searchOnly, true))
      verify(!Policy.eligible(result(), null, homeOnly, true))
      verify(!Policy.eligible(result(), preferences("off", true, true), homeOnly, true))
      verify(!Policy.eligible(result(), homeOnly, preferences("off", true, true), true))
      verify(!Policy.eligible(result(), homeOnly, searchOnly, true))
      const spoof = result()
      spoof.providerId = "extension"
      verify(!Policy.eligible(spoof, homeOnly, homeOnly, true))
      const descriptors = [
        { id: "workspaces", providerId: Protocol.ProviderIds.Hyprland, sourceId: "windows-workspaces", kind: "workspace" },
        { id: "windows", providerId: Protocol.ProviderIds.Hyprland, sourceId: "windows-workspaces", kind: "window" },
        { id: "files", sourceId: "files", kind: "file" }
      ]
      verify(!Policy.filtersAllowWorkspace([{ id: "workspaces", excluded: true }], descriptors))
      verify(Policy.filtersAllowWorkspace([{ id: "windows", excluded: true }], descriptors))
      verify(!Policy.filtersAllowWorkspace([{ id: "files" }], descriptors))
      verify(Policy.filtersAllowWorkspace([{ id: "windows" }, { id: "workspaces" }], descriptors))
      preview.mode = "snapshots"
      verify(preview.model.layoutAvailable)
    }

    function test_preview_click_type_close_and_reopen() {
      mouseClick(preview, 80, 80)
      keyClick(Qt.Key_W)
      keyClick(Qt.Key_S)
      keyClick(Qt.Key_Colon)
      compare(navigation.queryText, "ws:")
      verify(search.inputControl.activeFocus)
      preview.eligible = false
      compare(preview.model.descriptors.length, 0)
      const requests = backend.requests
      observeFor(150, () => backend.requests === requests, "ineligible previews do not refresh")
      compare(backend.requests, requests)
      preview.eligible = true
      waitForValue(search.inputControl, "activeFocus", true)
      compare(preview.model.layoutAvailable, false)
      keyClick(Qt.Key_A)
      compare(navigation.queryText, "ws:a")
    }
  }
}
