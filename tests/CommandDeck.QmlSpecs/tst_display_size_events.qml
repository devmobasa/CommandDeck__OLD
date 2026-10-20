import QtQuick
import QtTest
import "../../qml/common" as CommandDeck

Item {
  id: windowContent
  width: 1920
  height: 1080

  CommandDeck.Theme {
    id: liveTheme
    loadShellTokens: false
    densityScale: 0.75
    displayScale: scene.part_settingsPanel.displaySizePercent / 100
    reduceMotion: true
  }

  EventPathFixture { id: scene }
  EventPathTestCase {
    fixture: scene
    name: "CommandDeck_display_size"

    function test_display_size_keyboard_data() {
      return [
        { tag: "Space", resetKey: Qt.Key_Space },
        { tag: "Return", resetKey: Qt.Key_Return },
        { tag: "Enter", resetKey: Qt.Key_Enter }
      ]
    }

    function test_display_size_keyboard(data) {
      const panel = fixture.part_settingsPanel
      panel.displaySizePercent = 100
      panel.displaySizePercentChosen = -1
      panel.visible = true
      tryCompare(panel.searchControl, "activeFocus", true)
      typeText("display size")
      compare(panel.currentPage, "customization")
      keyClick(Qt.Key_Return)
      keyClick(Qt.Key_Tab)

      const slider = panel.displaySizeControl.sliderControl
      const reset = panel.displaySizeResetControl
      tryCompare(slider, "activeFocus", true)
      keyClick(Qt.Key_Right)
      compare(panel.displaySizePercentChosen, 105)
      keyClick(Qt.Key_Left)
      compare(panel.displaySizePercentChosen, 100)
      verify(!reset.enabled)
      keyClick(Qt.Key_Right)
      keyClick(Qt.Key_Tab)
      tryCompare(reset, "activeFocus", true)
      keyClick(Qt.Key_Backtab)
      tryCompare(slider, "activeFocus", true)
      keyClick(Qt.Key_Tab)
      tryCompare(reset, "activeFocus", true)
      keyClick(data.resetKey)
      compare(panel.displaySizePercentChosen, 100)
      compare(panel.displaySizeControl.value, 100)
      verify(!reset.enabled)
      verify(!panel.searchControl.activeFocus)
      verify(slider.activeFocus)
    }

    function test_display_size_mouse_and_typing_after_reset_data() {
      return [
        { tag: "from-slider", tabToReset: false },
        { tag: "already-focused-button", tabToReset: true }
      ]
    }

    function test_display_size_mouse_and_typing_after_reset(data) {
      const panel = fixture.part_settingsPanel
      panel.displaySizePercent = 115
      panel.displaySizePercentChosen = -1
      panel.visible = true
      tryCompare(panel.searchControl, "activeFocus", true)
      typeText("display size")
      const slider = panel.displaySizeControl.sliderControl
      verify(waitForRendering(slider))
      mouseClick(slider, slider.width * 0.9, slider.height / 2)
      verify(panel.displaySizePercentChosen > 115 && panel.displaySizePercentChosen <= 150)
      compare(panel.displaySizePercentChosen % 5, 0)
      compare(panel.displaySizeControl.value, panel.displaySizePercentChosen)

      if (data.tabToReset) {
        keyClick(Qt.Key_Tab)
        tryCompare(panel.displaySizeResetControl, "activeFocus", true)
      }
      verify(waitForRendering(panel.displaySizeResetControl))
      mouseClick(panel.displaySizeResetControl)
      compare(panel.displaySizePercentChosen, 100)
      verify(panel.searchControl.activeFocus)
      typeText(" percent")
      compare(panel.searchControl.text, "display size percent")
      keyClick(Qt.Key_Escape)
      keyClick(Qt.Key_Escape)
      verify(!panel.visible)
      panel.visible = true
      tryCompare(panel.searchControl, "activeFocus", true)
      compare(panel.displaySizeControl.value, 100)
      verify(!panel.displaySizeResetControl.enabled)
    }

    function test_drag_with_live_geometry_data() {
      return [
        { tag: "larger", initial: 100, direction: 1, final: 130 },
        { tag: "smaller", initial: 130, direction: -1, final: 100 }
      ]
    }

    function test_recommended_is_reachable_for_a_legacy_default_user() {
      const panel = fixture.part_settingsPanel
      panel.displaySizePercent = 100
      panel.displaySizeLegacy = true
      panel.visible = true
      tryCompare(panel.searchControl, "activeFocus", true)
      typeText("display size")
      keyClick(Qt.Key_Return)
      keyClick(Qt.Key_Tab)
      tryCompare(panel.displaySizeControl.sliderControl, "activeFocus", true)
      keyClick(Qt.Key_Tab)
      tryCompare(panel.displaySizeResetControl, "activeFocus", true)
      keyClick(Qt.Key_Space)
      verify(!panel.displaySizeLegacy)
      verify(!panel.displaySizeResetControl.enabled)
      verify(panel.displaySizeControl.sliderControl.activeFocus)
      keyClick(Qt.Key_Right)
      compare(panel.displaySizePercentChosen, 105)
      panel.displaySizePercent = 100
    }

    function test_drag_with_live_geometry(data) {
      const panel = fixture.part_settingsPanel
      panel.displaySizePercent = data.initial
      panel.theme = liveTheme
      // Match the centered, screen-bounded production Settings geometry.
      scene.width = Qt.binding(function() { return Math.min(Math.round(liveTheme.paletteWidth * 1.3), 1840) })
      scene.height = Qt.binding(function() { return Math.min(Math.round(liveTheme.paletteHeight * 1.35), 896) })
      scene.x = Qt.binding(function() { return (windowContent.width - scene.width) / 2 })
      scene.y = Qt.binding(function() { return (windowContent.height - scene.height) / 2 })
      panel.visible = true
      tryCompare(panel.searchControl, "activeFocus", true)
      typeText("display size")

      const slider = panel.displaySizeControl.sliderControl
      verify(waitForRendering(slider))
      const start = slider.mapToItem(windowContent, slider.handle.x + slider.handle.width / 2, slider.height / 2)
      const travel = slider.availableWidth - slider.handle.width
      const widthBefore = slider.width
      let previous = data.initial
      let backwards = 0
      mousePress(windowContent, start.x, start.y)
      try {
        for (let index = 1; index <= 20; index++) {
          mouseMove(windowContent, start.x + data.direction * travel * index / 50, start.y, 20)
          verify(waitForPolish(windowContent.Window.window))
          const current = panel.displaySizePercent
          if ((current - previous) * data.direction < 0) backwards++
          previous = current
        }
      } finally {
        mouseRelease(windowContent, start.x + data.direction * travel * 0.4, start.y)
        scene.x = 0
        scene.y = 0
      }
      compare(backwards, 0, "dragging in one direction must not reverse the percentage")
      compare(previous, data.final, "the value follows the initial track geometry during the drag")
      compare(panel.displaySizePercent, data.final, "releasing must not jump to the resized track's value")
      verify(slider.width !== widthBefore, "the real slider resizes during this journey")
    }

  }
}
