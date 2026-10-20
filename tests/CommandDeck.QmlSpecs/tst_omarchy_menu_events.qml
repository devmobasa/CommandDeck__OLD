import QtQuick
import QtTest
import "../../qml/common" as CommandDeck
import "../../qml/common/Protocol.js" as Protocol
import "../../qml/common/UiConstants.js" as Ui

EventPathFixture {
  id: scene

  Component {
    id: omarchyBarComponent
    CommandDeck.OmarchyMenuBar { }
  }

  // The launcher opens a submenu row's primary action itself, as the deck does.
  Connections {
    target: scene.part_controller
    function onPrimaryRequested() {
      const result = scene.part_controller.selectedResult()
      const action = result && result.actions ? result.actions[0] : null
      if (action && action.kind === Protocol.ActionKinds.BrowseOmarchyMenu) scene.part_controller.browseOmarchyMenu(result, action)
    }
  }

  EventPathTestCase {
    fixture: scene
    name: "CommandDeck_omarchy_menu"

    function submenu(id, path) {
      const values = { omarchyMenu: id, omarchyMenuPath: path, omarchyMenuItems: "2" }
      return { id: "omarchy:" + id, kind: "omarchy", sourceId: "omarchy-control", title: path.split(" › ").pop(),
        metadata: values, actions: [{ id: "open-submenu", title: "Open Submenu", kind: Protocol.ActionKinds.BrowseOmarchyMenu, arguments: values }] }
    }

    function entry(id, title) {
      return { id: "omarchy:" + id, kind: "omarchy", sourceId: "omarchy-control", title: title,
        actions: [{ id: "run", title: "Run", kind: "run-trusted-shell" }] }
    }

    function bar() {
      const created = createTemporaryObject(omarchyBarComponent, scene, {
        y: 90, width: 700, theme: fixture.part_theme, controller: fixture.part_controller,
        searchInput: fixture.part_searchField.inputControl, previousTabStop: fixture.part_searchField.settingsControl,
        nextTabStop: fixture.part_searchField.inputControl
      })
      verify(created !== null)
      return created
    }

    function test_enter_opens_typing_filters_and_escape_backs_out_level_by_level() {
      const theme = submenu("style.theme", "Style › Theme")
      const variants = submenu("style.theme.variants", "Style › Theme › Variants")
      const background = entry("style.background", "Background")
      const dark = entry("style.theme.dark", "Dark")
      const dismissals = fixture.part_controller.dismissCalls
      fixture.part_searchField.inputControl.forceActiveFocus()
      typeText("theme")
      compare(fixture.part_controller.queryText, "theme")
      fixture.part_controller.replaceResults([background, theme], true)
      keyClick(Qt.Key_Down)
      compare(fixture.part_controller.selectedResult().id, theme.id)
      keyClick(Qt.Key_Return)
      compare(fixture.part_controller.omarchyMenuId, "style.theme")
      compare(fixture.part_controller.omarchyMenuPath, "Style › Theme")
      compare(fixture.part_controller.queryText, "")
      tryCompare(fixture.part_searchField.inputControl, "activeFocus", true)
      compare(fixture.part_searchField.inputControl.text, "")
      typeText("va")
      compare(fixture.part_controller.queryText, "va")

      fixture.part_controller.replaceResults([dark, variants], true)
      keyClick(Qt.Key_Down)
      keyClick(Qt.Key_Return)
      compare(fixture.part_controller.omarchyMenuId, "style.theme.variants")
      compare(fixture.part_controller.omarchyParents.length, 2)
      tryCompare(fixture.part_searchField.inputControl, "activeFocus", true)
      typeText("x")
      compare(fixture.part_controller.queryText, "x")

      keyClick(Qt.Key_Escape)
      compare(fixture.part_controller.queryText, "")
      compare(fixture.part_controller.omarchyMenuId, "style.theme.variants")
      keyClick(Qt.Key_Escape)
      compare(fixture.part_controller.omarchyMenuId, "style.theme")
      compare(fixture.part_controller.queryText, "va")
      fixture.part_controller.replaceResults([dark, variants], true)
      compare(fixture.part_controller.selectedResult().id, variants.id)
      tryCompare(fixture.part_searchField.inputControl, "text", "va")

      keyClick(Qt.Key_Escape)
      compare(fixture.part_controller.queryText, "")
      compare(fixture.part_controller.omarchyMenuId, "style.theme")
      keyClick(Qt.Key_Escape)
      compare(fixture.part_controller.omarchyMenuId, "")
      compare(fixture.part_controller.omarchyMenuPath, "")
      compare(fixture.part_controller.queryText, "theme")
      fixture.part_controller.replaceResults([background, theme], true)
      compare(fixture.part_controller.selectedResult().id, theme.id)
      tryCompare(fixture.part_searchField.inputControl, "activeFocus", true)
      compare(fixture.part_controller.dismissCalls, dismissals)

      keyClick(Qt.Key_Return)
      compare(fixture.part_controller.omarchyMenuId, "style.theme")
      keyClick(Qt.Key_Escape, Qt.ShiftModifier)
      compare(fixture.part_controller.rootCalls, 1)
      fixture.part_controller.clear()
      compare(fixture.part_controller.omarchyMenuId, "")
      compare(fixture.part_controller.omarchyParents.length, 0)
    }

    function test_back_button_mouse_and_keyboard_keep_search_ready() {
      const theme = submenu("style.theme", "Style › Theme")
      fixture.part_controller.queryText = "the"
      fixture.part_controller.replaceResults([theme], true)
      fixture.part_controller.browseOmarchyMenu(theme, theme.actions[0])
      const path = bar()
      compare(path.pathControl.text, "Omarchy › Style › Theme")
      tryCompare(fixture.part_searchField.inputControl, "activeFocus", true)
      typeText("da")
      mouseClick(path.backControl)
      compare(fixture.part_controller.omarchyMenuId, "")
      tryCompare(fixture.part_searchField.inputControl, "activeFocus", true)
      tryCompare(fixture.part_searchField.inputControl, "text", "the")
      typeText("me")
      compare(fixture.part_controller.queryText, "theme")

      fixture.part_controller.replaceResults([theme], true)
      fixture.part_controller.browseOmarchyMenu(theme, theme.actions[0])
      fixture.part_searchField.additionalTabStop = path.firstControl
      // With no row to complete, Tab moves focus instead of completing a title.
      fixture.part_controller.replaceResults([], true)
      tryCompare(fixture.part_searchField.inputControl, "activeFocus", true)
      wait(20)
      keyClick(Qt.Key_Tab)
      tryCompare(fixture.part_searchField.settingsControl, "activeFocus", true)
      keyClick(Qt.Key_Tab)
      tryCompare(path.backControl, "activeFocus", true)
      keyClick(Qt.Key_Tab)
      tryCompare(fixture.part_searchField.inputControl, "activeFocus", true)
      keyClick(Qt.Key_Backtab, Qt.ShiftModifier)
      tryCompare(path.backControl, "activeFocus", true)
      typeText("x")
      compare(fixture.part_controller.queryText, "x")
      keyClick(Qt.Key_Backtab, Qt.ShiftModifier)
      keyClick(Qt.Key_Tab)
      tryCompare(path.backControl, "activeFocus", true)
      keyClick(Qt.Key_Return)
      compare(fixture.part_controller.omarchyMenuId, "")
      compare(fixture.part_controller.queryText, "theme")
      tryCompare(fixture.part_searchField.inputControl, "activeFocus", true)
    }
  }
}
