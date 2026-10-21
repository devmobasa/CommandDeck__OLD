pragma ComponentBehavior: Bound
import "DeckConnectionActions.js" as DeckConnectionActions
import "DeckConfirmationActions.js" as DeckConfirmationActions
import "DeckMessageHandling.js" as DeckMessageHandling
import "DeckInvocationActions.js" as DeckInvocationActions
import "DeckSearchActions.js" as DeckSearchActions
import "DeckPreferenceActions.js" as DeckPreferenceActions
import "DeckAssistantActions.js" as DeckAssistantActions
import "DeckAiSettingsActions.js" as DeckAiSettingsActions
import "DeckHelpActions.js" as DeckHelpActions
import "DeckDisplayActions.js" as DeckDisplayActions
import "WorkspaceResultOrder.js" as WorkspaceResultOrder
import QtQuick
import "UiConstants.js" as Ui
import "Protocol.js" as Protocol
import Quickshell

CommandDeckState {
  id: root
  property url themeTokenSource
  owner: root
  readonly property alias part_appTheme: appTheme
  readonly property alias part_settingsTheme: appTheme
  readonly property alias part_extensionManagementController: extensionManagementController
  readonly property alias part_requestTracker: requestTracker
  readonly property alias part_invocationController: invocationController
  readonly property alias part_extensionController: extensionController
  readonly property alias part_aiController: aiController
  readonly property alias part_rows: resultRows
  readonly property alias part_quicklinksController: quicklinksController
  readonly property alias part_personalization: personalization
  readonly property alias part_agentController: agentController
  readonly property alias part_aiFeaturesStore: aiFeaturesStore
  readonly property alias part_protocol: supervisor.protocolControl
  readonly property alias part_aiSettingsController: aiSettingsController
  readonly property alias part_transparencyStore: displayPreferences.transparencyStore
  readonly property alias part_displayScaleStore: displayPreferences.displayScaleStore
  readonly property alias part_layoutStore: displayPreferences.layoutStore
  readonly property alias part_behaviorStore: displayPreferences.behaviorStore
  readonly property alias part_colorThemeStore: displayPreferences.colorThemeStore
  readonly property alias part_navigation: navigation
  readonly property alias part_filterCatalog: filterCatalog
  readonly property alias part_clipboardController: clipboardController
  readonly property alias part_lifecycle: lifecycle

  property alias requestSequence: requestTracker.sequence
  property alias activeAiRequestId: requestTracker.aiRequestId
  property alias activePreviewRequestId: requestTracker.previewRequestId
  property alias activeBootstrapRequestId: requestTracker.bootstrapRequestId
  property alias activePreferencesRequestId: requestTracker.preferencesRequestId
  property alias activeStatusRequestId: requestTracker.statusRequestId
  property alias selectedIndex: navigation.selectedIndex
  property alias queryText: navigation.queryText
  property alias focusRevision: navigation.focusRevision
  property alias filterDescriptors: navigation.filterDescriptors
  property alias activeFilters: navigation.activeFilters
  onAssistantModeActiveChanged: {
    root.updateAssistantPrewarm()
    if (!assistantModeActive && root.answerMode === "agent") root.closeAnswer()
  }
  signal queryInputRequested(string value)

  // ListModel.get() is not a binding dependency, so the selected result is
  // published as a property and refreshed whenever selection or rows change.
  property alias currentResult: navigation.currentResult
  readonly property alias navigationController: navigation

  signal protocolRequestSent(var request)

  function shortcutActions(result) { return DeckDisplayActions.shortcutActions(root, result) }

  function invokeShortcut(id) { return DeckDisplayActions.invokeShortcut(root, id) }

  readonly property int actionCount: root.currentResult
    && root.currentResult.actions
      ? root.currentResult.actions.filter(action => action.hidden !== true).length
        + (root.currentResult.canCustomize ? 2 : 0)
        + (root.hasRankingExplanation(root.currentResult) ? 1 : 0)
      : 0
  readonly property bool personalizationBusy: personalization.busy
  readonly property bool preferenceUpdateInFlight: personalization.preferenceInFlight

  // Slice 2 discoverability state (teaching content in the main content area).
  property bool filterPaletteOpen: false
  // C10 first-run coach: mirrors the durable store's "seen" flag. Kept as a
  // plain property (not a binding) so a dismiss can latch it true durably.
  property bool coachSeen: false
  readonly property string coachStateDir: root.resolveStateDir()

  // Mirrors the QML-only store; 100% keeps the current sizing profile's baseline.
  property int displaySizePercent: Ui.DisplaySizePercent.Default
  readonly property real displaySizeMultiplier: root.displaySizePercent / 100
  property var presentingWindow: null
  property bool suspendedForScreen: false
  // The scale actually handed to the theme, exposed for tests and diagnostics.
  readonly property real effectiveDisplayScale: root.presentingWindow ? root.presentingWindow.part_appTheme.displayScale : 1

  // Applied immediately (the binding below recomputes) and persisted QML-only.
  function applyDisplaySizePercent(percent) { return DeckDisplayActions.applyDisplaySizePercent(root, percent) }
  function stepDisplaySize(direction) { return DeckDisplayActions.stepDisplaySize(root, direction) }
  function applyCompactLayout(enabled) { return DeckDisplayActions.applyCompactLayout(root, enabled) }
  function applyPreviewPane(visible) { return DeckDisplayActions.applyPreviewPane(root, visible) }
  function togglePreviewPane() { return DeckDisplayActions.togglePreviewPane(root) }
  function applyLauncherBehavior(escapeClears, keepSeconds) { return part_behaviorStore.set(escapeClears, keepSeconds) }
  function backToRoot() { return DeckSearchActions.backToRoot(root) }

  function applyPanelTransparency(enabled, percent) { return DeckDisplayActions.applyPanelTransparency(root, enabled, percent) }

  // The empty query has no useful home content to show when both the text and
  // the active filters are empty.
  readonly property bool queryIsEmpty: navigation.queryText.trim().length === 0
    && navigation.activeFilters.length === 0
  readonly property bool hasResults: resultRows.count > 0
  // The result model includes one section header before a sole result.
  readonly property bool calculatorOnly: resultRows.count === 2
    && currentResult !== null && currentResult.kind === Protocol.ResultKinds.Calculator
  // Any surface drawn over the content area; the coach must never cover one.
  readonly property bool anyBlockingSurface: settingsOpen || helpOpen || agentOpen
    || clipboardController.opened
    || part_invocationController.actionInputOpen || actionPanelOpen || aliasDialogOpen || confirmationOpen || detailDialogRequested
    || queryHistoryOpen || indexInspectorOpen || filterPaletteOpen
    || aiController.state !== Ui.AiState.Idle
  // A2 teaching empty-state: shown only when the empty query genuinely has no
  // home/recent content to display and we are not mid-search (debounce or reply).
  readonly property bool showEmptyState: opened && daemonAvailable && !reconciliationPending && queryIsEmpty
    && !hasResults && !part_searchSession.searching
  // C9 guiding no-results: a non-empty query (or active filter) with nothing to
  // show and no search debouncing or in flight.
  readonly property bool showNoResults: opened && daemonAvailable && !reconciliationPending && !queryIsEmpty
    && !hasResults && !part_searchSession.searching

  onCoachSeenChanged: {
    if (coachSeen) coachStore.markSeen()
  }

  LauncherProtocolController {
    id: supervisor
    host: root
  }

  LauncherLifecycleController {
    id: lifecycle
    host: root
  }

  ClipboardHistoryController {
    id: clipboardController
    nextRequestId: function() { return ++root.requestSequence }
    available: root.daemonAvailable
    frontendInstance: lifecycle.frontendInstance
    openGeneration: lifecycle.openGeneration
    onRequest: message => root.part_protocol.send(message)
  }

  ProtocolRequestTracker {
    id: requestTracker
  }

  InvocationController {
    id: invocationController
  }

  FirstRunStore {
    id: coachStore
    active: !root.testMode
    stateDir: root.coachStateDir
    onChanged: root.coachSeen = coachStore.seen
  }

  DisplayPreferences {
    id: displayPreferences
    host: root
  }

  // A question or permission request from the assistant needs the full
  // conversation, so an inline answer expands on its own.
  Connections {
    target: agentController
    function onPendingChanged() {
      if (root.answerMode === "agent" && agentController.pending) root.openAgentSurface()
    }
    function onEnabledChanged() { root.expandAnswerIfCardNeeded() }
    function onBackendChanged() { root.expandAnswerIfCardNeeded() }
    function onStateChanged() {
      if (agentController.state === Protocol.AgentConversationStates.Failed) root.expandAnswerIfCardNeeded()
    }
  }

  Connections {
    target: root.compositor
    function onScreensChanged() { DeckDisplayActions.reconcileScreens(root) }
    function onFocusedWorkspaceChanged() {
      const workspace = root.compositor.focusedWorkspace
      root.workspaceMruIds = WorkspaceResultOrder.noteRecent(
        root.workspaceMruIds,
        workspace ? workspace.id : null,
        100)
    }
  }

  AiFeaturesStore {
    id: aiFeaturesStore
    active: !root.testMode
    stateDir: root.coachStateDir
    onChanged: root.aiFeaturesEnabled = aiFeaturesStore.enabled
  }

  AiProviderSettingsController {
    id: aiSettingsController
    onRequest: message => root.part_protocol.send(message)
    onApplied: {
      // A management mutation changes the live inventory and may change the
      // reconciled selection; refresh the selection card from the daemon.
      if (root.settingsOpen && aiSettingsController.status !== Ui.ProviderSettingsState.Idle) {
        root.requestAiProviders()
        root.requestAiSelection()
      }
    }
  }

  AiController {
    id: aiController
    onRequest: message => root.part_protocol.send(message)
  }

  AgentController {
    id: agentController
    onStatusRefreshRequested: {
      if (root.daemonAvailable) agentController.refreshStatus(++root.requestSequence)
    }
    onRequest: message => root.part_protocol.send(message)
    // Self-heal a turn stuck in "starting": re-open the conversation with a
    // fresh request id so the daemon snapshot re-subscribes and corrects the UI.
    onResyncRequested: agentController.open(++root.requestSequence)
  }

  // Shared assistant-token registry (ai:/codex:/ask:); drives the intent
  // prewarm detection in assistantModeActive.
  FilterCatalog {
    id: filterCatalog
  }

  onStatusTextChanged: announcementCoalescer.publish(statusText)

  function reservedAliasValues() { return DeckHelpActions.reservedAliasValues(root) }

  function hasRankingExplanation(result) { return DeckHelpActions.hasRankingExplanation(root, result) }

  function rankingExplanationText(result) { return DeckHelpActions.rankingExplanationText(root, result) }

  function showRankingExplanation(result) { return DeckHelpActions.showRankingExplanation(root, result) }

  function toggleHelp(invoker) { return DeckHelpActions.toggleHelp(root, invoker) }

  function closeHelp() { return DeckHelpActions.closeHelp(root) }

  // C10 - resolve the durable coach marker directory: $XDG_STATE_HOME/commanddeck,
  // falling back to ~/.local/state/commanddeck when the variable is unset.
  function resolveStateDir() { return DeckHelpActions.resolveStateDir(root) }

  // Slice 2 assistant route (D12/C9). Reuse the documented `ai:` path: setting
  // the search field to the assistant filter runs the exact same prefix
  // conversion typing `ai:` by hand triggers — no new backend path. Any text
  // already typed is carried in after the token so the assistant sees it.
  function askAssistant(carryQuery) { return DeckHelpActions.askAssistant(root, carryQuery) }

  // A2/A3 - drop the user into a filter by inserting its token into the field.
  // Setting the input text runs the same prefix conversion as typing it.
  function insertFilterToken(token) { return DeckHelpActions.insertFilterToken(root, token) }

  function openFilterPalette(invoker) { return DeckHelpActions.openFilterPalette(root, invoker) }

  function closeFilterPalette(restoreFocus) { return DeckHelpActions.closeFilterPalette(root, restoreFocus) }

  // A3 - typing "?" as the whole query opens the palette. The sentinel is
  // cleared so the empty-state sits behind the palette, and closing it returns
  // to that empty query rather than a stray "?".
  function maybeHandleFilterSentinel() { return DeckHelpActions.maybeHandleFilterSentinel(root) }

  onQueryTextChanged: {
    root.part_searchRetention.edited()
    root.maybeHandleFilterSentinel()
  }
  onActiveFiltersChanged: {
    root.part_searchRetention.edited()
  }

  function resultStatus(list) { return DeckHelpActions.resultStatus(root, list) }

  function announceStatus(value) { return DeckHelpActions.announceStatus(root, value) }

  function requestBootstrap() { return DeckAiSettingsActions.requestBootstrap(root) }

  function openSettings() { return DeckAiSettingsActions.openSettings(root) }

  function requestAiProviders() { return DeckAiSettingsActions.requestAiProviders(root) }

  function requestAiSelection() { return DeckAiSettingsActions.requestAiSelection(root) }

  function requestAiModels(providerId) { return DeckAiSettingsActions.requestAiModels(root, providerId) }

  function updateAiSelection(providerId, modelId) { return DeckAiSettingsActions.updateAiSelection(root, providerId, modelId) }

  function requestAiTemplates() { return DeckAiSettingsActions.requestAiTemplates(root) }

  function saveAiTemplate(template) { return DeckAiSettingsActions.saveAiTemplate(root, template) }

  function confirmDeleteAiTemplate(template) { return DeckAiSettingsActions.confirmDeleteAiTemplate(root, template) }

  function deleteAiTemplate(template) { return DeckAiSettingsActions.deleteAiTemplate(root, template) }

  // The daemon owns the assistant turn, so opening and closing this surface
  // never cancels or clears it. Only the explicit Clear button does.
  function openAgentSurface() { return DeckAssistantActions.openAgentSurface(root) }

  function closeAgentSurface() { return DeckAssistantActions.closeAgentSurface(root) }

  readonly property string aiOffStatusText: "AI features are off. Turn them on in Settings › AI providers."

  // Every launcher-side AI entry point checks the master switch first.
  function refuseWhenAiOff() { return DeckAssistantActions.refuseWhenAiOff(root) }

  // Turning AI off also closes the assistant and switches direct AI off at
  // the daemon, so no request can be sent from any surface. Turning it back
  // on leaves direct AI as it is; the providers card shows its state.
  function setAiFeaturesEnabled(enabled) { return DeckAssistantActions.setAiFeaturesEnabled(root, enabled) }

  readonly property bool agentTurnRunning: agentController.state === Ui.AgentState.Starting
    || agentController.state === Protocol.AgentConversationStates.Running
    || agentController.state === Protocol.AgentConversationStates.Waiting

  // The conversation surface shows an enable or login card in exactly these
  // states; the pane answers directly in every other one, including "status
  // not fetched yet". If the daemon then reports a card is needed, the pane
  // expands to the conversation so the card is seen.
  function agentNeedsCard() { return DeckAssistantActions.agentNeedsCard(root) }

  function agentAnswerReady() { return DeckAssistantActions.agentAnswerReady(root) }

  function expandAnswerIfCardNeeded() { return DeckAssistantActions.expandAnswerIfCardNeeded(root) }

  function agentBackendLabel(backendId) { return DeckAssistantActions.agentBackendLabel(root, backendId) }

  function openAnswer(mode, query, backendId, label) { return DeckAssistantActions.openAnswer(root, mode, query, backendId, label) }

  // Esc, Close, or leaving assistant mode. A running turn is cancelled and
  // direct AI state is cleared so its preflight lease is released.
  function closeAnswer() { return DeckAssistantActions.closeAnswer(root) }

  // Hide without touching the daemon: the palette closing parks a running
  // turn the same way it parks the conversation.
  function hideAnswer() { return DeckAssistantActions.hideAnswer(root) }

  // Esc in the search field closes an open answer before it closes the palette.
  function escapeFromSearch() { return DeckAssistantActions.escapeFromSearch(root) }

  // Fire the intent prewarm once per entry into assistant mode, and re-arm on
  // exit so a later re-entry prewarms again. Driven by onAssistantModeActive-
  // Changed, so opening the surface or the query becoming an assistant token
  // each fire exactly once, not per keystroke. The daemon also no-ops safely,
  // but gating on enabled avoids a needless round trip.
  function updateAssistantPrewarm() { return DeckAssistantActions.updateAssistantPrewarm(root) }

  function syncAgentSettingsState() { return DeckAssistantActions.syncAgentSettingsState(root) }

  function requestAgentSettings() { return DeckAssistantActions.requestAgentSettings(root) }

  function sendAgentLoginAction(actionId) { return DeckAssistantActions.sendAgentLoginAction(root, actionId) }

  function requestPreferencesSnapshot(includeShortcuts) { return DeckPreferenceActions.requestPreferencesSnapshot(root, includeShortcuts) }

  function closeSettings() { return DeckPreferenceActions.closeSettings(root) }

  function openQueryHistory() { return part_queryHistoryController.open() }

  function closeQueryHistory() { return part_queryHistoryController.close() }

  function requestQueryHistory() { return part_queryHistoryController.refresh() }

  function deleteQueryHistory(historyId) { return part_queryHistoryController.remove(historyId) }

  function resetQueryHistory() { return part_queryHistoryController.reset() }

  function updatePreferences(preferences) { return DeckPreferenceActions.updatePreferences(root, preferences) }

  function refreshSettingsSnapshot() { return DeckPreferenceActions.refreshSettingsSnapshot(root) }

  function enqueuePersonalization(operation, values, context) { return DeckPreferenceActions.enqueuePersonalization(root, operation, values, context) }

  function setFavorite(result, enabled) { return DeckPreferenceActions.setFavorite(root, result, enabled) }
  function moveFavorite(direction) { return DeckPreferenceActions.moveFavorite(root, direction) }

  function setAlias(result, value) { return DeckPreferenceActions.setAlias(root, result, value) }

  function finishAliasDialog() { return DeckPreferenceActions.finishAliasDialog(root) }

  function applyCustomization(change) { return DeckPreferenceActions.applyCustomization(root, change) }

  function cancelPreview() { return DeckSearchActions.cancelPreview(root) }

  function requestPreview(result, matchIndex) { return DeckSearchActions.requestPreview(root, result, matchIndex) }

  function openPreviewMatch() { return DeckSearchActions.openPreviewMatch(root) }

  function toggle(payload) { return lifecycle.toggle(payload) }

  function clearModalState() { return DeckSearchActions.clearModalState(root) }

  function clearAiState() { return DeckSearchActions.clearAiState(root) }

  function open(payloadJson) { return lifecycle.open(payloadJson) }

  function close() { return lifecycle.close() }

  function dismiss() { return lifecycle.dismiss() }

  function requestSearch(force) { return root.part_searchSession.search(force) }

  function savedSearchText(expression) { return DeckSearchActions.savedSearchText(root, expression) }

  function applySavedSearch(search) { return DeckSearchActions.applySavedSearch(root, search) }

  function requestRuntimeStatus() { return DeckSearchActions.requestRuntimeStatus(root) }

  function invokeSelectedPrimary() { return DeckInvocationActions.invokeSelectedPrimary(root) }

  function selectFile(result, enabled) { return DeckInvocationActions.selectFile(root, result, enabled) }

  function clearFileSelection() { return DeckInvocationActions.clearFileSelection(root) }

  function runFileSelection(actionId) { return DeckInvocationActions.runFileSelection(root, actionId) }

  function invokeAction(result, action, confirmed, inputValues, fromUndo) { return DeckInvocationActions.invokeAction(root, result, action, confirmed, inputValues, fromUndo) }

  function handleMessage(message) { return DeckMessageHandling.handleMessage(root, message) }

  QuicklinksController {
    id: quicklinksController
    ready: root.part_protocol.ready
    nextRequestId: function() { return ++root.requestSequence }
    onRequestReady: request => root.part_protocol.send(request)
    onChanged: result => {
      root.fallbackQuicklinks = (result.quicklinks || []).filter(entry => entry.enabled)
        .map(entry => ({ id: entry.quicklink.id, title: entry.quicklink.title }))
      if (result.message) root.statusText = result.message
      if (result.message || (root.launcherPreferences
          && (root.launcherPreferences.webFallbackQuicklinkId || "") !== (result.webFallbackQuicklinkId || ""))) {
        root.requestPreferencesSnapshot()
      }
    }
  }

  PersonalizationController {
    id: personalization
    nextRequestId: function() { return ++root.requestSequence }
    onRequestReady: (request, context) => {
      root.preferencesSnapshotLoading = false
      root.part_protocol.send(request)
    }
    onDrained: {
      if (root.preferencesReloadPending) root.requestPreferencesSnapshot()
    }
    onPreferencesDraftChanged: draft => {
      root.launcherPreferences = draft
      root.refreshSettingsSnapshot()
    }
    onRollbackRequested: (preferences, errorText) => {
      root.statusText = errorText
      root.preferencesSaveError = errorText
      root.requestPreferencesSnapshot()
    }
  }

  function acceptConfirmation() { return DeckConfirmationActions.acceptConfirmation(root) }

  function cancelConfirmation() { return DeckConfirmationActions.cancelConfirmation(root) }

  function handleProtocolDisconnected() { return DeckConnectionActions.handleProtocolDisconnected(root) }

  function handleProtocolReconnected() { return DeckConnectionActions.handleProtocolReconnected(root) }

  function completeReconciliationIfReady() { return DeckConnectionActions.completeReconciliationIfReady(root) }

  // Shared colours and Omarchy tokens. Each presenting window owns its metrics.
  Theme {
    id: appTheme
    reduceMotion: root.launcherPreferences
      ? root.launcherPreferences.reduceMotion === true
      : false
    panelTransparencyEnabled: root.panelTransparencyEnabled
    panelTransparency: root.panelTransparencyPercent / 100
    tokenSource: root.themeTokenSource
    overrideHostColors: displayPreferences.colorThemeStore.overridesHost
    deckColors: displayPreferences.colorThemeStore.colors
  }

  Component.onCompleted: {
    const workspace = root.compositor.focusedWorkspace
    root.workspaceMruIds = WorkspaceResultOrder.noteRecent(
      root.workspaceMruIds,
      workspace ? workspace.id : null,
      100)
    coachStore.load()
    displayPreferences.load()
    aiFeaturesStore.load()
    root.requestBootstrap()
    root.requestRuntimeStatus()
  }

  ListModel {
    id: resultRows
  }

  SearchNavigationController {
    id: navigation
    rows: resultRows
    onSearchRequested: root.requestSearch()
    onSettingsRequested: root.openSettings()
    onHelpRequested: root.toggleHelp()
    // Esc from the search field: an open inline answer closes first.
    onDismissRequested: root.escapeFromSearch()
    onRootRequested: root.backToRoot()
    escapeClearsQuery: root.escapeClearsSearch
    onPrimaryRequested: root.invokeSelectedPrimary()
    onSelectionIdentityChanged: result => root.requestPreview(result)
  }

  // Streaming batches can update the visual count several times. Publish one
  // settled accessibility status after the burst instead of announcing every
  // partial frame.
  AnnouncementCoalescer {
    id: announcementCoalescer
    onAnnouncementChanged: {
      root.announcementText = announcement
      root.announceStatus(announcement)
    }
  }

  readonly property alias part_indexRootsController: indexRootsController
  IndexRootsController {
    id: indexRootsController
    nextRequestId: () => ++root.requestSequence
    available: root.daemonAvailable
    onRequest: value => root.part_protocol.send(value)
    onChanged: { root.requestRuntimeStatus(); root.requestSearch() }
  }

  ExtensionSessionController {
    id: extensionController
    nextRequestId: () => ++root.requestSequence
    onRequest: value => root.part_protocol.send(value)
    onReturned: { if (root.opened) root.requestSearch() }
    onExternalActionCompleted: {
      root.forgetSearchOnClose = true
      root.dismiss()
    }
  }
  ExtensionManagementController {
    id: extensionManagementController
    nextRequestId: () => ++root.requestSequence
    onRequest: value => root.part_protocol.send(value)
  }

  Variants {
    model: root.testMode
      ? (root.testSurfaceEnabled ? root.desktopIntegration.screens : [])
      : root.desktopIntegration.screens

    delegate: CommandDeckWindow {
      id: surface
      host: root
    }
  }
}
