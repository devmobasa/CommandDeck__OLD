import QtQuick

Item {
  id: root
  required property var theme
  // The search controller already delays this signal for fast queries.
  property bool loading: false
  property bool active: false
  readonly property bool animating: movement.running
  implicitHeight: Math.max(2, Math.round(2 * root.theme.scale))
  visible: active
  Accessible.role: Accessible.ProgressBar
  Accessible.name: "Searching"

  onLoadingChanged: {
    hideDelay.stop()
    if (loading) active = true
    else hideDelay.restart()
  }

  Timer {
    id: hideDelay
    interval: 100
    onTriggered: root.active = false
  }

  Rectangle {
    anchors.fill: parent
    color: root.theme.surfaceStrong
  }

  Rectangle {
    anchors.fill: parent
    visible: root.theme.reduceMotion
    color: root.theme.accent
  }

  Rectangle {
    id: segment
    width: Math.max(Math.round(32 * root.theme.scale), root.width * 0.2)
    height: parent.height
    color: root.theme.accent
    visible: !root.theme.reduceMotion
  }

  clip: true
  NumberAnimation {
    id: movement
    target: segment
    property: "x"
    from: -segment.width
    to: root.width
    duration: 1000
    loops: Animation.Infinite
    running: root.visible && !root.theme.reduceMotion
  }
}
