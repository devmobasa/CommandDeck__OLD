pragma ComponentBehavior: Bound
import QtQuick
import "UiConstants.js" as Ui
import "Protocol.js" as Protocol
import "DeckAssistantActions.js" as DeckAssistantActions
import Quickshell

SettingsPanel {
  workspaceSnapshotsAvailable: settingsPanel.host.host.desktopCapabilities.workspaceSnapshots
    && !!settingsPanel.host.host.workspaceSnapshotLifecycle
    && settingsPanel.host.host.workspaceSnapshotLifecycle.available
  required property var host
  // False until the window's on-demand loader has built this panel.
  required property bool presented

  id: settingsPanel
  visible: settingsPanel.presented && settingsPanel.host.host.settingsOpen && !settingsPanel.host.host.extensionManagementOpen
  onExtensionsRequested: settingsPanel.host.host.extensionManagementOpen = true
  opacity: visible ? 1 : 0
  Behavior on opacity {
    NumberAnimation { duration: settingsPanel.host.part_appTheme.durationNormal }
  }
  theme: settingsPanel.host.part_settingsTheme
  snapshot: settingsPanel.host.host.settingsSnapshot
  fallbackQuicklinks: settingsPanel.host.host.fallbackQuicklinks
  sourceDescriptors: settingsPanel.host.host.sourceDescriptors
  filterDescriptors: settingsPanel.host.host.filterDescriptors
  filterAliases: settingsPanel.host.host.filterAliases
  filterAliasError: settingsPanel.host.host.filterAliasError
  savedSearches: settingsPanel.host.host.savedSearches
  savedSearchError: settingsPanel.host.host.savedSearchError
  aiProviders: settingsPanel.host.host.aiProviders
  aiModels: settingsPanel.host.host.aiModels
  aiSelection: settingsPanel.host.host.aiSelection
  aiSelectionBusy: settingsPanel.host.host.aiSelectionBusy
  aiTemplates: settingsPanel.host.host.aiTemplates
  aiTemplateBusy: settingsPanel.host.host.aiTemplateBusy
  aiError: settingsPanel.host.host.aiSettingsError
  aiSettings: settingsPanel.host.host.part_aiSettingsController.settings
  aiFeaturesEnabled: settingsPanel.host.host.aiFeaturesEnabled
  onAiFeaturesToggled: enabled => settingsPanel.host.host.setAiFeaturesEnabled(enabled)
  aiSettingsStatus: settingsPanel.host.host.part_aiSettingsController.status
  aiSettingsErrorText: settingsPanel.host.host.part_aiSettingsController.errorText
  aiSettingsErrorCategory: settingsPanel.host.host.part_aiSettingsController.errorCategory
  aiSettingsBusy: settingsPanel.host.host.part_aiSettingsController.busy
  aiSettingsSupported: settingsPanel.host.host.part_aiSettingsController.supported
  aiTestResult: settingsPanel.host.host.part_aiSettingsController.testResult
  aiTestBusy: settingsPanel.host.host.part_aiSettingsController.testBusy
  onAiSettingsReloadRequested: settingsPanel.host.host.part_aiSettingsController.load(++settingsPanel.host.host.requestSequence)
  onAiEnableRequested: (enabled, revision) =>
    settingsPanel.host.host.part_aiSettingsController.setEnabled(++settingsPanel.host.host.requestSequence, enabled)
  onAiProviderSaveRequested: (provider, create, revision, selectAfterSave) =>
    settingsPanel.host.host.part_aiSettingsController.saveProvider(++settingsPanel.host.host.requestSequence, provider, create, selectAfterSave)
  onAiProviderDeleteRequested: (providerId, revision) =>
    settingsPanel.host.host.part_aiSettingsController.deleteProvider(++settingsPanel.host.host.requestSequence, providerId)
  onAiProviderTestRequested: provider =>
    settingsPanel.host.host.part_aiSettingsController.testDraft(++settingsPanel.host.host.requestSequence, provider)
  onAiProviderTestCancelRequested: settingsPanel.host.host.part_aiSettingsController.cancelTest(++settingsPanel.host.host.requestSequence)
  onAiDraftInvalidated: settingsPanel.host.host.part_aiSettingsController.invalidateDraft()
  agentBackends: settingsPanel.host.host.agentBackends
  agentModels: settingsPanel.host.host.agentModels
  agentGrants: settingsPanel.host.host.agentGrants
  agentChallenge: settingsPanel.host.host.agentChallenge
  agentStatus: settingsPanel.host.host.agentStatus
  agentPreferenceValues: settingsPanel.host.host.agentPreferences
  agentError: settingsPanel.host.host.agentSettingsError
  // Parsed only while Settings shows: a panel kept after its first open must
  // not re-parse the launcher query on every keystroke.
  currentExpression: settingsPanel.visible
    ? settingsPanel.host.host.part_navigation.queryExpression()
    : settingsPanel.closedExpression
  readonly property var closedExpression: ({ includeFilterIds: [], excludeFilterIds: [], clauses: [] })
  iconResolver: function(iconName) {
    return iconName.length > 0 ? Quickshell.iconPath(iconName, true) : ""
  }
  loading: settingsPanel.host.host.preferencesSnapshotLoading
  snapshotMutationBusy: settingsPanel.host.host.part_personalization.preferenceInFlight || settingsPanel.host.host.part_personalization.busy
  displaySizePercent: settingsPanel.host.host.displaySizePercent
  displaySizeLegacy: settingsPanel.host.host.part_displayScaleStore.legacy
  displaySizeDescription: settingsPanel.host.displayDescription
  maximumFittingPercent: settingsPanel.host.maximumFittingPercent
  onDisplaySizePercentSelected: percent => settingsPanel.host.host.applyDisplaySizePercent(percent)
  onDisplaySizeRecommendedRequested: settingsPanel.host.host.part_displayScaleStore.useRecommended()
  compactLayout: settingsPanel.host.host.compactLayout
  onCompactLayoutSelected: enabled => settingsPanel.host.host.applyCompactLayout(enabled)
  previewPaneVisible: settingsPanel.host.host.previewPaneVisible
  onPreviewPaneSelected: visible => settingsPanel.host.host.applyPreviewPane(visible)
  escapeClearsSearch: settingsPanel.host.host.escapeClearsSearch
  keepSearchSeconds: settingsPanel.host.host.keepSearchSeconds
  onLauncherBehaviorSelected: (escapeClears, keepSeconds) =>
    settingsPanel.host.host.applyLauncherBehavior(escapeClears, keepSeconds)
  panelTransparencyEnabled: settingsPanel.host.host.panelTransparencyEnabled
  panelTransparencyPercent: settingsPanel.host.host.panelTransparencyPercent
  onPanelTransparencyChanged: (enabled, percent) =>
    settingsPanel.host.host.applyPanelTransparency(enabled, percent)
  preferencesSaveError: settingsPanel.host.host.preferencesSaveError
  colorThemeSource: settingsPanel.host.host.part_colorThemeStore.source
  deckColors: settingsPanel.host.host.part_colorThemeStore.colors
  hostThemeAvailable: settingsPanel.host.part_appTheme.shellThemed
  onColorThemeChanged: change => settingsPanel.host.host.part_colorThemeStore.set(change)
  onColorThemeResetRequested: settingsPanel.host.host.part_colorThemeStore.resetColors()
  onDismissed: settingsPanel.host.host.closeSettings()
  onPreferencesChanged: preferences => settingsPanel.host.host.updatePreferences(preferences)
  onResetRequested: {
    settingsPanel.host.host.resetConfirmation = true
    settingsPanel.host.host.confirmationOpen = true
  }
  onExportRequested: {
    settingsPanel.host.host.enqueuePersonalization(
      Protocol.Operations.CustomizationsExport, {}, { kind: Ui.MutationKind.Export })
  }
  onOrphanRemoveRequested: customizationId => {
    settingsPanel.host.host.enqueuePersonalization(Protocol.Operations.FavoriteRemoveOrphan, {
      customizationId: customizationId
    }, { kind: Ui.MutationKind.Orphan, customizationId: customizationId })
  }
  hiddenItems: settingsPanel.host.host.hiddenItems
  hiddenItemsError: settingsPanel.host.host.hiddenItemsError
  onHiddenItemsRequested: (query, offset) => settingsPanel.host.host.requestHiddenItems(query, offset)
  onHiddenItemsDismissed: settingsPanel.host.host.part_hiddenItemsController.close()
  onHiddenItemRestoreRequested: customizationId => settingsPanel.host.host.restoreHidden(customizationId, true)
  onFavoritesReordered: stableIds => {
    settingsPanel.host.host.enqueuePersonalization(Protocol.Operations.FavoriteReorder, {
      stableIds: stableIds
    }, { kind: Ui.MutationKind.Reorder })
  }
  onHistoryRequested: settingsPanel.host.host.openQueryHistory()
  onFilterAliasSetRequested: (alias, filterId) => {
    settingsPanel.host.host.filterAliasError = ""
    settingsPanel.host.host.enqueuePersonalization(Protocol.Operations.FilterAliasSet, {
      alias: alias,
      targetFilterId: filterId
    }, { kind: Ui.MutationKind.FilterAlias, alias: alias })
  }
  onFilterAliasRemoveRequested: alias => {
    settingsPanel.host.host.filterAliasError = ""
    settingsPanel.host.host.enqueuePersonalization(Protocol.Operations.FilterAliasSet, {
      alias: alias,
      targetFilterId: null
    }, { kind: Ui.MutationKind.FilterAlias, alias: alias })
  }
  onSavedSearchSaveRequested: search => {
    settingsPanel.host.host.savedSearchError = ""
    settingsPanel.host.host.enqueuePersonalization(Protocol.Operations.SavedSearchSave, {
      savedSearch: search
    }, { kind: Ui.MutationKind.SavedSearch, id: search.id || "" })
  }
  onAiModelsRefreshRequested: providerId => settingsPanel.host.host.requestAiModels(providerId)
  onAiSelectionUpdateRequested: (providerId, modelId) =>
    settingsPanel.host.host.updateAiSelection(providerId, modelId)
  onAiTemplateSaveRequested: template => settingsPanel.host.host.saveAiTemplate(template)
  onAiTemplateDeleteRequested: template => settingsPanel.host.host.confirmDeleteAiTemplate(template)
  onSavedSearchDeleteRequested: search => {
    settingsPanel.host.host.savedSearchDeleteTarget = search
    settingsPanel.host.host.confirmationOpen = true
  }
  onSavedSearchRunRequested: search => settingsPanel.host.host.applySavedSearch(search)
  onAgentPreferencesChanged: preferences => {
    settingsPanel.host.host.part_agentController.updatePreferences(++settingsPanel.host.host.requestSequence, preferences)
  }
  onAgentModelsRefreshRequested: backendId => {
    const showHidden = settingsPanel.host.host.agentPreferences
      && settingsPanel.host.host.agentPreferences.showHiddenModels === true
    settingsPanel.host.host.part_agentController.requestModels(
      ++settingsPanel.host.host.requestSequence, showHidden, backendId)
  }
  onAgentTrustChallengeRequested: level => {
    settingsPanel.host.host.part_agentController.requestChallenge(++settingsPanel.host.host.requestSequence, level)
  }
  onAgentTrustUpdateRequested: (challengeId, level, phrase, duration) => {
    settingsPanel.host.host.part_agentController.updateTrust(
      ++settingsPanel.host.host.requestSequence, challengeId, level, phrase, duration)
  }
  onAgentChallengeCancelled: {
    settingsPanel.host.host.part_agentController.cancelChallenge()
    settingsPanel.host.host.agentChallenge = null
  }
  onAgentGrantRemoveRequested: grantId => {
    settingsPanel.host.host.part_agentController.removeGrant(++settingsPanel.host.host.requestSequence, grantId)
  }
  onAgentGrantsClearRequested: {
    settingsPanel.host.host.part_agentController.clearGrants(++settingsPanel.host.host.requestSequence)
  }
  onAgentLogoutRequested: settingsPanel.host.host.part_agentController.logout(++settingsPanel.host.host.requestSequence, "")
  onAgentLoginRequested: DeckAssistantActions.startLoginFromSettings(settingsPanel.host.host)
}
