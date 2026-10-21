.pragma library
.import "UiConstants.js" as Ui
.import "Protocol.js" as Protocol
.import "DeckHiddenItemActions.js" as DeckHiddenItemActions

function handleMessage(root, message) {
  // The search streams the most replies, so it is asked first.
  if (root.part_searchSession.handleMessage(message)) return
  if (root.part_clipboardController.handleMessage(message)) return
  if (root.part_queryHistoryController.handleMessage(message)) return
  if (root.part_queryRecall.handleMessage(message)) return
  if (root.part_extensionManagementController.handleMessage(message)) return
  if (root.part_extensionController.handleMessage(message)) return
  if (root.part_hiddenItemsController.handleMessage(message)) return
  if (root.part_aiController.handleMessage(message)) {
    root.activeAiRequestId = root.part_aiController.activeRequestId
    root.statusText = root.part_aiController.state === Ui.AiState.Preflight
      ? "Review the exact AI request"
      : root.part_aiController.state === Ui.AiState.Streaming
      ? "Generating AI response…"
      : root.part_aiController.error.length > 0 ? root.part_aiController.error : root.part_aiController.state
    return
  }
  if (root.part_agentController.handleMessage(message)) {
    root.syncAgentSettingsState()
    return
  }
  if (root.part_aiSettingsController.handleMessage(message)) return
  if (message.type === Protocol.Messages.AiProvidersResult
      && message.requestId === root.activeAiProvidersRequestId) {
    root.activeAiProvidersRequestId = 0
    root.aiProviders = message.aiProviders ? message.aiProviders.providers || [] : []
    return
  }
  if (message.type === Protocol.Messages.AiModelsResult
      && message.requestId === root.activeAiModelsRequestId) {
    root.activeAiModelsRequestId = 0
    root.aiModels = message.aiModels ? message.aiModels.models || [] : []
    root.aiSettingsError = message.aiModels && message.aiModels.error
      ? message.aiModels.error.summary || "Model refresh failed" : ""
    return
  }
  if (message.type === Protocol.Messages.AiSelectionResult
      && message.requestId === root.activeAiSelectionRequestId) {
    root.activeAiSelectionRequestId = 0
    root.aiSelectionBusy = false
    root.aiSelection = message.aiSelection || null
    return
  }
  if (message.type === Protocol.Messages.AiTemplatesResult
      && message.requestId === root.activeAiTemplatesRequestId) {
    root.activeAiTemplatesRequestId = 0
    root.aiTemplateBusy = false
    root.aiTemplates = message.aiTemplates ? message.aiTemplates.templates || [] : []
    return
  }
  if (message.type === Protocol.Messages.BootstrapResult
      && message.requestId === root.activeBootstrapRequestId) {
    root.activeBootstrapRequestId = 0
    const bootstrap = message.bootstrap || ({})
    root.part_personalization.resetPreferences(bootstrap.preferences || null)
    root.filterDescriptors = bootstrap.filters || []
    root.filterAliases = bootstrap.filterAliases || []
    root.savedSearches = bootstrap.savedSearches || []
    root.clipboardShortcutStateReady = !!bootstrap.preferences && Array.isArray(bootstrap.savedSearches)
    root.filterAliasError = ""
    root.sourceDescriptors = bootstrap.sources || []
    root.fallbackQuicklinks = bootstrap.fallbackQuicklinks || []
    return
  }

  if (message.type === Protocol.Messages.Status
      && message.requestId === root.activeStatusRequestId) {
    root.runtimeStatus = message.runtimeStatus || null
    return
  }

  if (message.type === Protocol.Messages.Done
      && message.requestId === root.activeStatusRequestId) {
    root.activeStatusRequestId = 0
    return
  }

  const activeMutation = root.part_personalization.active
  const mutationMatches = activeMutation !== null
    && activeMutation.request.requestId === message.requestId

  if (message.type === Protocol.Messages.PreferencesResult && mutationMatches) {
    const operation = activeMutation.request.operation
    const incoming = message.preferences || null
    if (incoming && root.preferencesSnapshot
        && (!incoming.shortcuts || incoming.shortcuts.length === 0)) {
      incoming.shortcuts = root.preferencesSnapshot.shortcuts || []
    }
    root.preferencesSnapshot = incoming
    if (incoming) {
      if (operation === Protocol.Operations.PreferencesUpdate) {
        root.preferencesSaveError = ""
        root.part_personalization.acceptPreferences(incoming.preferences)
      } else if (!root.part_personalization.preferenceInFlight) {
        root.part_personalization.resetPreferences(incoming.preferences)
      }
    }
    root.part_personalization.settle(message.requestId)
    root.preferencesSnapshotLoading = false
    root.refreshSettingsSnapshot()
    root.part_searchSession.refresh()
    if (operation === Protocol.Operations.CustomizationsReset) root.requestBootstrap()
    if (root.settingsOpen) root.requestPreferencesSnapshot()
    return
  }

  if (message.type === Protocol.Messages.PreferencesResult
      && message.requestId === root.activePreferencesRequestId) {
    root.activePreferencesRequestId = 0
    root.preferencesSnapshotLoading = false
    const incoming = message.preferences || null
    if (incoming && root.preferencesSnapshot
        && (!incoming.shortcuts || incoming.shortcuts.length === 0)) {
      incoming.shortcuts = root.preferencesSnapshot.shortcuts || []
    }
    root.preferencesSnapshot = incoming
    if (incoming) root.part_personalization.resetPreferences(incoming.preferences)
    if (root.reconciliationPending && incoming) {
      root.reconciliationPreferencesComplete = true
    }
    root.refreshSettingsSnapshot()
    if (!root.part_personalization.preferenceInFlight && !root.part_personalization.busy
        && root.preferencesReloadPending) {
      root.requestPreferencesSnapshot()
    }
    root.part_searchSession.refresh(root.reconciliationPending)
    return
  }

  if (message.type === Protocol.Messages.CustomizationResult && mutationMatches
      && activeMutation.context.kind === Ui.MutationKind.Hidden) {
    const change = message.customization
    DeckHiddenItemActions.applyHiddenChange(root, change, activeMutation.context)
    root.statusText = change && change.message ? change.message : "Customization saved"
    root.part_personalization.settle(message.requestId)
    // Home refills the slot the hidden row freed; a restore already re-searched.
    if (change && change.isHidden) root.part_searchSession.refreshHome()
    return
  }

  if (message.type === Protocol.Messages.CustomizationResult && mutationMatches) {
    const change = message.customization
    const wasEditingAlias = activeMutation.context.kind === Ui.MutationKind.Alias && root.aliasDialogOpen
    root.applyCustomization(change)
    if (root.actionPanelOpen && root.activeActionPanel) {
      root.activeActionPanel.updateCustomization(change)
    }
    root.statusText = change && change.message ? change.message : "Customization saved"
    if (wasEditingAlias) root.finishAliasDialog()
    root.part_personalization.settle(message.requestId)
    root.part_searchSession.refreshHome()
    if (root.settingsOpen) root.requestPreferencesSnapshot()
    if (!wasEditingAlias) root.focusRevision++
    return
  }

  if (message.type === Protocol.Messages.FilterAliasResult && mutationMatches) {
    const result = message.filterAliases || ({})
    root.filterAliases = result.aliases || []
    root.filterDescriptors = result.filters || root.filterDescriptors
    root.filterAliasError = ""
    root.statusText = result.message || "Filter aliases updated"
    root.part_personalization.settle(message.requestId)
    root.focusRevision++
    return
  }

  if (message.type === Protocol.Messages.SavedSearchResult && mutationMatches) {
    const result = message.savedSearches || ({})
    root.savedSearches = result.searches || []
    root.savedSearchError = ""
    root.statusText = result.message || "Saved searches updated"
    root.part_personalization.settle(message.requestId)
    root.part_searchSession.refreshHome()
    root.focusRevision++
    return
  }

  if (message.type === Protocol.Messages.ExportResult && mutationMatches) {
    const showExport = root.opened && activeMutation.context
      && activeMutation.context.modalIntentGeneration === root.modalIntentGeneration
    root.part_personalization.settle(message.requestId)
    if (showExport) {
      root.dialogTitle = "Customization export"
      root.detailContent = JSON.stringify(message.export || {}, null, 2)
      root.detailDialogRequested = true
    }
    return
  }

  if (message.type === Protocol.Messages.FileSelection && message.requestId === root.activeFileSelectionRequestId) {
    root.activeFileSelectionRequestId = 0
    root.fileSelection = message.fileSelection || []
    root.statusText = root.fileSelection.length + " paths selected"
    root.focusRevision++
    return
  }

  // Retention survives close/reopen; only an active invocation may update UI.
  if (message.type === Protocol.Messages.ActionResult) {
    root.part_searchRetention.complete(message.requestId, !!message.actionResult && message.actionResult.succeeded)
  }
  if (message.type === Protocol.Messages.Error) root.part_searchRetention.complete(message.requestId, false)

  if (message.type === Protocol.Messages.ActionResult
      && root.part_invocationController.busy
      && message.requestId === root.part_invocationController.requestId) {
    const outcome = message.actionResult
    const transition = root.part_invocationController.complete(message.requestId, outcome,
      !root.launcherPreferences || root.launcherPreferences.hideAfterAction !== false)
    root.statusText = transition.status
    // A successful change with a hidden undo takes over the undo strip once the
    // daemon returns the undo's own reference, which outlives the row the
    // refresh below replaces.
    if (outcome && outcome.succeeded && root.pendingUndo && outcome.undoInvocationReference) {
      root.lastHidden = null
      root.lastUndo = { result: root.pendingUndo.result, undoActionId: root.pendingUndo.undoActionId,
        invocationReference: String(outcome.undoInvocationReference), message: outcome.message || transition.status }
    }
    root.pendingUndo = null
    if (transition.output) {
      root.dialogTitle = outcome.message || "Action output"
      root.detailContent = outcome.detailMarkdown
      root.actionOutputPlainText = true
      root.detailDialogRequested = true
    } else if (transition.refresh) {
      root.requestSearch()
    } else if (transition.dismiss) {
      root.dismiss()
      root.showHud(transition.hud)
    }
    return
  }

  if (message.type === Protocol.Messages.PreviewResult
      && message.requestId === root.activePreviewRequestId) {
    root.filePreview = message.preview || null
    root.previewLoading = false
    root.previewError = ""
    return
  }

  if (message.type === Protocol.Messages.Done
      && message.requestId === root.activePreviewRequestId) {
    root.activePreviewRequestId = 0
    root.previewLoading = false
    return
  }

  if (message.type === Protocol.Messages.Error) {
    if (message.requestId === root.activeFileSelectionRequestId) {
      root.activeFileSelectionRequestId = 0
      root.statusText = message.error || "Could not update file selection"
      return
    }
    if (message.requestId === root.activePreviewRequestId) {
      root.previewLoading = false
      root.filePreview = null
      root.previewError = message.error || "Preview unavailable"
      return
    }

    if (mutationMatches) {
      root.statusText = message.error || "Unknown daemon error"
      if (activeMutation.context.kind === Ui.MutationKind.Alias && root.aliasDialogOpen) {
        root.aliasDialogError = root.statusText
      }
      if (activeMutation.context.kind === Ui.MutationKind.FilterAlias) {
        root.filterAliasError = root.statusText
      }
      if (activeMutation.context.kind === Ui.MutationKind.SavedSearch) {
        root.savedSearchError = root.statusText
      }
      if (activeMutation.request.operation === Protocol.Operations.PreferencesUpdate) {
        root.part_personalization.rejectPreferences(root.statusText)
      }
      if (root.settingsOpen) root.preferencesReloadPending = true
      root.part_personalization.settle(message.requestId)
      return
    }

    const relevant = message.requestId === root.part_invocationController.requestId
      || message.requestId === root.activeAiRequestId
      || message.requestId === root.activeBootstrapRequestId
      || message.requestId === root.activePreferencesRequestId
      || message.requestId === root.activeStatusRequestId
      || message.requestId === root.activeAiProvidersRequestId
      || message.requestId === root.activeAiModelsRequestId
      || message.requestId === root.activeAiSelectionRequestId
      || message.requestId === root.activeAiTemplatesRequestId
    if (!relevant) return
    root.statusText = root.reconciliationPending && root.reconciliationWarning.length > 0
      ? root.reconciliationWarning
      : message.error || "Unknown daemon error"
    if (message.requestId === root.activeAiTemplatesRequestId) {
      root.activeAiTemplatesRequestId = 0
      root.aiTemplateBusy = false
      root.aiSettingsError = message.error || "AI template update failed"
    }
    if (message.requestId === root.activeAiProvidersRequestId) {
      root.activeAiProvidersRequestId = 0
      root.aiSettingsError = message.error || "AI providers are unavailable"
    }
    if (message.requestId === root.activeAiModelsRequestId) {
      root.activeAiModelsRequestId = 0
      root.aiSettingsError = message.error || "AI model refresh failed"
    }
    if (message.requestId === root.activeAiSelectionRequestId) {
      root.activeAiSelectionRequestId = 0
      root.aiSelectionBusy = false
      root.aiSettingsError = message.error || "AI selection update failed"
    }
    root.part_invocationController.settle(message.requestId)
    if (message.requestId === root.activeAiRequestId) root.activeAiRequestId = 0
    if (message.requestId === root.activePreferencesRequestId) {
      root.activePreferencesRequestId = 0
      root.preferencesSnapshotLoading = false
    }
    if (message.requestId === root.activeStatusRequestId) root.activeStatusRequestId = 0

  }
}
