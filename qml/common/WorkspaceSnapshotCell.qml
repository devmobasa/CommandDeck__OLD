pragma ComponentBehavior: Bound
import QtQuick
import Quickshell.Wayland

// One still, with a deadline and explicit native-context destruction. Small
// display dimensions do not imply a small compositor capture allocation.
Item {
  id: root
  property var source: null
  property bool permitted: false
  property bool expired: false
  readonly property var captureItem: loader.item
  readonly property bool hasContent: captureItem !== null && captureItem.hasContent
  readonly property size frameSize: captureItem ? captureItem.frameSize : Qt.size(0, 0)
  readonly property bool allocated: loader.item !== null

  onPermittedChanged: if (!permitted) expired = true

  Loader {
    id: loader
    anchors.fill: parent
    active: root.permitted && !root.expired && root.source !== null
    sourceComponent: Item {
      id: frame
      readonly property bool hasContent: capture.hasContent
      readonly property size frameSize: capture.sourceSize
      property bool received: false

      ScreencopyView {
        id: capture
        anchors.fill: parent
        captureSource: root.source
        live: false
        paintCursor: false
        onHasContentChanged: {
          if (hasContent) {
            frame.received = true
            deadline.stop()
            retention.start()
          } else if (frame.received) {
            root.expired = true
          }
        }
        onSourceSizeChanged: {
          if (sourceSize.width > 4096 || sourceSize.height > 4096
              || sourceSize.width * sourceSize.height > 8388608) root.expired = true
        }
      }
      Timer { id: deadline; interval: 1200; running: true; onTriggered: root.expired = true }
      Timer { id: retention; interval: 15000; onTriggered: root.expired = true }
    }
  }
}
