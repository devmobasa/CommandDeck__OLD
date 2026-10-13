.pragma library
.import "UiConstants.js" as Ui
.import "Protocol.js" as Protocol

function focusBoundary(root, forward) {
  if (forward) {
    root.part_closeButton.forceActiveFocus()
    return
  }
  let current = root.part_closeButton
  let last = root.part_closeButton
  for (let count = 0; count < 256; count++) {
    const next = current.nextItemInFocusChain(true)
    if (!next || next === root.part_closeButton || !root.containsFocusItem(next)) break
    last = next
    current = next
  }
  last.forceActiveFocus()
}

function containsFocusItem(root, item) {
  let current = item
  while (current) {
    if (current === root) return true
    current = current.parent
  }
  return false
}
