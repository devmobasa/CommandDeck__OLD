import QtQuick

// Watch the whole list, including headers, gaps and empty space. Layout changes
// must leave the keyboard selection alone while the pointer rests.
HoverHandler {
  id: root
  required property ListView view
  property bool surfaceVisible: true
  signal indexHovered(int index)

  parent: view
  acceptedButtons: Qt.NoButton
  property PointerMoveGate gate: PointerMoveGate { }
  onSurfaceVisibleChanged: gate.reset()

  onPointChanged: {
    // A local-to-scene round trip can drift by a fraction after re-layout.
    // Keep the event's original scene coordinates for the movement check.
    const position = point.scenePosition
    if (!gate.moved(position.x, position.y)) return
    const contentPosition = view.mapToItem(view.contentItem, point.position.x, point.position.y)
    const index = view.indexAt(contentPosition.x, contentPosition.y)
    if (index >= 0) indexHovered(index)
  }
}
