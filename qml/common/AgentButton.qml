import QtQuick

// The assistant conversation owns its own focus cycling, so it uses the shared
// CommandDeck button without the settings tab fence.
DeckButton {
  tabFence: false
}
