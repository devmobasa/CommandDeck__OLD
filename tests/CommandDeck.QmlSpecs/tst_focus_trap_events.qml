import QtQuick
import QtTest
import "../../qml/common" as CommandDeck

// Modal surfaces keep Tab and Shift+Tab on their own usable controls: focus
// wraps at the ends, skips hidden and disabled controls, and never rests on
// the surface itself. Real keys from the surface's focused control.
Item {
  id: scene
  width: 900
  height: 700

  CommandDeck.Theme { id: theme; loadShellTokens: false }

  function workItems(count) {
    const values = []
    for (let index = 0; index < count; index++) {
      values.push({ path: "/tmp/item-" + index, state: "failed", reason: "unreadable" })
    }
    return values
  }

  function inspection(count) {
    return { workItems: scene.workItems(count), failedItemCount: count, staleItemCount: 0,
      pendingItemCount: 0, totalExclusionDiagnosticCount: 0, stats: { itemCount: 10 } }
  }

  CommandDeck.IndexInspector {
    id: inspector
    anchors.fill: parent
    theme: theme
    onDismissed: opened = false
  }

  DeadlineTestCase {
    name: "FocusTrap"
    when: windowShown

    function focused() { return inspector.Window.window.activeFocusItem }

    function cleanup() {
      inspector.opened = false
      inspector.healthDetails = ""
    }

    function test_inspector_tab_stays_on_close_when_it_is_the_only_control() {
      inspector.inspection = inspection(2)
      inspector.opened = true
      waitForValue(inspector.closeControl, "activeFocus", true)
      verify(!inspector.previousControl.enabled && !inspector.nextControl.enabled)

      keyClick(Qt.Key_Tab)
      compare(focused(), inspector.closeControl, "Tab keeps focus on Close")
      keyClick(Qt.Key_Tab, Qt.ShiftModifier)
      compare(focused(), inspector.closeControl, "Shift+Tab keeps focus on Close")
    }

    function test_inspector_tab_skips_the_disabled_page_button_and_wraps() {
      inspector.healthDetails = "The clipboard service is not running."
      inspector.inspection = inspection(6)
      inspector.opened = true
      waitForValue(inspector.closeControl, "activeFocus", true)
      verify(!inspector.previousControl.enabled && inspector.nextControl.enabled)

      keyClick(Qt.Key_Tab)
      compare(focused(), inspector.healthControl)
      keyClick(Qt.Key_Tab)
      compare(focused(), inspector.nextControl, "Tab skips the disabled Previous button")
      keyClick(Qt.Key_Tab)
      compare(focused(), inspector.closeControl, "Tab wraps to Close")
      keyClick(Qt.Key_Tab, Qt.ShiftModifier)
      compare(focused(), inspector.nextControl, "Shift+Tab wraps to the last control")

      keyClick(Qt.Key_PageDown)
      compare(inspector.page, 1)
      verify(inspector.previousControl.enabled && !inspector.nextControl.enabled)
      inspector.closeControl.forceActiveFocus()
      keyClick(Qt.Key_Tab, Qt.ShiftModifier)
      compare(focused(), inspector.previousControl, "Shift+Tab skips the disabled Next button")

      keyClick(Qt.Key_Escape)
      verify(!inspector.opened, "Escape dismisses the inspector")
    }
  }
}
