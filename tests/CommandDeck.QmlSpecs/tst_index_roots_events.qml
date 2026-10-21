import QtQuick
import QtTest
import "../../qml/common/Protocol.js" as Protocol

EventPathFixture {
  id: scene
  EventPathTestCase {
    fixture: scene
    name: "CommandDeck_indexed_folders"

    SignalSpy {
      id: refreshes
      target: scene.part_settingsPanel
      signalName: "indexRootsRequested"
    }

    function roots() {
      return [
        { id: "configured", path: "/folders/configured", source: "configuration", enabled: true, status: "Watching" },
        { id: "added", path: "/folders/added", source: "added", enabled: true, status: "Watching" }
      ]
    }
    function focusByTab(control) {
      for (let index = 0; index < 80 && !control.activeFocus; index++) keyClick(Qt.Key_Tab)
      verify(control.activeFocus, "Indexed folder controls must be reachable by Tab")
    }
    function openCard() {
      const panel = fixture.part_settingsPanel
      panel.part_indexRootsController.roots = roots()
      panel.part_indexRootsController.reindexPending = false
      panel.part_indexRootsController.progress = null
      panel.part_indexRootsController.reindexError = ""
      panel.part_indexRootsController.error = ""
      panel.part_indexRootsController.available = false
      panel.part_indexRootsController.available = true
      panel.delayRootReads = false
      panel.rootChanges = []
      panel.part_indexRootsCard.pathControl.text = ""
      panel.visible = true
      nextTurn()
      waitForValue(panel.searchControl, "activeFocus", true)
      typeText("indexed folders")
      compare(panel.currentPage, "search")
      const card = panel.part_indexRootsCard
      waitForValue(card, "renderedCount", 2)
      focusByTab(card.pathControl)
      return card
    }
    function scrollIntoView(item) {
      const panel = fixture.part_settingsPanel
      const view = panel.parts.searchScroll.contentItem
      const top = item.mapToItem(view.contentItem, 0, 0).y
      view.contentY = Math.max(0, Math.min(top - 30, view.contentHeight - view.height))
      waitForFrame(panel)
    }

    function test_add_error_mouse_and_reopen() {
      const panel = fixture.part_settingsPanel
      const card = openCard()
      compare(card.pathControl.Accessible.name, "Add folder path")
      typeText("/missing")
      keyClick(Qt.Key_Return)
      compare(panel.rootChanges.length, 1)
      compare(panel.rootChanges[0].operation, Protocol.Operations.IndexAdd)
      compare(panel.rootChanges[0].path, "/missing")
      panel.part_indexRootsController.handleMessage({ requestId: panel.part_indexRootsController.activeRequestId,
        type: Protocol.Messages.Error, error: "Folder does not exist." })
      waitForValue(card.errorControl, "visible", true)
      compare(card.errorControl.text, "Folder does not exist.")
      verify(card.pathControl.activeFocus)
      keyClick(Qt.Key_A, Qt.ControlModifier)
      typeText("/valid")
      keyClick(Qt.Key_Tab)
      verify(card.addControl.activeFocus)
      keyClick(Qt.Key_Return)
      compare(panel.rootChanges.length, 2)
      compare(panel.rootChanges[1].path, "/valid")
      panel.part_indexRootsController.error = ""
      panel.respondRoots(panel.part_indexRootsController.activeRequestId,
        roots().concat([{ id: "new", path: "/valid", source: "added", enabled: true, status: "Watching" }]))
      waitForValue(card, "renderedCount", 3)
      verify(card.pathControl.activeFocus)
      scrollIntoView(card.addControl)
      mouseClick(card.addControl)
      typeText("x")
      compare(card.pathControl.text, "/validx")
      verify(card.pathControl.activeFocus)
      panel.respondRoots(panel.part_indexRootsController.activeRequestId, panel.part_indexRootsController.roots)
      keyClick(Qt.Key_Escape)
      keyClick(Qt.Key_Escape)
      compare(panel.dismissals, 1)
      panel.visible = false
      panel.visible = true
      nextTurn()
      waitForValue(panel.searchControl, "activeFocus", true)
      panel.showIndexedFolders()
      waitForValue(card.pathControl, "activeFocus", true)
      panel.visible = false
    }

    function test_validation_error_survives_queued_remove_and_polling_until_edit() {
      const panel = fixture.part_settingsPanel
      const card = openCard()
      const controller = panel.part_indexRootsController
      typeText("/missing")
      keyClick(Qt.Key_Return)
      const add = controller.activeRequestId
      focusByTab(card.actionControlAt(1))
      keyClick(Qt.Key_Return)
      compare(controller.pendingChanges.length, 1)
      controller.handleMessage({ requestId: add, type: Protocol.Messages.Error, error: "Folder does not exist." })
      compare(panel.rootChanges[1].operation, Protocol.Operations.IndexRemove)
      compare(card.errorControl.text, "Folder does not exist.")
      verify(card.errorControl.visible)
      panel.respondRoots(controller.activeRequestId, [roots()[0]])
      waitForValue(card.pathControl, "activeFocus", true)
      verify(card.errorControl.visible)
      compare(card.errorControl.text, "Folder does not exist.")

      panel.delayRootReads = true
      panel.indexRootsRequested()
      verify(card.errorControl.visible)
      panel.respondRoots(controller.activeRequestId, [roots()[0]])
      compare(card.errorControl.text, "Folder does not exist.")
      verify(card.errorControl.visible)
      typeText("x")
      compare(card.pathControl.text, "/missingx")
      waitForValue(card.errorControl, "visible", false)
      panel.delayRootReads = false
      panel.visible = false
    }

    function test_disable_enable_replacement_remove_and_typing() {
      const panel = fixture.part_settingsPanel
      const card = openCard()
      keyClick(Qt.Key_Tab)
      keyClick(Qt.Key_Tab)
      verify(card.actionControlAt(0).activeFocus)
      compare(card.actionControlAt(0).text, "Disable")
      keyClick(Qt.Key_Space)
      compare(panel.rootChanges[0].operation, Protocol.Operations.IndexDisable)
      const disabled = roots()
      disabled[0].enabled = false
      disabled[0].status = "Disabled"
      panel.respondRoots(panel.part_indexRootsController.activeRequestId, disabled)
      waitForCondition(() => card.actionControlAt(0).activeFocus)
      compare(card.actionControlAt(0).text, "Enable")
      keyClick(Qt.Key_Return)
      compare(panel.rootChanges[1].operation, Protocol.Operations.IndexEnable)
      panel.respondRoots(panel.part_indexRootsController.activeRequestId, roots().reverse())
      waitForCondition(() => card.actionControlAt(1).activeFocus)
      keyClick(Qt.Key_Up)
      verify(card.actionControlAt(0).activeFocus)
      compare(card.actionControlAt(0).text, "Remove")
      keyClick(Qt.Key_Return)
      compare(panel.rootChanges[2].operation, Protocol.Operations.IndexRemove)
      compare(panel.rootChanges[2].rootId, "added")
      panel.respondRoots(panel.part_indexRootsController.activeRequestId, [roots()[0]])
      waitForValue(card.pathControl, "activeFocus", true)
      typeText("/next")
      scrollIntoView(card.actionControlAt(0))
      mouseClick(card.actionControlAt(0))
      typeText("x")
      verify(card.pathControl.activeFocus)
      compare(card.pathControl.text, "/nextx")
      focusByTab(card.actionControlAt(0))
      keyClick(Qt.Key_Backtab)
      verify(card.addControl.activeFocus)
      keyClick(Qt.Key_Backtab)
      verify(card.pathControl.activeFocus)
      panel.visible = false
    }


    function test_actions_supersede_delayed_progress_reads() {
      const panel = fixture.part_settingsPanel
      const card = openCard()
      const controller = panel.part_indexRootsController
      const cases = [Protocol.Operations.IndexDisable, Protocol.Operations.IndexEnable,
        Protocol.Operations.IndexRemove, Protocol.Operations.IndexAdd]
      for (let index = 0; index < cases.length; index++) {
        const rows = roots()
        rows[0].enabled = index !== 1
        controller.roots = rows
        nextTurn()
        panel.delayRootReads = true
        panel.indexRootsRequested()
        const read = controller.activeRequestId
        const before = panel.rootChanges.length
        if (index === 3) {
          focusByTab(card.pathControl)
          typeText("/queued-folder")
          keyClick(Qt.Key_Return)
        } else {
          focusByTab(card.actionControlAt(index === 2 ? 1 : 0))
          keyClick(Qt.Key_Space)
        }
        compare(panel.rootChanges.length, before + 1, "Activation during a delayed poll must reach the real controller")
        compare(panel.rootChanges[before].operation, cases[index])
        const mutation = controller.activeRequestId
        verify(mutation !== read)
        verify(!controller.handleMessage({ requestId: read, type: Protocol.Messages.IndexRootsResult,
          indexRoots: { roots: [] } }))
        panel.respondRoots(mutation, rows)
      }
      panel.delayRootReads = false
      panel.visible = false
    }

    function test_full_reindex_progress_without_enabled_roots_keeps_typing_ready() {
      const panel = fixture.part_settingsPanel
      const card = openCard()
      const controller = panel.part_indexRootsController
      controller.roots = [{ id: "configured", path: "/folders/configured", source: "configuration", enabled: false, status: "Disabled" }]
      controller.reindexPending = true
      controller.progress = { stage: "write", indexed: 27 }
      refreshes.clear()
      typeText("/another-folder")
      waitForCondition(() => refreshes.count > 0, 3000)
      verify(card.progressControl.visible)
      compare(card.progressControl.text, "Reindexing · 27 items")
      verify(card.pathControl.activeFocus)
      compare(card.pathControl.text, "/another-folder")

      controller.reindexError = "scan failed"
      compare(card.progressControl.text, "Retrying indexing: scan failed")
      typeText("x")
      compare(card.pathControl.text, "/another-folderx")
      controller.reindexPending = false
      controller.reindexError = ""
      verify(!card.progressControl.visible)
      const completed = refreshes.count
      observeFor(1200, () => refreshes.count === completed, "completed scan stops polling")
      panel.visible = false
    }

    function test_background_progress_refresh_preserves_editing_and_row_focus() {
      const panel = fixture.part_settingsPanel
      const card = openCard()
      const indexing = roots()
      indexing[0].status = "Indexing · 0 items"
      panel.part_indexRootsController.roots = indexing
      refreshes.clear()
      typeText("/next-folder")
      waitForCondition(() => refreshes.count > 0, 3000)
      verify(card.pathControl.activeFocus)
      compare(card.pathControl.text, "/next-folder")

      focusByTab(card.actionControlAt(0))
      const updated = roots()
      updated[0].status = "Indexing · 5 items"
      panel.part_indexRootsController.roots = updated
      waitForCondition(() => card.actionControlAt(0).activeFocus)
      keyClick(Qt.Key_Backtab)
      verify(card.addControl.activeFocus)
      keyClick(Qt.Key_Backtab)
      verify(card.pathControl.activeFocus)
      typeText("x")
      compare(card.pathControl.text, "/next-folderx")

      panel.part_indexRootsController.roots = roots()
      const completeCount = refreshes.count
      observeFor(1200, () => refreshes.count === completeCount, "completed scan stops polling")
      panel.part_indexRootsController.roots = indexing
      panel.visible = false
      const closedCount = refreshes.count
      observeFor(1200, () => refreshes.count === closedCount, "closed settings stop polling")
    }
  }
}
