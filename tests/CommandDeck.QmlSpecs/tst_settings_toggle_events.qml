import QtQuick
import QtTest

EventPathFixture {
  id: scene
  SignalSpy { id: changes; target: scene.part_settingsPanel; signalName: "preferencesChanged" }

  EventPathTestCase {
    fixture: scene
    name: "CommandDeck_settings_toggle"

    function test_choice_with_keyboard_and_mouse_data() {
      const rows = []
      for (const choice of [
        { field: "topMatches", label: "Top Matches", page: "search", initial: true },
        { field: "rememberSearches", label: "Remember searches", page: "customization", initial: false }
      ]) {
        for (const key of [
          { name: "return", code: Qt.Key_Return, modifiers: Qt.NoModifier },
          { name: "keypad_enter", code: Qt.Key_Enter, modifiers: Qt.KeypadModifier },
          { name: "space", code: Qt.Key_Space, modifiers: Qt.NoModifier }
        ]) {
          rows.push(Object.assign({}, choice, {
            tag: choice.field + "_" + key.name, key: key.code, modifiers: key.modifiers
          }))
        }
      }
      return rows
    }

    function test_choice_with_keyboard_and_mouse(data) {
      const panel = fixture.part_settingsPanel
      const original = panel.snapshot
      panel.snapshot = Object.assign({}, original, {
        preferences: Object.assign({}, original.preferences, { [data.field]: null }),
        defaults: { [data.field]: data.initial }
      })
      panel.visible = true
      tryCompare(panel.searchControl, "activeFocus", true)
      typeText(data.label.toLowerCase())
      compare(panel.currentPage, data.page)
      const toggle = data.field === "topMatches"
        ? findChild(panel, "topMatchesToggle") : panel.rememberSearchesControl
      verify(toggle.visible)
      compare(toggle.checked, data.initial)
      compare(toggle.Accessible.name, data.label)

      keyClick(Qt.Key_Return)
      tryCompare(panel.navigationControl, "activeFocus", true)
      for (let index = 0; index < 10 && !toggle.activeFocus; index++) keyClick(Qt.Key_Tab)
      verify(toggle.activeFocus, data.label + " is reachable in the tab order")
      changes.clear()

      keyPress(data.key, data.modifiers)
      keyRelease(data.key, data.modifiers)
      compare(changes.count, 1, "one change for the complete key press and release")
      compare(panel.changedPreferences[data.field], !data.initial)
      compare(panel.changedPreferences.sources.length, 2)
      panel.snapshot = Object.assign({}, panel.snapshot, { preferences: panel.changedPreferences })
      verify(toggle.activeFocus, "saving preserves the focused toggle")
      keyClick(Qt.Key_Tab, Qt.ShiftModifier)
      verify(!toggle.activeFocus)
      keyClick(Qt.Key_Tab)
      tryCompare(toggle, "activeFocus", true)

      keyClick(data.key, data.modifiers)
      compare(changes.count, 2, "the next press changes the choice once again")
      compare(panel.changedPreferences[data.field], data.initial)
      panel.snapshot = Object.assign({}, panel.snapshot, { preferences: panel.changedPreferences })
      keyClick(Qt.Key_F, Qt.ControlModifier)
      tryCompare(panel.searchControl, "activeFocus", true)
      keyClick(Qt.Key_End)
      waitForRendering(panel)

      mouseClick(toggle, toggle.width - toggle.pillWidth / 2, toggle.height / 2)
      compare(changes.count, 3, "a mouse click changes the choice once")
      compare(panel.changedPreferences[data.field], !data.initial)
      panel.snapshot = Object.assign({}, panel.snapshot, { preferences: panel.changedPreferences })
      typeText("x")
      compare(panel.searchControl.text, data.label.toLowerCase() + "x")
      verify(panel.searchControl.activeFocus, "typing after a click returns to search")
      compare(changes.count, 3)

      keyClick(Qt.Key_Escape)
      keyClick(Qt.Key_Escape)
      verify(!panel.visible)
      panel.visible = true
      tryCompare(panel.searchControl, "activeFocus", true)
      compare(toggle.checked, !data.initial, "reopening reflects the saved choice")
      panel.snapshot = original
    }
  }
}
