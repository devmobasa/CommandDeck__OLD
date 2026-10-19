.pragma library
.import "UiConstants.js" as Ui
.import "Protocol.js" as Protocol

// A search for another question drops the preview of the previous answer. The
// visible list is left alone: it is replaced in one step once the next batch
// is ready, so typing does not empty and refill the palette.
function searchStarting(root, identity) {
  root.actionRefreshChanged = false
  if (root.previewSearchIdentity !== identity) {
    root.cancelPreview()
    root.filePreview = null
  }

  if (root.part_rows.count === 0 && !root.reconciliationPending) {
    root.statusText = "Searching…"
  }
}

function searchCommitted(root, results) {
  if (results.length > 0 && !root.reconciliationPending) {
    root.statusText = root.resultStatus(results)
  }
  retargetPendingActions(root, false)
}

function searchFinished(root, completion) {
  if (root.previewSearchIdentity !== root.part_searchSession.activeIdentity) root.requestPreview(root.currentResult)

  if (root.reconciliationPending) {
    root.reconciliationSearchComplete = true
    root.completeReconciliationIfReady()
    return
  }

  const status = root.actionRefreshChanged ? "This item changed; review it again"
    : root.resultStatus(root.part_searchSession.pendingResults)
  root.statusText = completion && completion.complete === false
    ? status + " · Some sources did not finish"
    : status
  retargetPendingActions(root, true)
}

function searchFailed(root, error) {
  root.statusText = root.reconciliationPending && root.reconciliationWarning.length > 0
    ? root.reconciliationWarning
    : error || "Unknown daemon error"
}

// A clipboard-only filter is served by clipboard history, not a search.
function searchClipboard(root) {
  if (!root.part_clipboardController.opened) root.part_clipboardController.open(root.queryText)
}

// Partial batches may omit a retained row. Only completion proves removal.
function retargetPendingActions(root, completed) {
  function refreshed(target) {
    const index = root.part_navigation.indexOfResultId(target.id)
    return index >= 0 ? root.part_navigation.resultAt(index) : null
  }
  let changed = false
  const panel = root.actionPanelOpen ? root.activeActionPanel : null
  if (panel && panel.result) {
    const next = refreshed(panel.result)
    if ((next || completed) && !panel.retarget(next)) {
      panel.dismissed()
      changed = true
    }
  }
  const invocation = root.part_invocationController
  const target = invocation.pendingConfirmationResult || invocation.actionInputResult
  if (target) {
    const next = refreshed(target)
    if ((next || completed) && !invocation.retarget(next)) {
      root.confirmationOpen = false
      root.focusRevision++
      changed = true
    }
  }
  if (changed) {
    root.actionRefreshChanged = true
    root.statusText = "This item changed; review it again"
  }
  return changed
}

function requestRuntimeStatus(root) {
  if (!root.daemonAvailable || root.activeStatusRequestId > 0) return
  root.activeStatusRequestId = ++root.requestSequence
  root.part_protocol.send({
    version: Protocol.Version,
    requestId: root.activeStatusRequestId,
    operation: Protocol.Operations.Status
  })
}

function applySavedSearch(root, search) {
  if (!search || search.unavailable === true || !search.expression) return
  root.part_navigation.applySuggestion({
    text: root.savedSearchText(search.expression),
    expression: search.expression
  })
  root.queryInputRequested(root.part_navigation.queryText)
  root.requestSearch()
}

// A recalled query is typed as it was, so a leading filter prefix becomes a
// chip again; Up keeps stepping back while the query is left as recalled.
function applyRecalledQuery(root, query) {
  root.part_navigation.applySuggestion({ text: query, expression: {} })
  root.queryInputRequested(query)
  root.part_navigation.recalledText = root.part_navigation.queryText
  root.requestSearch()
}

