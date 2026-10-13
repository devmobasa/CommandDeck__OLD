import QtQuick
import Quickshell
import "." as CommandDeck
import "Protocol.js" as Protocol

QtObject {
  required property var host
  function run(context) {
    host.expect(host.part_queryHistory.entryCount === 1
        && host.part_queryHistory.entryItemAt(0).Accessible.role === Accessible.ListItem,
      "query history exposes retained entries as accessible list items")
    host.expect(host.part_queryHistory.confirmationControl !== null
        && host.part_queryHistory.resetControl.Accessible.description.indexOf("confirmation") >= 0,
      "query history destructive controls require confirmation")
    host.expect(host.part_indexInspector.pageCount === 2
        && host.part_indexInspector.visibleWorkItemCount === 2
        && host.part_indexInspector.focusTrapConfigured,
      "index inspector pages bounded work rows and traps focus")
    host.part_indexInspector.movePage(1)
    host.expect(host.part_indexInspector.page === 1
        && host.part_indexInspector.visibleWorkItemCount === 1,
      "index inspector advances to the remaining work page")
    const exactInspection = host.part_indexInspector.inspection
    host.part_indexInspector.inspection = Object.assign({}, exactInspection, {
      totalWorkItemCount: 300, workItemsTruncated: true, workItemCountsExact: false
    })
    host.expect(host.part_indexInspector.pageSummary.indexOf("loaded") >= 0
        && host.part_indexInspector.Accessible.description.indexOf("at least 1 failed") >= 0,
      "index inspector labels capped pages and lower-bound counts")
    host.part_indexInspector.inspection = exactInspection
    host.expect(host.part_integratedDeck.sentRequests.length === 2
        && host.part_integratedDeck.sentRequests.some(request => request.operation === Protocol.Operations.Bootstrap)
        && host.part_integratedDeck.sentRequests.some(request => request.operation === Protocol.Operations.Status),
      "integrated CommandDeck starts with bootstrap and runtime health requests")
    const bootstrapRequest = host.part_integratedDeck.sentRequests.find(
      request => request.operation === Protocol.Operations.Bootstrap)
    const statusRequest = host.part_integratedDeck.sentRequests.find(
      request => request.operation === Protocol.Operations.Status)
    host.part_integratedDeck.handleMessage({
      type: Protocol.Messages.BootstrapResult,
      requestId: bootstrapRequest.requestId,
      bootstrap: {
        preferences: {
          sources: [],
          reduceMotion: false,
          webFallbackQuicklinkId: null
        },
        filters: [
          { id: "files", label: "Files", token: "file:", aliases: ["f:"] },
          { id: "workspaces", label: "Workspaces", token: "workspace:", aliases: ["ws:"] }
        ],
        sources: [],
        fallbackQuicklinks: []
      }
    })
    host.part_integratedDeck.handleMessage({
      type: Protocol.Messages.Status,
      requestId: statusRequest.requestId,
      runtimeStatus: {
        catalog: { itemCount: 12 },
        indexing: {
          started: true,
          watchedRootCount: 1,
          unwatchedRoots: ["/missing"],
          pendingEventCount: 0,
          reconciliationRequired: false,
          extractorVersion: "test"
        },
        providers: [{
          id: "hyprland",
          label: "Windows & Workspaces",
          state: "available"
        }]
      }
    })
    host.part_integratedDeck.handleMessage({
      type: Protocol.Messages.Done,
      requestId: statusRequest.requestId
    })
    host.expect(host.part_integratedDeck.runtimeStatus.indexing.unwatchedRoots.length === 1
        && host.part_integratedDeck.activeStatusRequestId === 0,
      "integrated runtime health response is correlated through terminal done")
    host.part_integratedDeck.opened = true
    host.part_integratedDeck.currentDirectoryContext = "/projects/current"
    host.part_integratedDeck.selectedProjectContext = "/projects/selected"
    host.part_integratedDeck.requestSearch()
    const contextSearch = host.part_integratedDeck.sentRequests.filter(
      request => request.operation === Protocol.Operations.Search).slice(-1)[0]
    host.expect(contextSearch.currentDirectory === "/projects/current"
        && contextSearch.selectedProject === "/projects/selected",
      "integrated search forwards only explicit bounded directory and project context")
    const firstPendingSearch = host.part_integratedDeck.part_searchSession.activeRequestId
    host.expect(!host.part_integratedDeck.part_searchSession.pending,
      "a fast search does not immediately show loading feedback")
    host.part_integratedDeck.requestSearch()
    const replacementSearch = host.part_integratedDeck.part_searchSession.activeRequestId
    host.part_integratedDeck.part_searchSession.pending = true
    host.part_integratedDeck.handleMessage({ type: Protocol.Messages.Done, requestId: firstPendingSearch })
    host.expect(host.part_integratedDeck.part_searchSession.pending,
      "a stale completion cannot hide a replacement search's loading feedback")
    host.part_integratedDeck.handleMessage({ type: Protocol.Messages.Done, requestId: replacementSearch })
    host.expect(!host.part_integratedDeck.part_searchSession.pending,
      "current search completion clears loading feedback")
    host.part_integratedDeck.requestSearch()
    host.part_integratedDeck.handleMessage({ type: Protocol.Messages.Done,
      requestId: host.part_integratedDeck.part_searchSession.activeRequestId,
      searchCompletion: {complete: false, incompleteProviderCount: 1} })
    host.expect(host.part_integratedDeck.statusText.indexOf("Some sources did not finish") >= 0,
      "integrated search reports incomplete provider work without diagnostics")
    host.part_integratedDeck.requestSearch()
    host.part_integratedDeck.handleMessage({ type: Protocol.Messages.Done,
      requestId: host.part_integratedDeck.part_searchSession.activeRequestId,
      searchCompletion: {complete: true, incompleteProviderCount: 0} })
    host.expect(host.part_integratedDeck.statusText.indexOf("Some sources did not finish") < 0,
      "a complete search clears the partial status")
    host.part_integratedDeck.requestSearch()
    host.part_integratedDeck.part_searchSession.pending = true
    host.part_integratedDeck.handleMessage({
      type: Protocol.Messages.Error, requestId: host.part_integratedDeck.part_searchSession.activeRequestId, error: "Search interrupted"
    })
    host.expect(!host.part_integratedDeck.part_searchSession.pending,
      "a search failure clears loading feedback")
    host.part_integratedDeck.queryText = "road"
    host.part_integratedDeck.requestSearch()
    host.expect(host.part_integratedDeck.sentRequests.filter(
        request => request.operation === Protocol.Operations.Suggest).length === 0,
      "typing searches the main list without requesting a competing suggestion popup")
    host.expect(host.part_integratedDeck.navigationController.tryConvertPrefix("file: report")
        && host.part_integratedDeck.activeFilterId === "files"
        && host.part_integratedDeck.queryText === "report",
      "integrated CommandDeck converts pasted provider prefixes")
    host.expect(host.part_integratedDeck.navigationController.searchAccessibilityDescription()
        .indexOf("Files") >= 0
        && host.part_integratedDeck.navigationController.searchAccessibilityDescription()
          .indexOf("Search apps") >= 0,
      "integrated search accessibility names the filter and placeholder")
    host.expect(host.part_integratedDeck.navigationController.removeFilterFromBackspace(0)
        && host.part_integratedDeck.activeFilterId === "",
      "integrated Backspace removes an active filter at the query start")
    host.expect(host.part_integratedDeck.navigationController.tryConvertPrefix("file: report")
        && host.part_integratedDeck.navigationController.tryConvertPrefix("-ws: report")
        && host.part_integratedDeck.activeFilters.length === 2
        && host.part_integratedDeck.activeFilters[0].id === "files"
        && host.part_integratedDeck.activeFilters[1].id === "workspaces"
        && host.part_integratedDeck.activeFilters[1].excluded,
      "integrated query state retains positive and negative filter chips")
    const multiExpression = host.part_integratedDeck.navigationController.queryExpression()
    host.expect(multiExpression.includeFilterIds[0] === "files"
        && multiExpression.excludeFilterIds[0] === "workspaces",
      "integrated search emits the typed filter expression")
    host.part_integratedDeck.activeFilters = []
    host.part_integratedDeck.queryText = "unknown: report"
    host.expect(!host.part_integratedDeck.navigationController.tryConvertPrefix(host.part_integratedDeck.queryText)
        && host.part_integratedDeck.queryText === "unknown: report",
      "integrated CommandDeck preserves unknown prefixes")
    host.part_integratedDeck.activeFilters = [{ id: "files", excluded: false }]
    host.expect(!host.part_integratedDeck.navigationController.removeFilterFromBackspace(3)
        && host.part_integratedDeck.activeFilterId === "files",
      "integrated Backspace inside the query keeps the active filter")
    host.part_integratedDeck.activeFilters = []
    host.part_integratedDeck.part_searchSession.activeRequestId = 89
    host.part_integratedDeck.part_searchSession.pendingResults = [{
      id: "file:bookmark",
      kind: "file",
      title: "Bookmark",
      sourceId: "files",
      providerId: Protocol.ProviderIds.Catalog,
      section: { id: "bookmarks", title: "Bookmarks", order: 1 },
      actions: [{ id: "open", title: "Open", kind: "open-path" }]
    }, {
      id: "app:browser",
      kind: "application",
      title: "Browser",
      sourceId: "applications",
      providerId: Protocol.ProviderIds.Catalog,
      section: { id: "applications", title: "Applications", order: 2 },
      actions: [{ id: "launch", title: "Launch", kind: "launch-desktop" }]
    }]
    host.part_integratedDeck.part_searchSession.commit()
    const firstResultIndex = host.part_integratedDeck.navigationController.firstSelectableIndex()
    host.part_integratedDeck.selectedIndex = firstResultIndex
    host.part_integratedDeck.navigationController.moveSelection(1)
    host.expect(host.part_integratedDeck.navigationController.rowCount() === 4
        && host.part_integratedDeck.navigationController.rowAt(0).sectionTitle === "Bookmarks"
        && host.part_integratedDeck.navigationController.rowAt(0).sectionCount === 1
        && host.part_integratedDeck.navigationController.rowAt(2).sectionTitle === "Applications"
        && host.part_integratedDeck.navigationController.rowAt(2).sectionCount === 1
        && host.part_integratedDeck.navigationController.selectedResult().id === "app:browser",
      "integrated sections preserve daemon order and navigation skips headers")
    const selectedBrowser = host.part_integratedDeck.part_searchSession.pendingResults[1]
    const selectedBookmark = host.part_integratedDeck.part_searchSession.pendingResults[0]
    host.part_integratedDeck.part_searchSession.activeIdentity = "same-query"
    host.part_integratedDeck.part_searchSession.committedIdentity = "same-query"
    host.part_integratedDeck.part_searchSession.activeRequestId = 890
    host.part_integratedDeck.part_searchSession.pendingResults = [Object.assign({}, selectedBrowser, {
      section: { id: "top-match", title: "Top Matches" }
    }), selectedBookmark]
    host.part_integratedDeck.part_searchSession.commit()
    host.expect(host.part_integratedDeck.navigationController.selectedResult().id === "app:browser"
        && host.part_integratedDeck.navigationController.rowAt(0).sectionTitle === "Top Matches",
      "same-query refresh preserves selected ID when promoted across sections")
    host.part_integratedDeck.part_searchSession.activeRequestId = 891
    host.part_integratedDeck.part_searchSession.pendingResults = [selectedBookmark]
    host.part_integratedDeck.part_searchSession.commit(false)
    host.expect(host.part_integratedDeck.navigationController.selectedResult().id === "app:browser",
      "partial same-query refresh keeps the prior selection until done")
    host.part_integratedDeck.handleMessage({ type: Protocol.Messages.Result, requestId: 891, result: selectedBrowser })
    host.part_integratedDeck.handleMessage({ type: Protocol.Messages.Done, requestId: 891 })
    host.expect(host.part_integratedDeck.navigationController.selectedResult().id === "app:browser",
      "selection arriving in the final batch is retained")
    const pendingCount = host.part_integratedDeck.part_searchSession.pendingResults.length
    host.part_integratedDeck.handleMessage({ type: Protocol.Messages.Result, requestId: 89, result: selectedBookmark })
    host.part_integratedDeck.handleMessage({ type: Protocol.Messages.Done, requestId: 89 })
    host.expect(host.part_integratedDeck.part_searchSession.pendingResults.length === pendingCount
        && host.part_integratedDeck.navigationController.selectedResult().id === "app:browser",
      "stale pre-promotion responses cannot change visible selection")
    host.part_integratedDeck.part_searchSession.activeIdentity = "new-query"
    host.part_integratedDeck.part_searchSession.pendingResults = [selectedBookmark, selectedBrowser]
    host.part_integratedDeck.part_searchSession.commit()
    host.expect(host.part_integratedDeck.navigationController.selectedResult().id === "file:bookmark",
      "a deliberate new query selects its first row")
    host.part_integratedDeck.part_searchSession.activeRequestId = 90
    const fileSnapshot = "file-snapshot"
    host.part_integratedDeck.part_searchSession.pendingResults = [{
      id: "file:legacy",
      invocationReference: fileSnapshot,
      kind: "file",
      title: "Legacy File",
      sourceId: "files",
      providerId: Protocol.ProviderIds.Catalog,
      actions: [{ id: "open", title: "Open", kind: "open-path" }]
    }]
    host.part_integratedDeck.part_searchSession.commit()
    host.expect(host.part_integratedDeck.navigationController.rowAt(0).sectionTitle === "Files",
      "integrated results retain legacy kind grouping without sections")
    host.part_integratedDeck.queryText = "content:needle"
    host.part_integratedDeck.requestPreview(host.part_integratedDeck.currentResult)
    const firstPreviewId = host.part_integratedDeck.activePreviewRequestId
    const previewRequest = host.part_integratedDeck.sentRequests.slice(-1)[0]
    host.expect(previewRequest.operation === Protocol.Operations.Preview && previewRequest.query === "content:needle"
        && previewRequest.offset === 0 && previewRequest.invocationReference === fileSnapshot,
      "preview forwards current query and matching-line offset")
    host.part_integratedDeck.handleMessage({ type: Protocol.Messages.PreviewResult, requestId: firstPreviewId,
      preview: { text: "needle", matchCount: 2, matchIndex: 0, openResultId: "preview:line1" } })
    host.part_integratedDeck.requestPreview(host.part_integratedDeck.currentResult, 1)
    const nextPreviewId = host.part_integratedDeck.activePreviewRequestId
    host.expect(host.part_integratedDeck.filePreview.text === "needle" && host.part_integratedDeck.previewLoading,
      "match navigation retains controls while replacement is loading")
    host.part_integratedDeck.handleMessage({ type: Protocol.Messages.PreviewResult, requestId: firstPreviewId,
      preview: { text: "stale" } })
    host.expect(host.part_integratedDeck.filePreview.text === "needle",
      "late preview response cannot replace the current match")
    host.part_integratedDeck.handleMessage({ type: Protocol.Messages.PreviewResult, requestId: nextPreviewId,
      preview: { text: "next needle", matchCount: 2, matchIndex: 1, openResultId: "preview:line2" } })
    host.part_integratedDeck.queryText = "replacement"
    host.part_integratedDeck.requestSearch()
    const previewSearchId = host.part_integratedDeck.part_searchSession.activeRequestId
    host.part_integratedDeck.part_searchSession.pendingResults = [host.part_integratedDeck.currentResult]
    host.part_integratedDeck.handleMessage({ type: Protocol.Messages.Done, requestId: previewSearchId })
    host.expect(host.part_integratedDeck.activePreviewRequestId !== nextPreviewId
        && host.part_integratedDeck.sentRequests.slice(-1)[0].query === "replacement",
      "a changed query refreshes the preview even when selected file identity is unchanged")
    const serviceResultId = "systemd:backup.service"
    const serviceSnapshot = "service-snapshot"
    const serviceResult = { id: serviceResultId, invocationReference: serviceSnapshot,
      sourceId: Protocol.SourceIds.SystemdUnits, title: "backup.service" }
    const stopService = { id: Protocol.SystemdUnitActionIds.Stop,
      kind: Protocol.ActionKinds.SystemdUnit, title: "Stop user unit: backup.service", requiresConfirmation: true }
    const beforeStopRequests = host.part_integratedDeck.sentRequests.length
    host.part_integratedDeck.invokeAction(serviceResult, stopService, false, {})
    host.expect(host.part_integratedDeck.confirmationOpen && host.part_integratedDeck.part_invocationController.pendingConfirmationAction.title.indexOf("backup.service") >= 0
        && host.part_integratedDeck.sentRequests.length === beforeStopRequests,
      "service controls show the exact unit and send nothing before confirmation")
    host.part_integratedDeck.cancelConfirmation()
    host.expect(host.part_integratedDeck.sentRequests.length === beforeStopRequests,
      "canceling service confirmation does not invoke the unit")
    host.part_integratedDeck.invokeAction(serviceResult, stopService, false, {})
    host.part_integratedDeck.acceptConfirmation()
    const stopRequest = host.part_integratedDeck.sentRequests.slice(-1)[0]
    host.expect(stopRequest.operation === Protocol.Operations.Invoke && stopRequest.confirmed
        && stopRequest.resultId === serviceResultId && stopRequest.actionId === Protocol.SystemdUnitActionIds.Stop
        && stopRequest.invocationReference === serviceSnapshot,
      "confirmed service controls invoke only the cached target")
    host.part_integratedDeck.handleMessage({ type: Protocol.Messages.ActionResult, requestId: stopRequest.requestId,
      actionResult: { succeeded: true, message: "stop requested" } })
    host.expect(host.part_integratedDeck.opened && host.part_integratedDeck.sentRequests.slice(-1)[0].operation === Protocol.Operations.Search,
      "service control success keeps the launcher open and refreshes state")
    host.part_integratedDeck.invokeAction(serviceResult,
      { id: Protocol.SystemdUnitActionIds.Logs, kind: Protocol.ActionKinds.SystemdUnit }, false, {})
    const logsRequest = host.part_integratedDeck.sentRequests.slice(-1)[0]
    host.part_integratedDeck.handleMessage({ type: Protocol.Messages.ActionResult, requestId: logsRequest.requestId,
      actionResult: { succeeded: true, message: "Recent logs", detailMarkdown: "<script>literal log</script>" } })
    host.expect(host.part_integratedDeck.detailDialogRequested && host.part_integratedDeck.actionOutputPlainText
        && host.part_integratedDeck.detailContent === "<script>literal log</script>",
      "explicit logs are shown as literal output rather than active markup")
    host.part_integratedDeck.detailDialogRequested = false
    host.part_integratedDeck.part_searchSession.activeRequestId = 90
    host.part_integratedDeck.part_searchSession.pendingResults = [{
      id: "app:browser",
      kind: "application",
      title: "Browser",
      sourceId: "applications",
      providerId: Protocol.ProviderIds.Catalog,
      actions: [{ id: "launch", title: "Launch", kind: "launch-desktop" }]
    }]
    host.part_integratedDeck.part_searchSession.commit()
    host.expect(host.part_integratedDeck.navigationController.completionText() === "Browser",
      "integrated Tab completion uses the selected result title")
    host.part_integratedDeck.activeFilters = [{ id: "files", excluded: false }]
    host.expect(host.part_integratedDeck.navigationController.completionText() === "Browser",
      "integrated Tab completion remains available with an active chip")
    host.part_integratedDeck.activeFilters = []
    host.expect(host.part_integratedDeck.resultStatus([{
        providerId: Protocol.SourceIds.Status,
        actions: []
      }, {
        providerId: Protocol.ProviderIds.Fallback,
        actions: [{ id: "open" }]
      }]) === "Web fallback available",
      "integrated fallback completion has a meaningful announcement")
    const fallbackResult = {
      id: "web-fallback:test",
      kind: "quicklink",
      title: "Search the web",
      providerId: Protocol.ProviderIds.Fallback,
      sourceId: "commands-quicklinks",
      canCustomize: false,
      actions: [{ id: "open", title: "Search the Web", kind: Protocol.ActionKinds.OpenUrl }]
    }
    host.part_integratedDeck.navigationController.syncRows(
      host.part_integratedDeck.navigationController.buildRows([fallbackResult]))
    host.part_integratedDeck.selectedIndex = host.part_integratedDeck.navigationController.firstSelectableIndex()
    host.expect(host.part_integratedDeck.navigationController.selectedResult().providerId === Protocol.ProviderIds.Fallback
        && host.part_integratedDeck.actionCount === 1,
      "integrated fallback renders as executable but not bookmarkable")
    const rankedResult = {
      id: "app:ranked",
      kind: "application",
      title: "Ranked Browser",
      providerId: Protocol.ProviderIds.Catalog,
      sourceId: "applications",
      canCustomize: false,
      actions: [{ id: "launch", title: "Launch", kind: "launch-desktop" }],
      ranking: {
        placement: { tier: "learned", promoted: true, reason: "Repeated recent use and strong title match" },
        finalScore: 0.9,
        contributions: [
          { kind: "lexical", label: "Catalog lexical match", value: 0.9 }
        ],
        matches: [
          { field: "title", value: "Ranked Browser", ranges: [{ start: 0, length: 6 }] }
        ],
        policies: [
          { field: "content", included: false, reason: "root-default" }
        ]
      }
    }
    host.part_integratedDeck.navigationController.syncRows(
      host.part_integratedDeck.navigationController.buildRows([rankedResult]))
    host.part_integratedDeck.selectedIndex = host.part_integratedDeck.navigationController.firstSelectableIndex()
    host.part_integratedDeck.showRankingExplanation(rankedResult)
    host.expect(host.part_integratedDeck.detailDialogOpen
        && host.part_integratedDeck.dialogTitle === "Why this result?"
        && host.part_integratedDeck.detailDialogText.indexOf("Catalog lexical match") >= 0
        && host.part_integratedDeck.detailDialogText.indexOf("Placement: learned") >= 0
        && host.part_integratedDeck.detailDialogText.indexOf("content: excluded — root-default") >= 0
        && host.part_integratedDeck.detailDialogText.indexOf("\n\n\n") < 0,
      "integrated ranking action opens a plain-text explanation with single blank separators")
    host.part_integratedDeck.detailDialogRequested = false
    host.part_integratedDeck.detailContent = ""
    host.part_integratedDeck.dialogTitle = "AI response"
    const mutationStart = host.part_integratedDeck.sentRequests.length
    const customizable = {
      id: "app:browser",
      metadata: {},
      isFavorite: false,
      aliases: [],
      canCustomize: true,
      actions: []
    }
    host.part_integratedDeck.setFavorite(customizable, true)
    host.part_integratedDeck.setAlias(customizable, "browser")
    host.expect(host.part_integratedDeck.sentRequests.length === mutationStart + 1
        && host.part_integratedDeck.sentRequests[mutationStart].operation === Protocol.Operations.FavoriteSet,
      "integrated CommandDeck serializes heterogeneous mutations")
    host.part_integratedDeck.handleMessage({
      type: Protocol.Messages.CustomizationResult,
      requestId: host.part_integratedDeck.sentRequests[mutationStart].requestId,
      customization: {
        stableId: "app:browser",
        isFavorite: true,
        aliases: [],
        message: "Bookmark added"
      }
    })
    host.expect(host.part_integratedDeck.sentRequests.length === mutationStart + 2
        && host.part_integratedDeck.sentRequests[mutationStart + 1].operation === Protocol.Operations.AliasSet,
      "integrated mutation acknowledgement dispatches the next saved edit")
    host.part_integratedDeck.handleMessage({
      type: Protocol.Messages.CustomizationResult,
      requestId: host.part_integratedDeck.sentRequests[mutationStart + 1].requestId,
      customization: {
        stableId: "app:browser",
        isFavorite: true,
        aliases: ["browser"],
        message: "Alias saved"
      }
    })
    host.part_integratedDeck.aliasDialogOpen = true
    host.part_integratedDeck.setAlias(customizable, "conflict")
    const conflictingAliasRequest = host.part_integratedDeck.sentRequests[
      host.part_integratedDeck.sentRequests.length - 1]
    host.part_integratedDeck.handleMessage({
      type: Protocol.Messages.Error,
      requestId: conflictingAliasRequest.requestId,
      error: "Alias conflict is already assigned"
    })
    host.expect(host.part_integratedDeck.aliasDialogOpen
        && host.part_integratedDeck.aliasDialogError.indexOf("conflict") >= 0,
      "integrated alias conflicts remain visible in the originating dialog")
    host.part_integratedDeck.aliasDialogOpen = false
    host.part_integratedDeck.aliasDialogError = ""
    host.part_integratedDeck.navigationController.syncRows(
      host.part_integratedDeck.navigationController.buildRows([customizable]))
    host.part_integratedDeck.selectedIndex = host.part_integratedDeck.navigationController.firstSelectableIndex()
    host.part_integratedDeck.setFavorite(customizable, true)
    const rejectedFavoriteRequest = host.part_integratedDeck.sentRequests[
      host.part_integratedDeck.sentRequests.length - 1]
    host.part_integratedDeck.handleMessage({
      type: Protocol.Messages.Error,
      requestId: rejectedFavoriteRequest.requestId,
      error: "bookmark write failed"
    })
    host.expect(!host.part_integratedDeck.personalizationBusy
        && host.part_integratedDeck.navigationController.selectedResult().isFavorite === false
        && host.part_integratedDeck.statusText.indexOf("bookmark write failed") >= 0,
      "failed bookmark mutations preserve the acknowledged row state")

    const deck = host.part_integratedDeck
    const actionRow = { id: "app:action-one", kind: "application", sourceId: "applications",
      title: "Action one", actions: [{ id: "launch", title: "Launch", kind: "launch-desktop" }] }
    const launch = actionRow.actions[0]
    deck.queryText = "action one"
    deck.part_searchSession.committedIdentity = deck.part_searchSession.currentIdentity()
    deck.part_searchSession.committedRequestId = 900
    deck.fileSelection = [{ path: "/tmp/selected" }]
    const actionStart = deck.sentRequests.length
    deck.invokeAction(actionRow, launch, false, {})
    const firstAction = deck.sentRequests[deck.sentRequests.length - 1]
    deck.invokeAction(actionRow, launch, false, {})
    deck.runFileSelection("open")
    host.expect(firstAction.operation === Protocol.Operations.Invoke && deck.sentRequests.length === actionStart + 1
        && deck.part_invocationController.requestId === firstAction.requestId,
      "ordinary and batch actions share one admission slot")
    deck.handleMessage({ type: Protocol.Messages.ActionResult, requestId: firstAction.requestId,
      actionResult: { succeeded: false, message: "Launch failed" } })
    host.expect(deck.part_invocationController.requestId === 0,
      "an action outcome releases its admission slot")

    deck.queryText = "action two"
    const beforeChangedQuery = deck.sentRequests.length
    deck.invokeAction(actionRow, launch, false, {})
    host.expect(deck.sentRequests.length === beforeChangedQuery
        && deck.statusText.indexOf("current search") >= 0,
      "new query intent blocks retained-row activation before the debounce sends")
    deck.requestSearch()
    const replacementActionSearch = deck.part_searchSession.activeRequestId
    deck.invokeAction(actionRow, launch, false, {})
    host.expect(deck.sentRequests.filter(request => request.operation === Protocol.Operations.Invoke).length
        === deck.sentRequests.slice(0, actionStart + 1).filter(request => request.operation === Protocol.Operations.Invoke).length,
      "an in-flight replacement cannot activate an old row")
    deck.part_searchSession.pendingResults = [actionRow]
    deck.handleMessage({ type: Protocol.Messages.Done, requestId: replacementActionSearch })
    deck.invokeAction(actionRow, launch, false, {})
    const nextAction = deck.sentRequests[deck.sentRequests.length - 1]
    host.expect(nextAction.operation === Protocol.Operations.Invoke && nextAction.requestId !== firstAction.requestId,
      "the committed replacement can activate after completion")
    deck.handleMessage({ type: Protocol.Messages.ActionResult, requestId: nextAction.requestId,
      actionResult: { succeeded: false, message: "Launch failed" } })
    deck.fileSelection = []

    Object.assign(context, { bootstrapRequest, statusRequest, contextSearch, firstPendingSearch, replacementSearch, multiExpression, firstResultIndex, selectedBrowser, selectedBookmark, pendingCount, firstPreviewId, previewRequest, nextPreviewId, previewSearchId, serviceResult, stopService, beforeStopRequests, stopRequest, logsRequest, fallbackResult, rankedResult, mutationStart, customizable, conflictingAliasRequest, rejectedFavoriteRequest })
  }
}
