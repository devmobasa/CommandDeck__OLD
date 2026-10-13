import QtQuick
import QtTest
import "../../qml/common" as CommandDeck

// Tab and Shift+Tab from every fenced control reach the nearest ancestor that
// declares settingsTabFence, which decides where focus goes; controls outside
// a fence, and buttons that turn the fence off, keep Qt's focus chain.
Item {
  width: 640
  height: 720

  CommandDeck.Theme { id: theme; loadShellTokens: false }

  Item {
    id: fence
    readonly property bool settingsTabFence: true
    property var moves: []
    width: 640
    height: 600

    function cycleFocus(forward) {
      moves = moves.concat([forward])
    }

    Column {
      spacing: 2

      CommandDeck.DeckButton { id: button; theme: theme; text: "Button" }
      CommandDeck.SettingsToggle { id: toggle; theme: theme; text: "Toggle" }
      CommandDeck.SettingsCheck { id: check; theme: theme; text: "Check" }
      CommandDeck.SettingsField { id: field; theme: theme }
      CommandDeck.SettingsTextArea { id: area; theme: theme }
      CommandDeck.SettingsNumber { id: number; theme: theme }
      CommandDeck.SettingsSlider { id: slider; theme: theme; width: 240 }
      CommandDeck.SettingsChoice {
        id: choice
        theme: theme
        options: [{ id: "low", title: "Low" }, { id: "high", title: "High" }]
      }
      CommandDeck.SettingsNav {
        id: nav
        theme: theme
        pages: [{ id: "general", title: "General" }, { id: "search", title: "Search" }]
        currentId: "general"
      }
      Row {
        CommandDeck.DeckButton { id: ownCycling; theme: theme; tabFence: false; text: "Own cycling" }
        CommandDeck.DeckButton { id: afterOwnCycling; theme: theme; tabFence: false; text: "After" }
      }
    }
  }

  Row {
    y: 640
    CommandDeck.DeckButton { id: unfenced; theme: theme; text: "Outside" }
    CommandDeck.DeckButton { id: afterUnfenced; theme: theme; text: "Next" }
  }

  TestCase {
    name: "TabFence"
    when: windowShown

    function init() {
      fence.moves = []
    }

    function test_fenced_controls_hand_tab_to_the_fence_data() {
      return [
        { tag: "DeckButton", control: () => button },
        { tag: "SettingsToggle", control: () => toggle },
        { tag: "SettingsCheck", control: () => check },
        { tag: "SettingsField", control: () => field },
        { tag: "SettingsTextArea", control: () => area },
        { tag: "SettingsNumber", control: () => number },
        { tag: "SettingsSlider", control: () => slider.sliderControl },
        { tag: "SettingsChoice", control: () => choice },
        { tag: "SettingsNav", control: () => nav }
      ]
    }

    function test_fenced_controls_hand_tab_to_the_fence(data) {
      const control = data.control()
      control.forceActiveFocus()
      verify(control.activeFocus)

      keyClick(Qt.Key_Tab)
      keyClick(Qt.Key_Tab, Qt.ShiftModifier)
      keyClick(Qt.Key_Backtab)
      compare(fence.moves, [true, false, false])
      verify(control.activeFocus, "the fence alone moves focus")
      compare(area.text, "", "Tab never types into the text area")
    }

    function test_other_keys_stay_with_the_control() {
      field.forceActiveFocus()
      keyClick(Qt.Key_A)
      compare(field.text, "a")
      compare(fence.moves, [])
      field.clear()
    }

    function test_editable_number_hands_tab_to_the_fence() {
      mouseClick(number.contentItem)
      verify(number.contentItem.activeFocus, "clicking the number focuses its editor")

      keyClick(Qt.Key_Tab)
      keyClick(Qt.Key_Tab, Qt.ShiftModifier)
      keyClick(Qt.Key_Backtab)
      compare(fence.moves, [true, false, false])
      verify(number.contentItem.activeFocus, "the fence alone moves editor focus")
    }

    function test_a_button_without_the_fence_follows_the_focus_chain() {
      ownCycling.forceActiveFocus()
      keyClick(Qt.Key_Tab)
      compare(fence.moves, [])
      verify(afterOwnCycling.activeFocus)
    }

    function test_a_control_outside_a_fence_follows_the_focus_chain() {
      unfenced.forceActiveFocus()
      keyClick(Qt.Key_Tab)
      compare(fence.moves, [])
      verify(afterUnfenced.activeFocus)
    }
  }
}
