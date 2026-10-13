import QtQuick
import "Protocol.js" as Protocol
import "UiConstants.js" as Ui
import "QueryExpressions.js" as QueryExpressions

QtObject {
  id: controller
  required property var nextRequestId
  property bool available: true
  property string frontendInstance: ""
  property double openGeneration: 0
  property bool opened: false
  property string sessionId: ""
  property string query: ""
  property string requestedQuery: ""
  property bool pinnedOnly: false
  property string kindFilter: ""
  readonly property var kindFilters: [
    "", Protocol.ClipboardPresentationKinds.Text, Protocol.ClipboardPresentationKinds.Links,
    Protocol.ClipboardPresentationKinds.Images, Protocol.ClipboardPresentationKinds.Files
  ]
  property double queryGeneration: 0
  property var entries: []
  // Set once the session receives a page, so an empty list reads as empty
  // rather than flashing that while the first page loads.
  property bool pageLoaded: false
  property int selectedIndex: -1
  readonly property var selected: selectedIndex >= 0 && selectedIndex < entries.length ? entries[selectedIndex] : null
  property var preview: null
  property string cursor: ""
  property bool newerAvailable: false
  property var settings: null
  property var capabilities: null
  readonly property bool canCopy: capabilities !== null && capabilities.copy === true
  readonly property bool canInspectOpenHandlers: capabilities !== null && capabilities.inspectOpenHandlers === true
  property var openPreparation: null
  property var preparingOpen: null
  property string error: ""
  property string notice: ""
  property var pending: ({})
  readonly property bool loading: Boolean(pending[Ui.ClipboardPendingKey.Open]
    || pending[Ui.ClipboardPendingKey.Query] || pending[Ui.ClipboardPendingKey.More])
  readonly property bool mutationReady: opened && selected !== null && !pending[Ui.ClipboardPendingKey.Action]
    && !loading && query === requestedQuery
  readonly property Timer statusPoll: Timer {
    interval: 1000
    repeat: true
    running: controller.opened && controller.available && controller.sessionId.length > 0
    onTriggered: {
      if (!controller.pending[Ui.ClipboardPendingKey.Settings] && !controller.pending[Ui.ClipboardPendingKey.SettingsSet])
        controller.send(Ui.ClipboardPendingKey.Settings, Protocol.Operations.ClipboardSettingsGet)
    }
  }
  signal request(var message)
  signal dismissed()
  signal openPrepared()

  function send(key, operation, fields, confirmed, expression, confirmationToken) {
    if (!available) { error = "Clipboard history is unavailable while offline"; return }
    const id = nextRequestId()
    const values = Object.assign({}, fields || {})
    values.frontendInstance = frontendInstance
    values.openGeneration = openGeneration
    if (sessionId) values.sessionId = sessionId
    const next = Object.assign({}, pending)
    next[key] = id
    pending = next
    request({version: Protocol.Version, requestId: id, operation: operation,
      clipboard: values, confirmed: confirmed === true, expression: expression || {clauses: []},
      confirmationToken: confirmationToken})
  }

  function open(initialQuery) {
    pending = ({})
    sessionId = ""
    query = initialQuery || ""
    pinnedOnly = false
    kindFilter = ""
    entries = []
    pageLoaded = false
    selectedIndex = -1
    preview = null
    cursor = ""
    error = ""
    notice = ""
    openPreparation = null
    preparingOpen = null
    capabilities = null
    opened = true
    send(Ui.ClipboardPendingKey.Open, Protocol.Operations.ClipboardOpen)
    send(Ui.ClipboardPendingKey.Settings, Protocol.Operations.ClipboardSettingsGet)
  }

  function close(notifyServer) {
    if (notifyServer !== false && sessionId) send(Ui.ClipboardPendingKey.Close, Protocol.Operations.ClipboardClose)
    opened = false
    pending = ({})
    sessionId = ""
    entries = []
    pageLoaded = false
    preview = null
    settings = null
    capabilities = null
    openPreparation = null
    preparingOpen = null
    dismissed()
  }

  function refresh(more) {
    if (!opened || !sessionId || more && !cursor) return
    invalidateOpenPreparation()
    const next = Object.assign({}, pending)
    delete next[Ui.ClipboardPendingKey.Query]
    delete next[Ui.ClipboardPendingKey.More]
    delete next[Ui.ClipboardPendingKey.Preview]
    pending = next
    if (!more) queryGeneration++
    requestedQuery = query
    preview = null
    error = ""
    send(more ? "more" : "query", Protocol.Operations.ClipboardQuery,
      {queryGeneration: queryGeneration, pinnedOnly: pinnedOnly, kindFilter: kindFilter || undefined,
        cursor: more ? cursor : undefined}, false,
      QueryExpressions.parse(query, [], false))
  }

  function setKind(kind) {
    const next = kindFilters.indexOf(kind) >= 0 ? kind : ""
    if (next === kindFilter) return
    kindFilter = next
    refresh(false)
  }

  // Steps through the kind filters, wrapping; step defaults to the next kind.
  function cycleKind(step) {
    const count = kindFilters.length
    const index = kindFilters.indexOf(kindFilter) + (step === undefined ? 1 : step)
    setKind(kindFilters[((index % count) + count) % count])
  }

  function togglePinnedOnly() {
    pinnedOnly = !pinnedOnly
    refresh(false)
  }

  function kindLabel(kind) {
    if (kind === Protocol.ClipboardPresentationKinds.Text) return "Text"
    if (kind === Protocol.ClipboardPresentationKinds.Links) return "Links"
    if (kind === Protocol.ClipboardPresentationKinds.Images) return "Images"
    if (kind === Protocol.ClipboardPresentationKinds.Files) return "Files"
    return "All"
  }

  function describe(entry) {
    const presentation = entry && entry.presentation ? entry.presentation : {kind: Protocol.ClipboardPresentationKinds.Text}
    if (presentation.kind === Protocol.ClipboardPresentationKinds.Images)
      return "Image" + (presentation.imageWidth ? " " + presentation.imageWidth + "×" + presentation.imageHeight : "")
    if (presentation.kind === Protocol.ClipboardPresentationKinds.Links) return "Link"
    if (presentation.kind === Protocol.ClipboardPresentationKinds.Files) return presentation.destinationCount === 1 ? "File" : presentation.destinationCount + " files"
    return ""
  }

  function select(index) {
    invalidateOpenPreparation()
    selectedIndex = Math.max(-1, Math.min(entries.length - 1, index))
    preview = null
    if (selected) requestPreview(0)
  }

  function entryFields() {
    return selected ? {entryId: selected.id, incarnation: selected.incarnation,
      entryRevision: selected.revision, queryGeneration: queryGeneration} : null
  }

  function requestPreview(matchIndex) {
    const fields = entryFields()
    if (!fields) return
    fields.matchIndex = Math.max(0, matchIndex)
    send(Ui.ClipboardPendingKey.Preview, Protocol.Operations.ClipboardPreview, fields)
  }

  function mutate(action, keywords) {
    const fields = entryFields()
    if (!fields || !mutationReady) return
    fields.action = action
    if (keywords !== undefined) fields.keywords = keywords
    notice = ""
    send(Ui.ClipboardPendingKey.Action, Protocol.Operations.ClipboardAction, fields)
  }

  function prepareOpen() {
    const fields = entryFields()
    if (!fields || !canInspectOpenHandlers || pending[Ui.ClipboardPendingKey.Prepare] || loading || query !== requestedQuery || !opened) return
    if (!selected.presentation || !selected.presentation.hasOpenDestination) return
    error = ""
    openPreparation = null
    fields.action = Protocol.ClipboardActionIds.Open
    send(Ui.ClipboardPendingKey.Prepare, Protocol.Operations.ClipboardActionsPrepare, fields)
    preparingOpen = Object.assign({requestId: pending[Ui.ClipboardPendingKey.Prepare], query: requestedQuery}, fields)
  }

  function invalidateOpenPreparation() {
    if (!pending[Ui.ClipboardPendingKey.Prepare] && !preparingOpen && !openPreparation) return
    const next = Object.assign({}, pending)
    delete next[Ui.ClipboardPendingKey.Prepare]
    pending = next
    preparingOpen = null
    openPreparation = null
  }

  function abortRelease(releaseId) {
    send(Ui.ClipboardPendingKey.Abort, Protocol.Operations.ClipboardReleaseAbort, {releaseId: releaseId || undefined})
  }

  function copySelected() {
    const fields = entryFields()
    if (!fields || !canCopy || pending[Ui.ClipboardPendingKey.Copy] || loading || query !== requestedQuery || !opened) return
    error = ""
    notice = ""
    fields.action = Protocol.ClipboardActionIds.Copy
    send(Ui.ClipboardPendingKey.Copy, Protocol.Operations.ClipboardAction, fields)
  }

  function capture(consent, paused, confirmed) {
    if (!settings || !settings.policy) return
    send(Ui.ClipboardPendingKey.SettingsSet, Protocol.Operations.ClipboardSettingsSet,
      {policyRevision: settings.policy.revision, consent: consent, paused: paused}, confirmed)
  }

  function clearHistory() {
    if (!settings || !settings.migration) return
    send(Ui.ClipboardPendingKey.Clear, Protocol.Operations.ClipboardAction,
      {action: Protocol.ClipboardActionIds.Clear, storeRevision: settings.migration.storeRevision}, true)
  }

  function acknowledgeMigration() {
    if (!settings) return
    send(Ui.ClipboardPendingKey.Migration, Protocol.Operations.ClipboardMigrationAcknowledge,
      {policyRevision: settings.policy.revision, storeRevision: settings.migration.storeRevision}, true)
  }

  function handleMessage(message) {
    const key = Object.keys(pending).find(value => pending[value] === message.requestId)
    if (!key) return false
    if (message.type !== Protocol.Messages.ClipboardResult && message.type !== Protocol.Messages.Error) return true
    const next = Object.assign({}, pending)
    delete next[key]
    pending = next
    if (message.type === Protocol.Messages.Error) {
      if (key === Ui.ClipboardPendingKey.Prepare) preparingOpen = null
      error = message.error || "Clipboard operation failed"
      return true
    }
    if (!opened) return true
    const response = message.clipboard || {}
    if (response.policy) settings = response
    if (key === Ui.ClipboardPendingKey.Open) {
      sessionId = response.sessionId || ""
      capabilities = response.capabilities || null
      refresh(false)
    }
    else if (key === Ui.ClipboardPendingKey.Query || key === Ui.ClipboardPendingKey.More) {
      const previous = selected ? selected.incarnation : ""
      const page = response.page || {}
      entries = key === Ui.ClipboardPendingKey.More ? entries.concat(page.entries || []) : (page.entries || [])
      cursor = page.cursor || ""
      newerAvailable = page.newerAvailable === true
      pageLoaded = true
      const index = entries.findIndex(value => value.incarnation === previous)
      select(index >= 0 ? index : entries.length ? 0 : -1)
    } else if (key === Ui.ClipboardPendingKey.Preview) {
      const value = response.preview
      if (selected && value && value.incarnation === selected.incarnation && value.revision === selected.revision
          && value.queryGeneration === queryGeneration) preview = value
    } else if (key === Ui.ClipboardPendingKey.Copy) {
      const result = response.action || {}
      if (result.succeeded) notice = "Copied"
      else error = result.message || "Clipboard copy failed"
    } else if (key === Ui.ClipboardPendingKey.Prepare) {
      const binding = preparingOpen
      preparingOpen = null
      if (response.actions && selected && binding && binding.requestId === message.requestId
          && binding.entryId === selected.id && binding.incarnation === selected.incarnation
          && binding.entryRevision === selected.revision && binding.queryGeneration === queryGeneration
          && binding.query === requestedQuery && query === requestedQuery) {
        openPreparation = response.actions
        openPrepared()
      }
    } else if (key === Ui.ClipboardPendingKey.Abort) {
      openPreparation = null
    } else if (key === Ui.ClipboardPendingKey.Clear) {
      notice = "Clipboard history cleared"
      sessionId = ""
      entries = []
      pageLoaded = false
      preview = null
      send(Ui.ClipboardPendingKey.Open, Protocol.Operations.ClipboardOpen)
    } else if (key === Ui.ClipboardPendingKey.Action || key === Ui.ClipboardPendingKey.Migration) {
      notice = "Clipboard history updated"
      refresh(false)
    }
    return true
  }

  onAvailableChanged: {
    if (!available) {
      pending = ({})
      sessionId = ""
      entries = []
      pageLoaded = false
      preview = null
      settings = null
      capabilities = null
      openPreparation = null
      preparingOpen = null
      if (opened) error = "Clipboard history is unavailable while offline"
    }
  }
}
