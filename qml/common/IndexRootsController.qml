import QtQuick
import "Protocol.js" as Protocol

QtObject {
  id: controller
  required property var nextRequestId
  property bool available: true
  property bool opened: false
  property bool busy: false
  property var roots: []
  property bool reindexPending: false
  property var progress: null
  property string reindexError: ""
  property string error: ""
  property string errorOperation: ""
  property string errorRootId: ""
  property int activeRequestId: 0
  property string activeOperation: ""
  property string activeRootId: ""
  property var pendingChanges: []
  signal request(var message)
  signal changed()

  function refresh() { return change(Protocol.Operations.IndexRoots, "", "") }
  function clearActionError(operation, rootId) {
    if (errorOperation === operation && errorRootId === (rootId || "")) {
      error = ""
      errorOperation = ""
      errorRootId = ""
    }
  }
  function change(operation, path, rootId) {
    opened = true
    if (operation !== Protocol.Operations.IndexRoots) clearActionError(operation, rootId)
    if (!available) {
      error = "Indexed folders are unavailable while the daemon is offline"
      errorOperation = Protocol.Operations.IndexRoots
      errorRootId = ""
      return 0
    }
    if (busy) {
      if (operation === Protocol.Operations.IndexRoots) return 0
      if (activeOperation !== Protocol.Operations.IndexRoots) {
        const queued = nextRequestId()
        pendingChanges = pendingChanges.concat([{ requestId: queued, operation: operation, path: path, rootId: rootId }])
        return queued
      }
    }
    return send({ requestId: nextRequestId(), operation: operation, path: path, rootId: rootId })
  }

  function send(change) {
    if (change.operation !== Protocol.Operations.IndexRoots) clearActionError(change.operation, change.rootId)
    activeRequestId = change.requestId
    activeOperation = change.operation
    activeRootId = change.rootId || ""
    busy = true
    request({ version: Protocol.Version, requestId: activeRequestId,
      operation: change.operation, path: change.path || null, rootId: change.rootId || null })
    return activeRequestId
  }

  function finish() {
    activeRequestId = 0
    activeOperation = ""
    activeRootId = ""
    busy = false
    if (pendingChanges.length > 0) {
      const next = pendingChanges[0]
      pendingChanges = pendingChanges.slice(1)
      send(next)
    }
  }

  onAvailableChanged: {
    if (!available) {
      busy = false
      activeRequestId = 0
      activeOperation = ""
      activeRootId = ""
      pendingChanges = []
      if (opened) {
        error = "Indexed folders are unavailable while the daemon is offline"
        errorOperation = Protocol.Operations.IndexRoots
        errorRootId = ""
      }
    } else if (opened) refresh()
  }

  function handleMessage(message) {
    if (!activeRequestId || message.requestId !== activeRequestId) return false
    if (message.type === Protocol.Messages.Error) {
      if (activeOperation !== Protocol.Operations.IndexRoots || !error || errorOperation === Protocol.Operations.IndexRoots) {
        error = message.error || "Indexed folders could not be changed"
        errorOperation = activeOperation
        errorRootId = activeRootId
      }
      finish()
      return true
    }
    if (message.type !== Protocol.Messages.IndexRootsResult) return false
    roots = message.indexRoots ? message.indexRoots.roots || [] : []
    reindexPending = !!(message.indexRoots && message.indexRoots.reindexPending)
    progress = message.indexRoots ? message.indexRoots.progress || null : null
    reindexError = message.indexRoots ? message.indexRoots.reindexError || "" : ""
    clearActionError(Protocol.Operations.IndexRoots, "")
    changed()
    finish()
    return true
  }
}
