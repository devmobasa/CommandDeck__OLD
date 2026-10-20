import QtQuick
import "Protocol.js" as Protocol

// Up on an empty search steps back through recent queries, newest first and
// each one once. They come from the opt-in query history, read when Up is
// first pressed after opening and forgotten again on close; with history off
// there is nothing to recall and Up does nothing.
QtObject {
  id: controller
  required property var nextRequestId
  property bool available: true
  property var queries: []
  property bool loaded: false
  property int position: -1
  property int activeRequestId: 0
  property bool stepPending: false
  signal request(var message)
  signal recalled(string query)

  readonly property string current: position >= 0 && position < queries.length ? queries[position] : ""

  function step(restart) {
    if (!available) return
    if (restart === true) position = -1
    if (!loaded) {
      stepPending = true
      if (activeRequestId === 0 && available) {
        activeRequestId = nextRequestId()
        request({ version: Protocol.Version, requestId: activeRequestId,
          operation: Protocol.Operations.QueryHistoryGet, limit: 256 })
      }
      return
    }

    stepPending = false
    if (position + 1 >= queries.length) return
    position++
    recalled(queries[position])
  }

  function clear() {
    queries = []
    loaded = false
    position = -1
    activeRequestId = 0
    stepPending = false
  }

  function distinctQueries(snapshot) {
    const seen = ({})
    const values = []
    const entries = snapshot && snapshot.enabled !== false && snapshot.entries ? snapshot.entries : []
    for (let index = 0; index < entries.length; index++) {
      const query = String(entries[index].query || "").trim()
      if (query.length === 0 || seen[query] === true) continue
      seen[query] = true
      values.push(query)
    }
    return values
  }

  onAvailableChanged: if (!available) clear()

  function handleMessage(message) {
    if (!activeRequestId || message.requestId !== activeRequestId) return false
    activeRequestId = 0
    if (message.type === Protocol.Messages.QueryHistoryResult) {
      queries = distinctQueries(message.queryHistory)
      loaded = true
      if (stepPending) step()
    } else {
      stepPending = false
    }
    return true
  }
}
