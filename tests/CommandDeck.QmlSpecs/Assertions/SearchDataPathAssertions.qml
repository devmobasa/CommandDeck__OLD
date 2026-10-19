import QtQuick
import "Protocol.js" as Protocol
import "." as CommandDeck

// Request and model traffic of the integrated deck's search path: what a
// Settings change, a completed search, and a refresh send and repaint.
QtObject {
  required property var host

  property TextMetrics literalMetrics: TextMetrics { }

  function result(id, title) {
    return {
      id: id, kind: Protocol.ResultKinds.File, title: title, subtitle: "/tmp/" + title,
      sourceId: Protocol.SourceIds.Files, providerId: Protocol.ProviderIds.Catalog,
      invocationReference: "reference-" + id, canCustomize: false, isFavorite: false,
      aliases: [], metadata: {}, actions: [{ id: "open", title: "Open", kind: "open-path" }]
    }
  }

  function deliver(deck, results) {
    const requestId = deck.part_searchSession.activeRequestId
    for (const value of results) {
      deck.handleMessage({ type: Protocol.Messages.Result, requestId: requestId, result: value })
    }
    deck.handleMessage({ type: Protocol.Messages.Done, requestId: requestId })
  }

  function operationsSince(deck, start) {
    return deck.sentRequests.slice(start).map(request => request.operation)
  }

  function lastRequest(deck, start, operation) {
    return deck.sentRequests.slice(start).filter(request => request.operation === operation).slice(-1)[0]
  }

  function preferencesSnapshot(preferences) {
    return { preferences: preferences, shortcuts: [], customizations: [] }
  }

  function run(context) {
    const deck = host.part_integratedDeck
    const wasOpened = deck.opened
    deck.daemonAvailable = true
    deck.reconciliationPending = false
    if (!wasOpened) deck.open("{}")
    deliver(deck, [result("file:before", "before.txt")])

    verifySettingsToggleSearchesOnce(deck)
    verifyEmptyStatesWaitForTheReply(deck)
    verifyOnlyRunningSearchesAreCancelled(deck)
    verifyLocalEditsKeepFreshReferences(deck)
    verifyUpRecallsRecentQueries(deck)
    verifyHiddenPreviewPaneLoadsNothing(deck)
    verifyClosedSearchIsKeptForReopen(deck)
    verifyCopyMessageOutlivesTheLauncher(deck)
    verifyResetRankingRefreshesInPlace(deck)
    verifyHomeBookmarksMoveWithTheKeyboard(deck)

    if (!wasOpened) deck.close()
  }

  // Ctrl+Alt+Down on the first Home bookmark moves it to the second position
  // and keeps it selected; the ends and non-bookmarks send nothing.
  function verifyHomeBookmarksMoveWithTheKeyboard(deck) {
    const bookmark = (id, title) => Object.assign(result(id, title), {
      canCustomize: true, isFavorite: true, section: { id: "bookmarks", title: "Bookmarks", order: 1 } })
    deck.queryText = ""
    deck.requestSearch()
    deliver(deck, [bookmark("file:first", "first.txt"), bookmark("file:second", "second.txt"),
      Object.assign(result("file:recent", "recent.txt"), { canCustomize: true,
        section: { id: "recent-frequent", title: "Recent & Frequent", order: 2 } })])
    const moves = start => deck.sentRequests.slice(start)
      .filter(request => request.operation === Protocol.Operations.FavoriteMove)
    deck.selectedIndex = deck.navigationController.indexOfResultId("file:first")
    let start = deck.sentRequests.length
    deck.moveFavorite(-1)
    host.expect(moves(start).length === 0, "the first bookmark cannot move up")
    deck.moveFavorite(1)
    const move = moves(start)[0]
    host.expect(move !== undefined && move.resultId === "file:first" && move.offset === 1
        && deck.navigationController.restoreSelectionId === "file:first",
      "moving the first bookmark down sends its new position and keeps it selected")
    deck.handleMessage({ type: Protocol.Messages.PreferencesResult, requestId: move.requestId,
      preferences: preferencesSnapshot(deck.part_personalization.acknowledged) })
    deliver(deck, [])
    deck.requestSearch()
    deliver(deck, [bookmark("file:first", "first.txt"), Object.assign(result("file:recent", "recent.txt"), {
      section: { id: "recent-frequent", title: "Recent & Frequent", order: 2 } })])
    deck.selectedIndex = deck.navigationController.indexOfResultId("file:recent")
    start = deck.sentRequests.length
    deck.moveFavorite(-1)
    host.expect(moves(start).length === 0, "a row outside Bookmarks never moves")
    deck.navigationController.restoreSelectionId = ""
  }

  // Reset Ranking keeps the launcher open and searches again so the result
  // re-ranks without its history.
  function verifyResetRankingRefreshesInPlace(deck) {
    const reset = { id: "reset-ranking", title: "Reset Ranking", kind: Protocol.ActionKinds.ResetRanking }
    deck.queryText = "rank"
    deck.requestSearch()
    deliver(deck, [Object.assign(result("file:ranked", "ranked.txt"), {
      actions: [{ id: "open", title: "Open", kind: "open-path" }, reset] })])
    const start = deck.sentRequests.length
    deck.invokeAction(deck.currentResult, reset, false)
    const invoke = lastRequest(deck, start, Protocol.Operations.Invoke)
    deck.handleMessage({ type: Protocol.Messages.ActionResult, requestId: invoke.requestId,
      actionResult: { succeeded: true, message: "Ranking reset for ranked.txt." } })
    host.expect(invoke.actionId === "reset-ranking" && deck.opened && deck.hudText === ""
        && deck.statusText === "Ranking reset for ranked.txt."
        && operationsSince(deck, start).filter(operation => operation === Protocol.Operations.Search).length === 1,
      "Reset Ranking keeps the launcher open and searches again")
    deliver(deck, [])
  }

  // A copy that closes the launcher leaves its message for a moment; reopening
  // clears it, and a launch that closes the launcher shows nothing.
  function verifyCopyMessageOutlivesTheLauncher(deck) {
    const runAndSucceed = (action, message) => {
      deck.queryText = "hud"
      deck.requestSearch()
      deliver(deck, [Object.assign(result("file:hud", "hud.txt"), { actions: [action] })])
      const start = deck.sentRequests.length
      deck.invokeAction(deck.currentResult, action, false)
      const invoke = lastRequest(deck, start, Protocol.Operations.Invoke)
      deck.handleMessage({ type: Protocol.Messages.ActionResult, requestId: invoke.requestId,
        actionResult: { succeeded: true, message: message } })
    }
    runAndSucceed({ id: "copy-path", title: "Copy Path", kind: Protocol.ActionKinds.CopyText }, "Copied.")
    host.expect(!deck.opened && deck.hudText === "Copied." && deck.part_hudTimer.running,
      "a copy that closes the launcher leaves its message on screen")
    deck.open("{}")
    host.expect(deck.hudText === "", "reopening the launcher clears the message")
    deliver(deck, [])
    runAndSucceed({ id: "open", title: "Open", kind: Protocol.ActionKinds.OpenPath }, "Opened.")
    host.expect(!deck.opened && deck.hudText === "", "an open that closes the launcher shows no message")
    deck.open("{}")
    deliver(deck, [])
  }

  // With a kept-search time, closing keeps the search and reopening soon
  // shows it with its selection. A search an action ran from, an expired or
  // private one, and an explicit open query are not restored. Shift+Escape
  // returns to an empty Home search from any filter or folder.
  function verifyClosedSearchIsKeptForReopen(deck) {
    const rows = () => [result("file:k1", "k1.txt"), result("file:k2", "k2.txt")]
    const searchFor = query => {
      deck.queryText = query
      deck.requestSearch()
      deliver(deck, rows())
    }
    deck.applyLauncherBehavior(false, 30)
    searchFor("kept")
    deck.selectedIndex = deck.navigationController.indexOfResultId("file:k2")
    deck.close()
    host.expect(!deck.opened && deck.lastSearch !== null && deck.lastSearch.query === "kept",
      "closing keeps the search the user closed")
    const start = deck.sentRequests.length
    deck.open("{}")
    const search = lastRequest(deck, start, Protocol.Operations.Search)
    host.expect(deck.queryText === "kept" && search !== undefined && search.query === "kept",
      "reopening soon shows and searches the kept query")
    deliver(deck, rows())
    host.expect(deck.currentResult !== null && deck.currentResult.id === "file:k2",
      "the reopened search selects the result that was selected")

    deck.forgetSearchOnClose = true
    deck.close()
    deck.open("{}")
    host.expect(deck.queryText === "", "a search an action ran from is not kept")
    deliver(deck, [])

    searchFor("stale")
    deck.close()
    deck.lastSearch.closedAt -= 31000
    deck.open("{}")
    host.expect(deck.queryText === "", "a kept search expires after its time")
    deliver(deck, [])

    searchFor("kept")
    deck.close()
    deck.open(JSON.stringify({ query: "explicit" }))
    host.expect(deck.queryText === "explicit", "an explicit open query wins over the kept search")
    deliver(deck, [])

    deck.activeFilters = [{ id: Protocol.SourceIds.Hey, excluded: false }]
    deck.queryText = "invoice"
    deck.close()
    host.expect(deck.lastSearch === null, "a private source search is never kept")
    deck.open("{}")
    deliver(deck, [])

    deck.activeFilters = [{ id: Protocol.SourceIds.Files, excluded: false }]
    deck.queryText = "x"
    deck.browseDirectory = "/tmp"
    const rootStart = deck.sentRequests.length
    deck.backToRoot()
    host.expect(deck.queryText === "" && deck.activeFilters.length === 0 && deck.browseDirectory === ""
        && lastRequest(deck, rootStart, Protocol.Operations.Search) !== undefined,
      "Shift+Escape returns to an empty Home search")
    deliver(deck, [])
    deck.applyLauncherBehavior(false, 0)
  }

  // A hidden preview pane requests no file preview as the selection moves;
  // showing it again (Ctrl+I) previews the selected file.
  function verifyHiddenPreviewPaneLoadsNothing(deck) {
    deck.queryText = "preview"
    deck.requestSearch()
    deliver(deck, [result("file:p1", "p1.txt"), result("file:p2", "p2.txt")])
    const start = deck.sentRequests.length
    deck.applyPreviewPane(false)
    deck.selectedIndex = deck.navigationController.indexOfResultId("file:p2")
    host.expect(!deck.previewPaneVisible && lastRequest(deck, start, Protocol.Operations.Preview) === undefined,
      "a hidden preview pane requests no file preview")
    deck.togglePreviewPane()
    const shown = lastRequest(deck, start, Protocol.Operations.Preview)
    host.expect(deck.previewPaneVisible && shown !== undefined && shown.resultId === "file:p2"
        && deck.statusText === "Preview pane shown",
      "showing the preview pane again previews the selected file")
    deck.cancelPreview()
    deck.queryText = ""
  }

  // Up on an empty search reads the opt-in history once, applies the newest
  // query and searches it; Up again on that query applies the next older one.
  // Closing forgets what was read.
  function verifyUpRecallsRecentQueries(deck) {
    deck.queryText = ""
    deck.activeFilters = []
    deck.requestSearch()
    deliver(deck, [result("file:home", "home.txt")])
    const start = deck.sentRequests.length
    const want = deck.part_navigation.recallWanted()
    deck.part_queryRecall.step(want === "restart")
    const historyRequest = lastRequest(deck, start, Protocol.Operations.QueryHistoryGet)
    host.expect(want === "restart" && historyRequest !== undefined,
      "Up on an empty search reads query history")
    deck.handleMessage({ type: Protocol.Messages.QueryHistoryResult, requestId: historyRequest.requestId,
      queryHistory: { enabled: true, entries: [
        { id: 2, query: "roadmap", stableId: "file:roadmap", actionId: "open" },
        { id: 1, query: "gaps", stableId: "config:gaps", actionId: "edit" }] } })
    const search = lastRequest(deck, start, Protocol.Operations.Search)
    host.expect(deck.queryText === "roadmap" && search !== undefined && JSON.stringify(search).indexOf("roadmap") >= 0
        && deck.part_navigation.recallWanted() === "continue",
      "the newest recalled query is shown and searched")
    deliver(deck, [result("file:roadmap", "roadmap.md")])

    const older = deck.sentRequests.length
    deck.part_queryRecall.step(false)
    host.expect(deck.queryText === "gaps" && lastRequest(deck, older, Protocol.Operations.Search) !== undefined
        && lastRequest(deck, older, Protocol.Operations.QueryHistoryGet) === undefined,
      "Up again recalls the older query without reading history again")
    deliver(deck, [result("file:gaps", "gaps.conf")])

    deck.clearModalState()
    host.expect(!deck.part_queryRecall.loaded && deck.part_navigation.recalledText === "",
      "closing forgets the recalled queries")
    deck.queryText = ""
  }

  // A refresh that changes only invocation references leaves the model rows
  // alone. Bookmarking or hiding a row rebuilds the list and must keep the
  // other rows' fresh references, or invoking them would be refused.
  function verifyLocalEditsKeepFreshReferences(deck) {
    const kept = result("file:kept", "kept.txt")
    const edited = Object.assign(result("file:edited", "edited.txt"), { canCustomize: true })
    const hidden = Object.assign(result("file:hidden", "hidden.txt"), { canCustomize: true })
    deck.queryText = "edits"
    deck.requestSearch()
    deliver(deck, [kept, edited, hidden])
    deck.requestSearch()
    deliver(deck, [kept, edited, hidden].map(
      value => Object.assign({}, value, { invocationReference: "fresh-" + value.id })))

    deck.applyCustomization({ stableId: edited.id, isFavorite: true, aliases: [] })
    const editedResult = deck.navigationController.resultAt(deck.navigationController.indexOfResultId(edited.id))
    const keptResult = deck.navigationController.resultAt(deck.navigationController.indexOfResultId(kept.id))
    host.expect(editedResult.isFavorite && editedResult.invocationReference === "fresh-file:edited"
        && keptResult.invocationReference === "fresh-file:kept",
      "bookmarking a row keeps every row's refreshed invocation reference")

    const hideStart = deck.sentRequests.length
    deck.setHidden(hidden)
    const hideRequest = lastRequest(deck, hideStart, Protocol.Operations.HiddenSet)
    deck.handleMessage({ requestId: hideRequest.requestId, type: Protocol.Messages.CustomizationResult,
      customization: { stableId: hidden.id, isHidden: true, isFavorite: false, aliases: [], message: "Hidden" } })
    const remaining = deck.navigationController.resultAt(deck.navigationController.indexOfResultId(kept.id))
    host.expect(deck.navigationController.indexOfResultId(hidden.id) < 0
        && remaining.invocationReference === "fresh-file:kept",
      "hiding a row keeps the remaining rows' refreshed invocation references")
    deck.lastHidden = null
  }

  function containsResultRow(item) {
    if (!item) return false
    if (item.titleControl !== undefined && item.accessibilityName !== undefined) return true
    for (let index = 0; index < item.children.length; index++) {
      if (containsResultRow(item.children[index])) return true
    }
    return false
  }

  // Runs inside the production host journey while its surface is shown, just
  // before the journey clicks the row and types. The daemon answers a refresh
  // with the same rows under fresh invocation references.
  function verifyProductionRefresh(list, shown) {
    const deck = host.part_integratedDeck
    const row = list.itemAtIndex(deck.selectedIndex)
    host.expect(!containsResultRow(list.itemAtIndex(0)) && containsResultRow(row),
      "a section header row builds no result row")

    let repaints = 0
    const countRepaint = () => repaints++
    deck.part_rows.dataChanged.connect(countRepaint)
    deck.requestSearch()
    deliver(deck, [Object.assign({}, shown, { invocationReference: "refreshed-reference" })])
    deck.part_rows.dataChanged.disconnect(countRepaint)
    host.expect(repaints === 0 && list.itemAtIndex(deck.selectedIndex) === row
        && deck.currentResult.invocationReference === "refreshed-reference",
      "an unchanged refresh repaints no row and keeps the selected delegate with its fresh reference ("
        + repaints + " rows rewritten)")
  }

  // The status line shows daemon messages verbatim, so markup in one must read
  // as text instead of formatting it.
  function verifyProductionStatusLine(list) {
    const deck = host.part_integratedDeck
    let content = list
    while (content && content.part_statusLine === undefined) content = content.parent
    const line = content ? content.part_statusLine : null
    if (!line) {
      host.expect(false, "the production status line is reachable")
      return
    }

    const previous = deck.statusText
    const message = "<b>Launch failed</b> &amp; <i>retry</i>"
    deck.statusText = message
    literalMetrics.font = line.font
    literalMetrics.text = message
    host.expect(line.text === message && Math.abs(line.implicitWidth - literalMetrics.advanceWidth) <= 2,
      "a daemon message with markup shows literally in the status line ("
        + Math.round(line.implicitWidth) + " of " + Math.round(literalMetrics.advanceWidth) + " px)")
    deck.statusText = previous
  }

  function verifyOnlyRunningSearchesAreCancelled(deck) {
    deck.queryText = "report"
    deck.requestSearch()
    const finished = deck.part_searchSession.activeRequestId
    deliver(deck, [result("file:report", "report.txt")])

    const nextStart = deck.sentRequests.length
    deck.queryText = "reports"
    deck.requestSearch()
    host.expect(!deck.sentRequests.slice(nextStart).some(request => request.operation === Protocol.Operations.Cancel
          && request.targetRequestId === finished)
        && operationsSince(deck, nextStart).slice(-1)[0] === Protocol.Operations.Search,
      "the next keystroke does not cancel a search that already finished")

    const running = deck.part_searchSession.activeRequestId
    const replaceStart = deck.sentRequests.length
    deck.queryText = "reportsx"
    deck.requestSearch()
    const cancel = lastRequest(deck, replaceStart, Protocol.Operations.Cancel)
    host.expect(cancel !== undefined && cancel.targetRequestId === running,
      "a search still running is cancelled by its replacement")
    deliver(deck, [])
  }

  // The guiding states describe a reply, so a search that has not answered
  // yet, or an edit still in its debounce, shows neither.
  function verifyEmptyStatesWaitForTheReply(deck) {
    deck.close()
    deck.open(JSON.stringify({ query: "no such thing" }))
    host.expect(deck.part_searchSession.inFlight && !deck.part_searchSession.pending
        && deck.navigationController.rowCount() === 0 && !deck.showNoResults,
      "No matches stays hidden until the first search replies")
    deliver(deck, [])
    host.expect(deck.showNoResults, "No matches appears once the search replies with nothing")

    deck.queryText = "no such things"
    deck.part_searchSession.schedule()
    host.expect(!deck.showNoResults, "an edit waiting for its debounced search does not claim No matches")
    deck.requestSearch()
    host.expect(!deck.part_searchSession.debounce.running,
      "a search sent now replaces the one the edit scheduled")
    deliver(deck, [])

    deck.close()
    deck.open("{}")
    host.expect(deck.part_searchSession.inFlight && !deck.showEmptyState,
      "the empty-query guide waits for the home search to reply")
    deliver(deck, [])
    host.expect(deck.showEmptyState, "the empty-query guide appears when home has nothing to show")
  }

  function verifySettingsToggleSearchesOnce(deck) {
    const original = JSON.parse(JSON.stringify(deck.launcherPreferences))
    const settingsStart = deck.sentRequests.length
    deck.openSettings()
    deck.handleMessage({
      type: Protocol.Messages.PreferencesResult,
      requestId: lastRequest(deck, settingsStart, Protocol.Operations.PreferencesGet).requestId,
      preferences: preferencesSnapshot(original)
    })

    const toggled = Object.assign({}, original, { reduceMotion: !original.reduceMotion })
    const toggleStart = deck.sentRequests.length
    deck.updatePreferences(toggled)
    const update = lastRequest(deck, toggleStart, Protocol.Operations.PreferencesUpdate)
    deck.handleMessage({ type: Protocol.Messages.PreferencesResult, requestId: update.requestId,
      preferences: preferencesSnapshot(toggled) })
    const reload = lastRequest(deck, toggleStart, Protocol.Operations.PreferencesGet)
    deck.handleMessage({ type: Protocol.Messages.PreferencesResult, requestId: reload.requestId,
      preferences: preferencesSnapshot(toggled) })
    const toggleOperations = operationsSince(deck, toggleStart).join(",")
    host.expect(toggleOperations
        === [Protocol.Operations.PreferencesUpdate, Protocol.Operations.PreferencesGet].join(","),
      "a Settings toggle saves and reloads preferences without searching hidden results ("
        + toggleOperations + ")")

    const closeStart = deck.sentRequests.length
    deck.closeSettings()
    host.expect(operationsSince(deck, closeStart).filter(
        operation => operation === Protocol.Operations.Search).length === 1,
      "closing Settings searches the stale results once")
    deliver(deck, [result("file:after", "after.txt")])
    host.expect(deck.currentResult !== null && deck.currentResult.id === "file:after"
        && deck.navigationController.rowCount() === 2,
      "the list shown after closing Settings reflects the changed preferences")

    deck.settingsOpen = true
    deck.reconciliationPending = true
    const reconcileStart = deck.sentRequests.length
    deck.requestPreferencesSnapshot()
    deck.handleMessage({ type: Protocol.Messages.PreferencesResult,
      requestId: lastRequest(deck, reconcileStart, Protocol.Operations.PreferencesGet).requestId,
      preferences: preferencesSnapshot(toggled) })
    host.expect(operationsSince(deck, reconcileStart).indexOf(Protocol.Operations.Search) >= 0,
      "reconciliation after a reconnect still searches while Settings is open")
    deliver(deck, [result("file:after", "after.txt")])
    host.expect(!deck.reconciliationPending, "that search completes reconciliation")

    deck.settingsOpen = false
    deck.part_personalization.resetPreferences(original)
  }
}
