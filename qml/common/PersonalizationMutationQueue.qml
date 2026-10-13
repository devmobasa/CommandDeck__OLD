import QtQml

QtObject {
  id: root

  property var pending: []
  property var active: null
  readonly property bool busy: active !== null || pending.length > 0

  signal dispatchRequested(var request, var context)
  signal drained()
  signal aborted(var entries, string errorText)

  function clone(value) {
    return value === null || value === undefined
      ? value
      : JSON.parse(JSON.stringify(value))
  }

  function submit(request, context) {
    pending = pending.concat([{
      request: clone(request),
      context: clone(context || ({}))
    }])
    pump()
  }

  function pump() {
    if (active !== null || pending.length === 0) return
    active = pending[0]
    pending = pending.slice(1)
    dispatchRequested(clone(active.request), clone(active.context))
  }

  function settle(requestId) {
    if (active === null || active.request.requestId !== requestId) return null
    const settled = active
    active = null
    pump()
    if (!busy) drained()
    return settled
  }

  function abort(errorText) {
    const entries = active === null ? pending.slice() : [active].concat(pending)
    active = null
    pending = []
    aborted(clone(entries), errorText || "Personalization connection lost")
    return entries
  }
}
