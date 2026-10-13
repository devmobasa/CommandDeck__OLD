import QtQuick

// Proves that text from extensions, results, and files is shown as written:
// finds the label that displays a string and measures it against the literal
// string. Markup rendered as rich text measures differently (tags vanish,
// sizes and entities change).
Item {
  id: probe

  TextMetrics { id: metrics }

  // The innermost item under `root` that displays `text`: a Text or a stock
  // control's label, never the control itself.
  function labelShowing(root, text) {
    if (!root) return null

    const children = root.children || []
    for (let index = 0; index < children.length; index++) {
      const found = probe.labelShowing(children[index], text)
      if (found) return found
    }
    return root.text === text && root.contentItem === undefined ? root : null
  }

  // Empty when `text` is shown as written under `root`; otherwise why not.
  function problem(root, text) {
    const label = probe.labelShowing(root, text)
    if (!label) return "nothing under " + root + " shows " + text

    metrics.font = label.font
    metrics.text = text
    const width = label.implicitWidth - (label.leftPadding || 0) - (label.rightPadding || 0)
    return Math.abs(width - metrics.advanceWidth) <= 2
      ? ""
      : text + " renders " + width + " wide instead of " + metrics.advanceWidth
  }
}
