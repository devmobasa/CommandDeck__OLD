.pragma library

// One keyboard contract for every list. Up and Down, or Ctrl+P and Ctrl+N,
// move one row; Page Up and Page Down move a page; Ctrl+Home and Ctrl+End
// reach the ends, and so do Home and End unless a text field has focus, which
// keeps them for its cursor. Keys with other modifiers are left to the focused
// control, so Shift+arrows still select text. A surface that binds Ctrl+P to
// another command (clipboard history pins with it) turns off both letters.

// The move a key asks for: { delta } rows, { edge: -1 } for the first row or
// { edge: 1 } for the last, or null when the key is not list navigation.
function moveFor(event, pageSize, textInput, controlLetters) {
  const modifiers = event.modifiers & ~Qt.KeypadModifier
  const plain = modifiers === Qt.NoModifier
  const control = modifiers === Qt.ControlModifier
  const letters = control && controlLetters !== false
  const edges = control || (plain && textInput !== true)

  if ((plain && event.key === Qt.Key_Up) || (letters && event.key === Qt.Key_P)) return { delta: -1 }
  if ((plain && event.key === Qt.Key_Down) || (letters && event.key === Qt.Key_N)) return { delta: 1 }
  if (plain && event.key === Qt.Key_PageUp) return { delta: -pageSize }
  if (plain && event.key === Qt.Key_PageDown) return { delta: pageSize }
  if (edges && event.key === Qt.Key_Home) return { edge: -1 }
  if (edges && event.key === Qt.Key_End) return { edge: 1 }
  return null
}

// Applies a list key to `list`, which moves its own selection through
// step(delta), first() and last() and may set controlLetters: false, and
// accepts the key.
function handle(event, list, pageSize, textInput) {
  const move = moveFor(event, pageSize, textInput, list.controlLetters)
  if (move === null) return false

  if (move.edge < 0) list.first()
  else if (move.edge > 0) list.last()
  else list.step(move.delta)
  event.accepted = true
  return true
}

// The same for a list whose rows 0 to count - 1 are selected by index:
// select(next) receives the row, clamped to the ends.
function handleIndex(event, index, count, pageSize, textInput, select) {
  const clamp = value => Math.max(0, Math.min(count - 1, value))
  return handle(event, {
    step: delta => { if (count > 0) select(clamp(index + delta)) },
    first: () => { if (count > 0) select(0) },
    last: () => { if (count > 0) select(count - 1) }
  }, pageSize, textInput)
}

// A page is the rows that fit in the view, less one kept for context.
function pageSize(viewHeight, rowHeight) {
  return Math.max(1, Math.floor(viewHeight / Math.max(1, rowHeight)) - 1)
}
