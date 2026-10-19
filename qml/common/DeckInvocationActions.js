.pragma library
.import "UiConstants.js" as Ui
.import "Protocol.js" as Protocol

function waitsForCurrentResults(root, result) {
  if (!result || !result.id || root.part_searchSession.canActivate()) return false

  root.statusText = "Wait for results matching the current search"
  return true
}

function invokeAction(root, result, action, confirmed, inputValues, fromUndo) {
  if (root.part_invocationController.busy || root.part_invocationController.collecting) {
    root.statusText = "An action is still in progress"
    return
  }
  // An undo is bound to the row it was offered for, whatever the search is now.
  if (fromUndo !== true && waitsForCurrentResults(root, result)) return
  if (result && result.kind === Protocol.ResultKinds.ExtensionCommand) {
    root.actionPanelOpen = false
    root.part_extensionController.open(result)
    return
  }
  if (!root.daemonAvailable || root.reconciliationPending) return
  if (action.requiresConfirmation && confirmed !== true) {
    root.part_invocationController.requestConfirmation(result, action)
    root.confirmationOpen = true
    return
  }

  if (action.kind === Protocol.ActionKinds.OpenClipboardHistory) {
    root.actionPanelOpen = false
    root.part_clipboardController.open("")
    return
  }
  if (action.kind === Protocol.ActionKinds.BrowseHerdrTabs) {
    root.actionPanelOpen = false
    root.part_navigation.browseHerdrSpace(result)
    return
  }
  // A row that changes the search (a HEY box, "Search again") replaces the
  // query and its filter chip, searches, and leaves the search field ready.
  if (action.kind === Protocol.ActionKinds.ApplyQuery) {
    root.actionPanelOpen = false
    const filterId = action.arguments && action.arguments.filterId ? String(action.arguments.filterId) : ""
    root.part_navigation.applySuggestion({
      text: action.arguments ? String(action.arguments.query || "") : "",
      expression: { includeFilterIds: filterId.length > 0 ? [filterId] : [] }
    })
    root.queryInputRequested(root.part_navigation.queryText)
    root.requestSearch()
    root.focusRevision++
    return
  }
  if (action.kind === Protocol.ActionKinds.BrowseFolder) {
    root.applyingBrowseNavigation = true
    root.browseDirectory = action.arguments.path
    root.browseOffset = Number(action.arguments.offset || 0)
    root.activeFilters = [{ id: Protocol.SourceIds.Folders, excluded: false }]
    root.queryText = action.arguments.query || ""
    root.queryInputRequested(root.queryText)
    root.applyingBrowseNavigation = false
    root.requestSearch()
    root.focusRevision++
    return
  }
  if (action.kind === Protocol.ActionKinds.CollectFile) {
    root.selectFile(result, !root.fileSelection.some(item => item.path === action.arguments.path))
    return
  }

  if (action.inputs && action.inputs.length > 0 && inputValues === undefined) {
    root.actionPanelOpen = false
    root.part_invocationController.requestInput(result, action, confirmed === true)
    return
  }

  if (action.kind === Protocol.ActionKinds.AgentPrompt) {
    const prompt = action.arguments ? String(action.arguments.prompt || "") : ""
    const backendId = action.arguments ? String(action.arguments.backendId || "") : ""
    if (root.agentAnswerReady()) {
      if (!root.refuseWhenAiOff()) return
      if (root.agentTurnRunning) {
        root.statusText = "The assistant is still answering · Esc cancels"
        return
      }
      if (root.part_agentController.enabled === null || root.part_agentController.enabled === undefined) {
        root.part_agentController.refreshStatus(++root.requestSequence)
      }
      root.openAnswer("agent", prompt, backendId, root.agentBackendLabel(backendId))
      root.part_agentController.startTurn(++root.requestSequence, prompt, backendId)
      root.statusText = "Asking " + root.answerBackendLabel + "…"
      return
    }
    root.openAgentSurface()
    root.part_agentController.startTurn(++root.requestSequence, prompt, backendId)
    root.statusText = "Starting the assistant turn…"
    return
  }

  if (action.kind === Protocol.ActionKinds.AgentOpen) {
    root.openAgentSurface()
    root.part_agentController.open(++root.requestSequence)
    root.statusText = "Resuming the assistant conversation…"
    return
  }

  if (action.kind === Protocol.ActionKinds.AiPrompt) {
    if (!root.refuseWhenAiOff()) return
    root.openAnswer("direct", action.arguments.prompt || root.queryText, "",
      String(action.arguments.modelId || action.arguments.model || "Direct AI"))
    const attachments = action.arguments.resultId
      ? [{
          kind: action.arguments.attachmentKind || Protocol.AiAttachmentKinds.Result,
          resultId: action.arguments.resultId,
          displayName: result.title || "Selected result"
        }]
      : []
    root.activeAiRequestId = ++root.requestSequence
    root.part_aiController.begin({
      providerId: action.arguments.providerId,
      modelId: action.arguments.modelId || action.arguments.model || "",
      userText: action.arguments.prompt || root.queryText,
      workflowId: action.arguments.workflowId || root.integrationIds.DefaultAiWorkflow,
      attachments: attachments
    }, root.activeAiRequestId)
    root.statusText = "Preparing exact AI request…"
    return
  }

  const refreshAfterAction = (action.kind === Protocol.ActionKinds.ClipboardHistory && action.id !== Ui.OutputAction.Copy)
    || action.kind === Protocol.ActionKinds.HerdrCreateTab || action.kind === Protocol.ActionKinds.HerdrRenameTab
    || action.kind === Protocol.ActionKinds.HeyTriage || action.kind === Protocol.ActionKinds.ResetRanking
    || (action.kind === Protocol.ActionKinds.SystemdUnit
      && [Protocol.SystemdUnitActionIds.Start, Protocol.SystemdUnitActionIds.Stop,
        Protocol.SystemdUnitActionIds.Restart].indexOf(action.id) >= 0)
  const herdrEditPending = action.kind === Protocol.ActionKinds.HerdrCreateTab || action.kind === Protocol.ActionKinds.HerdrRenameTab
    || action.kind === Protocol.ActionKinds.HerdrOpenProject
  const requestId = ++root.requestSequence
  if (!root.part_invocationController.begin(requestId, refreshAfterAction, herdrEditPending, action.kind)) return
  // A change whose row carries its hidden undo is offered for undo once it succeeds.
  const undo = fromUndo !== true && result && result.actions
    ? result.actions.find(value => value.hidden === true && value.id === Protocol.UndoActions.Prefix + action.id)
    : null
  root.pendingUndo = undo ? { result: result, undoActionId: undo.id } : null
  root.part_protocol.send({
    version: Protocol.Version,
    requestId: requestId,
    operation: Protocol.Operations.Invoke,
    resultId: result.id,
    invocationReference: result.invocationReference,
    actionId: action.id,
    inputValues: inputValues || null,
    confirmed: confirmed === true
  })
}