function savedSearchText(root, expression) {
  if (expression && typeof expression.literalQuery === "string") return expression.literalQuery
  const clauses = expression && expression.clauses ? expression.clauses : []
  const includes = expression && expression.includeFilterIds ? expression.includeFilterIds : []
  if (includes.length === 1 && includes[0] === "ai"
      && clauses.every(value => !value.field && !value.excluded)) {
    return clauses.map(value => String(value.text || "")).join(" ")
  }
  return clauses.map(value => {
    const escaped = String(value.text || "").replace(/\\/g, "\\\\").replace(/\"/g, "\\\"")
    const token = (value.field ? value.field + ":" : "") + "\"" + escaped + "\""
    return (value.excluded ? "-" : "") + token
  }).join(" ")
}

function dismiss(root) {
  root.close()
  if (root.shell && typeof root.shell.hide === "function") {
    Qt.callLater(function() {
      root.shell.hide((root.manifest && root.manifest.id) || root.integrationIds.Plugin)
    })
  }
}

// A search the user closed, kept for a reopen within keepSearchSeconds. Folder
// and Herdr views, private sources, and a search an action just ran from are
// not kept.
function keptSearch(root) {
  const forget = root.forgetSearchOnClose
  root.forgetSearchOnClose = false
  const filters = root.activeFilters || []
  if (forget || root.keepSearchSeconds <= 0 || root.browseDirectory.length > 0
      || root.part_navigation.herdrWorkspaceId.length > 0
      || (root.queryText.length === 0 && filters.length === 0)
      || filters.some(value => value.id === Protocol.SourceIds.Clipboard || value.id === Protocol.SourceIds.Hey)) {
    return null
  }
  return { query: root.queryText, filters: filters.slice(),
    selectedId: root.currentResult && root.currentResult.id ? root.currentResult.id : "", closedAt: Date.now() }
}

function close(root) {
  root.lastSearch = keptSearch(root)
  root.extensionManagementOpen = false
  root.part_extensionController.close()
  if (root.fileSelection.length > 0 || root.activeFileSelectionRequestId > 0) root.clearFileSelection()
  root.activeFileSelectionRequestId = 0
  root.browseDirectory = ""
  root.part_navigation.clearHerdrBrowse()
  root.browseOffset = 0
  root.cancelPreview()
  root.opened = false
  root.part_requestTracker.clearInteractionRequests()
  root.part_invocationController.clear()
  root.part_searchSession.clear()
  root.clearModalState()
  root.hideAnswer()
  root.settingsOpen = false
  root.activeFilters = []
  root.queryText = ""
  root.currentDirectoryContext = ""
  root.selectedProjectContext = ""
  root.openWorkspaceMruIds = []
  root.currentResult = null
  root.filePreview = null
  root.previewError = ""
  root.part_rows.clear()
}

function open(root, payloadJson) {
  let payload = {}
  if (typeof payloadJson === "string" && payloadJson.length > 0) {
    try {
      payload = JSON.parse(payloadJson)
    } catch (error) {
      payload = {}
    }
  } else if (payloadJson && typeof payloadJson === "object") {
    payload = payloadJson
  }

  // A failed bridge is retried now instead of at the end of its backoff wait.
  root.part_protocol.reconnectNow()

  root.openedScreenName = root.compositor.focusedMonitor && root.compositor.focusedMonitor.name
    ? root.compositor.focusedMonitor.name
    : ""
  root.openWorkspaceMruIds = root.workspaceMruIds.slice()
  root.part_navigation.clearHerdrBrowse()
  const kept = root.lastSearch
  root.lastSearch = null
  const restore = kept !== null && !payload.query && !payload.filterId
    && Date.now() - kept.closedAt <= root.keepSearchSeconds * 1000
  root.queryText = restore ? kept.query : payload.query || ""
  root.currentDirectoryContext = payload.currentDirectory || ""
  root.selectedProjectContext = payload.selectedProject || ""
  root.activeFilters = restore ? kept.filters
    : payload.filterId ? [{ id: payload.filterId, label: payload.filterLabel || "", excluded: false }] : []
  if (restore) root.part_navigation.restoreSelectionId = kept.selectedId
  root.opened = true
  root.clearModalState()
  root.part_invocationController.clear()
  root.activeAiRequestId = 0
  root.statusText = root.reconciliationWarning
  root.settingsOpen = false
  if (payload.filterId || root.filterDescriptors.length === 0) root.requestBootstrap()
  root.requestSearch()
}

