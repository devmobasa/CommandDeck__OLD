import QtQuick
import QtTest
import "../../qml/common" as CommandDeck
import "../../qml/common/Protocol.js" as Protocol
import "../../qml/common/UiConstants.js" as Ui

EventPathFixture {
  id: scene
  EventPathTestCase {
    fixture: scene
    name: "CommandDeck_search_presentation"
    function test_search_loading_settles_and_respects_reduced_motion() {
      waitForValue(fixture.part_searchLoading, "active", false)
      verify(!fixture.part_searchLoading.animating)
      fixture.part_searchLoading.loading = true
      verify(fixture.part_searchLoading.active)
      verify(fixture.part_searchLoading.animating)
      fixture.part_searchLoading.loading = false
      verify(fixture.part_searchLoading.active, "brief gaps do not flash the loading line")
      fixture.part_searchLoading.loading = true
      observeFor(120, () => fixture.part_searchLoading.active, "a replacement query cancels the pending hide")
      verify(fixture.part_searchLoading.active, "a replacement query cancels the pending hide")
      fixture.part_theme.reduceMotion = true
      verify(fixture.part_searchLoading.active)
      verify(!fixture.part_searchLoading.animating)
      compare(fixture.part_searchLoading.Accessible.name, "Searching")
      fixture.part_searchLoading.loading = false
      waitForValue(fixture.part_searchLoading, "active", false)
      verify(!fixture.part_searchLoading.visible)
      verify(!fixture.part_searchLoading.animating)
    }

    function test_numbered_results_require_only_control_modifier() {
      fixture.part_searchField.inputControl.forceActiveFocus()
      keyClick(Qt.Key_2, Qt.AltModifier)
      keyClick(Qt.Key_2, Qt.ControlModifier | Qt.ShiftModifier)
      keyClick(Qt.Key_2, Qt.ControlModifier | Qt.AltModifier)
      compare(fixture.part_controller.primaryCalls, 0)
      keyClick(Qt.Key_2, Qt.ControlModifier)
      compare(fixture.part_controller.primaryCalls, 1)
      compare(fixture.part_controller.selectedResult().id, "app:editor")
    }

    function test_footer_actions_click_keyboard_and_focus() {
      mouseClick(fixture.part_footerActions.primaryControl)
      compare(fixture.part_footerActions.primaryCalls, 1)
      mouseClick(fixture.part_footerActions.actionsControl)
      compare(fixture.part_footerActions.actionCalls, 1)
      fixture.part_footerActions.primaryControl.forceActiveFocus()
      keyClick(Qt.Key_Return)
      compare(fixture.part_footerActions.primaryCalls, 2)
      keyClick(Qt.Key_Tab)
      verify(fixture.part_footerActions.actionsControl.activeFocus)
      keyClick(Qt.Key_Space)
      compare(fixture.part_footerActions.actionCalls, 2)
      keyClick(Qt.Key_Tab)
      verify(fixture.part_footerActions.prefixesControl.activeFocus)
      keyClick(Qt.Key_Tab)
      verify(fixture.part_footerActions.helpControl.activeFocus)
      keyClick(Qt.Key_Tab)
      verify(fixture.part_searchField.inputControl.activeFocus)
      fixture.part_footerActions.primaryControl.forceActiveFocus()
      const previousDismissals = fixture.part_controller.dismissCalls
      keyClick(Qt.Key_Escape)
      compare(fixture.part_controller.dismissCalls, previousDismissals + 1)
      keyClick(Qt.Key_F1)
      waitForCondition(function() { return fixture.part_eventHelpSheet.activeFocus })
      keyClick(Qt.Key_Escape)
      waitForCondition(function() { return fixture.part_footerActions.primaryControl.activeFocus })
      const requests = fixture.part_searchField.actionRequests
      keyClick(Qt.Key_K, Qt.ControlModifier)
      compare(fixture.part_searchField.actionRequests, requests + 1)
      fixture.part_footerActions.actionCount = 1
      verify(!fixture.part_footerActions.actionsControl.visible)
      fixture.part_footerActions.primaryActionTitle = ""
      verify(!fixture.part_footerActions.primaryControl.visible)
      fixture.part_footerActions.primaryActionTitle = "Open"
      fixture.part_footerActions.enabled = false
      mouseClick(fixture.part_footerActions.primaryControl)
      compare(fixture.part_footerActions.primaryCalls, 2)
    }

    function test_themed_tooltip_is_bounded_and_does_not_steal_focus() {
      const title = "Signal <b>literal</b> & " + "long-name-".repeat(30)
      const row = createTemporaryObject(fixture.part_tooltipRowComponent, fixture, {
        theme: fixture.part_theme,
        hint: title + "\nSignal - Private Messenger\nAliases: chat"
      })
      verify(row !== null)
      mouseClick(fixture.part_searchField.inputControl)
      verify(fixture.part_searchField.inputControl.activeFocus)
      mouseMove(row, 20, 20)
      const tip = row.toolTipControl
      waitForValue(tip, "visible", true, 2000)
      verify(waitForFrame(row))
      compare(tip.parent, row, "the tooltip belongs to the hovered row")
      compare(tip.text, title + "\nSignal - Private Messenger\nAliases: chat")
      compare(tip.contentItem.textFormat, Text.PlainText)
      compare(tip.contentItem.color, fixture.part_theme.text)
      compare(tip.background.color, Qt.rgba(fixture.part_theme.background.r, fixture.part_theme.background.g, fixture.part_theme.background.b, 1))
      compare(tip.background.border.color, fixture.part_theme.border)
      verify(tip.width <= 420 * fixture.part_theme.scale)
      const position = tip.contentItem.mapToItem(fixture, 0, 0)
      verify(position.x >= 0 && position.x + tip.contentItem.width <= fixture.width,
        "even a long tooltip at the right edge stays inside the window")
      verify(fixture.part_searchField.inputControl.activeFocus, "hover never takes keyboard focus")
      keyClick(Qt.Key_A)
      verify(fixture.part_searchField.inputControl.text.endsWith("a"))
      mouseClick(row, 20, 20)
      compare(row.activations, 1, "a visible tooltip does not block the row action")
      mouseMove(fixture, 5, fixture.height - 5)
      waitForValue(tip, "visible", false)
    }

    function test_footer_discovery_is_persistent_and_restores_focus() {
      fixture.part_footerActions.primaryActionTitle = ""
      fixture.part_footerActions.actionCount = 0
      fixture.part_footerActions.actionsEnabled = false
      fixture.part_footerActions.availableWidth = 300
      fixture.part_searchField.precedingTabStop = fixture.part_footerActions.lastControl
      verify(fixture.part_footerActions.prefixesControl.visible && fixture.part_footerActions.prefixesControl.enabled)
      verify(fixture.part_footerActions.helpControl.visible && fixture.part_footerActions.helpControl.enabled)
      compare(fixture.part_footerActions.firstControl, fixture.part_footerActions.prefixesControl)
      verify(waitForFrame(fixture.part_footerActions))
      verify(fixture.part_footerActions.implicitWidth <= fixture.part_footerActions.availableWidth)

      mouseClick(fixture.part_footerActions.prefixesControl)
      waitForCondition(function() { return fixture.part_eventFilterPalette.activeFocus })
      keyClick(Qt.Key_Down)
      compare(fixture.part_eventFilterPalette.selectedToken(), ">")
      keyClick(Qt.Key_Tab)
      verify(fixture.part_eventFilterPalette.activeFocus, "the palette traps focus")
      keyClick(Qt.Key_Escape)
      waitForCondition(function() { return fixture.part_footerActions.prefixesControl.activeFocus })
      keyClick(Qt.Key_Right)
      verify(fixture.part_footerActions.helpControl.activeFocus)
      keyClick(Qt.Key_Return)
      waitForCondition(function() { return fixture.part_eventHelpSheet.activeFocus })
      keyClick(Qt.Key_Backtab)
      verify(fixture.part_eventHelpSheet.activeFocus)
      keyClick(Qt.Key_Escape)
      waitForCondition(function() { return fixture.part_footerActions.helpControl.activeFocus })
      keyClick(Qt.Key_Left)
      verify(fixture.part_footerActions.prefixesControl.activeFocus)
      keyClick(Qt.Key_Space)
      waitForCondition(function() { return fixture.part_eventFilterPalette.activeFocus })
      keyClick(Qt.Key_Escape)
      waitForCondition(function() { return fixture.part_footerActions.prefixesControl.activeFocus })
      keyClick(Qt.Key_Tab)
      verify(fixture.part_footerActions.helpControl.activeFocus)
      keyClick(Qt.Key_Tab)
      verify(fixture.part_searchField.inputControl.activeFocus)
      keyClick(Qt.Key_Backtab)
      verify(fixture.part_footerActions.helpControl.activeFocus)
      mouseClick(fixture.part_footerActions.helpControl)
      waitForCondition(function() { return fixture.part_eventHelpSheet.activeFocus })
      keyClick(Qt.Key_F1)
      waitForCondition(function() { return fixture.part_footerActions.helpControl.activeFocus })
    }

    function test_dialog_focus_return_resolves_the_current_control() {
      const footer = fixture.part_footerActions
      const help = fixture.part_eventHelpSheet
      const filters = fixture.part_eventFilterPalette

      help.returnFocusTarget = footer.helpControl
      help.opened = true
      waitForCondition(function() { return help.activeFocus })
      help.opened = false
      help.returnFocusTarget = footer.prefixesControl
      waitForCondition(function() { return footer.prefixesControl.activeFocus })

      filters.returnFocusTarget = footer.prefixesControl
      filters.opened = true
      waitForCondition(function() { return filters.activeFocus })
      filters.opened = false
      filters.returnFocusTarget = footer.helpControl
      waitForCondition(function() { return footer.helpControl.activeFocus })
    }

    function test_dialog_focus_return_does_not_interrupt_a_new_surface() {
      const help = fixture.part_eventHelpSheet
      const filters = fixture.part_eventFilterPalette
      help.returnFocusTarget = fixture.part_footerActions.helpControl
      help.opened = true
      waitForCondition(function() { return help.activeFocus })

      help.opened = false
      help.focusReturnAllowed = false
      try {
        filters.opened = true
        waitForCondition(function() { return filters.activeFocus })
        waitForFrame(filters)
        verify(filters.activeFocus)
      } finally {
        filters.opened = false
        help.focusReturnAllowed = true
      }
    }

    function test_search_keyboard_and_settings_button() {
      fixture.part_searchField.inputControl.forceActiveFocus()
      typeText("file: report")
      compare(fixture.part_controller.activeFilterId, "files")
      compare(fixture.part_controller.queryText, "report")

      fixture.part_searchField.inputControl.text = "Bro"
      fixture.part_searchField.inputControl.cursorPosition = fixture.part_searchField.inputControl.length
      keyClick(Qt.Key_Tab)
      compare(fixture.part_searchField.inputControl.text, "Browser")

      keyClick(Qt.Key_Comma, Qt.ControlModifier)
      compare(fixture.part_controller.settingsCalls, 1)
      mouseClick(fixture.part_searchField.settingsControl)
      compare(fixture.part_controller.settingsCalls, 2)

      // The preceding mouseClick left focus on the settings button.
      fixture.part_searchField.inputControl.forceActiveFocus()

      // F1 must reach the controller rather than being swallowed by the
      // text field, and a bare slash must still type a slash.
      keyClick(Qt.Key_F1)
      compare(fixture.part_controller.helpCalls, 1)
      keyClick(Qt.Key_Slash, Qt.ControlModifier)
      compare(fixture.part_controller.helpCalls, 2)

      fixture.part_searchField.inputControl.text = ""
      typeText("/")
      compare(fixture.part_controller.helpCalls, 2)
      compare(fixture.part_searchField.inputControl.text, "/")
    }
  }
}