function runFileSelection(root, actionId) {
  if (root.part_invocationController.busy || root.part_invocationController.collecting) {
    root.statusText = "An action is still in progress"
    return
  }
  if (root.activeFileSelectionRequestId > 0 || !root.fileSelection.length) return
  const requestId = ++root.requestSequence
  const copying = actionId === Protocol.FileSelectionActionIds.CopyPaths ? Protocol.ActionKinds.CopyText : ""
  if (!root.part_invocationController.begin(requestId, false, false, copying)) return
  root.part_protocol.send({ version: Protocol.Version, requestId: requestId,
    operation: Protocol.Operations.FilesBatch, actionId: actionId })
}

function clearFileSelection(root) {
  root.activeFileSelectionRequestId = ++root.requestSequence
  root.part_protocol.send({ version: Protocol.Version, requestId: root.activeFileSelectionRequestId, operation: Protocol.Operations.FilesClear })
  root.fileSelection = []
}

function selectFile(root, result, enabled) {
  if (root.activeFileSelectionRequestId > 0) return
  root.activeFileSelectionRequestId = ++root.requestSequence
  root.part_protocol.send({ version: Protocol.Version, requestId: root.activeFileSelectionRequestId,
    operation: Protocol.Operations.FilesSelect, resultId: result.id, enabled: enabled })
}

// The primary action that takes its inputs inline: one to three of them, and
// no confirmation, which would ask again without the entered values.
function pillTarget(root, result) {
  const action = result && result.actions && result.actions.length > 0 ? result.actions[0] : null
  if (!action || action.requiresConfirmation === true || result.kind === Protocol.ResultKinds.ExtensionCommand) return null
  const inputs = action.inputs || []
  return inputs.length >= 1 && inputs.length <= 3 ? { result: result, action: action } : null
}

function submitPills(root, values) {
  const target = pillTarget(root, root.part_navigation.selectedResult())
  if (!target) return
  root.focusRevision++
  root.invokeAction(target.result, target.action, false, values)
}

function invokeSelectedPrimary(root) {
  if (!root.daemonAvailable || root.reconciliationPending) return
  const result = root.part_navigation.selectedResult()
  if (waitsForCurrentResults(root, result)) return
  if (result && result.kind === Protocol.ResultKinds.ExtensionCommand) {
    root.part_extensionController.open(result)
    return
  }
  if (!result || !result.actions || result.actions.length === 0) {
    return
  }

  if (result.kind === Protocol.ResultKinds.SavedSearch && result.savedSearch) {
    root.applySavedSearch(result.savedSearch)
    return
  }

  root.invokeAction(result, result.actions[0], false)
}

// Undoes the latest HEY change through the hidden undo action of the row it
// was made on, or else the latest hide. The refresh after the change replaces
// that row and may offer other changes, so the undo is never looked up there:
// it runs with the reference the daemon returned for it.
function undoLast(root) {
  const pending = root.lastUndo
  if (!pending) return root.undoHide()
  root.lastUndo = null
  const action = pending.result.actions ? pending.result.actions.find(value => value.id === pending.undoActionId) : null
  if (!action || !pending.invocationReference) {
    root.statusText = "That change can no longer be undone here"
    return 0
  }
  const row = Object.assign({}, pending.result, { invocationReference: pending.invocationReference })
  root.invokeAction(row, action, false, undefined, true)
  return 1
}
