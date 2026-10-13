import QtQml

QtObject {
  id: root

  property var acknowledged: null
  property var draft: null
  property var queued: null
  property bool inFlight: false

  signal dispatchRequested(var preferences)
  signal rollbackRequested(var preferences, string errorText)

  function clone(value) {
    return value === null || value === undefined
      ? null
      : JSON.parse(JSON.stringify(value))
  }

  function equal(left, right) {
    // Qt maps can reorder object fields; array order still matters.
    if (left === right) return true
    if (left === null || right === null || typeof left !== "object" || typeof right !== "object") return false
    if (Array.isArray(left) !== Array.isArray(right)) return false
    const keys = Object.keys(left)
    if (keys.length !== Object.keys(right).length) return false
    return keys.every(key => Object.prototype.hasOwnProperty.call(right, key)
      && root.equal(left[key], right[key]))
  }

  function reset(preferences) {
    acknowledged = clone(preferences)
    draft = clone(preferences)
    queued = null
    inFlight = false
  }

  function submit(preferences) {
    draft = clone(preferences)
    if (inFlight) {
      queued = clone(preferences)
      return
    }

    dispatch(preferences)
  }

  function dispatch(preferences) {
    inFlight = true
    dispatchRequested(clone(preferences))
  }

  function accept(preferences) {
    acknowledged = clone(preferences)
    inFlight = false
    if (queued !== null && !equal(queued, acknowledged)) {
      const next = clone(queued)
      if (next.registryRevision === acknowledged.registryRevision
          && acknowledged.preferenceRevision !== undefined) {
        next.preferenceRevision = acknowledged.preferenceRevision
      }
      queued = null
      draft = clone(next)
      dispatch(next)
      return
    }

    queued = null
    draft = clone(acknowledged)
  }

  function reject(errorText) {
    inFlight = false
    queued = null
    draft = clone(acknowledged)
    rollbackRequested(clone(acknowledged), errorText || "Preference update failed")
  }

  function abort() {
    inFlight = false
    queued = null
    draft = clone(acknowledged)
  }
}
