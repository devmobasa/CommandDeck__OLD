import QtQuick
import QtTest
import "../../qml/common" as Deck

Item {
  id: scene
  width: 700
  height: 400
  property int created: 0
  property int destroyed: 0
  QtObject { id: sourceA }
  QtObject { id: sourceB }
  Deck.Theme { id: theme; loadShellTokens: false }
  Component {
    id: fakeCapture
    Item {
      property var source: null
      property bool permitted: false
      readonly property bool hasContent: permitted && !!source
      readonly property bool expired: false
      Component.onCompleted: scene.created++
      Component.onDestruction: scene.destroyed++
    }
  }
  Deck.WorkspaceSnapshotGrid {
    id: grid
    anchors.fill: parent
    theme: theme
    captureComponent: fakeCapture
  }
  DeadlineTestCase {
    name: "WorkspaceSnapshotGrid"
    when: windowShown
    function entry(source, width, height) {
      return { source: source, label: "Synthetic", width: width || 640, height: height || 360 }
    }
    function init() {
      grid.permitted = false
      grid.epoch = "session:1"
      grid.selectionKey = "workspace:1"
      grid.entries = [entry(sourceA)]
      waitForValue(grid, "count", 0)
      waitForValue(scene, "destroyed", scene.created)
      scene.created = 0
      scene.destroyed = 0
    }
    function cleanup() { grid.permitted = false; waitForValue(scene, "destroyed", scene.created) }
    function test_unknown_or_blocked_never_allocates() {
      observeFor(200, () => scene.created === 0, "blocked snapshots allocate nothing")
      compare(scene.created, 0)
      grid.epoch = ""
      grid.permitted = true
      observeFor(200, () => scene.created === 0, "blocked snapshots allocate nothing")
      compare(scene.created, 0)
    }
    function test_one_window_fills_grid_and_four_window_limit() {
      grid.permitted = true
      waitForValue(grid, "count", 1)
      const first = findChild(grid, "workspaceSnapshotCell0")
      compare(first.width, grid.width)
      compare(first.height, grid.height)
      grid.entries = Array(64).fill(entry(sourceA))
      compare(grid.count, 0)
      waitForValue(grid, "count", 4)
      compare(scene.created - scene.destroyed, 4)
    }
    function test_source_pixel_budget_and_invalid_dimensions() {
      grid.entries = [entry(sourceA, 8192, 8192), entry(sourceA, -1, 32),
        entry(sourceA, 4096, 2048), entry(sourceB, 4096, 2048), entry(sourceA)]
      grid.permitted = true
      waitForValue(grid, "count", 2)
      compare(scene.created - scene.destroyed, 2)
    }
    function test_lock_disconnect_dismissal_and_epoch_clear_synchronously() {
      grid.permitted = true
      waitForValue(grid, "count", 1)
      grid.permitted = false
      compare(grid.count, 0)
      waitForValue(scene, "destroyed", scene.created)
      grid.permitted = true
      waitForValue(grid, "count", 1)
      grid.epoch = "session:2"
      compare(grid.count, 0)
      waitForValue(grid, "count", 1)
      grid.epoch = ""
      compare(grid.count, 0)
      waitForValue(scene, "destroyed", scene.created)
    }
    function test_selection_recreation_and_pending_settle_cannot_keep_old_source() {
      grid.permitted = true
      grid.selectionKey = "workspace:2"
      grid.entries = [entry(sourceB)]
      waitForValue(grid, "count", 1)
      compare(findChild(grid, "workspaceSnapshotCell0").capture.source, sourceB)
      grid.entries = []
      compare(grid.count, 0)
      grid.entries = [entry(sourceA)]
      compare(grid.count, 0)
      waitForValue(grid, "count", 1)
      compare(findChild(grid, "workspaceSnapshotCell0").capture.source, sourceA)
    }
  }
}
