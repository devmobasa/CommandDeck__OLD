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

// Qt's generic ScrollView does not scroll arbitrary focused controls into view.
// Keep keyboard navigation visible on every Settings page and nested editor.
function revealControl(root, item) {
  if (!root.visible || !item || !item.activeFocus || !containsFocusItem(root, item)) return
  for (let current = item.parent; current && current !== root; current = current.parent) {
    if (typeof current.contentY !== "number" || typeof current.contentHeight !== "number"
        || !current.contentItem || typeof current.returnToBounds !== "function") continue
    const point = item.mapToItem(current.contentItem, 0, 0)
    const bottom = point.y + item.height
    let position = current.contentY
    if (point.y < position) position = point.y
    else if (bottom > position + current.height) position = bottom - current.height
    current.contentY = Math.max(0, Math.min(position, current.contentHeight - current.height))
  }
}
