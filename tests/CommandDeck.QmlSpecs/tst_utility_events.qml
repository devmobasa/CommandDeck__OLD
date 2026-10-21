import QtQuick
import QtTest
import "../../qml/common" as CommandDeck
import "../../qml/common/Protocol.js" as Protocol
import "../../qml/common/UiConstants.js" as Ui

EventPathFixture {
  id: scene
  EventPathTestCase {
    fixture: scene
    name: "CommandDeck_utility"
    function test_calculator_layout_and_copy() {
      const card = createTemporaryObject(fixture.part_calculatorComponent, fixture, { width: 840, theme: fixture.part_theme })
      verify(card !== null)
      const expression = findChild(card, "calculatorExpression")
      const value = findChild(card, "calculatorValue")
      const copy = findChild(card, "calculatorCopyButton")
      nextTurn()
      verify(!card.stacked)
      compare(expression.text, "5 + 5")
      compare(value.text, "10")
      verify(expression.mapToItem(card, 0, 0).x < value.mapToItem(card, 0, 0).x)
      compare(copy.text, "Copy Result", "the button uses the primary action title")
      mouseClick(copy)
      compare(card.copyCalls, 1)

      card.width = 280
      card.expression = "105 °F to °C"
      card.value = "40.555555555555555555555555555555555555555555555555555555555555 °C"
      nextTurn()
      verify(card.stacked)
      waitForCondition(() => expression.mapToItem(card, 0, 0).y < value.mapToItem(card, 0, 0).y)
      verify(value.height > value.font.pixelSize * 2)
      verify(value.width <= card.width)
      compare(value.textFormat, Text.PlainText)
      card.value = ""
      verify(!copy.enabled)
      card.actionTitle = ""
      verify(!copy.visible, "a result without an action offers no copy button")
    }

    function test_health_indicator_is_compact_and_keyboard_accessible() {
      const providers = []
      for (let index = 0; index < 32; index++) {
        providers.push({
          label: "Provider " + index,
          state: "degraded",
          detail: "The optional tool is missing.",
          remediation: "Install with: sudo pacman -S --needed example-package"
        })
      }
      const banner = createTemporaryObject(fixture.part_runtimeBannerComponent, fixture, {
        theme: fixture.part_theme,
        status: { providers: providers }
      })
      verify(banner !== null)
      compare(banner.summary, "32 service issues")
      verify(banner.details.indexOf("Provider 31") >= 0)
      verify(banner.implicitHeight <= 36)
      fixture.part_searchField.additionalTabStop = Qt.binding(function() { return banner.visible ? banner : null })
      fixture.part_controller.selectedIndex = -1
      fixture.part_searchField.inputControl.forceActiveFocus()
      keyClick(Qt.Key_Tab)
      compare(fixture.part_searchField.settingsControl.activeFocus, true)
      keyClick(Qt.Key_Tab)
      waitForValue(banner, "activeFocus", true)

      compare(banner.inspectorRequests, 0)
      keyClick(Qt.Key_Return)
      keyClick(Qt.Key_Space)
      compare(banner.inspectorRequests, 2)
      keyClick(Qt.Key_Tab)
      compare(fixture.part_searchField.inputControl.activeFocus, true)
      keyClick(Qt.Key_Backtab)
      compare(banner.activeFocus, true)
      keyClick(Qt.Key_Backtab)
      compare(fixture.part_searchField.settingsControl.activeFocus, true)

      fixture.part_controller.activeFilters = [{ id: "files", excluded: false }]
      keyClick(Qt.Key_Tab)
      compare(banner.activeFocus, true)
      keyClick(Qt.Key_Tab)
      compare(fixture.part_searchField.filterChipControl.activeFocus, true)
      keyClick(Qt.Key_Backtab)
      compare(banner.activeFocus, true)
      keyClick(Qt.Key_Tab)
      keyClick(Qt.Key_Tab)
      compare(fixture.part_searchField.inputControl.activeFocus, true)

      fixture.part_controller.activeFilters = []
      banner.status = { providers: [] }
      waitForValue(banner, "visible", false)
      keyClick(Qt.Key_Tab)
      compare(fixture.part_searchField.settingsControl.activeFocus, true)
      keyClick(Qt.Key_Tab)
      compare(fixture.part_searchField.inputControl.activeFocus, true)
      fixture.part_searchField.additionalTabStop = null
    }
  }
}
