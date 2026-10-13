pragma ComponentBehavior: Bound
import QtQuick

// Range overlays drawn beneath a plain TextEdit; never markup or re-segment the text.
Item {
  id: overlay
  required property TextEdit target
  required property int start
  required property int length
  property bool selected: false
  property color color: "transparent"
  property color selectedColor: color
  readonly property var segments: computeSegments(target.text, target.width, target.contentHeight, start, length)
  readonly property alias segmentCount: repeater.count
  anchors.fill: target
  z: -0.5

  function segmentAt(index) { return repeater.itemAt(index) }

  FontMetrics {
    id: fontMetrics
    font: overlay.target.font
  }

  function computeSegments(text, layoutWidth, layoutHeight, first, count) {
    if (layoutWidth <= 0 || layoutHeight <= 0 || count <= 0 || first < 0 || first + count > text.length) return []
    const rtlBoxes = computeRtlBoxes(text, first, first + count)
    const parts = []
    for (let index = first; index < first + count; index++) {
      const box = rtlBoxes[index]
      if (box !== undefined) {
        appendSegment(parts, box.x, box.y, box.width, box.height)
        continue
      }

      const head = target.positionToRectangle(index)
      const tail = target.positionToRectangle(index + 1)
      if (Math.abs(head.y - tail.y) >= 1) {
        const code = text.charCodeAt(index)
        if (code === 0x0a || code === 0x0d) continue
        if (isRtlCharacter(code)) {
          const glyphWidth = Math.abs(fontMetrics.advanceWidth(text.charAt(index)))
          appendSegment(parts, head.x - glyphWidth, head.y, glyphWidth, head.height)
        } else {
          appendSegment(parts, head.x, head.y, Math.max(target.contentWidth, head.x + 2) - head.x, head.height)
        }
        continue
      }
      const left = Math.min(head.x, tail.x)
      const right = Math.max(head.x, tail.x)
      if (right - left >= 0.5)
        appendSegment(parts, left, head.y, right - left, Math.max(head.height, tail.height))
    }
    return parts
  }

  function computeRtlBoxes(text, first, end) {
    const boxes = ({})
    let index = first
    if (index > 0 && isRtlCharacter(text.charCodeAt(index)))
      while (index > 0 && isRtlCharacter(text.charCodeAt(index - 1))) index--
    while (index < end) {
      if (!isRtlCharacter(text.charCodeAt(index))) { index++; continue }
      const runStart = index
      while (index < text.length && isRtlCharacter(text.charCodeAt(index))) index++
      const runEnd = index
      const positions = []
      let sameLine = true
      let y = -1
      let height = 0
      for (let position = runStart; position <= runEnd; position++) {
        const rectangle = target.positionToRectangle(position)
        if (y < 0) y = rectangle.y
        else if (Math.abs(y - rectangle.y) >= 1) sameLine = false
        positions.push(rectangle.x)
        height = Math.max(height, rectangle.height)
      }
      if (!sameLine) continue
      positions.sort(function(left, right) { return left - right })
      for (let offset = 0; offset < runEnd - runStart; offset++) {
        const left = positions[positions.length - offset - 2]
        const right = positions[positions.length - offset - 1]
        boxes[runStart + offset] = {x: left, y: y, width: Math.max(2, right - left), height: height}
      }
    }
    return boxes
  }

  function isRtlCharacter(code) {
    if (code >= 0x0660 && code <= 0x0669 || code >= 0x06f0 && code <= 0x06f9) return false
    return code >= 0x0590 && code <= 0x08ff
      || code >= 0xfb1d && code <= 0xfdff
      || code >= 0xfe70 && code <= 0xfeff
  }

  function appendSegment(parts, x, y, width, height) {
    for (let index = 0; index < parts.length; index++) {
      const part = parts[index]
      if (Math.abs(part.y - y) >= 1 || x > part.x + part.width + 1 || part.x > x + width + 1) continue
      const left = Math.min(part.x, x)
      const right = Math.max(part.x + part.width, x + width)
      parts[index] = {x: left, y: Math.min(part.y, y), width: Math.max(2, right - left), height: Math.max(part.height, height)}
      return
    }

    parts.push({x: x, y: y, width: Math.max(2, width), height: height})
  }

  Repeater {
    id: repeater
    model: overlay.segments
    delegate: Rectangle {
      required property var modelData
      x: modelData.x
      y: modelData.y
      width: modelData.width
      height: modelData.height
      radius: 2
      color: overlay.selected ? overlay.selectedColor : overlay.color
    }
  }
}
