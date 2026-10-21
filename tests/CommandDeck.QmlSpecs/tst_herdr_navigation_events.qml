import QtQuick
import QtTest
import "../../qml/common" as CommandDeck
import "../../qml/common/Protocol.js" as Protocol
import "../../qml/common/UiConstants.js" as Ui

EventPathFixture {
  id: scene
  EventPathTestCase {
    fixture: scene
    name: "CommandDeck_herdr_navigation"
    function test_herdr_browse_type_back_refresh_and_reopen() {
      const space = { id: "herdr:workspace:w1", sourceId: "herdr", kind: "workspace", title: "CommandDeck",
        metadata: { herdrWorkspaceId: "w1" } }
      const other = { id: "herdr:workspace:w2", sourceId: "herdr", kind: "workspace", title: "Other",
        metadata: { herdrWorkspaceId: "w2" } }
      fixture.part_controller.queryText = "deck"
      fixture.part_controller.activeFilters = [{ id: "herdr", excluded: false }]
      fixture.part_controller.replaceResults([other, space], true)
      fixture.part_controller.selectedIndex = fixture.part_controller.indexOfResultId(space.id)
      fixture.part_controller.browseHerdrSpace(space)
      waitForValue(fixture.part_searchField.inputControl, "activeFocus", true)
      compare(fixture.part_controller.herdrWorkspaceId, "w1")
      typeText("tests")
      compare(fixture.part_controller.queryText, "tests")
      keyClick(Qt.Key_Escape)
      compare(fixture.part_controller.queryText, "")
      compare(fixture.part_controller.herdrWorkspaceId, "w1")
      keyClick(Qt.Key_Escape)
      compare(fixture.part_controller.herdrWorkspaceId, "")
      compare(fixture.part_controller.queryText, "deck")
      fixture.part_controller.replaceResults([other, space], true)
      compare(fixture.part_controller.selectedResult().id, space.id)
      waitForValue(fixture.part_searchField.inputControl, "activeFocus", true)

      fixture.part_controller.browseHerdrSpace(space)
      const bar = createTemporaryObject(fixture.part_herdrBarComponent, fixture, {
        y: 90, width: 700, theme: fixture.part_theme, controller: fixture.part_controller,
        searchInput: fixture.part_searchField.inputControl, previousTabStop: fixture.part_searchField.settingsControl,
        nextTabStop: fixture.part_searchField.inputControl
      })
      verify(bar !== null)
      mouseClick(bar.attentionControl)
      typeText("release")
      compare(fixture.part_controller.queryText, "release")
      compare(fixture.part_controller.activeFilterId, "herdr-attention")
      compare(fixture.part_controller.herdrWorkspaceId, "w1")
      mouseClick(bar.lastControl)
      typeText("s")
      compare(fixture.part_controller.queryText, "releases")
      mouseClick(bar.backControl)
      waitForValue(fixture.part_searchField.inputControl, "activeFocus", true)
      compare(fixture.part_controller.queryText, "deck")
      fixture.part_controller.browseHerdrSpace(space)
      fixture.part_searchField.additionalTabStop = bar.firstControl
      fixture.part_controller.replaceResults([], true)
      waitForValue(fixture.part_searchField.inputControl, "activeFocus", true)
      waitForFrame(bar)
      keyClick(Qt.Key_Tab)
      waitForValue(fixture.part_searchField.settingsControl, "activeFocus", true)
      keyClick(Qt.Key_Tab)
      waitForValue(bar.backControl, "activeFocus", true)
      keyClick(Qt.Key_Right)
      waitForValue(bar.attentionControl, "activeFocus", true)
      keyClick(Qt.Key_Return)
      waitForValue(bar.attentionControl, "activeFocus", true)
      keyClick(Qt.Key_Right)
      waitForValue(bar.lastControl, "activeFocus", true)
      keyClick(Qt.Key_Space)
      waitForValue(bar.lastControl, "activeFocus", true)
      fixture.part_controller.replaceResults([{ id: "herdr:tab:w1:t1", sourceId: "herdr", kind: "tab", title: "updated" }], false)
      verify(bar.lastControl.activeFocus)
      typeText("now")
      compare(fixture.part_controller.queryText, "now")
      keyClick(Qt.Key_Tab)
      waitForValue(fixture.part_searchField.inputControl, "activeFocus", true)
      fixture.part_controller.clear()
      compare(fixture.part_controller.herdrWorkspaceId, "")
    }

    function test_herdr_refresh_click_and_live_replacement_keep_typing_ready() {
      const space = { id: "herdr:workspace:w1", sourceId: "herdr", kind: "workspace", title: "Deck",
        metadata: { herdrWorkspaceId: "w1" } }
      const tab = { id: "herdr:tab:w1:t1", sourceId: "herdr", kind: "tab", title: "tests" }
      const second = { id: "herdr:tab:w1:t2", sourceId: "herdr", kind: "tab", title: "tests release" }
      fixture.part_controller.browseHerdrSpace(space)
      waitForValue(fixture.part_searchField.inputControl, "activeFocus", true)
      typeText("test")
      fixture.part_controller.replaceResults([tab, second], true)
      fixture.part_controller.selectedIndex = fixture.part_controller.indexOfResultId(second.id)
      const bar = createTemporaryObject(fixture.part_herdrBarComponent, fixture, {
        y: 90, width: 700, theme: fixture.part_theme, controller: fixture.part_controller,
        searchInput: fixture.part_searchField.inputControl, previousTabStop: fixture.part_searchField.settingsControl,
        nextTabStop: fixture.part_searchField.inputControl
      })
      mouseClick(bar.lastControl)
      typeText("s")
      compare(fixture.part_controller.queryText, "tests")
      verify(fixture.part_searchField.inputControl.activeFocus)

      fixture.part_controller.replaceResults([Object.assign({}, second, { metadata: { herdrAgentStatus: "blocked" } }), tab], false)
      compare(fixture.part_controller.selectedResult().id, second.id)
      verify(fixture.part_searchField.inputControl.activeFocus)
      typeText(" release")
      compare(fixture.part_controller.queryText, "tests release")
      fixture.part_controller.replaceResults([second], true)
      compare(fixture.part_controller.rows.count, 2)
      compare(fixture.part_controller.selectedResult().id, second.id)
    }

    function test_herdr_name_dialog_initial_focus_keyboard_mouse_and_reopen() {
      const dialog = createTemporaryObject(fixture.part_actionInputComponent, fixture, {
        width: 900, height: 600, theme: fixture.part_theme,
        action: { title: "Rename tab", inputs: [{ name: "Name", defaultValue: "tests" }] }, opened: true
      })
      verify(dialog !== null)
      waitForCondition(() => dialog.rowAt(0).input.activeFocus)
      keyClick(Qt.Key_A, Qt.ControlModifier)
      typeText("release")
      keyClick(Qt.Key_Tab)
      keyClick(Qt.Key_Tab)
      keyClick(Qt.Key_Return)
      compare(dialog.submittedValues.Name, "release")
      compare(dialog.submissions, 1)
      keyClick(Qt.Key_Tab)
      verify(dialog.rowAt(0).input.activeFocus)
      dialog.opened = false
      dialog.action = { title: "New tab in this space", inputs: [{ name: "Name", defaultValue: "New tab" }] }
      dialog.opened = true
      waitForCondition(() => dialog.rowAt(0).input.activeFocus)
      keyClick(Qt.Key_A, Qt.ControlModifier)
      typeText("scratch")
      mouseClick(dialog.submitControl)
      compare(dialog.submittedValues.Name, "scratch")
    }
  }
}
