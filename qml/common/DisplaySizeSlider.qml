import QtQuick

// Applying this slider resizes its own window. Keep each drag anchored to the
// screen coordinates and track width at press time, while sizes still apply live.
SettingsSlider {
  id: root

  readonly property bool dragging: pointer.pressed

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
