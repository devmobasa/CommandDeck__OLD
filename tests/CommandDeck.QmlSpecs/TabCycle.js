.pragma library

// Real-key check of a modal surface's focus trap. From `start`, Tab must
// visit only visible, enabled controls inside `surface` (never the surface
// itself) and come back to `start`; Shift+Tab must then retrace that cycle
// in reverse. `test` is the running TestCase and `window` the surface's
// window. Returns the cycle.

function inside(surface, item) {
  for (let current = item; current; current = current.parent) {
    if (current === surface) return true
  }
  return false
}

function verifyCycle(test, window, surface, start, limit) {
  const focused = () => window.activeFocusItem
  start.forceActiveFocus()
  test.verify(start.activeFocus, "the cycle starts on its first control")

  const cycle = [start]
  for (let step = 0; step < limit; step++) {
    test.keyClick(Qt.Key_Tab)
    const item = focused()
    test.verify(item && item !== surface && inside(surface, item) && item.visible && item.enabled,
      "Tab step " + (step + 1) + " stays on a usable control inside the surface (" + item + ")")
    if (item === start) break
    cycle.push(item)
  }
  test.compare(focused(), start, "Tab wraps back to the first control")

  // Platforms deliver Shift+Tab as Backtab with Shift held.
  for (let index = cycle.length - 1; index >= 0; index--) {
    test.keyClick(Qt.Key_Backtab, Qt.ShiftModifier)
    test.compare(focused(), cycle[index], "Shift+Tab retraces the Tab order")
  }
  return cycle
}
