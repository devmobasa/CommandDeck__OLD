import QtQuick
import "Protocol.js" as Protocol

QtObject {
  id: root
  required property var nextRequestId
  property int activeRequestId: 0
  property var pending: ({})
  property bool busy: false
  property bool loaded: false
  property string error: ""
  property var inventory: ({ revision: 0, enabled: false, packages: [], total: 0, offset: 0 })
  signal request(var value)
  signal received(string action, var inventory)
  function send(input, query, offset) {
    const id = nextRequestId()
    activeRequestId = id
    pending[id] = input.action
    busy = true; error = ""
    request({ version: Protocol.Version, requestId: id, operation: Protocol.Operations.ExtensionsManage,
      extension: input, query: query || "", offset: offset || 0, limit: 32 })
  }
  function load(query, offset) { send({ action: Protocol.ExtensionManagementActions.List }, query, offset) }
  function change(input) {
    if (busy || (!loaded && input.action !== Protocol.ExtensionManagementActions.Inspect)) return
    send(Object.assign({ expectedRevision: inventory.revision }, input))
  }
  function handleMessage(message) {
    const action = pending[message.requestId]
    if (!action) return false
    if (message.requestId === activeRequestId) {
      if (message.type === Protocol.Messages.ExtensionsResult) {
        inventory = message.extensions
        loaded = true
        busy = false
        received(action, inventory)
      } else if (message.type === Protocol.Messages.Error) {
        error = message.error || "The extension change failed. Reload before retrying."
        busy = false
      } else if (message.type === Protocol.Messages.Done) busy = false
    }
    if (message.type === Protocol.Messages.Done) delete pending[message.requestId]
    return true
  }
  function disconnected() {
    pending = ({}); activeRequestId = 0; busy = false
    loaded = false
    error = "Disconnected. Reconnect and reload before changing extensions."
  }
}
