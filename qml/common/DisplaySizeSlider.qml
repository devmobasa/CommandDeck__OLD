import QtQuick

// Applying this slider resizes its own window. Keep each drag anchored to the
// screen coordinates and track width at press time, while sizes still apply live.
SettingsSlider {
  id: root

  readonly property bool dragging: pointer.pressed
  property int maximumFittingPercent: 150
  Accessible.description: "75 to 150 percent; 100 is Recommended; "
    + (maximumFittingPercent === 0 ? "no percentage fits this screen without limiting or scrolling"
      : "maximum that fits this screen: " + maximumFittingPercent + "%")

  Rectangle {
    objectName: "displaySizeLimitMarker"
    parent: root.sliderControl.background
    readonly property real fittingPosition: Math.max(0, (root.maximumFittingPercent - root.from) / (root.to - root.from))
    visible: root.maximumFittingPercent < root.to
    x: root.sliderControl.mirrored ? 0 : parent.width * fittingPosition
    width: parent.width * (1 - fittingPosition)
    height: parent.height
    radius: height / 2
    color: root.theme.background
    opacity: 0.7
  }

  MouseArea {
    id: pointer
    parent: root.sliderControl
    anchors.fill: parent
    acceptedButtons: Qt.LeftButton
    preventStealing: true

    property real startX: 0
    property real startValue: 0
    property real travel: 1
    property int direction: 1

    function apply(value) {
      const snapped = root.from + Math.round((value - root.from) / root.stepSize) * root.stepSize
      const next = Math.max(root.from, Math.min(root.to, snapped))
      if (next === root.value) return

      root.value = next
      root.moved()
    }

    function move(mouse) {
      const delta = pointer.mapToGlobal(mouse.x, mouse.y).x - pointer.startX
      pointer.apply(pointer.startValue + delta / pointer.travel * (root.to - root.from) * pointer.direction)
    }

    onPressed: mouse => {
      const slider = root.sliderControl
      const halfHandle = slider.handle.width / 2
      const left = slider.mapToGlobal(slider.leftPadding + halfHandle, 0).x
      const right = slider.mapToGlobal(slider.width - slider.rightPadding - halfHandle, 0).x
      pointer.startX = pointer.mapToGlobal(mouse.x, mouse.y).x
      pointer.travel = Math.max(1, right - left)
      pointer.direction = slider.mirrored ? -1 : 1
      slider.forceActiveFocus(Qt.MouseFocusReason)

      // Track clicks jump to the clicked value; grabbing the handle keeps its
      // offset so the first movement cannot jump underneath the pointer.
      if (mouse.x < slider.handle.x || mouse.x > slider.handle.x + slider.handle.width) {
        const position = (pointer.startX - left) / pointer.travel
        pointer.apply(root.from + (slider.mirrored ? 1 - position : position) * (root.to - root.from))
      }
      pointer.startValue = root.value
    }
    onPositionChanged: mouse => { if (pressed) pointer.move(mouse) }
    onReleased: mouse => pointer.move(mouse)
  }
}
