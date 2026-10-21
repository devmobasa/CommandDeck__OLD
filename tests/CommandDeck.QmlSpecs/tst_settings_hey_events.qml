import QtQuick
import QtTest
import "../../qml/common" as CommandDeck
import "../../qml/common/Protocol.js" as Protocol

// Settings → Search → HEY mail with real key events: searching Settings finds
// the card, Tab reaches its picker, and choosing where threads open saves at
// once without touching the other preferences.
EventPathFixture {
  id: scene

  EventPathTestCase {
    fixture: scene
    name: "CommandDeck_settings_hey"

    property var originalSnapshot: null

    function cleanup() {
      if (originalSnapshot !== null) fixture.part_settingsPanel.snapshot = originalSnapshot
      originalSnapshot = null
      fixture.part_settingsPanel.visible = false
    }

    function test_choosing_where_hey_threads_open_saves_at_once() {
      const panel = fixture.part_settingsPanel
      originalSnapshot = panel.snapshot
      panel.visible = true
      waitForValue(panel.searchControl, "activeFocus", true)
      typeText("hey")
      compare(panel.currentPage, "search")
      const card = panel.part_heyCard
      verify(card.matchesSearch && card.visible, "searching Settings for hey finds the card")
      verify(!panel.part_behaviorCard.visible, "cards that do not mention HEY are filtered out")

      const picker = card.openTargetControl
      compare(picker.currentOption.id, Protocol.HeyOpenTargets.Web, "threads open in the web app by default")
      keyClick(Qt.Key_Return)
      for (let tab = 0; tab < 20 && !picker.activeFocus; tab++) keyClick(Qt.Key_Tab)
      verify(picker.activeFocus, "Tab reaches the picker")

      keyClick(Qt.Key_Space)
      waitForValue(picker.popup, "visible", true)
      keyClick(Qt.Key_Down)
      keyClick(Qt.Key_Return)
      waitForValue(picker.popup, "visible", false)
      verify(panel.changedPreferences !== null)
      compare(panel.changedPreferences.heyOpenTarget, Protocol.HeyOpenTargets.Terminal)
      compare(panel.changedPreferences.sources.length, 2, "other preferences are kept")
      verify(picker.activeFocus, "choosing returns focus to the picker")

      // The daemon's acknowledged snapshot drives the picker, not the other way round.
      const acknowledged = JSON.parse(JSON.stringify(panel.snapshot))
      acknowledged.preferences.heyOpenTarget = Protocol.HeyOpenTargets.Terminal
      panel.snapshot = acknowledged
      compare(picker.currentOption.id, Protocol.HeyOpenTargets.Terminal)
      acknowledged.preferences.heyOpenTarget = Protocol.HeyOpenTargets.Web
      panel.snapshot = JSON.parse(JSON.stringify(acknowledged))
      compare(picker.currentOption.id, Protocol.HeyOpenTargets.Web)
    }
  }
}