function clearAiState(root) {
  if (root.daemonAvailable && root.part_aiController.state !== Ui.AiState.Idle) {
    root.part_protocol.send({
      version: Protocol.Version,
      requestId: ++root.requestSequence,
      operation: Protocol.Operations.AiClear
    })
  }
  root.part_aiController.clear()
  root.activeAiRequestId = 0
}

function clearModalState(root) {
  root.part_invocationController.cancelInput()
  root.modalIntentGeneration++
  root.helpOpen = false
  root.filterPaletteOpen = false
  root.actionPanelOpen = false
  root.aliasDialogOpen = false
  root.aliasTarget = null
  root.aliasDialogError = ""
  root.resetConfirmation = false
  root.savedSearchDeleteTarget = null
  root.aiTemplateDeleteTarget = null
  root.part_invocationController.takeConfirmation()
  root.confirmationOpen = false
  root.detailDialogRequested = false
  root.actionOutputPlainText = false
  root.detailContent = ""
  root.dialogTitle = "AI response"
  root.part_queryHistoryController.clear()
  root.part_queryRecall.clear()
  root.part_navigation.recalledText = ""
  root.clearAiState()
  // Deliberately not `agentController.clear()`: the daemon keeps the turn
  // and the transcript, so closing the palette must be resumable.
  root.agentOpen = false
  root.agentChallenge = null
}

// Shift+Escape: an empty search on Home, leaving any filter, folder or Herdr view.
function backToRoot(root) {
  root.browseDirectory = ""
  root.browseOffset = 0
  root.part_navigation.clearHerdrBrowse()
  root.activeFilters = []
  root.queryText = ""
  root.queryInputRequested("")
  root.requestSearch()
  root.focusRevision++
}

function toggle(root, payload) {
  if (root.opened) root.dismiss()
  else root.open(payload || "{}")
}

function openPreviewMatch(root) {
  if (root.previewLoading || !root.filePreview || !root.filePreview.openResultId) return
  root.invokeAction({ id: root.filePreview.openResultId,
    invocationReference: root.filePreview.openInvocationReference },
    { id: "edit", kind: Protocol.ActionKinds.EditAtLine }, false, {})
}

function requestPreview(root, result, matchIndex) {
  root.cancelPreview()
  if (matchIndex === undefined) root.filePreview = null
  root.previewError = ""
  if (!root.opened || !root.previewPaneVisible || !result || result.kind !== Protocol.ResultKinds.File) {
    return
  }

  root.activePreviewRequestId = ++root.requestSequence
  root.previewSearchIdentity = root.part_searchSession.activeIdentity
  root.previewLoading = true
  root.part_protocol.send({
    version: Protocol.Version,
    requestId: root.activePreviewRequestId,
    operation: Protocol.Operations.Preview,
    resultId: result.id,
    invocationReference: result.invocationReference,
    query: root.queryText,
    expression: root.part_navigation.queryExpression(),
    offset: matchIndex === undefined ? 0 : matchIndex
  })
}

function cancelPreview(root) {
  const previousRequestId = root.activePreviewRequestId
  root.activePreviewRequestId = 0
  root.previewLoading = false
  if (previousRequestId > 0) {
    root.part_protocol.send({
      version: Protocol.Version,
      requestId: ++root.requestSequence,
      operation: Protocol.Operations.Cancel,
      targetRequestId: previousRequestId
    })
  }
}
