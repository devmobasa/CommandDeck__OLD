.pragma library

// Tab and Shift+Tab stay inside a dialog that declares `settingsTabFence`:
// the nearest such ancestor of `start` decides where focus goes next, through
// its cycleFocus(forward). Without one the key is left to Qt's focus chain.
// Returns whether the key was handled.
function route(start, event) {
  if (event.key !== Qt.Key_Tab && event.key !== Qt.Key_Backtab) return false

  let fence = start
  while (fence && fence.settingsTabFence !== true) fence = fence.parent
  if (!fence) return false

  fence.cycleFocus(event.key === Qt.Key_Tab && !(event.modifiers & Qt.ShiftModifier))
  event.accepted = true
  return true
}
