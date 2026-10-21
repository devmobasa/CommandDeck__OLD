import QtQuick
import "Protocol.js" as Protocol

Item {
  id: root
  required property var nextRequestId
  required property bool ready
  property var quicklinks: []
  property string fallbackId: ""
  property string errorText: ""
  property int errorTicket: 0
  property string statusText: ""
  property var active: null
  property var pending: []
  property int submissionSequence: 0
  property bool refreshPending: false
  readonly property bool busy: active !== null
  signal requestReady(var request)
  signal replacing()
  signal changed(var result)
  signal saved(string id, int ticket)
  signal failed(int ticket)
  onReadyChanged: { if (ready) dispatchNext() }

  function load() {
    errorText = ""
    refreshPending = true
    dispatchNext()
  }

  function submit(action, values) {
    errorText = ""
    const fields = JSON.parse(JSON.stringify(values || ({})))
    const ticket = ++submissionSequence
    pending = pending.concat([{ action: action, values: fields, ticket: ticket }])
    dispatchNext()
    return ticket
  }

  function dispatchNext() {
    if (!ready || active) return
    let next
    if (pending.length > 0) {
      next = pending[0]
      pending = pending.slice(1)
    } else if (refreshPending) {
      refreshPending = false
      next = { action: "get", values: ({}) }
    } else return
    const operations = { get: Protocol.Operations.QuicklinksGet, save: Protocol.Operations.QuicklinksSave,
      remove: Protocol.Operations.QuicklinksRemove, disable: Protocol.Operations.QuicklinksDisable,
      enable: Protocol.Operations.QuicklinksEnable }
    const request = Object.assign({ version: Protocol.Version,
      requestId: nextRequestId(), operation: operations[next.action] }, next.values)
    active = { request: request, failed: false, received: false, ticket: next.ticket || 0 }
    requestReady(request)
  }

  function handleMessage(message) {
    if (!active || message.requestId !== active.request.requestId) return false
    if (message.type === Protocol.Messages.QuicklinksResult && message.quicklinks) {
      active.received = true
      replacing()
      quicklinks = message.quicklinks.quicklinks || []
      fallbackId = message.quicklinks.webFallbackQuicklinkId || ""
      statusText = message.quicklinks.message || ""
      if (active.ticket >= errorTicket) {
        errorText = ""
        errorTicket = 0
      }
      changed(message.quicklinks)
    } else if (message.type === Protocol.Messages.Error) {
      active.failed = true
      if (active.ticket === 0 || active.ticket === submissionSequence) {
        errorTicket = active.ticket
        errorText = message.error || "Quicklink change failed."
      }
      failed(active.ticket)
    } else if (message.type === Protocol.Messages.Done) {
      const completed = active
      active = null
      if (!completed.failed && completed.received
          && completed.request.operation === Protocol.Operations.QuicklinksSave) {
        saved(completed.request.quicklink.id, completed.ticket)
      }
      dispatchNext()
    }
    return true
  }

  function disconnect() {
    if (active || pending.length > 0) errorText = "Connection lost. Refresh before retrying; the active quicklink change may have saved."
    if (active) failed(active.ticket)
    for (const submission of pending) failed(submission.ticket)
    active = null
    pending = []
    refreshPending = false
  }
}
