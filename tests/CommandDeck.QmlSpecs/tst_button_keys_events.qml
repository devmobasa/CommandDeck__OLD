import QtQuick
import QtTest
import "../../qml/common" as CommandDeck

// Enter, Return, and Space press a focused CommandDeck button exactly once,
// whatever Qt platform theme is loaded. Qt's generic theme maps only Space to
// buttons; the GTK theme Omarchy sets also maps Enter, and must not press twice.
// LiteralButton covers the extension and file-chip buttons with plain-text labels.
Item {
  width: 600
  height: 250

  CommandDeck.Theme { id: theme; loadShellTokens: false }

  CommandDeck.DeckButton {
    id: deckButton
    property int clicks: 0
    theme: theme
    text: "Deck"
    onClicked: clicks++
  }

  CommandDeck.AgentButton {
    id: agentButton
    property int clicks: 0
    y: 50
    theme: theme
    text: "Agent"
    onClicked: clicks++
  }

  CommandDeck.ClipboardActionButton {
    id: clipboardButton
    property int clicks: 0
    y: 100
    theme: theme
    text: "Refresh"
    onClicked: clicks++
  }

  CommandDeck.LiteralButton {
    id: literalButton
    property int clicks: 0
    y: 150
    text: "<b>Save</b>"
    onClicked: clicks++
  }

  TestCase {
    name: "ButtonKeys"
    when: windowShown

    function test_keys_press_a_focused_button_once_data() {
      return [
        { tag: "DeckButton", button: deckButton },
        { tag: "AgentButton", button: agentButton },
        { tag: "ClipboardActionButton", button: clipboardButton },
        { tag: "LiteralButton", button: literalButton }
      ]
    }

    function test_keys_press_a_focused_button_once(data) {
      const button = data.button
      button.clicks = 0
      button.forceActiveFocus()
      verify(button.activeFocus)

      keyClick(Qt.Key_Return)
      compare(button.clicks, 1)
      keyClick(Qt.Key_Enter, Qt.KeypadModifier)
      compare(button.clicks, 2)
      keyClick(Qt.Key_Space)
      compare(button.clicks, 3)

      verify(button.activeFocus)
      verify(!button.down)
    }
  }
}
