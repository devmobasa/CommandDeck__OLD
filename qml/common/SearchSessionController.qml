import QtQml
import "Protocol.js" as Protocol
import "QueryExpressions.js" as QueryExpressions
import "WorkspaceResultOrder.js" as WorkspaceResultOrder

// The launcher search: it sends the search the bound launcher state describes,
// cancels a replaced search that is still running, buffers streamed rows and
// publishes them in one step, and remembers which search the list answers.
QtObject {
  id: session

  // Returns the next protocol request ID.
  required property var nextRequestId
  // Replaces the visible list; returns false when a partial refresh keeps it.
  required property var replaceResults
  property bool available: true
  property bool opened: false
  // Settings covers the results: a refresh only marks them stale, and they
  // are searched once when Settings closes.
  property bool paused: false
  // Another surface serves this query (clipboard history for its filter).
  property bool diverted: false
  property string query: ""
  property var filters: []
  property string browseDirectory: ""
  property int browseOffset: 0
  property string herdrWorkspaceId: ""
  property string omarchyMenuId: ""
  property string activeWorkspace: ""
  property string currentDirectory: ""
  property string selectedProject: ""
  // The empty query lists open workspaces in this recency order.
  property var recentWorkspaceIds: []

  property int activeRequestId: 0
  property var pendingResults: []
  property int committedRequestId: 0
  property string activeIdentity: ""
  property string committedIdentity: ""
  // Set when a search runs long enough to show loading feedback.
  property bool pending: false
  property bool inFlight: false
  // The visible results predate a preference change made while they were
  // hidden; the next search replaces them.
  property bool stale: false
  // An edit waits for its search or a search is running, so the visible list
  // does not answer the current query yet.
  readonly property bool searching: inFlight || debounce.running

  signal request(var message)
  // Sent before the request, with the identity of the search it starts.
  signal starting(string identity)
  signal committed(var results)
  signal finished(var completion)
  signal failed(string error)
  signal divertRequested()

  readonly property Timer debounce: Timer {
    interval: 45
    onTriggered: session.search()
  }
  // Streamed rows are published in batches, not one repaint per row.
  readonly property Timer commitDelay: Timer {
    interval: 24
    onTriggered: session.commit(false)
  }
  // Fast searches never dim the retained result list.
  readonly property Timer pendingDelay: Timer {
    interval: 140
    onTriggered: session.pending = true
  }

  onPausedChanged: {
    if (!paused && stale) search()
  }

  function expression() {
    return QueryExpressions.parse(query, filters)
  }

  // Two searches with the same identity answer the same question: a refresh
  // keeps the selection, and a row it shows may still run.
  function currentIdentity() {
    return JSON.stringify([query, expression(), browseDirectory, browseOffset, herdrWorkspaceId, omarchyMenuId])
  }

  // A row shown for an older query must not run once the query has changed:
  // Enter right after typing would otherwise open what the previous search found.
  function canActivate() {
    return committedRequestId === 0 || committedIdentity === currentIdentity()
  }

  // Typing searches once the edits pause.
  function schedule() {
    debounce.restart()
  }

  // Sends the search now; it replaces one that typing has scheduled.
  function search(force) {
    if ((!opened && force !== true) || !available) return false

    debounce.stop()
    if (diverted) {
      divertRequested()
      return false
    }

    cancelUnfinished()
    const identity = currentIdentity()
    starting(identity)
    begin(nextRequestId(), identity)
    request({
      version: Protocol.Version,
      requestId: activeRequestId,
      operation: Protocol.Operations.Search,
      query: query,
      browseDirectory: browseDirectory || null,
      herdrWorkspaceId: herdrWorkspaceId || null,
      omarchyMenuId: omarchyMenuId || null,
      browseOffset: browseOffset,
      expression: expression(),
      // A submenu is listed whole, up to the daemon's default maximum.
      limit: omarchyMenuId ? 200 : 50,
      activeWorkspace: activeWorkspace,
      currentDirectory: currentDirectory.length > 0 ? currentDirectory : undefined,
      selectedProject: selectedProject.length > 0 ? selectedProject : undefined
    })
    return true
  }

  // A preference change refreshes the results. Under Settings they are only
  // marked stale; a forced search (reconciliation after a reconnect) still
  // runs at once.
  function refresh(force) {
    if (paused && force !== true) {
      stale = true
      return false
    }

    return search(force)
  }

  // Home shows bookmarks and saved searches and refills a hidden row's slot,
  // so those changes search it again; any other query keeps its rows.
  function refreshHome() {
    if (query.length > 0 || filters.length > 0) return false
    return search()
  }

  // A replacement cancels only a search that is still streaming. The finished
  // one keeps its ID so late or duplicate replies are still recognized.
  function cancelUnfinished() {
    if (!inFlight || activeRequestId === 0) return
    request({
      version: Protocol.Version,
      requestId: nextRequestId(),
      operation: Protocol.Operations.Cancel,
      targetRequestId: activeRequestId
    })
  }

  function begin(requestId, identity) {
    activeRequestId = requestId
    activeIdentity = identity
    pendingResults = []
    pending = false
    inFlight = true
    stale = false
    commitDelay.stop()
    pendingDelay.restart()
  }

  // Consumes the replies of the active search; replies of a replaced search
  // are left for the caller, which ignores them.
  function handleMessage(message) {
    if (activeRequestId === 0 || message.requestId !== activeRequestId) return false

    if (message.type === Protocol.Messages.Result) {
      if (inFlight) {
        pendingResults.push(message.result)
        commitDelay.restart()
      }
      return true
    }

    if (message.type === Protocol.Messages.Done) {
      inFlight = false
      commitDelay.stop()
      commit(true)
      finished(message.searchCompletion)
      return true
    }

    if (message.type === Protocol.Messages.Error) {
      activeRequestId = 0
      inFlight = false
      pending = false
      commitDelay.stop()
      pendingDelay.stop()
      failed(message.error || "")
      return true
    }

    return false
  }

  // Buffered results are published here: a new search swaps the whole list
  // once; refreshes of the same search keep the selected stable result ID.
  function commit(completed) {
    const reset = committedRequestId === 0 || committedIdentity !== activeIdentity
    const recent = query.trim().length === 0 ? recentWorkspaceIds : []
    const ordered = WorkspaceResultOrder.order(pendingResults, recent)
    if (!replaceResults(ordered, reset, completed !== false)) return false

    committedRequestId = activeRequestId
    committedIdentity = activeIdentity
    pending = false
    pendingDelay.stop()
    committed(pendingResults)
    return true
  }

  // A local edit (a bookmark, a hide) also changes the buffered rows, so a
  // later commit of the same search keeps it.
  function updateResults(update) {
    for (let index = 0; index < pendingResults.length; index++) {
      pendingResults[index] = update(pendingResults[index])
    }
  }

  function removeResults(matches) {
    pendingResults = pendingResults.filter(result => !matches(result))
  }

  function clear() {
    debounce.stop()
    commitDelay.stop()
    pendingDelay.stop()
    activeRequestId = 0
    pendingResults = []
    committedRequestId = 0
    activeIdentity = ""
    committedIdentity = ""
    pending = false
    inFlight = false
    stale = false
  }
}
