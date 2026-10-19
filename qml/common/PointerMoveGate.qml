import QtQml

// Tells a real pointer move from a hover update with the pointer standing
// still. Rows that scroll or appear under a resting pointer receive hover
// events at the same window position; only a changed position is a move. The
// first position after a reset is only recorded, so a launcher opening under
// the pointer does not select the row there.
QtObject {
  id: root

  property real lastX: NaN
  property real lastY: NaN

  function moved(x, y) {
    const changed = !isNaN(lastX) && (x !== lastX || y !== lastY)
    lastX = x
    lastY = y
    return changed
  }

  function reset() {
    lastX = NaN
    lastY = NaN
  }
}
