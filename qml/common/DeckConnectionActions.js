.pragma library
.import "UiConstants.js" as Ui
.import "Protocol.js" as Protocol

function completeReconciliationIfReady(root) {
  if (!root.reconciliationPending || !root.reconciliationPreferencesComplete
      || !root.reconciliationSearchComplete) return
  root.reconciliationPending = false
  root.reconciliationPreferencesComplete = false
  root.reconciliationSearchComplete = false
  root.reconciliationWarning = ""
  root.statusText = root.resultStatus(root.part_searchSession.pendingResults)
}

function handleProtocolReconnected(root) {
  root.part_lifecycle.reconnected()
  root.daemonAvailable = true
  root.reconciliationPreferencesComplete = false
  root.reconciliationSearchComplete = false
  root.requestBootstrap()
  root.requestPreferencesSnapshot(root.settingsOpen)
  if (root.settingsOpen) {
    root.requestAiProviders()
    root.requestAiSelection()
    root.requestAiTemplates()
    root.part_aiSettingsController.load(++root.requestSequence)
    root.requestAgentSettings()
  }
  root.requestRuntimeStatus()
}

function handleProtocolDisconnected(root) {
  root.part_extensionController.disconnected()
  root.part_extensionManagementController.disconnected()
  root.fileSelection = []
  root.activeFileSelectionRequestId = 0
  root.daemonAvailable = false
  root.clipboardShortcutStateReady = false
  const interrupted = root.part_personalization.abort("CommandDeck bridge disconnected")
  root.preferencesReloadPending = root.settingsOpen
  root.preferencesSnapshotLoading = false
  root.part_requestTracker.clearAllRequests()
  root.part_invocationController.clear()
  root.part_searchSession.clear()
  root.activeAiProvidersRequestId = 0
  root.activeAiModelsRequestId = 0
  root.activeAiSelectionRequestId = 0
  root.activeAiTemplatesRequestId = 0
  root.aiProviders = []
  root.aiModels = []
  root.aiTemplates = []
  root.aiTemplateBusy = false
  root.aiSelectionBusy = false
  root.runtimeStatus = null
  root.previewLoading = false
  root.previewError = "Preview unavailable while the daemon is offline"
  root.part_rows.clear()
  root.selectedIndex = 0
  root.currentResult = null
  root.clearModalState()
  root.part_aiController.disconnect()
  root.part_aiSettingsController.disconnect()
  root.part_agentController.disconnect()
  root.agentBackends = []
  root.agentModels = []
  root.agentGrants = []
  root.agentChallenge = null
  const count = interrupted ? interrupted.length : 0
  const labels = []
  const labelByKind = {
    [Ui.MutationKind.Preferences]: "settings",
    [Ui.MutationKind.Favorite]: "bookmark",
    [Ui.MutationKind.Alias]: "alias",
    [Ui.MutationKind.Reorder]: "reorder",
    [Ui.MutationKind.Orphan]: "unavailable-item removal",
    [Ui.MutationKind.Reset]: "reset",
    [Ui.MutationKind.Export]: "export"
  }
  for (let index = 0; index < count; index++) {
    const kind = interrupted[index].context
      ? interrupted[index].context.kind
      : "change"
    const label = labelByKind[kind] || String(kind)
    if (labels.indexOf(label) < 0) labels.push(label)
  }
  if (count > 0) {
    root.reconciliationWarning = "Connection lost: " + count + " personalization change"
      + (count === 1 ? "" : "s")
      + " need review (" + labels.join(", ")
      + "); the active change may have saved and queued changes were not sent."
  }
  root.reconciliationPending = true
  root.reconciliationPreferencesComplete = false
  root.reconciliationSearchComplete = false
  root.statusText = root.reconciliationWarning.length > 0
    ? root.reconciliationWarning
    : "CommandDeck daemon unavailable"
}
