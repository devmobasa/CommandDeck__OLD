import QtQuick
import "UiConstants.js" as Ui
import "Protocol.js" as Protocol
import "ProtocolErrors.js" as ProtocolErrors

// Client mirror of the daemon-owned AI provider settings.
//
// The daemon owns the managed document, its revision, validation, credential
// resolution, and the live provider registry. This object only correlates
// requests, keeps the acknowledged snapshot, rejects late replies, and tells
// the panel which of saving, saved, conflict, failed, or connection-lost it
// is looking at. Nothing here decides whether a definition is valid.
QtObject {
  id: root

  readonly property var requestKinds: Object.freeze({
    Load: "load",
    Update: "update",
    Save: "save",
    Delete: "delete",
    Test: "test",
    Cancel: "cancel"
  })

  // The last acknowledged ai-settings-result payload, or null before load.
  property var settings: null
  readonly property bool loaded: root.settings !== null && root.settings !== undefined
  readonly property bool enabled: root.loaded && root.settings.enabled === true
  readonly property int revision: root.loaded ? Number(root.settings.revision || 0) : 0
  readonly property string source: root.loaded ? String(root.settings.source || "") : ""
  readonly property bool legacyIgnored: root.loaded && root.settings.legacyIgnored === true
  readonly property var providers: root.loaded ? (root.settings.providers || []) : []
  readonly property var presets: root.loaded ? (root.settings.presets || []) : []
  readonly property var reservedIds: root.loaded ? (root.settings.reservedIds || []) : []
  readonly property var selection: root.loaded ? (root.settings.selection || null) : null
  readonly property var configurationError: root.loaded ? (root.settings.error || null) : null

  // False once the daemon answers ai.settings.get with an unknown-operation
  // error: an older daemon that cannot manage providers. The panel then shows
  // an explicit unsupported state instead of an empty provider list.
  property bool supported: true
  property bool busy: false
  // idle | loading | saving | saved | conflict | failed | unsupported | connection-lost
  property string status: Ui.ProviderSettingsState.Idle
  property string errorCategory: ""
  property string errorText: ""
  property var lastSave: null

  // Draft tests are bound to a client-side draft revision so a late reply for
  // an older draft can never populate a newer one.
  property int draftRevision: 0
  property bool testBusy: false
  property int testRequestId: 0
  property var testResult: null

  // requestId -> request kind.
  property var trackedRequests: ({})

  signal request(var message)
  signal applied()
  signal saveCompleted(var result)
  signal mutationFailed(string category, string summary)

  function dispatch(requestId, kind, message) {
    const tracked = root.trackedRequests
    tracked[requestId] = kind
    root.trackedRequests = tracked
    root.request(message)
  }

  function release(requestId) {
    const tracked = root.trackedRequests
    delete tracked[requestId]
    root.trackedRequests = tracked
  }

  function load(requestId) {
    root.status = Ui.ProviderSettingsState.Loading
    root.errorCategory = ""
    root.errorText = ""
    root.dispatch(requestId, root.requestKinds.Load, {
      version: Protocol.Version,
      requestId: requestId,
      operation: Protocol.Operations.AiSettingsGet
    })
  }

  function beginMutation(requestId, kind, message) {
    if (root.busy) return false
    root.busy = true
    root.status = Ui.ProviderSettingsState.Saving
    root.errorCategory = ""
    root.errorText = ""
    root.lastSave = null
    root.dispatch(requestId, kind, message)
    return true
  }

  function setEnabled(requestId, enabled) {
    return root.beginMutation(requestId, root.requestKinds.Update, {
      version: Protocol.Version,
      requestId: requestId,
      operation: Protocol.Operations.AiSettingsUpdate,
      enabled: enabled === true,
      expectedRevision: root.revision
    })
  }

  function saveProvider(requestId, provider, create, selectAfterSave) {
    return root.beginMutation(requestId, root.requestKinds.Save, {
      version: Protocol.Version,
      requestId: requestId,
      operation: Protocol.Operations.AiProvidersSave,
      aiProvider: provider,
      create: create === true,
      expectedRevision: root.revision,
      selectAfterSave: selectAfterSave === true
    })
  }

  function deleteProvider(requestId, providerId) {
    return root.beginMutation(requestId, root.requestKinds.Delete, {
      version: Protocol.Version,
      requestId: requestId,
      operation: Protocol.Operations.AiProvidersDelete,
      providerId: providerId,
      expectedRevision: root.revision,
      confirmed: true
    })
  }

  // Any change to the adapter, endpoint, credential, or consent of a draft
  // invalidates its previous test result.
  function invalidateDraft() {
    root.draftRevision++
    root.testResult = null
    if (root.testBusy) root.testBusy = false
  }

  function testDraft(requestId, provider) {
    root.testResult = null
    root.testBusy = true
    root.testRequestId = requestId
    root.dispatch(requestId, root.requestKinds.Test, {
      version: Protocol.Version,
      requestId: requestId,
      operation: Protocol.Operations.AiProvidersTest,
      aiProvider: provider,
      draftRevision: root.draftRevision,
      expectedRevision: root.revision
    })
  }

  function cancelTest(requestId) {
    if (root.testRequestId === 0) return
    root.dispatch(requestId, root.requestKinds.Cancel, {
      version: Protocol.Version,
      requestId: requestId,
      operation: Protocol.Operations.Cancel,
      targetRequestId: root.testRequestId
    })
  }

  function describeError(error) {
    if (!error) return ""
    return String(error.summary || error.category || "")
  }

  function splitError(text) { return ProtocolErrors.split(text) }

  function handleMessage(message) {
    if (!message) return false
    const id = Number(message.requestId || 0)
    const kind = root.trackedRequests[id]
    if (kind === undefined) return false
    const type = String(message.type || "")

    if (type === Protocol.Messages.Done) {
      root.release(id)
      if (kind === root.requestKinds.Test && id === root.testRequestId) {
        root.testBusy = false
        root.testRequestId = 0
      }
      return true
    }

    if (type === Protocol.Messages.AiSettingsResult) {
      root.settings = message.aiSettings || null
      root.supported = true
      root.busy = false
      root.status = kind === root.requestKinds.Load ? Ui.ProviderSettingsState.Idle : Ui.ProviderSettingsState.Saved
      root.applied()
      return true
    }

    if (type === Protocol.Messages.AiProviderSaveResult) {
      const result = message.aiProviderSave || ({})
      root.settings = result.settings || root.settings
      root.lastSave = result
      root.busy = false
      root.status = result.selectionRequested === true && result.selectionApplied !== true
        ? Ui.ProviderSettingsState.SavedSelectionFailed
        : Ui.ProviderSettingsState.Saved
      root.applied()
      root.saveCompleted(result)
      return true
    }

    if (type === Protocol.Messages.AiProviderTestResult) {
      const result = message.aiProviderTest || null
      // A reply for an older draft revision is discarded, never rendered.
      if (result && Number(result.draftRevision || -1) === root.draftRevision) {
        root.testResult = result
      }
      return true
    }

    if (type === Protocol.Messages.Error) {
      const parsed = root.splitError(message.error)
      if (kind === root.requestKinds.Load) {
        root.busy = false
        if (parsed.summary.indexOf("Unknown operation") >= 0) {
          root.supported = false
          root.status = Ui.ProviderSettingsState.Unsupported
        } else {
          root.status = Ui.ProviderSettingsState.Failed
        }
        root.errorCategory = parsed.category
        root.errorText = parsed.summary
        return true
      }
      if (kind === root.requestKinds.Test) {
        if (id === root.testRequestId) {
          root.testBusy = false
          root.testRequestId = 0
          root.testResult = {
            draftRevision: root.draftRevision,
            outcome: Protocol.AiProviderTestOutcomes.Failed,
            error: { category: parsed.category || Protocol.AiErrorCategories.InvalidConfiguration, summary: parsed.summary }
          }
        }
        return true
      }
      if (kind === root.requestKinds.Cancel) return true
      root.busy = false
      root.status = parsed.category === Protocol.AiErrorCategories.Conflict ? Ui.ProviderSettingsState.Conflict : Ui.ProviderSettingsState.Failed
      root.errorCategory = parsed.category
      root.errorText = parsed.summary
      root.mutationFailed(root.errorCategory, root.errorText)
      return true
    }

    return false
  }

  // The bridge dropped: every in-flight request has an unknown outcome. The
  // panel keeps its draft and must reload before mutating again.
  function disconnect() {
    root.trackedRequests = ({})
    root.busy = false
    root.testBusy = false
    root.testRequestId = 0
    root.status = Ui.ProviderSettingsState.ConnectionLost
    root.errorCategory = ""
    root.errorText = "Connection lost. Reconnecting reloads the provider settings; nothing was assumed saved."
  }
}
