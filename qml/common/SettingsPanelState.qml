pragma ComponentBehavior: Bound
import QtQuick
import "UiConstants.js" as Ui
import "Protocol.js" as Protocol

// Settings inputs, derived summaries, and control references shared by the pages.
FocusScope {
  id: settingsState
  required property var owner
  required property var parts
  readonly property var part_aiHub: parts.aiHub
  property bool workspaceSnapshotsAvailable: false
  readonly property var part_closeButton: parts.closeButton
  readonly property var part_aiRemoveConfirmation: parts.aiRemoveConfirmation
  readonly property var part_aiProviderEditor: parts.aiProviderEditor
  readonly property var part_fileSearchCard: parts.searchScroll.part_fileSearchCard
  readonly property var part_homeFolderOnlyCheck: parts.searchScroll.part_homeFolderOnlyCheck
  readonly property var part_omarchyMenuStateCheck: parts.searchScroll.part_omarchyMenuStateCheck
  readonly property var part_sourcesCard: parts.searchScroll.part_sourcesCard
  readonly property var part_manageExtensions: parts.searchScroll.part_manageExtensions
  readonly property var part_sourceRepeater: parts.searchScroll.part_sourceRepeater
  readonly property var part_sourceList: parts.searchScroll.part_sourceList
  readonly property var part_heyCard: parts.searchScroll.part_heyCard
  readonly property var part_behaviorCard: parts.searchScroll.part_behaviorCard
  readonly property var part_fallbackChoice: parts.searchScroll.part_fallbackChoice
  readonly property var part_assistantCard: parts.assistantScroll.part_assistantCard
  readonly property var part_agentEnabledCheck: parts.assistantScroll.part_agentEnabledCheck
  readonly property var part_agentLogin: parts.assistantScroll.part_agentLogin
  readonly property var part_agentLogout: parts.assistantScroll.part_agentLogout
  readonly property var part_modelCard: parts.assistantScroll.part_modelCard
  readonly property var part_agentBackendChoice: parts.assistantScroll.part_agentBackendChoice
  readonly property var part_agentModelChoice: parts.assistantScroll.part_agentModelChoice
  readonly property var part_agentModelsRefresh: parts.assistantScroll.part_agentModelsRefresh
  readonly property var part_agentEffortChoice: parts.assistantScroll.part_agentEffortChoice
  readonly property var part_agentHiddenModelsCheck: parts.assistantScroll.part_agentHiddenModelsCheck
  readonly property var part_trustCard: parts.assistantScroll.part_trustCard
  readonly property var part_agentTrustChoice: parts.assistantScroll.part_agentTrustChoice
  readonly property var part_agentChallengeCard: parts.assistantScroll.part_agentChallengeCard
  readonly property var part_agentChallengePrompt: parts.assistantScroll.part_agentChallengePrompt
  readonly property var part_agentWarningRepeater: parts.assistantScroll.part_agentWarningRepeater
  readonly property var part_agentPhraseField: parts.assistantScroll.part_agentPhraseField
  readonly property var part_agentDurationChoice: parts.assistantScroll.part_agentDurationChoice
  readonly property var part_agentChallengeConfirm: parts.assistantScroll.part_agentChallengeConfirm
  readonly property var part_agentChallengeCancel: parts.assistantScroll.part_agentChallengeCancel
  readonly property var part_agentContextPolicyChoice: parts.assistantScroll.part_agentContextPolicyChoice
  readonly property var part_agentWebSearchCheck: parts.assistantScroll.part_agentWebSearchCheck
  readonly property var part_limitsCard: parts.assistantScroll.part_limitsCard
  readonly property var part_agentRetentionSpin: parts.assistantScroll.part_agentRetentionSpin
  readonly property var part_agentTurnTimeoutSpin: parts.assistantScroll.part_agentTurnTimeoutSpin
  readonly property var part_grantsCard: parts.assistantScroll.part_grantsCard
  readonly property var part_agentGrantRepeater: parts.assistantScroll.part_agentGrantRepeater
  readonly property var part_agentGrantsClear: parts.assistantScroll.part_agentGrantsClear
  readonly property var part_diagnosticsCard: parts.assistantScroll.part_diagnosticsCard
  readonly property var part_agentStatusText: parts.assistantScroll.part_agentStatusText
  readonly property var part_editorsCard: parts.editorsScroll.part_editorsCard
  readonly property var part_preferredEditorChoice: parts.editorsScroll.part_preferredEditorChoice
  readonly property var part_graphicalEditorExecutable: parts.editorsScroll.part_graphicalEditorExecutable
  readonly property var part_graphicalEditorArguments: parts.editorsScroll.part_graphicalEditorArguments
  readonly property var part_terminalEditorExecutable: parts.editorsScroll.part_terminalEditorExecutable
  readonly property var part_terminalEditorArguments: parts.editorsScroll.part_terminalEditorArguments
  readonly property var part_editorSave: parts.editorsScroll.part_editorSave
  readonly property var part_filterAliasesCard: parts.filtersScroll.part_filterAliasesCard
  readonly property var part_filterAliasInput: parts.filtersScroll.part_filterAliasInput
  readonly property var part_filterAliasTarget: parts.filtersScroll.part_filterAliasTarget
  readonly property var part_filterAliasSave: parts.filtersScroll.part_filterAliasSave
  readonly property var part_filterAliasRepeater: parts.filtersScroll.part_filterAliasRepeater
  readonly property var part_savedSearchesCard: parts.filtersScroll.part_savedSearchesCard
  readonly property var part_savedSearchName: parts.filtersScroll.part_savedSearchName
  readonly property var part_savedSearchShortcut: parts.filtersScroll.part_savedSearchShortcut
  readonly property var part_savedSearchHome: parts.filtersScroll.part_savedSearchHome
  readonly property var part_savedSearchSave: parts.filtersScroll.part_savedSearchSave
  readonly property var part_savedSearchRepeater: parts.filtersScroll.part_savedSearchRepeater
  readonly property var part_aiFeaturesCard: parts.aiHub.part_aiFeaturesCard
  readonly property var part_aiFeaturesToggle: parts.aiHub.part_aiFeaturesToggle
  readonly property var part_aiManagementCard: parts.aiScroll.part_aiManagementCard
  readonly property var part_aiEnableToggle: parts.aiScroll.part_aiEnableToggle
  readonly property var part_aiManagementStatus: parts.aiScroll.part_aiManagementStatus
  readonly property var part_aiProviderRepeater: parts.aiScroll.part_aiProviderRepeater
  readonly property var part_aiAddProvider: parts.aiScroll.part_aiAddProvider
  readonly property var part_aiReload: parts.aiScroll.part_aiReload
  readonly property var part_aiProvidersCard: parts.aiScroll.part_aiProvidersCard
  readonly property var part_aiProviderChoice: parts.aiScroll.part_aiProviderChoice
  readonly property var part_aiModelChoice: parts.aiScroll.part_aiModelChoice
  readonly property var part_aiSelectionSave: parts.aiScroll.part_aiSelectionSave
  readonly property var part_aiStatus: parts.aiScroll.part_aiStatus
  readonly property var part_templatesCard: parts.aiScroll.part_templatesCard
  readonly property var part_aiTemplateRepeater: parts.aiScroll.part_aiTemplateRepeater
  readonly property var part_aiTemplateId: parts.aiScroll.part_aiTemplateId
  readonly property var part_aiTemplateName: parts.aiScroll.part_aiTemplateName
  readonly property var part_aiTemplateSystem: parts.aiScroll.part_aiTemplateSystem
  readonly property var part_aiTemplateUser: parts.aiScroll.part_aiTemplateUser
  readonly property var part_shortcutsCard: parts.shortcutsScroll.part_shortcutsCard
  readonly property var part_shortcutRepeater: parts.shortcutsScroll.part_shortcutRepeater
  readonly property var part_settingsSearch: parts.settingsSearch
  readonly property var part_displayCard: parts.customizationScroll.part_displayCard
  readonly property var part_displaySizeChoice: parts.customizationScroll.part_displaySizeChoice
  readonly property var part_layoutCard: parts.customizationScroll.part_layoutCard
  readonly property var part_compactLayoutCheck: parts.customizationScroll.part_compactLayoutCheck
  readonly property var part_rememberSearchesCheck: parts.customizationScroll.part_rememberSearchesCheck
  readonly property var part_learnFromUseCheck: parts.customizationScroll.part_learnFromUseCheck
  readonly property var part_previewPaneCheck: parts.customizationScroll.part_previewPaneCheck
  readonly property var part_closingCard: parts.customizationScroll.part_closingCard
  readonly property var part_escapeClearsCheck: parts.customizationScroll.part_escapeClearsCheck
  readonly property var part_keepSearchChoice: parts.customizationScroll.part_keepSearchChoice
  readonly property var part_transparencyCard: parts.customizationScroll.part_transparencyCard
  readonly property var part_colorsCard: parts.customizationScroll.part_colorsCard
  readonly property var part_panelTransparencyCheck: parts.customizationScroll.part_panelTransparencyCheck
  readonly property var part_panelTransparencySlider: parts.customizationScroll.part_panelTransparencySlider
  readonly property var part_favoritesCard: parts.customizationScroll.part_favoritesCard
  readonly property var part_hiddenItemsCard: parts.customizationScroll.part_hiddenItemsCard
  readonly property var part_dataCard: parts.customizationScroll.part_dataCard
  readonly property var part_resetCard: parts.customizationScroll.part_resetCard

  // Account-auth metadata uses camelCase, unlike the login-operation method IDs.
  readonly property var accountAuthMethods: Object.freeze({ ApiKey: "apiKey" })
  readonly property var templateDefaults: Object.freeze({ Workflow: "custom", Parameter: "input" })


  required property var theme
  signal extensionsRequested()
  readonly property var extensionsControl: owner.part_manageExtensions
  property var snapshot: null
  // Hidden items arrive separately from the snapshot, in bounded pages.
  property var hiddenItems: null
  property string hiddenItemsError: ""
  property var fallbackQuicklinks: []
  property var sourceDescriptors: []
  property var filterDescriptors: []
  property var filterAliases: []
  property string filterAliasError: ""
  property var savedSearches: []
  property string savedSearchError: ""
  property var aiProviders: []
  property var aiModels: []
  property var aiSelection: null
  property bool aiSelectionBusy: false
  property var aiTemplates: []
  property bool aiTemplateBusy: false
  property var editingAiTemplate: null
  property string aiError: ""
  // Daemon-owned provider management (ai.settings.* / ai.providers.*). The
  // controller in CommandDeck.qml owns correlation; this panel renders the
  // acknowledged snapshot and its saving/saved/conflict/lost states.
  property var aiSettings: null
  // AI master switch owned by the deck's AiFeaturesStore.
  property bool aiFeaturesEnabled: true
  onAiFeaturesEnabledChanged: settingsState.owner.part_aiFeaturesToggle.checked = settingsState.owner.aiFeaturesEnabled
  property string aiSettingsStatus: Ui.ProviderSettingsState.Idle
  property string aiSettingsErrorText: ""
  property string aiSettingsErrorCategory: ""
  property bool aiSettingsBusy: false
  property bool aiSettingsSupported: true
  property var aiTestResult: null
  property bool aiTestBusy: false
  property var aiEditorInvoker: null
  property var aiRemovalTarget: null
  property var aiRowTestStates: ({})
  readonly property bool aiSettingsLoaded: settingsState.owner.aiSettings !== null && settingsState.owner.aiSettings !== undefined
  readonly property bool aiDirectEnabled: settingsState.owner.aiSettingsLoaded && settingsState.owner.aiSettings.enabled === true
  readonly property int aiSettingsRevision: settingsState.owner.aiSettingsLoaded ? Number(settingsState.owner.aiSettings.revision || 0) : 0
  readonly property var aiManagedProviders: owner.aiSettingsLoaded ? (owner.aiSettings.providers || []) : []
  readonly property var aiPresets: owner.aiSettingsLoaded ? (owner.aiSettings.presets || []) : []
  readonly property var aiReservedIds: owner.aiSettingsLoaded ? (owner.aiSettings.reservedIds || []) : []
  readonly property var aiConfigurationError: owner.aiSettingsLoaded ? (owner.aiSettings.error || null) : null
  readonly property bool aiManagementMutable: settingsState.owner.aiSettingsLoaded && settingsState.owner.aiSettingsSupported
    && !settingsState.owner.aiSettingsBusy && settingsState.owner.aiConfigurationError === null
    && settingsState.owner.aiSettingsStatus !== Ui.ProviderSettingsState.ConnectionLost
  readonly property string aiManagementSummary: {
    if (!settingsState.owner.aiSettingsSupported) {
      return "This daemon cannot manage providers from Settings. Update CommandDeck; the configuration file still applies."
    }
    if (settingsState.owner.aiSettingsStatus === Ui.ProviderSettingsState.ConnectionLost) return "Cannot load provider settings: the daemon is unavailable. Known providers stay listed until it reconnects."
    if (!settingsState.owner.aiSettingsLoaded) return "Loading provider settings…"
    if (settingsState.owner.aiConfigurationError) {
      return "The managed provider settings document is invalid: " + String(settingsState.owner.aiConfigurationError.summary || "")
        + " Direct AI stays off until the file is repaired and the daemon restarts."
    }
    const parts = []
    const providers = settingsState.owner.aiManagedProviders
    if (!settingsState.owner.aiDirectEnabled && providers.length > 0) parts.push("Direct AI is off. Providers stay editable; enable direct AI to use them.")
    else if (providers.length === 0) parts.push("Add a provider to use direct AI.")
    else if (providers.every(value => value.enabled === false)) parts.push("All providers are disabled. Enable one or edit it.")
    else parts.push(providers.length + " provider(s). Testing and model discovery happen only when you ask.")
    parts.push(String(settingsState.owner.aiSettings.source) === Protocol.AiProviderSettingsSources.ManagedDocument
      ? "Managed by CommandDeck; the ai section of commanddeck.jsonc is inactive."
      : "Read from commanddeck.jsonc; the first change moves providers into CommandDeck's managed settings.")
    switch (settingsState.owner.aiSettingsStatus) {
      case Ui.ProviderSettingsState.Saving: parts.push("Saving…"); break
      case Ui.ProviderSettingsState.Saved: parts.push("Saved."); break
      case Ui.ProviderSettingsState.SavedSelectionFailed: parts.push("Provider saved; selection was not changed."); break
      case Ui.ProviderSettingsState.Conflict: parts.push("Settings changed elsewhere: reload before saving again."); break
      case Ui.ProviderSettingsState.Failed: parts.push(settingsState.owner.aiSettingsErrorText); break
      default: break
    }
    return parts.join(" ")
  }
  property var agentBackends: []
  readonly property var assistantBackends: agentBackends.filter(backend =>
    backend && backend.kind !== Protocol.AgentBackendKinds.AnswerOnly)
  property var agentModels: []
  property var agentGrants: []
  property var agentChallenge: null
  property var agentStatus: null
  property var agentPreferenceValues: null
  property string agentError: ""
  readonly property string agentRequiredPhrase: "ALLOW UNRESTRICTED AGENT"
  readonly property var agentTrustLevelNames: ["Answers", "Launcher",
    "Approved context", "Read entire home", "Packages", "Confirmed commands",
    "Unrestricted"]
  readonly property var agentTrustLevelDescriptions: [
    "Chat only. No tools and no access to your files.",
    "Search and open what the launcher already shows, after you confirm.",
    "Read files inside folders you approve, one folder at a time.",
    "Read anything in your home except protected credential paths.",
    "Preview, install, and remove pacman packages behind a password prompt.",
    "Run exact commands you confirm; each one can be remembered.",
    "No sandbox: full home, network, and session. Needs the phrase and lapses after an hour."
  ]
  property var currentExpression: ({ includeFilterIds: [], excludeFilterIds: [], clauses: [] })
  property var editingSavedSearch: null
  property bool savedSearchAwaitingAck: false
  property var iconResolver: function(iconName) { return "" }
  property bool loading: false
  property bool snapshotMutationBusy: false
  // D13 - the active display-size preset (QML-only, persisted outside the
  // daemon snapshot). The launcher owns persistence; this panel only chooses.
  property string displaySizePreset: Ui.DisplaySize.Auto
  readonly property var displaySizeOptions: [
    { id: Ui.DisplaySize.Auto, title: "Auto", description: "Match the screen (recommended)." },
    { id: Ui.DisplaySize.Small, title: "Small", description: "90% of the automatic size." },
    { id: Ui.DisplaySize.Default, title: "Default", description: "The standard size." },
    { id: Ui.DisplaySize.Large, title: "Large", description: "115% for easier reading." },
    { id: Ui.DisplaySize.ExtraLarge, title: "Extra large", description: "130% for high-resolution displays." }
  ]
  function displaySizeIndex(preset) {
    for (let index = 0; index < settingsState.owner.displaySizeOptions.length; index++) {
      if (settingsState.owner.displaySizeOptions[index].id === preset) return index
    }
    return 0
  }
  // Compact layout (QML-only, persisted outside the daemon snapshot).
  property bool compactLayout: false
  property bool previewPaneVisible: true
  // Closing and reopening (QML-only, persisted outside the daemon snapshot).
  property bool escapeClearsSearch: false
  property int keepSearchSeconds: 0
  readonly property var keepSearchOptions: [
    { id: 0, title: "Off", description: "Reopening always starts on Home." },
    { id: 30, title: "30 seconds", description: "Reopening within 30 seconds shows the same search." },
    { id: 120, title: "2 minutes", description: "Reopening within 2 minutes shows the same search." },
    { id: 600, title: "10 minutes", description: "Reopening within 10 minutes shows the same search." }
  ]
  function keepSearchIndex(seconds) {
    const index = settingsState.owner.keepSearchOptions.findIndex(option => option.id === seconds)
    return index >= 0 ? index : 0
  }
  // Panel transparency (QML-only, persisted outside the daemon snapshot).
  property bool panelTransparencyEnabled: false
  property int panelTransparencyPercent: 20
  // Why the last save was rejected (the deck owns it); empty when saves succeed.
  property string preferencesSaveError: ""
  // Launcher colours (QML-only): the Omarchy theme or CommandDeck's palette.
  property string colorThemeSource: Ui.ColorThemeSource.Omarchy
  property var deckColors: null
  property bool hostThemeAvailable: true
  property string currentPage: Ui.SettingsPage.Search
  property bool agentModelsAutoRequested: false
  readonly property var pages: [
    { id: Ui.SettingsPage.Search, title: "Search", subtitle: "Sources, fallback, motion", icon: "⌕" },
    { id: Ui.SettingsPage.Ai, title: "AI", subtitle: "Assistant and quick answers", icon: "✦" },
    { id: Ui.SettingsPage.Editors, title: "Editors", subtitle: "Open files and lines", icon: "✎" },
    { id: Ui.SettingsPage.Filters, title: "Filters", subtitle: "Aliases and saved searches", icon: "⚲" },
    { id: Ui.SettingsPage.Shortcuts, title: "Shortcuts", subtitle: "Effective bindings", icon: "⌘" },
    { id: Ui.SettingsPage.Customization, title: "Customization", subtitle: "Size, colors, favorites", icon: "☆" }
  ]
  readonly property var pageCards: ({
    search: [settingsState.owner.part_fileSearchCard, settingsState.owner.part_sourcesCard, settingsState.owner.part_heyCard, settingsState.owner.part_behaviorCard],
    assistant: [settingsState.owner.part_assistantCard, settingsState.owner.part_modelCard, settingsState.owner.part_trustCard, settingsState.owner.part_limitsCard, settingsState.owner.part_grantsCard, settingsState.owner.part_diagnosticsCard],
    editors: [settingsState.owner.part_editorsCard],
    filters: [settingsState.owner.part_filterAliasesCard, settingsState.owner.part_savedSearchesCard],
    quickAnswers: [settingsState.owner.part_aiManagementCard, settingsState.owner.part_aiProvidersCard, settingsState.owner.part_templatesCard],
    ai: [settingsState.owner.part_aiFeaturesCard, settingsState.owner.part_assistantCard, settingsState.owner.part_modelCard,
      settingsState.owner.part_trustCard, settingsState.owner.part_limitsCard, settingsState.owner.part_grantsCard,
      settingsState.owner.part_diagnosticsCard, settingsState.owner.part_aiManagementCard,
      settingsState.owner.part_aiProvidersCard, settingsState.owner.part_templatesCard],
    shortcuts: [settingsState.owner.part_shortcutsCard],
    customization: [settingsState.owner.part_displayCard, settingsState.owner.part_layoutCard, settingsState.owner.part_closingCard, settingsState.owner.part_colorsCard, settingsState.owner.part_transparencyCard, settingsState.owner.part_favoritesCard, settingsState.owner.part_hiddenItemsCard, settingsState.owner.part_dataCard, settingsState.owner.part_resetCard]
  })
  readonly property var filteredPages: owner.pages.filter(page =>
    page.id === Ui.SettingsPage.Ai && !settingsState.owner.aiFeaturesEnabled
      ? settingsState.owner.part_aiFeaturesCard.matchesSearch
      : settingsState.owner.pageCards[page.id].some(card => card && card.matchesSearch))
  onFilteredPagesChanged: {
    if (filteredPages.length > 0 && !filteredPages.some(page => page.id === settingsState.owner.currentPage))
      settingsState.owner.showPage(filteredPages[0].id)
  }
  readonly property var searchControl: parts.settingsSearch
  readonly property var backgroundControl: parts.settingsBackground
  readonly property var pagePickerControl: parts.pagePicker
  readonly property string selectedFallbackId: settingsState.owner.snapshot && settingsState.owner.snapshot.preferences
    ? settingsState.owner.snapshot.preferences.webFallbackQuicklinkId || ""
    : ""
  readonly property bool fallbackSelectionUnavailable:
    settingsState.owner.selectedFallbackId.length > 0 && !settingsState.owner.hasFallback(settingsState.owner.selectedFallbackId)
  readonly property string fallbackDisplayText: settingsState.owner.part_fallbackChoice.currentText
  readonly property bool customizationOverflow:
    settingsState.owner.snapshot && settingsState.owner.snapshot.customizationsTruncated === true
  readonly property bool favoritesReorderAvailable: !settingsState.owner.customizationOverflow
  readonly property string customizationOverflowText: settingsState.owner.customizationOverflow
    ? "Showing " + settingsState.owner.snapshot.customizations.length + " of "
      + settingsState.owner.snapshot.totalCustomizationCount
      + " customizations. Use commanddeck preferences export for a complete backup; "
      + "reordering is disabled until the legacy overflow is reduced."
    : ""
  signal dismissed()
  signal preferencesChanged(var preferences)
  signal displaySizeSelected(string preset)
  signal panelTransparencyChanged(bool enabled, int percent)
  signal compactLayoutSelected(bool enabled)
  signal previewPaneSelected(bool visible)
  signal launcherBehaviorSelected(bool escapeClears, int keepSeconds)
  signal colorThemeChanged(var change)
  signal colorThemeResetRequested()
  signal resetRequested()
  signal exportRequested()
  signal orphanRemoveRequested(string customizationId)
  signal hiddenItemsRequested(string query, int offset)
  signal hiddenItemsDismissed()
  signal hiddenItemRestoreRequested(string customizationId)
  signal favoritesReordered(var stableIds)
  signal historyRequested()
  signal filterAliasSetRequested(string alias, string filterId)
  signal filterAliasRemoveRequested(string alias)
  signal savedSearchSaveRequested(var search)
  signal savedSearchDeleteRequested(var search)
  signal savedSearchRunRequested(var search)
  signal aiModelsRefreshRequested(string providerId)
  signal aiSelectionUpdateRequested(string providerId, string modelId)
  signal aiTemplateSaveRequested(var template)
  signal aiTemplateDeleteRequested(var template)
  signal aiSettingsReloadRequested()
  signal aiEnableRequested(bool enabled, int revision)
  signal aiFeaturesToggled(bool enabled)
  signal aiProviderSaveRequested(var provider, bool create, int revision, bool selectAfterSave)
  signal aiProviderDeleteRequested(string providerId, int revision)
  signal aiProviderTestRequested(var provider)
  signal aiProviderTestCancelRequested()
  signal aiDraftInvalidated()
  signal agentPreferencesChanged(var preferences)
  signal agentModelsRefreshRequested(string backendId)
  signal agentTrustChallengeRequested(int level)
  signal agentTrustUpdateRequested(string challengeId, int level, string phrase,
    string duration)
  signal agentChallengeCancelled()
  signal agentGrantRemoveRequested(string grantId)
  signal agentGrantsClearRequested()
  signal agentLogoutRequested()
  signal agentLoginRequested()

  readonly property bool agentChallengeRequiresPhrase: settingsState.owner.agentChallenge
    ? settingsState.owner.agentChallenge.requiresPhrase === true : false
  readonly property bool agentChallengeRequiresDuration: settingsState.owner.agentChallenge
    ? settingsState.owner.agentChallenge.requiresDuration === true : false
  readonly property bool agentChallengeSatisfied: settingsState.owner.agentChallenge !== null
    && settingsState.owner.agentChallenge !== undefined
    && (!settingsState.owner.agentChallengeRequiresPhrase
      || settingsState.owner.part_agentPhraseField.text === settingsState.owner.agentRequiredPhrase)
  readonly property bool agentEnabled: settingsState.owner.agentPreferenceValues
    ? settingsState.owner.agentPreferenceValues.enabled === true : false
  readonly property string agentLoginState: settingsState.owner.agentStatus && settingsState.owner.agentStatus.login
    ? String(settingsState.owner.agentStatus.login.state || "") : ""
  readonly property bool agentAuthenticated: settingsState.owner.agentLoginState === Protocol.AgentAuthenticationStates.Authenticated
  readonly property string agentAccountSummary: {
    if (!settingsState.owner.agentStatus) return "Status has not been read yet."
    const status = settingsState.owner.agentStatus
    const parts = []
    const backendName = status.backend && status.backend.displayName ? String(status.backend.displayName) : "the backend"
    if (settingsState.owner.agentLoginState === Protocol.AgentAuthenticationStates.NotApplicable) {
      parts.push(backendName + " answers through the direct AI preflight; no sign-in")
    } else if (settingsState.owner.agentAuthenticated) {
      const method = status.login && status.login.method ? String(status.login.method) : ""
      parts.push(method === settingsState.owner.accountAuthMethods.ApiKey ? "Signed in to " + backendName + " with an API key" : "Signed in to " + backendName)
    } else if (settingsState.owner.agentLoginState === Protocol.AgentAuthenticationStates.Pending) {
      parts.push(backendName + " sign-in in progress")
    } else if (status.backend && status.backend.available === false) {
      parts.push(backendName + " CLI is not installed")
    } else {
      parts.push("Not signed in to " + backendName)
    }
    if (status.binaryVersion) {
      parts.push("Codex " + String(status.binaryVersion)
        + (status.binaryReviewed === false ? " (unreviewed)" : ""))
    }
    if (status.sandboxAvailable === false) parts.push("Sandbox unavailable")
    return parts.join(" · ")
  }

  readonly property string agentStatusSummary: {
    if (settingsState.owner.agentError.length > 0) return settingsState.owner.agentError
    const status = settingsState.owner.agentStatus
    if (!status) return "Assistant status has not been read yet."
    const parts = []
    const helper = status.helper || null
    if (helper) {
      parts.push(helper.installed === true
        ? "Package helper: installed"
          + (helper.versionMatches === false ? " (version mismatch)" : "")
        : "Package helper: not installed")
    } else {
      parts.push("Package helper: not installed")
    }
    const trust = status.trust || null
    if (trust) {
      parts.push("Trust " + String(trust.level !== undefined ? trust.level : "?")
        + ": " + String(trust.levelName || ""))
      const expiry = Number(trust.leaseExpiresUnixMilliseconds || 0)
      if (expiry > 0) {
        const minutes = Math.max(0, Math.round((expiry - Date.now()) / 60000))
        parts.push(minutes > 0
          ? "Lease ends in " + minutes + " min"
          : "Lease expired")
      }
    }
    if (status.sandboxAvailable === false) parts.push("Sandbox unavailable")
    if (status.transientUnitAvailable === false) {
      parts.push("Transient unit unavailable")
    }
    const warnings = status.warnings || []
    for (let index = 0; index < warnings.length; index++) {
      parts.push(String(warnings[index]))
    }
    return parts.join(" · ")
  }

}
