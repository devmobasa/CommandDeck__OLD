import QtQuick
import QtTest

Item {
  width: 1280
  height: 1080
  EventPathFixture { id: scene }

  EventPathTestCase {
    fixture: scene
    name: "CommandDeck_layout_size"

    function openLayout(axis) {
      const panel = fixture.part_settingsPanel
      panel.launcherWidthPercent = 100
      panel.launcherHeightPercent = 100
      panel.visible = true
      tryCompare(panel.searchControl, "activeFocus", true)
      typeText("panel percent")
      keyClick(Qt.Key_Return)
      keyClick(Qt.Key_Tab)
      tryCompare(panel.layoutWidthControl.sliderControl, "activeFocus", true)
      if (axis === "Height") keyClick(Qt.Key_Tab)
      const control = panel["layout" + axis + "Control"]
      tryCompare(control.sliderControl, "activeFocus", true)
      return control
    }

    function test_keyboard_data() {
      const cases = []
      for (const axis of ["Width", "Height"]) {
        for (const key of [Qt.Key_Return, Qt.Key_Enter, Qt.Key_Space]) {
          cases.push({ tag: axis + "-" + key, axis: axis, resetKey: key })
        }
      }
      return cases
    }

    function test_keyboard(data) {
      const control = openLayout(data.axis)
      const panel = fixture.part_settingsPanel
      const otherAxis = data.axis === "Width" ? "Height" : "Width"
      const reset = panel["layout" + data.axis + "ResetControl"]
      keyClick(Qt.Key_Right)
      compare(control.value, 105)
      compare(panel["launcher" + data.axis + "Percent"], 105)
      compare(panel["launcher" + otherAxis + "Percent"], 100)
      keyClick(Qt.Key_Left)
      compare(control.value, 100)
      verify(!reset.enabled)
      for (let step = 0; step < 12; step++) keyClick(Qt.Key_Right)
      compare(control.value, 140)
      for (let step = 0; step < 14; step++) keyClick(Qt.Key_Left)
      compare(control.value, 80)
      keyClick(Qt.Key_Tab)
      tryCompare(reset, "activeFocus", true)
      keyClick(Qt.Key_Backtab)
      tryCompare(control.sliderControl, "activeFocus", true)
      keyClick(Qt.Key_Tab)
      keyClick(data.resetKey)
      compare(control.value, 100)
      verify(!reset.enabled)
      verify(control.sliderControl.activeFocus)
      keyClick(Qt.Key_Escape)
      keyClick(Qt.Key_Escape)
      verify(!panel.visible)
      panel.visible = true
      tryCompare(panel.searchControl, "activeFocus", true)
      compare(control.value, 100)
    }

    function test_mouse_reset_then_type_data() {
      return [{ tag: "width", axis: "Width" }, { tag: "height", axis: "Height" }]
    }

    function test_mouse_reset_then_type(data) {
      const control = openLayout(data.axis)
      const panel = fixture.part_settingsPanel
      const slider = control.sliderControl
      verify(waitForRendering(slider))
      mouseClick(slider, slider.width * 0.9, slider.height / 2)
      verify(control.value > 100 && control.value <= 140)
      compare(control.value % 5, 0)
      compare(panel["launcher" + data.axis + "Percent"], control.value)
      keyClick(Qt.Key_Tab)
      const reset = panel["layout" + data.axis + "ResetControl"]
      tryCompare(reset, "activeFocus", true)
      verify(waitForRendering(reset))
      mouseClick(reset)
      compare(control.value, 100)
      verify(panel.searchControl.activeFocus)
      typeText(" width")
      compare(panel.searchControl.text, "panel percent width")
    }
  }
}
