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
  QtObject {
    id: displayHost
    property var part_appTheme: liveTheme
    property var part_displayScaleStore: QtObject {
      property bool legacy: false
      property int legacyPercent: 100
    }
    property int launcherWidthPercent: 100
    property int launcherHeightPercent: 100
    property int displaySizePercent: scene.part_settingsPanel.displaySizePercent
    property bool settingsOpen: scene.part_settingsPanel.visible
    property bool extensionManagementOpen: false
  }
  CommandDeck.WindowDisplay { id: display; host: displayHost; width: 1440; height: 900 }

  EventPathTestCase {
    fixture: scene
    name: "CommandDeck_display_size"

    function test_fitting_limit_feedback_preserves_full_slider_range() {
      const panel = fixture.part_settingsPanel
      panel.displaySizeDescription = Qt.binding(function() { return display.description })
      panel.maximumFittingPercent = Qt.binding(function() { return display.maximumFittingPercent })
      panel.displaySizePercent = 130
      panel.visible = true
      waitForValue(panel.searchControl, "activeFocus", true)
      typeText("display size")
      keyClick(Qt.Key_Return)
      keyClick(Qt.Key_Tab)

      const slider = panel.displaySizeControl.sliderControl
      const marker = findChild(panel, "displaySizeLimitMarker")
      waitForValue(slider, "activeFocus", true)
      compare(panel.maximumFittingPercent, 135)
      verify(marker.visible && marker.width > 0)
      verify(slider.Accessible.description.indexOf("maximum that fits this screen: 135%") >= 0)
      keyClick(Qt.Key_Right)
      keyClick(Qt.Key_Right)
      compare(panel.displaySizePercent, 140, "keyboard zoom can exceed the marked limit")
      verify(findChild(panel, "displaySizeRow").help.indexOf("limited to fit (max 135% on this screen)") >= 0)
      verify(waitForFrame(slider))
      mouseClick(slider, slider.width - slider.rightPadding - slider.handle.width / 2, slider.height / 2)
      compare(panel.displaySizePercent, 150, "the dimmed track still accepts mouse input")

      display.width = 3840
      display.height = 2160
      waitForValue(panel, "maximumFittingPercent", 150)
      verify(!marker.visible)
      verify(slider.Accessible.description.indexOf("maximum that fits this screen: 150%") >= 0)
      verify(findChild(panel, "displaySizeRow").help.indexOf("limited to fit") < 0)

      panel.maximumFittingPercent = 150
      panel.displaySizeDescription = ""
      display.width = 1440
      display.height = 900
    }

    function test_no_fitting_percentage_feedback() {
      const panel = fixture.part_settingsPanel
      panel.displaySizeDescription = Qt.binding(function() { return display.description })
      panel.maximumFittingPercent = Qt.binding(function() { return display.maximumFittingPercent })
      display.width = 320
      display.height = 240
      panel.visible = true
      waitForValue(panel.searchControl, "activeFocus", true)
      typeText("display size")
      keyClick(Qt.Key_Return)
      keyClick(Qt.Key_Tab)

      const slider = panel.displaySizeControl.sliderControl
      const marker = findChild(panel, "displaySizeLimitMarker")
      waitForValue(slider, "activeFocus", true)
      waitForValue(panel, "maximumFittingPercent", 0)
      verify(marker.visible)
      fuzzyCompare(marker.width, marker.parent.width, 0.1, "the entire track is constrained")
      verify(slider.Accessible.description.indexOf("no percentage fits this screen") >= 0)
      verify(findChild(panel, "displaySizeRow").help.indexOf("no percentage fits on this screen") >= 0)
      verify(slider.Accessible.description.indexOf("maximum that fits") < 0)
      keyClick(Qt.Key_Right)
      verify(slider.activeFocus)

      panel.maximumFittingPercent = 150
      panel.displaySizeDescription = ""
      display.width = 1440
      display.height = 900
    }

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
      waitForValue(panel.searchControl, "activeFocus", true)
      typeText("display size")
      compare(panel.currentPage, "customization")
      keyClick(Qt.Key_Return)
      keyClick(Qt.Key_Tab)

      const slider = panel.displaySizeControl.sliderControl
      const reset = panel.displaySizeResetControl
      waitForValue(slider, "activeFocus", true)
      keyClick(Qt.Key_Right)
      compare(panel.displaySizePercentChosen, 105)
      keyClick(Qt.Key_Left)
      compare(panel.displaySizePercentChosen, 100)
      verify(!reset.enabled)
      keyClick(Qt.Key_Right)
      keyClick(Qt.Key_Tab)
      waitForValue(reset, "activeFocus", true)
      keyClick(Qt.Key_Backtab)
      waitForValue(slider, "activeFocus", true)
      keyClick(Qt.Key_Tab)
      waitForValue(reset, "activeFocus", true)
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
      waitForValue(panel.searchControl, "activeFocus", true)
      typeText("display size")
      const slider = panel.displaySizeControl.sliderControl
      verify(waitForFrame(slider))
      mouseClick(slider, slider.width * 0.9, slider.height / 2)
      verify(panel.displaySizePercentChosen > 115 && panel.displaySizePercentChosen <= 150)
      compare(panel.displaySizePercentChosen % 5, 0)
      compare(panel.displaySizeControl.value, panel.displaySizePercentChosen)

      if (data.tabToReset) {
        keyClick(Qt.Key_Tab)
        waitForValue(panel.displaySizeResetControl, "activeFocus", true)
      }
      verify(waitForFrame(panel.displaySizeResetControl))
      mouseClick(panel.displaySizeResetControl)
      compare(panel.displaySizePercentChosen, 100)
      verify(panel.searchControl.activeFocus)
      typeText(" percent")
      compare(panel.searchControl.text, "display size percent")
      keyClick(Qt.Key_Escape)
      keyClick(Qt.Key_Escape)
      verify(!panel.visible)
      panel.visible = true
      waitForValue(panel.searchControl, "activeFocus", true)
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
      waitForValue(panel.searchControl, "activeFocus", true)
      typeText("display size")
      keyClick(Qt.Key_Return)
      keyClick(Qt.Key_Tab)
      waitForValue(panel.displaySizeControl.sliderControl, "activeFocus", true)
      verify(panel.displaySizeControl.sliderControl.Accessible.description.indexOf("100 keeps Previous sizing") >= 0)
      verify(panel.displaySizeControl.sliderControl.Accessible.description.indexOf("100 is Recommended") < 0)
      keyClick(Qt.Key_Right)
      keyClick(Qt.Key_Left)
      verify(panel.displaySizeLegacy, "returning to 100 percent keeps the profile")
      keyClick(Qt.Key_Tab)
      waitForValue(panel.displaySizeResetControl, "activeFocus", true)
      keyClick(Qt.Key_Space)
      verify(!panel.displaySizeLegacy)
      verify(panel.displaySizeControl.sliderControl.Accessible.description.indexOf("100 is Recommended") >= 0)
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
      waitForValue(panel.searchControl, "activeFocus", true)
      typeText("display size")

      const slider = panel.displaySizeControl.sliderControl
      verify(waitForFrame(slider))
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
