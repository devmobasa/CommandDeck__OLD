import QtQuick
import QtTest
import "../../qml/common" as CommandDeck
import "../../qml/common/Protocol.js" as Protocol
import "../../qml/common/UiConstants.js" as Ui

DeadlineTestCase {
  required property var fixture
  when: windowShown
  function typeText(value) {
    for (let index = 0; index < value.length; index++) {
      keyClick(value.charAt(index))
    }
  }

  function init() {
    fixture.part_controller.clearHerdrBrowse()
    fixture.part_controller.clearOmarchyBrowse()
    fixture.width = 900
    fixture.part_theme.background = "#090909"
    fixture.part_searchLoading.loading = false
    fixture.part_theme.reduceMotion = false
    fixture.part_searchField.directActions = []
    fixture.part_searchField.shortcutId = ""
    fixture.part_searchField.recallRequests = []
    fixture.part_searchField.zoomRequests = []
    fixture.part_searchField.previewToggles = 0
    fixture.part_searchField.favoriteMoves = []
    fixture.part_searchField.pillTarget = null
    fixture.part_searchField.pillSubmissions = []
    fixture.part_searchField.missingPill = ""
    fixture.part_controller.recalledText = ""
    fixture.part_controller.escapeClearsQuery = false
    fixture.part_controller.rootCalls = 0
    fixture.part_footerActions.availableWidth = 100000
    fixture.part_footerActions.directActions = []
    fixture.part_footerActions.compact = false
    fixture.part_footerActions.shortcutId = ""
    fixture.part_footerActions.primaryActionTitle = "Open"
    fixture.part_footerActions.actionCount = 2
    fixture.part_footerActions.enabled = true
    fixture.part_footerActions.actionsEnabled = true
    fixture.part_searchField.precedingTabStop = Qt.binding(function() { return fixture.part_searchField.additionalTabStop })
    fixture.part_footerActions.primaryCalls = 0
    fixture.part_footerActions.actionCalls = 0
    fixture.part_aliasDialog.opened = false
    fixture.part_aliasDialog.saved = ""
    fixture.part_aliasDialog.localError = ""
    fixture.part_actionPanel.result = null
    fixture.part_actionPanel.shortcuts = []
    fixture.part_actionPanel.chosen = ""
    fixture.part_actionPanel.dismissals = 0
    fixture.part_aiPreflightSurface.acceptCount = 0
    fixture.part_aiPreflightSurface.cancelCount = 0
    fixture.part_aiStreamingSurface.requestedAction = ""
    fixture.part_aiStreamingSurface.requestedPath = ""
    fixture.part_aiStreamingSurface.requestedToken = ""
    fixture.part_aiStreamingSurface.cancelCount = 0
    fixture.part_aiStreamingSurface.retryCount = 0
    fixture.part_aiStreamingSurface.dismissCount = 0
    fixture.part_aiSurfaceController.state = "completed"
    fixture.part_aiSurfaceController.output = "Safe output"
    fixture.part_aiSurfaceController.sentRequests = []
    fixture.part_aiSurfaceController.overwritePending = false
    fixture.part_aiSurfaceController.savedOutputAvailable = false
    fixture.part_controller.activeFilters = []
    fixture.part_controller.queryText = ""
    fixture.part_controller.settingsCalls = 0
    fixture.part_controller.helpCalls = 0
    fixture.part_controller.primaryCalls = 0
    fixture.part_controller.syncRows(fixture.part_controller.buildRows([
      { id: "app:browser", kind: "application", title: "Browser" },
      { id: "app:editor", kind: "application", title: "Editor" },
      { id: "app:terminal", kind: "application", title: "Terminal" }
    ]))
    fixture.part_controller.selectedIndex = fixture.part_controller.firstSelectableIndex()
    fixture.part_searchField.actionRequests = 0
    fixture.part_searchField.additionalTabStop = null
    fixture.part_searchField.inputControl.text = ""
    fixture.part_sourceRow.searchChanges = 0
    fixture.part_filterChip.removals = 0
    fixture.part_settingsPanel.visible = false
    fixture.part_settingsPanel.theme = fixture.part_theme
    fixture.part_settingsPanel.dismissals = 0
    fixture.part_settingsPanel.changedPreferences = null
    fixture.part_settingsPanel.queueChanges = false
    fixture.part_settingsPanel.filterAliases = []
    fixture.part_settingsPanel.filterAliasError = ""
    fixture.part_settingsPanel.filterAliasSaved = ""
    fixture.part_settingsPanel.filterAliasTargetSaved = ""
    fixture.part_settingsPanel.filterAliasRemoved = ""
    fixture.part_settingsPanel.filterAliasInputControl.text = ""
    fixture.part_settingsPanel.aiProviders = []
    fixture.part_settingsPanel.aiModels = []
    fixture.part_settingsPanel.aiSelection = null
    fixture.part_settingsPanel.aiSelectionBusy = false
    fixture.part_settingsPanel.aiError = ""
    fixture.part_settingsPanel.aiModelsRequestedFor = ""
    fixture.part_settingsPanel.aiSelectionProviderRequested = ""
    fixture.part_settingsPanel.aiSelectionModelRequested = ""
    fixture.part_settingsPanel.aiProviderEditorControl.opened = false
    fixture.part_settingsPanel.aiRemoveConfirmationControl.opened = false
    fixture.part_settingsPanel.aiSettings = null
    fixture.part_settingsPanel.aiSettingsStatus = "idle"
    fixture.part_settingsPanel.aiSettingsErrorText = ""
    fixture.part_settingsPanel.aiSettingsErrorCategory = ""
    fixture.part_settingsPanel.aiSettingsBusy = false
    fixture.part_settingsPanel.aiSettingsSupported = true
    fixture.part_settingsPanel.aiTestResult = null
    fixture.part_settingsPanel.aiTestBusy = false
    fixture.part_settingsPanel.aiRowTestStates = ({})
    fixture.part_settingsPanel.aiEditorInvoker = null
    fixture.part_settingsPanel.aiRemovalTarget = null
    fixture.part_settingsPanel.aiSaveRequested = null
    fixture.part_settingsPanel.aiDeleteRequestedId = ""
    fixture.part_settingsPanel.aiDeleteRevision = -1
    fixture.part_settingsPanel.aiTestRequested = null
    fixture.part_settingsPanel.aiEnableRequests = 0
    fixture.part_settingsPanel.aiEnableRequestedValue = false
    fixture.part_settingsPanel.aiEnableRevision = -1
    fixture.part_settingsPanel.aiDraftInvalidations = 0
    fixture.part_settingsPanel.aiReloads = 0
    fixture.part_settingsPanel.aiTestCancels = 0
    fixture.part_settingsPersonalization.requestSequence = 0
    fixture.part_settingsPersonalization.dispatched = null
    fixture.part_settingsPersonalization.rollbackError = ""
    fixture.part_settingsPersonalization.resetPreferences(fixture.part_settingsPanel.snapshot.preferences)
    fixture.part_confirmation.opened = false
    fixture.part_confirmation.accepts = 0
    fixture.part_confirmation.cancels = 0
    fixture.part_detailDialog.opened = false
    fixture.part_detailDialog.dismissals = 0
    fixture.part_backgroundTarget.clicks = 0
    fixture.part_eventHelpSheet.returnFocusTarget = null
    fixture.part_eventHelpSheet.focusReturnAllowed = true
    fixture.part_eventFilterPalette.returnFocusTarget = null
    fixture.part_eventFilterPalette.focusReturnAllowed = true
    fixture.part_eventFilterPalette.opened = false
    fixture.part_eventHelpSheet.opened = false
    fixture.part_eventHelpSheet.dismissals = 0
    fixture.part_queryHistory.opened = false
    fixture.part_queryHistory.dismissals = 0
    fixture.part_queryHistory.deletedId = 0
    fixture.part_queryHistory.resets = 0
    fixture.part_queryHistory.pendingDeleteId = 0
    fixture.part_queryHistory.pendingReset = false
    fixture.part_aiPreflightSurface.opened = false
    fixture.part_aiStreamingSurface.opened = false
    fixture.part_agentEventSurface.opened = false
    fixture.part_agentEventSurface.backToSearchCount = 0
    fixture.part_agentEventSurface.dismissCount = 0
    fixture.part_agentEventSurface.cancelCount = 0
    fixture.part_agentEventSurface.submitted = ""
    fixture.part_agentEventSurface.submittedLoginCode = ""
    fixture.part_agentEventSurface.loginMethod = ""
    fixture.part_agentEventSurface.decisions = []
    fixture.part_agentEventSurface.answeredPendingId = ""
    fixture.part_agentEventSurface.answeredText = ""
    fixture.part_agentEventSurface.loginMethod = ""
    fixture.part_agentEventSurface.enableCount = 0
    fixture.part_agentEventSurface.copyCodeCount = 0
    fixture.part_agentEventSurface.backendSettingsCount = 0
    fixture.part_agentEventSurface.requestedOutputAction = ""
    fixture.part_agentEventSurface.pendingHidden = false
    fixture.part_agentEventSurface.reasoningExpanded = false
    fixture.part_agentEventController.sentRequests = []
    fixture.part_agentEventController.resetConversation()
    fixture.part_agentEventController.models = []
    fixture.part_agentEventController.status = null
    fixture.part_agentEventController.latestModelsRequestId = 0
    fixture.part_agentEventController.enabled = true
    fixture.part_agentEventController.backend = {
      id: "codex.local",
      displayName: "Codex",
      processLocality: "local",
      inferenceLocality: "OpenAI",
      authenticationState: "authenticated"
    }
    fixture.part_agentEventController.trust = { level: 2, levelName: "Approved context" }
    fixture.part_agentEventController.preferences = {
      enabled: true,
      backendId: "codex.local",
      modelId: "gpt-5-codex",
      reasoningEffort: "medium",
      trustLevel: 2,
      contextPolicy: "approved-folders",
      webSearchEnabled: false,
      conversationTtlMinutes: 30,
      turnTimeoutMinutes: 20
    }
    fixture.part_settingsPanel.agentBackends = []
    fixture.part_settingsPanel.agentModels = []
    fixture.part_settingsPanel.agentGrants = []
    fixture.part_settingsPanel.agentChallenge = null
    fixture.part_settingsPanel.agentStatus = null
    fixture.part_settingsPanel.agentPreferenceValues = null
    fixture.part_settingsPanel.agentError = ""
    fixture.part_settingsPanel.agentPreferencesEmitted = null
    fixture.part_settingsPanel.agentTrustChallengeLevel = -1
    fixture.part_settingsPanel.agentTrustUpdate = null
    fixture.part_settingsPanel.agentModelsBackendRequested = ""
    fixture.part_settingsPanel.agentGrantRemoved = ""
    fixture.part_settingsPanel.agentGrantsCleared = 0
    fixture.part_settingsPanel.agentLogouts = 0
    fixture.part_settingsPanel.agentPhraseControl.text = ""
  }

  function aiSpecPresets() {
    return [
      { id: "ollama", displayName: "Ollama", kind: "ollama", baseUrl: "http://127.0.0.1:11434",
        credentialKind: "none", credentialName: null, remoteEndpoint: false, description: "Local Ollama server." },
      { id: "openai", displayName: "OpenAI", kind: "openai-chat-completions", baseUrl: "https://api.openai.com/v1",
        credentialKind: "environment", credentialName: "OPENAI_API_KEY", remoteEndpoint: true, description: "OpenAI Chat Completions." },
      { id: "xai-grok", displayName: "xAI Grok", kind: "openai-chat-completions", baseUrl: "https://api.x.ai/v1",
        credentialKind: "environment", credentialName: "XAI_API_KEY", remoteEndpoint: true, description: "xAI Grok." }
    ]
  }

  function aiSpecSettings(enabled, revision, providers) {
    return {
      supported: true,
      enabled: enabled,
      source: "managed-document",
      revision: revision,
      legacyIgnored: true,
      providers: providers,
      selection: null,
      presets: aiSpecPresets(),
      reservedIds: ["codex.local", "claude.local", "cursor.local", "grok.local"],
      error: null
    }
  }

  function aiSpecProvider(id, enabled) {
    return {
      id: id, displayName: id.toUpperCase(), kind: "ollama", baseUrl: "http://127.0.0.1:11434",
      model: "qwen3:8b", credential: { kind: "none", name: null }, allowRemoteEndpoint: false,
      enabled: enabled, contextLimit: null
    }
  }
}
