import QtQuick
import "UiConstants.js" as Ui
import "Protocol.js" as Protocol

Item {
  id: root

  required property var nextRequestId
  readonly property alias busy: mutations.busy
  readonly property alias active: mutations.active
  readonly property alias preferenceInFlight: preferenceUpdates.inFlight
  readonly property alias draft: preferenceUpdates.draft
  readonly property alias acknowledged: preferenceUpdates.acknowledged
  signal requestReady(var request, var context)
  signal preferencesDraftChanged(var draft)
  signal rollbackRequested(var preferences, string errorText)
  signal drained()

  function enqueue(operation, values, context) {
    const request = {
      version: Protocol.Version,
      requestId: nextRequestId(),
      operation: operation
    }
    const fields = values || ({})
    for (const key in fields) request[key] = fields[key]
    mutations.submit(request, context || ({}))
    return request.requestId
  }

  function submitPreferences(preferences) {
    preferenceUpdates.submit(preferences)
  }

  function preferenceRequest(preferences) {
    const previous = preferenceUpdates.acknowledged
    if (!previous || previous.registryRevision === undefined
        || previous.registryRevision !== preferences.registryRevision) return { preferences: preferences }
    const before = JSON.parse(JSON.stringify(previous))
    const after = JSON.parse(JSON.stringify(preferences))
    delete before.sources
    delete after.sources
    delete before.preferenceRevision
    delete after.preferenceRevision
    if (!preferenceUpdates.equal(before, after)) return { preferences: preferences }
    const oldSources = previous.sources || []
    const sources = preferences.sources || []
    if (sources.length !== oldSources.length) return { preferences: preferences }
    const changes = []
    for (const source of sources) {
      const old = oldSources.find(value => value.sourceId === source.sourceId)
      if (!old) return { preferences: preferences }
      const change = { sourceId: source.sourceId }
      for (const field of ["enabledInSearch", "enabledOnHome", "position"]) {
        if (source[field] !== old[field]) change[field] = source[field]
      }
      if (Object.keys(change).length > 1) changes.push(change)
    }
    if (changes.length < 1 || changes.length > 32) return { preferences: preferences }
    return { sourcePatch: {
      registryRevision: previous.registryRevision,
      preferenceRevision: previous.preferenceRevision,
      changes: changes
    } }
  }

  function resetPreferences(preferences) {
    preferenceUpdates.reset(preferences)
  }

  function acceptPreferences(preferences) {
    preferenceUpdates.accept(preferences)
  }

  function rejectPreferences(errorText) {
    preferenceUpdates.reject(errorText)
  }

  function settle(requestId) {
    return mutations.settle(requestId)
  }

  function abort(errorText) {
    const interrupted = mutations.abort(errorText)
    preferenceUpdates.abort()
    return interrupted
  }

  PersonalizationMutationQueue {
    id: mutations
    onDispatchRequested: (request, context) => root.requestReady(request, context)
    onDrained: root.drained()
  }

  PreferenceUpdateQueue {
    id: preferenceUpdates
    onDraftChanged: root.preferencesDraftChanged(draft)
    onDispatchRequested: preferences => root.enqueue(Protocol.Operations.PreferencesUpdate,
      root.preferenceRequest(preferences), { kind: Ui.MutationKind.Preferences })
    onRollbackRequested: (preferences, errorText) =>
      root.rollbackRequested(preferences, errorText)
  }
}
