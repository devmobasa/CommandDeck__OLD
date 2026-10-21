import QtQuick
import QtTest

TestCase {
  id: test
  readonly property int readinessTimeout: 30000

  function waitForValue(object, property, expected, timeout, message) {
    tryCompare(object, property, expected, Math.max(timeout || 0, readinessTimeout), message)
  }

  function waitForCondition(condition, timeout, message) {
    try {
      tryVerify(condition, Math.max(timeout || 0, readinessTimeout), message || condition.toString())
    } catch (error) {
      console.error("Readiness deadline: " + (message || condition.toString())
        + "; observed " + observedState(test.parent))
      throw error
    }
  }

  function nextTurn() {
    let processed = false
    Qt.callLater(() => { processed = true })
    waitForCondition(() => processed, readinessTimeout, "queued event turn completes")
  }

  // Negative contracts need an observation interval. Check throughout it,
  // including after scheduler stalls, rather than sleeping before one sample.
  function observeFor(milliseconds, condition, message) {
    const until = Date.now() + milliseconds
    waitForCondition(() => {
      verify(condition(), message)
      return Date.now() >= until
    }, milliseconds + readinessTimeout, message)
  }

  function waitForFrame(item) {
    if (!waitForRendering(item, readinessTimeout)) {
      fail("rendering deadline: " + String(item) + "; observed " + observedState(item))
    }
    return true
  }

  function observedState(object) {
    const values = []
    const visited = []
    // Fixtures contain synthetic data only. Include scalar state and the focused
    // item so a predicate timeout has values as well as its source expression.
    function visit(item, depth) {
      if (!item || depth > 2 || values.length >= 100 || visited.indexOf(item) >= 0) return
      visited.push(item)
      const state = {}
      for (const key in item) {
        const value = item[key]
        if (typeof value === "number" || typeof value === "boolean") state[key] = value
        else if (typeof value === "string") state[key] = value.slice(0, 256)
      }
      values.push(String(item) + " " + JSON.stringify(state))
      for (const child of Array.from(item.data || item.children || [])) visit(child, depth + 1)
    }
    visit(test, 0)
    visit(object, 0)
    return values.join("; ")
  }
}
