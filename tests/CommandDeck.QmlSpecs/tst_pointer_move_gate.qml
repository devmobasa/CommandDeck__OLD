import QtQuick
import QtTest
import "../../qml/common" as Deck

// Only a changed window position is a pointer move. The first position after
// a reset is recorded without counting, and a hover update at the recorded
// position (a row moving under a resting pointer) never counts.
TestCase {
  name: "PointerMoveGate"

  Deck.PointerMoveGate { id: gate }

  function init() {
    gate.reset()
  }

  function test_only_a_changed_position_is_a_move() {
    verify(!gate.moved(40, 120), "the first position after opening")
    verify(!gate.moved(40, 120), "a hover update at the same position")
    verify(gate.moved(41, 120))
    verify(gate.moved(41, 160))
    verify(!gate.moved(41, 160))

    gate.reset()
    verify(!gate.moved(300, 10), "a reset forgets the last position")
    verify(gate.moved(300, 11))
  }
}
