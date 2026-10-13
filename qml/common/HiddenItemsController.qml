import QtQuick
import "Protocol.js" as Protocol

QtObject {
  id: controller
  required property var nextRequestId
  property bool available: true
  property bool opened: false
  property bool loading: false
  property var snapshot: null
  property string query: ""
  property int offset: 0
  property int activeRequestId: 0
  property string error: ""
  readonly property int pageSize: 64
  signal request(var message)

  function refresh(searchQuery, pageOffset) {
    opened = true
    activeRequestId = 0
    query = searchQuery || ""
    offset = pageOffset || 0
    error = ""

    if (!available) {
      loading = false
      error = "Hidden items are unavailable while the daemon is offline"
      return 0
    }

    // Append only to the same query's contiguous, already loaded page.
    if (offset > 0 && (!snapshot || snapshot.query !== query
        || offset !== snapshot.items.length)) offset = 0

    loading = true
    activeRequestId = nextRequestId()
    request({ version: Protocol.Version, requestId: activeRequestId,
      operation: Protocol.Operations.HiddenList, query: query,
      limit: pageSize, offset: offset })
    return activeRequestId
  }

  function close() {
    opened = false
    activeRequestId = 0
    loading = false
    error = ""
  }

  onAvailableChanged: {
    if (!available) {
      activeRequestId = 0
      loading = false
      snapshot = null
      if (opened) error = "Hidden items are unavailable while the daemon is offline"
    } else if (opened) refresh(query, 0)
  }

  function handleMessage(message) {
    if (!message || !activeRequestId || message.requestId !== activeRequestId) return false
    if (message.type === Protocol.Messages.Error) {
      activeRequestId = 0
      loading = false
      error = message.error || "Hidden items could not be loaded"
      return true
    }
    if (message.type !== Protocol.Messages.HiddenItemsResult) return false

    const payload = message.hiddenItems || ({})
    const previous = offset > 0 && snapshot ? snapshot.items : []
    snapshot = { query: query, items: previous.concat(payload.items || []),
      total: payload.total || 0, truncated: payload.truncated === true }
    activeRequestId = 0
    loading = false
    error = ""
    return true
  }
}
