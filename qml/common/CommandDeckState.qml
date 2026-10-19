pragma ComponentBehavior: Bound
import "DeckHiddenItemActions.js" as DeckHiddenItemActions
import "DeckInvocationActions.js" as DeckInvocationActions
import "DeckSearchActions.js" as DeckSearchActions
import "Protocol.js" as Protocol
import QtQuick
import Quickshell

// Launcher session state and derived presentation values.
Item {
  id: sessionState
  required property var owner
  readonly property alias part_searchSession: searchSession
  SearchSessionController {
    id: searchSession
    nextRequestId: function() { return ++sessionState.owner.requestSequence }
    replaceResults: function(results, reset, completed) {
      return sessionState.owner.part_navigation.replaceResults(results, reset, completed)
    }
    available: sessionState.daemonAvailable
    opened: sessionState.opened
    paused: sessionState.settingsOpen
    diverted: sessionState.owner.activeFilters.length === 1
      && sessionState.owner.activeFilters[0].id === Protocol.SourceIds.Clipboard
      && sessionState.owner.activeFilters[0].excluded !== true
    query: sessionState.owner.queryText
    filters: sessionState.owner.activeFilters
    browseDirectory: sessionState.browseDirectory
    browseOffset: sessionState.browseOffset
    herdrWorkspaceId: sessionState.owner.part_navigation.herdrWorkspaceId
    activeWorkspace: sessionState.compositor.activeWorkspace && sessionState.compositor.activeWorkspace.name
      ? sessionState.compositor.activeWorkspace.name : ""
    currentDirectory: sessionState.currentDirectoryContext
    selectedProject: sessionState.selectedProjectContext
    recentWorkspaceIds: sessionState.openWorkspaceMruIds
    onRequest: message => sessionState.owner.part_protocol.send(message)
    onStarting: identity => DeckSearchActions.searchStarting(sessionState.owner, identity)
    onCommitted: results => DeckSearchActions.searchCommitted(sessionState.owner, results)
    onFinished: completion => DeckSearchActions.searchFinished(sessionState.owner, completion)
    onFailed: error => DeckSearchActions.searchFailed(sessionState.owner, error)
    onDivertRequested: DeckSearchActions.searchClipboard(sessionState.owner)
  }
  readonly property alias part_queryHistoryController: queryHistoryController
  property alias queryHistoryOpen: queryHistoryController.opened
  property alias queryHistorySnapshot: queryHistoryController.snapshot
  property alias queryHistoryLoading: queryHistoryController.loading
  property alias queryHistoryError: queryHistoryController.error
  property alias activeQueryHistoryRequestId: queryHistoryController.activeRequestId
  QueryHistoryController {
    id: queryHistoryController
    nextRequestId: function() { return ++sessionState.owner.requestSequence }
    available: sessionState.daemonAvailable
    onRequest: message => sessionState.owner.part_protocol.send(message)
    onDismissed: sessionState.owner.focusRevision++
    onUpdated: sessionState.statusText = "Query history updated"
  }

  readonly property alias part_queryRecall: queryRecall
  QueryRecallController {
    id: queryRecall
    nextRequestId: function() { return ++sessionState.owner.requestSequence }
    available: sessionState.daemonAvailable
    onRequest: message => sessionState.owner.part_protocol.send(message)
    onRecalled: query => DeckSearchActions.applyRecalledQuery(sessionState.owner, query)
  }

  // Hidden items: the last hide feeds the undo strip; the settings list is
  // loaded on demand in bounded pages, never as part of the preferences snapshot.
  property var lastHidden: null
  readonly property alias part_hiddenItemsController: hiddenItemsController
  readonly property alias hiddenItems: hiddenItemsController.snapshot
  readonly property alias hiddenItemsQuery: hiddenItemsController.query
  readonly property alias hiddenItemsError: hiddenItemsController.error
  HiddenItemsController {
    id: hiddenItemsController
    nextRequestId: function() { return ++sessionState.owner.requestSequence }
    available: sessionState.daemonAvailable
    onRequest: message => sessionState.owner.part_protocol.send(message)
  }
  function setHidden(result) { return DeckHiddenItemActions.setHidden(sessionState.owner, result) }
  function undoHide() { return DeckHiddenItemActions.undoHide(sessionState.owner) }
  // A HEY change whose row has a hidden undo:<id> action offers Undo in the same
  // strip; only the latest change or hide can be undone.
  property var pendingUndo: null
  property var lastUndo: null
  function undoLast() { return DeckInvocationActions.undoLast(sessionState.owner) }
  function restoreHidden(customizationId, fromSettings) { return DeckHiddenItemActions.restoreHidden(sessionState.owner, customizationId, fromSettings) }
  function requestHiddenItems(query, offset) { return hiddenItemsController.refresh(query, offset) }
  // Durable display and AI choices, plus the inline answer presentation sessionState.
  property bool panelTransparencyEnabled: false
  property bool aiFeaturesEnabled: true
  readonly property string aiSaveDirectory: {
    const home = Quickshell.env("HOME")
    return home && home.length > 0 ? home + "/Documents" : ""
  }
  property string answerMode: ""
  property string answerQuery: ""
  property string answerBackendId: ""
  property string answerBackendLabel: ""
  readonly property bool answerOpen: sessionState.owner.answerMode.length > 0
  property bool aiSurfaceExpanded: false
  property var answerTestPane: null
  property int panelTransparencyPercent: 20
  readonly property color appliedPanelBackground: sessionState.owner.part_appTheme.panelBackground
  readonly property color appliedOverlayScrim: sessionState.owner.part_appTheme.overlayScrimColor
  property DesktopIntegration desktopIntegration: DesktopIntegration { }
  readonly property var compositor: desktopIntegration
  readonly property var desktop: Quickshell
  readonly property var integrationIds: Object.freeze({
    AgentLoginResult: "agent:login",
    DefaultAiWorkflow: "explain",
    Plugin: "local.commanddeck"
  })

  property var shell: null
  property var manifest: null
  property bool testMode: false
  property var testExtensionView: null
  property var testExtensionManagementView: null
  readonly property var extensionTestController: owner.part_extensionController
  property bool testSurfaceEnabled: false
  property bool opened: false
  property bool extensionManagementOpen: false
  property bool preferencesSnapshotLoading: false
  property bool preferencesReloadPending: false
  // Why the last Settings save was rejected; shown inside Settings until the next
  // successful save or reopening, since the footer is hidden behind it.
  property string preferencesSaveError: ""
  property bool actionPanelOpen: false
  property string statusText: ""
  property string announcementText: ""
  property bool daemonAvailable: true
  property bool reconciliationPending: false
  property bool reconciliationPreferencesComplete: false
  property bool reconciliationSearchComplete: false
  property string reconciliationWarning: ""
  property string openedScreenName: ""
  property var workspaceMruIds: []
  property var openWorkspaceMruIds: []
  property bool actionRefreshChanged: false
  property bool confirmationOpen: false
  property int herdrRefreshInterval: 2000
  readonly property bool herdrViewOpen: sessionState.owner.part_navigation.herdrWorkspaceId.length > 0
    || sessionState.owner.activeFilters.some(value => !value.excluded && value.id.indexOf("herdr") === 0)
  readonly property bool herdrLiveUpdatesEnabled: opened && herdrViewOpen && daemonAvailable
    && !reconciliationPending && !sessionState.owner.anyBlockingSurface && !sessionState.owner.part_extensionController.opened
    && !extensionManagementOpen && !sessionState.owner.part_invocationController.busy && !searchSession.searching
  property var filePreview: null
  property string previewSearchIdentity: ""
  property bool previewLoading: false
  property string previewError: ""
  property var launcherPreferences: null
  readonly property var desktopCapabilities: desktopIntegration.capabilities
  readonly property var workspacePreviewBackend: desktopIntegration.workspacePreviewBackend
  readonly property var workspaceSnapshotLifecycle: desktopIntegration.workspaceSnapshotLifecycle
  readonly property bool workspaceSnapshotsEnabled: sessionState.opened && !!sessionState.launcherPreferences
    && sessionState.launcherPreferences.workspacePreviewMode === "snapshots"
  Binding {
    target: sessionState.desktopIntegration
    property: "workspaceSnapshotsEnabled"
    value: sessionState.workspaceSnapshotsEnabled
  }
  property var preferencesSnapshot: null
  property var settingsSnapshot: null
  property var filterAliases: []
  property string filterAliasError: ""
  property var savedSearches: []
  property bool clipboardShortcutStateReady: false
  property string savedSearchError: ""
  property var savedSearchDeleteTarget: null
  property var sourceDescriptors: []
  property var fallbackQuicklinks: []
  property var runtimeStatus: null
  property string currentDirectoryContext: ""
  property string selectedProjectContext: ""
  readonly property string activeFilterId: sessionState.owner.part_navigation.activeFilterId
  property bool settingsOpen: false
  property bool helpOpen: false
  property string helpFocusTarget: ""
  property string filterFocusTarget: ""
  property bool indexInspectorOpen: false
  property int activeAiProvidersRequestId: 0
  property int activeAiModelsRequestId: 0
  property int activeAiSelectionRequestId: 0
  property int activeAiTemplatesRequestId: 0
  property var aiProviders: []
  property var aiModels: []
  property var aiSelection: null
  property bool aiSelectionBusy: false
  property var aiTemplates: []
  property var aiTemplateDeleteTarget: null
  property bool aiTemplateBusy: false
  property string aiSettingsError: ""
  property bool resetConfirmation: false
  property string dialogTitle: "AI response"
  property int modalIntentGeneration: 0
  property bool detailDialogRequested: false
  property string detailContent: ""
  property bool actionOutputPlainText: false
  property string browseDirectory: ""
  property int browseOffset: 0
  property bool applyingBrowseNavigation: false
  property var fileSelection: []
  property int activeFileSelectionRequestId: 0
  property bool aliasDialogOpen: false
  property string aliasDialogError: ""
  property var aliasTarget: null
  property var activeActionPanel: null
  property var testSurfaceWindow: null
  property var testSearchInput: null
  property var testSettingsButton: null
  property var testSettingsPanel: null
  property var testAliasDialog: null
  property var testRuntimeStatusSurface: null
  property var testIndexInspector: null
  property var testDetailDialog: null
  property var testQueryHistorySurface: null
  property var testAiPreflightSurface: null
  property var testAiStreamingSurface: null
  property var testAgentSurface: null
  property var testClipboardSurface: null
  property var testEmptyState: null
  property var testNoResults: null
  property var testFilterPalette: null
  property var testFooterActions: null
  property var testFirstRunCoach: null
  property bool agentOpen: false
  property var agentBackends: []
  property var agentModels: []
  property var agentGrants: []
  property var agentChallenge: null
  property var agentStatus: null
  property var agentPreferences: null
  property string agentSettingsError: ""
  // Feature A intent prewarm. Assistant mode is active whenever the assistant
  // surface is open or the search query resolves to an assistant filter
  // (ai:/codex:/ask:). Entering it fires one best-effort agent.prewarm so the
  // daemon spawns Codex ahead of Enter; the latch keeps it to once per entry.
  property bool assistantPrewarmArmed: false
  readonly property bool assistantModeActive: {
    if (!sessionState.owner.aiFeaturesEnabled) return false
    if (sessionState.owner.agentOpen) return true
    const descriptor = sessionState.owner.part_navigation.filterDescriptor(sessionState.owner.part_navigation.activeFilterId)
    return descriptor ? sessionState.owner.part_filterCatalog.isAssistantToken(descriptor.token) : false
  }
  readonly property bool detailDialogOpen: sessionState.owner.detailDialogRequested
  readonly property string detailDialogText: sessionState.owner.detailContent
  readonly property string aiState: sessionState.owner.part_aiController.state
  readonly property var aiPreflight: owner.part_aiController.preflight
  readonly property string aiOutput: sessionState.owner.part_aiController.output
  readonly property var aiTestController: owner.testMode ? owner.part_aiController : null
  readonly property var agentTestController: owner.testMode ? owner.part_agentController : null
  readonly property string agentState: sessionState.owner.part_agentController.state
  readonly property string primaryActionTitle: sessionState.owner.currentResult
    && sessionState.owner.currentResult.actions
    && sessionState.owner.currentResult.actions.length > 0
      ? sessionState.owner.currentResult.actions[0].title
      : ""
  readonly property var directActions: sessionState.owner.shortcutActions(sessionState.owner.currentResult)

}
