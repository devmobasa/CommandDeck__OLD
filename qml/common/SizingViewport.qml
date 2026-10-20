import QtQuick
import QtQuick.Window

// Extremely constrained surfaces retain readable metrics and scroll rather
// than shrinking forever. Ordinary surfaces have no extra scrollable extent.
Rectangle {
  id: root
  property real requestedWidth: width
  property real requestedHeight: height
  default property alias content: canvas.data

  function reveal(item) {
    if (!viewport.interactive || !item || !item.activeFocus) return
    let ancestor = item
    while (ancestor && ancestor !== canvas) ancestor = ancestor.parent
    if (!ancestor) return

    const point = item.mapToItem(canvas, 0, 0)
    if (point.x < viewport.contentX) viewport.contentX = point.x
    else if (point.x + item.width > viewport.contentX + viewport.width)
      viewport.contentX = point.x + item.width - viewport.width
    if (point.y < viewport.contentY) viewport.contentY = point.y
    else if (point.y + item.height > viewport.contentY + viewport.height)
      viewport.contentY = point.y + item.height - viewport.height
    viewport.contentX = Math.max(0, Math.min(viewport.contentX, viewport.contentWidth - viewport.width))
    viewport.contentY = Math.max(0, Math.min(viewport.contentY, viewport.contentHeight - viewport.height))
  }

  property Flickable viewport: Flickable {
    id: viewport
    parent: root
    anchors.fill: parent
    clip: true
    contentWidth: Math.max(width, root.requestedWidth)
    contentHeight: Math.max(height, root.requestedHeight)
    boundsBehavior: Flickable.StopAtBounds
    interactive: contentWidth > width || contentHeight > height
    onInteractiveChanged: {
      if (!interactive) { contentX = 0; contentY = 0 }
    }

    Item {
      id: canvas
      width: viewport.contentWidth
      height: viewport.contentHeight
    }
  }

  property Connections focusEvents: Connections {
    target: root.Window.window
    function onActiveFocusItemChanged() { root.reveal(root.Window.window.activeFocusItem) }
  }
}
