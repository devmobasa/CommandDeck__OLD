import QtQuick
import "Protocol.js" as Protocol

QtObject {
  id: controller
  required property var nextRequestId
  property bool available: true
  property bool opened: false
  property bool loading: false
  property var snapshot: null
  property string error: ""
  property int activeRequestId: 0
  signal request(var message)
  signal dismissed()
  signal updated()

  function send(operation, historyId) {
    if (!available) {
      loading = false
      error = "Query history is unavailable while the daemon is offline"
      return
    }
    loading = true
    error = ""
    activeRequestId = nextRequestId()
    const message = { version: Protocol.Version, requestId: activeRequestId,
      operation: operation, limit: 256 }
    if (operation !== Protocol.Operations.QueryHistoryGet) message.confirmed = true
    if (historyId !== undefined) message.historyId = historyId
    request(message)
  }

  function refresh() { send(Protocol.Operations.QueryHistoryGet) }
  function remove(historyId) { if (!loading) send(Protocol.Operations.QueryHistoryDelete, historyId) }
  function reset() { if (!loading) send(Protocol.Operations.QueryHistoryReset) }
  function open() { opened = true; refresh() }
  function close() {
    opened = false
    loading = false
    activeRequestId = 0
    error = ""
    dismissed()
  }
  function clear() { close(); snapshot = null }

  onAvailableChanged: {
    if (!available) {
      activeRequestId = 0
      loading = false
      if (opened) error = "Query history is unavailable while the daemon is offline"
    }
  }

  function handleMessage(message) {
    if (!activeRequestId || message.requestId !== activeRequestId) return false
    if (message.type === Protocol.Messages.QueryHistoryResult) {
      activeRequestId = 0
      loading = false
      error = ""
      snapshot = message.queryHistory || null
      updated()
      return true
    }
    if (message.type === Protocol.Messages.Error) {
      activeRequestId = 0
      loading = false
      error = message.error || "Query history update failed"
      return true
    }
    return false
  }
}
