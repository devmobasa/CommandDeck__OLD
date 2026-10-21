import QtQuick
import QtTest
import "../../qml/common" as CommandDeck

// The undo strip with real clicks: a change's message replaces "Hidden …" in
// the text and the accessible names, the Undo button requests the undo, and a
// newer change restarts the hold so it gets the full time on screen.
EventPathFixture {
  id: scene

  Component {
    id: stripComponent
    CommandDeck.HiddenUndoBar {
      theme: scene.part_theme
    }
  }

  EventPathTestCase {
    fixture: scene
    name: "CommandDeck_undo_strip"

    function test_a_change_message_offers_undo_and_a_full_hold() {
      const strip = createTemporaryObject(stripComponent, fixture,
        { width: 600, x: 20, y: 20, z: 100, holdMs: 300000, title: "notes.txt" })
      verify(strip !== null)
      compare(strip.displayText, "Hidden notes.txt")
      compare(strip.undoControl.Accessible.name, "Undo hiding notes.txt")

      let undos = 0
      let dismissals = 0
      strip.undoRequested.connect(() => undos++)
      strip.dismissed.connect(() => dismissals++)
      strip.message = "Moved “Lunch?” to Reply Later"
      strip.active = true
      waitForFrame(strip)
      compare(strip.displayText, "Moved “Lunch?” to Reply Later")
      compare(strip.Accessible.name, "Moved “Lunch?” to Reply Later. Undo is available.")
      compare(strip.undoControl.Accessible.name, "Undo: Moved “Lunch?” to Reply Later")

      mouseClick(strip.undoControl)
      compare(undos, 1)

      const hold = Array.from(strip.data).find(item => typeof item.restart === "function" && item.interval === strip.holdMs)
      verify(hold !== undefined, "the strip owns its hold timer")
      strip.holdMs = 1000
      let restarts = 0
      const countRestart = () => { if (hold.running) restarts++ }
      hold.runningChanged.connect(countRestart)
      const replacedAt = Date.now()
      let dismissedAt = 0
      strip.dismissed.connect(() => { dismissedAt = Date.now() })
      strip.message = "Set “Lunch?” aside"
      compare(restarts, 1, "a newer change restarts the hold")
      hold.runningChanged.disconnect(countRestart)
      compare(dismissals, 0)
      verify(hold.running, "the replacement keeps its hold running")
      waitForCondition(() => dismissals === 1, 30000, "the same replacement still hides itself")
      verify(dismissedAt - replacedAt >= strip.holdMs - 16,
        "the replacement gets its full hold; dismissed after " + (dismissedAt - replacedAt) + " ms")
    }

    // Clicking Undo or ✕ leaves typing in search; Tab still reaches both
    // buttons, and Space activates the focused one.
    function test_clicking_the_strip_keeps_search_focused() {
      const strip = createTemporaryObject(stripComponent, fixture,
        { width: 600, x: 20, y: 400, z: 100, message: "Marked “Lunch?” seen", active: true })
      verify(strip !== null)
      let undos = 0
      let dismissals = 0
      strip.undoRequested.connect(() => undos++)
      strip.dismissed.connect(() => dismissals++)
      const input = fixture.part_searchField.inputControl
      input.text = ""
      input.forceActiveFocus()
      waitForFrame(strip)

      mouseClick(strip.undoControl)
      compare(undos, 1)
      verify(input.activeFocus, "clicking Undo leaves search focused")
      typeText("lunch")
      compare(input.text, "lunch")

      strip.undoControl.forceActiveFocus()
      keyClick(Qt.Key_Tab)
      verify(strip.dismissControl.activeFocus, "Tab moves on to the dismiss button")
      keyClick(Qt.Key_Space)
      compare(dismissals, 1)

      input.forceActiveFocus()
      mouseClick(strip.dismissControl)
      compare(dismissals, 2)
      verify(input.activeFocus, "clicking ✕ leaves search focused")
      input.text = ""
    }
  }
}
