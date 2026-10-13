import QtQuick
import "Protocol.js" as Protocol
import "UiConstants.js" as Ui

QtObject {
  id: root
  required property var nextRequestId
  property bool opened: false
  property bool busy: false
  property bool failed: false
  property bool completed: false
  property bool noView: false
  property string error: ""
  property string sessionId: ""
  property string packageKey: ""
  property string packageTitle: ""
  property var debugLease: null
  property var confirmation: null
  property string commandId: ""
  property int openRequestId: 0
  property int activeActionRequestId: 0
  property bool externalActionPending: false
  property int closingRequestId: 0
  property var queuedOpen: null
  property int sequence: 0
  property var pending: ({})
  property var snapshot: ({ revision: 0, content: { title: "Extension", items: [], actions: [] } })
  readonly property var supportedFeatures: [
    Protocol.ExtensionFeatures.Commands,
    Protocol.ExtensionFeatures.NoViewCommands,
    Protocol.ExtensionFeatures.CopyText,
    Protocol.ExtensionFeatures.PngAssets,
    Protocol.ExtensionFeatures.ListView,
    Protocol.ExtensionFeatures.DetailView,
    Protocol.ExtensionFeatures.FormView,
    Protocol.ExtensionFeatures.Settings,
    Protocol.ExtensionFeatures.Storage,
    Protocol.ExtensionFeatures.ViewStack,
    Protocol.ExtensionFeatures.Pagination,
    Protocol.ExtensionFeatures.CopyTextAction,
    Protocol.ExtensionFeatures.OpenUrlAction,
    Protocol.ExtensionFeatures.DevelopmentDebug,
    Protocol.ExtensionFeatures.Brokers
  ]
  signal request(var value)
  signal returned()
  signal externalActionCompleted()

  function send(operation, input, extra, purpose) {
    const id = nextRequestId()
    pending[id] = purpose || operation
    request(Object.assign({ version: Protocol.Version, requestId: id, operation: operation,
      sessionId: sessionId || undefined, extensionInput: input || {} }, extra || {}))
    return id
  }
  function open(result) {
    if (closingRequestId) {
      queuedOpen = result
      opened = true
      busy = true
      return
    }
    pending = ({})
    activeActionRequestId = 0
    externalActionPending = false
    commandId = result.id
    packageTitle = result.title
    debugLease = null
    confirmation = null
    sessionId = ""
    packageKey = result.id.indexOf("extension:") === 0 ? result.id.split(":")[1] : ""
    sequence = 0
    error = ""
    failed = false
    completed = false
    noView = false
    busy = true
    snapshot = { revision: 0, content: { title: result.title, loading: true, items: [], actions: [] } }
    opened = true
    openRequestId = send(Protocol.Operations.ExtensionOpen,
      { searchLease: result.metadata ? result.metadata.extensionSearchLease : undefined,
        features: supportedFeatures }, { resultId: result.id })
  }
  function decideRemote(confirmed) {
    if (!opened || !sessionId || !confirmation) return
    const id = confirmation.id
    confirmation = null
    send(Protocol.Operations.ExtensionAction, { sequence: ++sequence, revision: snapshot.revision, confirmationId: id, confirmed: confirmed }, {}, "remote-decision")
  }
  function present(revision) {
    if (opened && sessionId && revision > 0)
      send(Protocol.Operations.ExtensionPresent, { sequence: ++sequence, revision: revision })
  }
  function activate(revision, itemId, handle) {
    if (!opened || !sessionId || busy || failed) return
    busy = true
    error = ""
    const content = snapshot.content || {}
    const item = (content.items || []).find(value => value.id === itemId)
    const actions = (content.actions || []).concat(item ? item.actions || [] : [])
    externalActionPending = actions.some(action => action.handle === handle && action.kind === Protocol.ExtensionActionKinds.OpenUrl)
    activeActionRequestId = send(Protocol.Operations.ExtensionAction,
      { sequence: ++sequence, revision: revision, itemId: itemId || undefined, handle: handle })
  }
  function close() {
    if (!opened) return
    confirmation = null
    queuedOpen = null
    if (!closingRequestId) {
      if (sessionId && !failed) closingRequestId = send(Protocol.Operations.ExtensionClose)
      else if (!completed && !sessionId && openRequestId && pending[openRequestId]) {
        // Cancel's acknowledgement only confirms delivery. The original open
        // completes after the daemon has stopped the child and released ownership.
        closingRequestId = openRequestId
        send(Protocol.Operations.Cancel, {}, { targetRequestId: openRequestId })
      }
    }
    opened = false
    externalActionPending = false
    busy = false
    sessionId = ""
    openRequestId = 0
    activeActionRequestId = 0
    returned()
  }
  function submit(revision, handle, formRevision, values) {
    if (!opened || !sessionId || busy || failed) return
    busy = true; error = ""
    activeActionRequestId = send(Protocol.Operations.ExtensionAction,
      { sequence: ++sequence, revision: revision, handle: handle, formRevision: formRevision, values: values })
  }
  function restart() { if (failed && !busy) open({ id: commandId, title: packageTitle }) }
  function disable() {
    if (!packageKey || busy) return
    busy = true
    send(Protocol.Operations.ExtensionsManage, {}, { extension: { action: Protocol.ExtensionManagementActions.List } }, Ui.ExtensionSessionPurpose.DisableList)
  }
  function disconnected() {
    confirmation = null
    externalActionPending = false
    pending = ({})
    closingRequestId = 0
    queuedOpen = null
    activeActionRequestId = 0
    openRequestId = 0
    sessionId = ""
    if (!opened) return
    failed = true
    busy = false
    error = "The daemon disconnected. Reopen the command after it reconnects."
  }
  function handleMessage(message) {
    const purpose = pending[message.requestId]
    if (closingRequestId) {
      if (message.type === Protocol.Messages.Done) {
        delete pending[message.requestId]
        if (message.requestId === closingRequestId) {
          closingRequestId = 0
          const next = queuedOpen
          queuedOpen = null
          if (next) open(next)
        }
      }
      return !!purpose || message.type === Protocol.Messages.ExtensionSession
    }
    if (message.type === Protocol.Messages.ExtensionSession) {
      const state = message.extensionSession
      if (!opened || message.requestId !== openRequestId || !state || (sessionId && state.sessionId !== sessionId)) return true
      sessionId = state.sessionId
      packageKey = state.packageKey
      packageTitle = state.title
      debugLease = state.debug || null
      confirmation = state.confirmation || null
      if (state.status === Protocol.ExtensionSessionStatuses.Running) noView = true
      if (state.content) snapshot = state
      if (state.status === Protocol.ExtensionSessionStatuses.Ready) error = state.error || ""
      if (state.status === Protocol.ExtensionSessionStatuses.Ready && !activeActionRequestId) busy = false
      if (state.status === Protocol.ExtensionSessionStatuses.Completed) {
        completed = true
        busy = false
        sessionId = ""
      }
      if (state.status === Protocol.ExtensionSessionStatuses.Failed || state.status === Protocol.ExtensionSessionStatuses.Closed) {
        externalActionPending = false
        failed = true
        busy = false
        error = state.error || "This command was closed or its package changed."
      }
      return true
    }
    if (!purpose) return false
    if (message.type === Protocol.Messages.ExtensionsResult && purpose === Ui.ExtensionSessionPurpose.DisableList) {
      send(Protocol.Operations.ExtensionsManage, {}, { extension: {
        action: Protocol.ExtensionManagementActions.Disable, key: packageKey, expectedRevision: message.extensions.revision } }, Ui.ExtensionSessionPurpose.DisableSave)
    }
    if (message.type === Protocol.Messages.ExtensionsResult && purpose === Ui.ExtensionSessionPurpose.DisableSave) {
      failed = true
      close()
    }
    if (message.type === Protocol.Messages.Error) {
      if (message.requestId === activeActionRequestId) externalActionPending = false
      error = message.error || "The extension request failed."
      if (!activeActionRequestId) busy = false
      if (message.requestId === openRequestId) failed = true
    }
    if (message.type === Protocol.Messages.Done) {
      delete pending[message.requestId]
      if (message.requestId === activeActionRequestId) {
        activeActionRequestId = 0
        busy = false
        if (externalActionPending) externalActionCompleted()
        externalActionPending = false
      }
    }
    return true
  }
}
