import QtQuick
import QtTest
import "../../qml/common" as CommandDeck
import "../../qml/common/UiConstants.js" as Ui

// Settings → Search sources with real pointer and key events: dragging a source
// by its grip to a new place, a drag that goes nowhere, the arrows, the
// keyboard grid and its one Tab stop, focus following a moved source, dimmed
// hidden sources, the column headings, and the reason shown when a save is
// rejected.
EventPathFixture {
  id: scene

  SignalSpy { id: moveSpy; signalName: "moveRequested" }

  EventPathTestCase {
    fixture: scene
    name: "CommandDeck_settings_sources"

    property var originalSnapshot: null
    property var originalDescriptors: null

    function sourceIds(preferences) {
      return preferences.sources.map(source => source.sourceId).join(",")
    }

    function openSources() {
      const panel = fixture.part_settingsPanel
      originalSnapshot = panel.snapshot
      const snapshot = JSON.parse(JSON.stringify(panel.snapshot))
      snapshot.preferences.sources = ["web-search", "applications", "files", "windows-workspaces"]
        .map((sourceId, index) => ({ sourceId: sourceId, enabledInSearch: true, enabledOnHome: true, position: index + 1 }))
      panel.snapshot = snapshot
      panel.changedPreferences = null
      panel.visible = true
      waitForValue(panel.searchControl, "activeFocus", true)
      panel.showPage("search")
      waitForFrame(panel)
      compare(panel.renderedSourceCount, 4)
      scrollIntoView(panel.part_sourceList)
      return panel
    }

    function scrollingPage(item) {
      let page = item.parent
      while (page && page.contentY === undefined) page = page.parent
      return page
    }

    // Settings pages scroll; bring the list into view before pointing at it.
    function scrollIntoView(item) {
      const page = scrollingPage(item)
      verify(page !== null, "the list sits in a scrolling page")
      const top = item.mapToItem(page.contentItem, 0, 0).y
      page.contentY = Math.max(0, Math.min(top - 20, page.contentHeight - page.height))
      waitForFrame(fixture.part_settingsPanel)
    }

    function cleanup() {
      if (originalSnapshot !== null) fixture.part_settingsPanel.snapshot = originalSnapshot
      originalSnapshot = null
      if (originalDescriptors !== null) fixture.part_settingsPanel.sourceDescriptors = originalDescriptors
      originalDescriptors = null
      fixture.part_settingsPanel.preferencesSaveError = ""
      // The grid remembers its last cell; start each test from the first one.
      fixture.part_settingsPanel.part_sourceList.currentSourceId = ""
      fixture.part_settingsPanel.part_sourceList.currentColumn = Ui.SourceGridColumn.Search
      fixture.part_settingsPanel.visible = false
      scene.width = 900
    }

    function test_dragging_a_source_moves_it_in_one_save() {
      const panel = openSources()
      const list = panel.part_sourceList
      const grip = panel.renderedSourceAt(3).dragControl
      const target = panel.renderedSourceAt(1)
      const x = grip.width / 2
      const start = grip.height / 2
      const end = target.mapToItem(grip, 0, target.height / 4).y

      mousePress(grip, x, start)
      mouseMove(grip, x, (start + end) / 2)
      mouseMove(grip, x, end)
      compare(list.dragIndex, 3)
      compare(list.dropGap, 1)
      verify(list.dropChangesOrder, "a line marks where the source will land")
      verify(panel.renderedSourceAt(3).dragging)
      mouseRelease(grip, x, end)

      verify(panel.changedPreferences !== null)
      compare(sourceIds(panel.changedPreferences), "web-search,windows-workspaces,applications,files")
      compare(panel.changedPreferences.sources.map(source => source.position).join(","), "1,2,3,4")
      compare(list.dragIndex, -1)
    }

    // Scrolls so the given row's top sits `offset` pixels below the page top.
    function placeRow(page, row, offset) {
      page.contentY = row.mapToItem(page.contentItem, 0, 0).y - offset
      verify(page.contentY >= 0 && page.contentY <= page.contentHeight - page.height, "the page can scroll there")
      waitForFrame(fixture.part_settingsPanel)
    }

    function test_holding_a_row_still_does_not_scroll_the_page() {
      const panel = openSources()
      const page = scrollingPage(panel.part_sourceList)
      const grip = panel.renderedSourceAt(3).dragControl

      // The list's top is above the viewport and the row is well inside it.
      placeRow(page, panel.renderedSourceAt(3), 100)
      verify(panel.part_sourceList.mapToItem(page, 0, 0).y < 0)
      const middle = page.contentY
      mousePress(grip, grip.width / 2, grip.height / 2)
      observeFor(150, () => page.contentY === middle, "a stationary middle press does not scroll")
      compare(page.contentY, middle, "a press in the middle of the page leaves it where it is")
      mouseRelease(grip, grip.width / 2, grip.height / 2)

      // Even a row inside the edge zone waits for the pointer to move.
      const edgeGrip = panel.renderedSourceAt(1).dragControl
      placeRow(page, panel.renderedSourceAt(1), 4)
      const edge = page.contentY
      mousePress(edgeGrip, edgeGrip.width / 2, edgeGrip.height / 2)
      observeFor(150, () => page.contentY === edge, "a stationary edge press does not scroll")
      compare(page.contentY, edge, "a click near the edge is not a drag")

      const dragY = edgeGrip.height / 2 - edgeGrip.drag.threshold - 4
      mouseMove(edgeGrip, edgeGrip.width / 2, dragY)
      waitForCondition(() => page.contentY < edge, 2000, "a real drag near the edge scrolls")
      mouseRelease(edgeGrip, edgeGrip.width / 2, dragY)
      compare(panel.part_sourceList.dragIndex, -1)
    }

    // The drag is the current interaction: Escape drops it and leaves the
    // order alone; only the next Escape closes Settings.
    function test_escape_during_a_drag_cancels_it_and_keeps_settings_open() {
      const panel = openSources()
      const list = panel.part_sourceList
      panel.dismissals = 0
      const grip = panel.renderedSourceAt(3).dragControl
      const target = panel.renderedSourceAt(1)
      const x = grip.width / 2
      const start = grip.height / 2
      const end = target.mapToItem(grip, 0, target.height / 4).y

      mousePress(grip, x, start)
      mouseMove(grip, x, end)
      verify(list.dropChangesOrder)
      keyClick(Qt.Key_Escape)
      compare(panel.dismissals, 0, "Escape ends the drag, not Settings")
      verify(panel.visible)
      compare(list.dragIndex, -1)
      verify(!list.dropChangesOrder, "the drop line goes away")
      verify(!panel.renderedSourceAt(3).dragging)

      mouseMove(grip, x, end + 4)
      mouseRelease(grip, x, end + 4)
      compare(panel.changedPreferences, null, "releasing after Escape saves nothing")
      verify(panel.searchControl.activeFocus, "focus stays where it was")
      keyClick(Qt.Key_Escape)
      compare(panel.dismissals, 1, "the next Escape closes Settings")
    }

    function test_a_drag_released_in_place_saves_nothing() {
      const panel = openSources()
      const grip = panel.renderedSourceAt(2).dragControl
      mousePress(grip, grip.width / 2, grip.height / 2)
      mouseMove(grip, grip.width / 2, grip.height / 2 + 4)
      verify(!panel.part_sourceList.dropChangesOrder)
      mouseRelease(grip, grip.width / 2, grip.height / 2 + 4)
      compare(panel.changedPreferences, null)
    }

    function test_arrows_still_move_a_source_one_place() {
      const panel = openSources()
      mouseClick(panel.renderedSourceAt(3).moveUpControl)
      compare(sourceIds(panel.changedPreferences), "web-search,applications,windows-workspaces,files")
      panel.changedPreferences = null
      panel.renderedSourceAt(0).moveDownControl.forceActiveFocus()
      keyClick(Qt.Key_Space)
      compare(sourceIds(panel.changedPreferences), "applications,web-search,files,windows-workspaces")

      // Return moves it exactly one place too.
      moveSpy.target = panel.renderedSourceAt(0)
      moveSpy.clear()
      panel.renderedSourceAt(0).moveDownControl.forceActiveFocus()
      keyClick(Qt.Key_Return)
      compare(moveSpy.count, 1)
      compare(moveSpy.signalArguments[0][0], 1)
    }

    // Every Search, Home, and arrow cell in the list that Tab can reach.
    function tabStops(panel) {
      const stops = []
      for (let index = 0; index < panel.renderedSourceCount; index++) {
        const row = panel.renderedSourceAt(index)
        for (const cell of [row.searchToggleControl, row.homeToggleControl, row.moveUpControl, row.moveDownControl]) {
          if (cell.activeFocusOnTab && cell.enabled) stops.push(cell)
        }
      }
      return stops
    }

    function gridHasFocus(panel) {
      for (let index = 0; index < panel.renderedSourceCount; index++) {
        if (panel.renderedSourceAt(index).hasCellFocus()) return true
      }
      return false
    }

    // What the launcher does once a change is accepted: a new snapshot, which
    // rebuilds every row.
    function applyChange(panel) {
      const snapshot = JSON.parse(JSON.stringify(panel.snapshot))
      snapshot.preferences = JSON.parse(JSON.stringify(panel.changedPreferences))
      panel.snapshot = snapshot
      panel.changedPreferences = null
    }

    function test_the_grid_is_one_tab_stop_walked_with_arrow_keys() {
      const panel = openSources()
      const first = panel.renderedSourceAt(0)
      const second = panel.renderedSourceAt(1)
      compare(tabStops(panel).length, 1)
      compare(tabStops(panel)[0], first.searchToggleControl)

      panel.extensionsControl.forceActiveFocus()
      keyClick(Qt.Key_Tab)
      verify(first.searchToggleControl.activeFocus, "Tab enters the grid at its one stop")
      keyClick(Qt.Key_Right)
      verify(first.homeToggleControl.activeFocus)
      keyClick(Qt.Key_Right)
      verify(first.moveDownControl.activeFocus, "the top row's disabled up arrow is skipped")
      keyClick(Qt.Key_Right)
      verify(first.moveDownControl.activeFocus, "Right stops at the last column")
      keyClick(Qt.Key_Down)
      verify(second.moveDownControl.activeFocus)
      keyClick(Qt.Key_Left)
      verify(second.moveUpControl.activeFocus)
      keyClick(Qt.Key_Left)
      verify(second.homeToggleControl.activeFocus)
      keyClick(Qt.Key_Up)
      keyClick(Qt.Key_Up)
      verify(first.homeToggleControl.activeFocus, "Up stops at the first row")
      keyClick(Qt.Key_Down)

      keyClick(Qt.Key_Space)
      compare(panel.changedPreferences.sources[1].sourceId, "applications")
      compare(panel.changedPreferences.sources[1].enabledOnHome, false)
      compare(panel.changedPreferences.sources[1].enabledInSearch, true)
      panel.changedPreferences = null
      keyClick(Qt.Key_Return)
      compare(panel.changedPreferences.sources[1].enabledOnHome, true)

      keyClick(Qt.Key_Tab)
      verify(!gridHasFocus(panel), "Tab leaves the grid instead of walking every cell")
      keyClick(Qt.Key_Backtab)
      verify(second.homeToggleControl.activeFocus, "Shift+Tab returns to the cell last used")
      compare(tabStops(panel).length, 1)
    }

    function test_alt_arrows_move_the_focused_source_and_focus_follows_it() {
      const panel = openSources()
      panel.renderedSourceAt(2).homeToggleControl.forceActiveFocus()
      keyClick(Qt.Key_Down, Qt.AltModifier)
      compare(sourceIds(panel.changedPreferences), "web-search,applications,windows-workspaces,files")
      applyChange(panel)
      compare(panel.renderedSourceLabelAt(3), panel.sourceLabel("files"))
      waitForCondition(() => panel.renderedSourceAt(3).homeToggleControl.activeFocus, 5000,
        "focus stays on the moved source's Home cell")

      keyClick(Qt.Key_Down, Qt.AltModifier)
      compare(panel.changedPreferences, null, "the last source cannot move further down")

      // The up arrow on the second row moves its source to the top, where the
      // up arrow cannot act; focus lands on the down arrow beside it.
      panel.renderedSourceAt(1).moveUpControl.forceActiveFocus()
      keyClick(Qt.Key_Return)
      compare(sourceIds(panel.changedPreferences), "applications,web-search,windows-workspaces,files")
      applyChange(panel)
      waitForCondition(() => panel.renderedSourceAt(0).moveDownControl.activeFocus)
      compare(tabStops(panel).length, 1)
    }

    // Every way of moving a source says where it landed, so a screen reader
    // hears the new order and not only the focused checkbox.
    function test_a_reorder_is_announced() {
      const panel = openSources()
      const list = panel.part_sourceList
      compare(list.Accessible.role, Accessible.List)
      compare(list.Accessible.name, "Search sources")

      panel.renderedSourceAt(2).homeToggleControl.forceActiveFocus()
      keyClick(Qt.Key_Down, Qt.AltModifier)
      compare(list.moveAnnouncement, panel.sourceLabel("files") + " moved to position 4 of 4")
      compare(list.Accessible.description, list.moveAnnouncement)
      applyChange(panel)
      waitForCondition(() => panel.renderedSourceAt(3).homeToggleControl.activeFocus)
      waitForFrame(panel)

      mouseClick(panel.renderedSourceAt(1).moveUpControl)
      compare(list.moveAnnouncement, panel.sourceLabel("applications") + " moved to position 1 of 4")
      applyChange(panel)
      scrollIntoView(list)

      const grip = panel.renderedSourceAt(3).dragControl
      const target = panel.renderedSourceAt(1)
      const end = target.mapToItem(grip, 0, target.height / 4).y
      mousePress(grip, grip.width / 2, grip.height / 2)
      mouseMove(grip, grip.width / 2, end)
      mouseRelease(grip, grip.width / 2, end)
      compare(list.moveAnnouncement, panel.sourceLabel("files") + " moved to position 2 of 4")
    }

    function test_a_rebuild_does_not_take_focus_the_grid_did_not_have() {
      const panel = openSources()
      panel.renderedSourceAt(1).searchToggleControl.forceActiveFocus()
      panel.extensionsControl.forceActiveFocus()
      mouseClick(panel.renderedSourceAt(0).moveDownControl)
      panel.extensionsControl.forceActiveFocus()
      applyChange(panel)
      nextTurn()
      verify(panel.extensionsControl.activeFocus)
      verify(!gridHasFocus(panel))
    }

    function test_a_source_off_everywhere_is_dimmed() {
      const panel = openSources()
      const snapshot = JSON.parse(JSON.stringify(panel.snapshot))
      snapshot.preferences.sources[2].enabledInSearch = false
      snapshot.preferences.sources[2].enabledOnHome = false
      snapshot.preferences.sources[1].enabledOnHome = false
      panel.snapshot = snapshot

      const hidden = panel.renderedSourceAt(2)
      const searchOnly = panel.renderedSourceAt(1)
      verify(hidden.hiddenEverywhere)
      verify(!searchOnly.hiddenEverywhere)
      verify(Qt.colorEqual(hidden.nameControl.color, panel.theme.textMuted))
      verify(Qt.colorEqual(searchOnly.nameControl.color, panel.theme.text))
      verify(hidden.Accessible.name.indexOf("hidden") >= 0)
      verify(!hidden.searchToggleControl.checked && !hidden.homeToggleControl.checked)
    }

    function test_headings_sit_over_their_columns() {
      const panel = openSources()
      const list = panel.part_sourceList
      const row = panel.renderedSourceAt(0)
      const centre = item => item.mapToItem(list, item.width / 2, 0).x
      const search = findChild(list.headingControl, "sourceSearchHeading")
      const home = findChild(list.headingControl, "sourceHomeHeading")
      verify(Math.abs(centre(search) - centre(row.searchToggleControl)) <= 1)
      verify(Math.abs(centre(home) - centre(row.homeToggleControl)) <= 1)
      verify(row.height < Math.round(48 * panel.theme.scale), "rows stay compact")
    }

    // A narrow Settings (a small screen or a large display size) tightens the
    // grid instead of pushing the arrows past the list's edge.
    function test_the_grid_fits_a_narrow_settings_panel() {
      scene.width = 520
      const panel = openSources()
      const list = panel.part_sourceList
      verify(panel.railCollapsed)
      const right = item => item.mapToItem(list, item.width, 0).x
      const centre = item => item.mapToItem(list, item.width / 2, 0).x
      for (let index = 0; index < panel.renderedSourceCount; index++) {
        const row = panel.renderedSourceAt(index)
        verify(row.width <= list.width, "row " + index + " is " + row.width + " wide in " + list.width)
        verify(right(row.moveDownControl) <= list.width,
          "the down arrow ends at " + right(row.moveDownControl) + ", inside " + list.width)
        verify(row.nameControl.width >= Math.round(40 * panel.theme.scale), "the name keeps some room")
      }

      const first = panel.renderedSourceAt(0)
      const search = findChild(list.headingControl, "sourceSearchHeading")
      const home = findChild(list.headingControl, "sourceHomeHeading")
      verify(Math.abs(centre(search) - centre(first.searchToggleControl)) <= 1)
      verify(Math.abs(centre(home) - centre(first.homeToggleControl)) <= 1)

      mouseClick(first.searchToggleControl)
      compare(panel.changedPreferences.sources[0].enabledInSearch, false)
      panel.changedPreferences = null
      mouseClick(panel.renderedSourceAt(3).moveUpControl)
      compare(sourceIds(panel.changedPreferences), "web-search,applications,windows-workspaces,files")
    }

    // HEY mail never appears on Home: its row shows no Home switch, and the
    // grid steps over that cell and never parks its one Tab stop there.
    function test_a_source_without_home_has_no_home_switch() {
      const panel = fixture.part_settingsPanel
      originalDescriptors = panel.sourceDescriptors
      panel.sourceDescriptors = originalDescriptors.concat([
        { id: "hey", label: "HEY", icon: "internet-mail", homeAvailable: false }])
      originalSnapshot = panel.snapshot
      const snapshot = JSON.parse(JSON.stringify(panel.snapshot))
      snapshot.preferences.sources = [
        { sourceId: "applications", enabledInSearch: true, enabledOnHome: true, position: 1 },
        { sourceId: "hey", enabledInSearch: true, enabledOnHome: false, position: 2 },
        { sourceId: "files", enabledInSearch: true, enabledOnHome: true, position: 3 }]
      panel.snapshot = snapshot
      panel.changedPreferences = null
      panel.visible = true
      waitForValue(panel.searchControl, "activeFocus", true)
      panel.showPage("search")
      waitForFrame(panel)

      const hey = panel.renderedSourceAt(1)
      compare(hey.label, "HEY")
      verify(!hey.homeToggleControl.visible && !hey.homeToggleControl.enabled)
      verify(panel.renderedSourceAt(0).homeToggleControl.visible)

      hey.searchToggleControl.forceActiveFocus()
      keyClick(Qt.Key_Right)
      verify(hey.moveUpControl.activeFocus, "Right steps over the missing Home switch")
      keyClick(Qt.Key_Left)
      verify(hey.searchToggleControl.activeFocus)

      panel.renderedSourceAt(0).homeToggleControl.forceActiveFocus()
      keyClick(Qt.Key_Down)
      verify(hey.searchToggleControl.activeFocus, "Down lands on the nearest cell that can act")
      compare(tabStops(panel).length, 1)
      compare(tabStops(panel)[0], hey.searchToggleControl)

      keyClick(Qt.Key_Space)
      compare(panel.changedPreferences.sources[1].enabledInSearch, false)
      compare(panel.changedPreferences.sources[1].enabledOnHome, false)
    }

    function test_a_rejected_save_says_why() {
      const panel = openSources()
      verify(!panel.saveErrorControl.visible)
      panel.preferencesSaveError = "The source registry changed. Refresh Settings and try again."
      verify(panel.saveErrorControl.visible)
      verify(panel.saveErrorControl.text.indexOf("The source registry changed") >= 0)
      compare(panel.saveErrorControl.Accessible.role, Accessible.AlertMessage)
    }
  }
}
