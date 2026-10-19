.pragma library
.import "UiConstants.js" as Ui

// Direct result shortcuts. Each chord asks for a key slot; the launcher fills
// the slots for the selected result (DeckDisplayActions.shortcutActions), and
// the result list, footer and action menu all read the same entries.

// The key slot a chord asks for, or "" when the chord has no slot.
function slotForKey(key, modifiers) {
  const plain = modifiers & ~Qt.KeypadModifier
  const control = plain === Qt.ControlModifier
  const controlShift = plain === (Qt.ControlModifier | Qt.ShiftModifier)
  const altShift = plain === (Qt.AltModifier | Qt.ShiftModifier)
  const enter = key === Qt.Key_Return || key === Qt.Key_Enter

  if (control && enter) return Ui.ShortcutSlot.Secondary
  if (controlShift && enter) return Ui.ShortcutSlot.Tertiary
  if (controlShift && key === Qt.Key_C) return Ui.ShortcutSlot.Copy
  if (control && key === Qt.Key_D) return Ui.ShortcutSlot.Favorite
  if (altShift && key === Qt.Key_A) return Ui.ShortcutSlot.Alias
  if (altShift && key === Qt.Key_D) return Ui.ShortcutSlot.Hide
  if (altShift && key === Qt.Key_R) return Ui.ShortcutSlot.ResetRanking
  return ""
}

// The entry filling a slot, or null.
function entryForSlot(shortcuts, slot) {
  return slot ? (shortcuts || []).find(value => value.slot === slot) || null : null
}

// The chord the action menu shows for an action: a fixed meaning first, then
// the second or third action slot, else "" (the menu shows Ctrl+number).
function labelFor(shortcuts, actionId) {
  const entries = (shortcuts || []).filter(value => value.id === actionId)
  const fixed = entries.find(value =>
    value.slot !== Ui.ShortcutSlot.Secondary && value.slot !== Ui.ShortcutSlot.Tertiary)
  const any = fixed || entries[0]
  return any ? any.keyLabel : ""
}
