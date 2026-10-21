import QtQuick
import QtTest
import Quickshell
import "." as CommandDeck
import "Protocol.js" as Protocol

ShellRoot {
  id: root
  readonly property alias part_host: host
  readonly property alias slice2Filters: discovery.slice2Filters
  QuicklinkAssertions { id: quicklinkAssertions; host: root }
  LauncherProtocolAssertions { id: stage0; host: root }
  PreferenceRecoveryAssertions { id: stage1; host: root }
  AssistantRoutingAssertions { id: stage2; host: root }
  PreviewAndPreferenceAssertions { id: stage3; host: root }
  DisplayAssertions { id: stage4; host: root }
  DeferredLayoutAssertions { id: stage5; host: root }
  HiddenItemAssertions { id: stage6; host: root }
  SearchDataPathAssertions { id: stage7; host: root }
  ResultListAssertions { id: resultListAssertions; host: root }
  BridgeBackoffAssertions { id: bridgeBackoff; host: root }
  DisplayStoreAssertions { id: displayStoreAssertions; host: root }
  readonly property alias part_accessibleResult: accessibleResult
  readonly property alias part_actions: actions
  readonly property alias part_aiMarkdown: aiMarkdown
  readonly property alias part_aliasDialog: aliasDialog
  readonly property alias part_announcements: announcements
  readonly property alias part_answerPaneSpec: answerPaneSpec
  readonly property alias part_confirmation: confirmation
  readonly property alias part_filePreview: filePreview
  readonly property alias part_filterChip: filterChip
  readonly property alias part_helpSheet: helpSheet
  readonly property alias part_indexInspector: indexInspector
  readonly property alias part_motionTheme: motionTheme
  readonly property alias part_mutationQueue: mutationQueue
  readonly property alias part_pane: pane
  readonly property alias part_preferenceQueue: preferenceQueue
  readonly property alias part_preview: preview
  readonly property alias part_unsupportedPreview: unsupportedPreview
  readonly property alias part_queryHistory: queryHistory
  readonly property alias part_reconnectClient: reconnectClient
  readonly property alias part_settings: settings
  IntegratedSurfaceAssertions { id: verifyIntegratedSurfacePart; host: root }
  WorkspacePreviewAssertions { id: workspacePreviewAssertions; host: root }
  DiscoverabilitySlice2Assertions { id: verifyDiscoverabilitySlice2Part; host: root }
  SearchFieldHintsAssertions { id: verifySearchFieldHintsPart; host: root }
  AgentSettingsAssertions { id: verifyAgentSettingsPart; host: root }
  AiProviderSettingsAssertions { id: verifyAiProviderSettingsPart; host: root }
  AgentSurfaceAssertions { id: verifyAgentSurfacePart; host: root }
  AgentControllerAssertions { id: verifyAgentControllerPart; host: root }
  HerdrLiveUpdatesAssertions { id: verifyHerdrLiveUpdatesPart; host: root }
  OmarchyMenuBrowseAssertions { id: verifyOmarchyMenuBrowsePart; host: root }
  HeyMailAssertions { id: verifyHeyMailPart; host: root }
  readonly property alias part_agentSettingsPanel: agentSettingsPanel
  readonly property alias part_agentSpecController: agentSpecController
  readonly property alias part_agentSpecSurface: agentSpecSurface
  readonly property alias part_aiFeaturesStoreSpec: discovery.aiFeaturesStoreSpecControl
  readonly property alias part_aiSettingsSpec: aiSettingsSpec
  readonly property alias part_catalogSpec: discovery.catalogSpecControl
  readonly property alias part_coachSpec: discovery.coachSpecControl
  readonly property alias part_coachStoreSpec: discovery.coachStoreSpecControl
  readonly property alias part_displayStoreSpec: discovery.displayStoreSpecControl
  readonly property alias part_emptyStateSpec: discovery.emptyStateSpecControl
  readonly property alias part_filterPaletteSpec: discovery.filterPaletteSpecControl
  readonly property alias part_footerHintBar: footerHintBar
  readonly property alias part_integratedDeck: integratedDeck
  readonly property alias part_hostEvents: hostEvents
  readonly property alias part_staleActivationJourney: staleActivationJourney
  readonly property alias part_kindCueRow: kindCueRow
  readonly property alias part_liveCheckTimer: liveCheckTimer
  readonly property alias part_narrowSettingsRow: narrowSettingsRow
  readonly property alias part_noResultsSpec: discovery.noResultsSpecControl
  readonly property alias part_reduceMotionSearchField: reduceMotionSearchField
  readonly property alias part_resultIcon: resultIcon
  readonly property alias part_searchFieldNav: searchFieldNav
  readonly property alias part_searchFieldSpec: searchFieldSpec
  readonly property alias part_theme: theme
  readonly property alias part_transparencyStoreSpec: discovery.transparencyStoreSpecControl
  readonly property alias part_colorThemeStoreSpec: discovery.colorThemeStoreSpecControl
  readonly property alias part_wideSettingsRow: wideSettingsRow

  property int failures: 0
  property int assertions: 0
  // Specs that wait on real processes or timers; the harness exits after them.
  property int pendingAsyncSpecs: 0
  property bool productionJourneyFinished: false

  property var liveSteps: []
  property int liveStep: 0
  // One step per tick, restarted only after the step returns: real key events
  // process pending events, so a repeating timer could start the next step
  // inside the current one. Asynchronous specs keep it ticking until they end.
  Timer {
    id: liveCheckTimer
    interval: 60
    repeat: false
    onTriggered: {
      if (root.liveStep < root.liveSteps.length) {
        const step = root.liveStep++
        try {
          root.liveSteps[step]()
        } catch (error) {
          root.failures++
          console.error("FAIL live step " + step + " threw: " + error)
        }
      }
      if (root.liveStep < root.liveSteps.length || root.pendingAsyncSpecs > 0) {
        start()
        return
      }
      stop()
      if (!root.productionJourneyFinished) {
        productionHostJourney.run(function() {
          root.productionJourneyFinished = true
          root.liveSteps = root.onDemandSurfaceSteps()
          root.liveStep = 0
          liveCheckTimer.start()
        })
        return
      }
      console.log("Executed " + root.assertions + " QML assertions.")
      console.log(root.failures === 0 ? "QML specs passed." : "QML specs failed.")
      Qt.exit(root.failures === 0 ? 0 : 1)
    }
  }

  function verifyHerdrLiveUpdates() { verifyHerdrLiveUpdatesPart.run() }
  function verifyOmarchyMenuBrowse() { verifyOmarchyMenuBrowsePart.run() }
  function verifyHeyMail() { verifyHeyMailPart.run() }
  function openHeyUndoKeys() { verifyHeyMailPart.openForUndoKeys() }
  function verifyHeyUndoKeys() { verifyHeyMailPart.verifyUndoKeys() }
  function verifyHeyUndoClick() { verifyHeyMailPart.verifyUndoClick() }
  ComponentThemeFixture {
    id: theme
    host: root
  }

  Item {
    id: host

    width: 840
    height: 360
    visible: true
  }

  CommandDeck.DetailPane {
    id: pane

    parent: host
    width: 420
    height: 360
    theme: theme
    result: ({
      title: "Empty",
      subtitle: "No fields",
      details: []
    })
  }

  CommandDeck.WindowPreview {
    id: preview

    parent: host
    width: 420
    theme: theme
    integration: CommandDeck.DesktopIntegration {
      capabilities: CommandDeck.DesktopCapabilities {
        windows: true
        windowPreviews: true
      }
    }
    opened: false
    selected: false
    toplevels: []
    result: ({
      kind: "window",
      metadata: { address: "0xAbC123" }
    })
  }

  CommandDeck.WindowPreview {
    id: unsupportedPreview

    parent: host
    width: 420
    theme: theme
    integration: CommandDeck.DesktopIntegration { }
  }

  CommandDeck.ResultIcon {
    id: resultIcon

    parent: host
    theme: theme
    result: ({
      kind: "window",
      icon: "resolved-window-icon",
      metadata: { "class": "stale-class-guess" }
    })
  }

  CommandDeck.ResultRow {
    id: accessibleResult
    parent: host
    visible: false
    theme: theme
    selected: false
    ordinal: 3
    result: ({
      title: "Brave",
      subtitle: "Browser",
      isFavorite: true,
      section: { id: "applications", title: "Applications", order: 2 },
      matchedRanges: []
    })
  }

  // B7/B5 - a row used to prove the per-kind cue and the inline action
  // affordance. `result` and the flags are reassigned during the specs.
  CommandDeck.ResultRow {
    id: kindCueRow
    width: 460
    parent: host
    visible: false
    theme: theme
    selected: false
    ordinal: 0
    actionCount: 0
    result: ({
      title: "systemctl restart",
      subtitle: "Service",
      sourceId: Protocol.SourceIds.SystemdUnits,
      kind: "service",
      actions: [{ id: Protocol.SystemdUnitActionIds.Restart, title: "Restart" }]
    })
  }

  CommandDeck.FooterHintBar {
    id: footerHintBar
    parent: host
    visible: false
    theme: theme
    primaryActionTitle: "Open"
    actionCount: 1
  }

  CommandDeck.FilePreview {
    id: filePreview

    parent: host
    width: 420
    theme: theme
    preview: ({
      text: "# literal markdown\n<script>literal</script>",
      language: "markdown",
      truncated: true
    })
  }

  CommandDeck.Theme {
    id: motionTheme
    reduceMotion: true
  }

  CommandDeck.ActionPanel {
    id: actions
    property string chosenActionId: ""
    parent: host
    width: 360
    height: 300
    visible: false
    theme: theme
    onActionChosen: action => chosenActionId = action.id
  }

  CommandDeck.AliasDialog {
    id: aliasDialog
    parent: host
    anchors.fill: host
    opened: false
    theme: theme
  }

  ComponentIndexFixture {
    id: indexInspector
    host: root
  }

  CommandDeck.AiMarkdownView {
    id: aiMarkdown
    parent: host
    visible: false
    theme: theme
  }

  // Two rows with identical content but different widths, to prove SettingsRow
  // reflows: the narrow one must stack its control beneath the label, the wide
  // one must keep the control beside it. Mirrors the Model row (a picker and a
  // Refresh button in one control slot).
  CommandDeck.SettingsRow {
    id: narrowSettingsRow
    parent: host
    width: 300
    theme: theme
    label: "Reasoning effort"
    help: "Low answers fastest."

    CommandDeck.SettingsChoice {
      id: narrowSettingsRowChoice
      theme: theme
      options: [{ id: "low", title: "Low" }, { id: "high", title: "High" }]
    }

    CommandDeck.DeckButton {
      id: narrowSettingsRowButton
      theme: theme
      text: "Refresh"
    }
  }

  CommandDeck.SettingsRow {
    id: wideSettingsRow
    parent: host
    width: 1000
    theme: theme
    label: "Reasoning effort"
    help: "Low answers fastest."

    CommandDeck.SettingsChoice {
      theme: theme
      options: [{ id: "low", title: "Low" }, { id: "high", title: "High" }]
    }

    CommandDeck.DeckButton {
      theme: theme
      text: "Refresh"
    }
  }

  ComponentSettingsFixture {
    id: settings
    host: root
  }

  CommandDeck.QueryHistorySurface {
    id: queryHistory
    parent: host
    anchors.fill: host
    opened: false
    theme: theme
    snapshot: ({
      enabled: true,
      retentionDays: 90,
      maximumEntries: 1000,
      totalEntries: 1,
      entries: [{ id: 7, query: "report", stableId: "file:report", actionId: "open" }]
    })
  }

  CommandDeck.PreferenceUpdateQueue {
    id: preferenceQueue
    property var dispatched: []
    property int rollbacks: 0
    onDispatchRequested: preferences => dispatched = dispatched.concat([
      JSON.parse(JSON.stringify(preferences))
    ])
    onRollbackRequested: (preferences, errorText) => rollbacks++
  }

  CommandDeck.PersonalizationMutationQueue {
    id: mutationQueue
    property var dispatched: []
    property int aborts: 0
    property var abortedEntries: []
    onDispatchRequested: (request, context) => dispatched = dispatched.concat([{
      request: request,
      context: context
    }])
    onAborted: (entries, errorText) => {
      aborts++
      abortedEntries = entries
    }
  }

  CommandDeck.AnnouncementCoalescer {
    id: announcements
    interval: 1
  }

  CommandDeck.ProtocolClient {
    id: reconnectClient
    enabled: false
    property int reconnects: 0
    property int forwardedMessages: 0
    onReconnected: reconnects++
    onMessage: value => forwardedMessages++
  }

  CommandDeck.FilterChip {
    id: filterChip
    parent: host
    visible: false
    theme: theme
    label: "Files"
    property int removals: 0
    onRemoveRequested: removals++
  }

  ListModel { id: searchFieldRows }

  CommandDeck.SearchNavigationController {
    id: searchFieldNav
    rows: searchFieldRows
    filterDescriptors: [
      { id: "services", label: "Services", token: "svc:", aliases: ["service:"] },
      { id: "ai", label: "Assistant", token: "ai:", aliases: [] },
      { id: "files", label: "Files", token: "file:", aliases: ["text:"] },
      { id: "twod", label: "2D", token: "2d:", aliases: [] }
    ]
  }

  CommandDeck.CommandDeckSearchField {
    id: searchFieldSpec
    parent: host
    visible: false
    theme: theme
    controller: searchFieldNav
    filterDescriptors: searchFieldNav.filterDescriptors
    surfaceVisible: false
  }

  CommandDeck.CommandDeckSearchField {
    id: reduceMotionSearchField
    parent: host
    visible: false
    theme: motionTheme
    controller: searchFieldNav
    filterDescriptors: searchFieldNav.filterDescriptors
    surfaceVisible: false
  }

  ComponentDiscoveryFixture {
    id: discovery
    host: root
  }

  CommandDeck.CommandDeck {
    id: integratedDeck
    visible: false
    testMode: true
    desktopIntegration: CommandDeck.DesktopIntegration {
      screens: Quickshell.screens.length > 0 ? [Quickshell.screens[0]] : []
      focusedMonitor: screens.length > 0 ? screens[0] : null
      capabilities: CommandDeck.DesktopCapabilities {
        windows: true
        windowPreviews: true
        workspaces: true
        workspaceLayouts: true
      }
      workspacePreviewBackend: QtObject {
        property var workspaces: []
        signal invalidated()
        function refresh() { }
      }
    }
    property var sentRequests: []
    property var routedInputs: []
    onProtocolRequestSent: request => sentRequests = sentRequests.concat([
      JSON.parse(JSON.stringify(request))
    ])
    onQueryInputRequested: value => routedInputs = routedInputs.concat([value])
  }

  TestCase {
    id: hostEvents
    parent: integratedDeck.testSearchInput
    when: false
  }

  ProductionHostJourneyAssertions { id: productionHostJourney; host: root }
  StaleActivationJourneyAssertions { id: staleActivationJourney; host: root }
  OnDemandSurfaceAssertions { id: onDemandSurfaces; host: root }

  function onDemandSurfaceSteps() { return onDemandSurfaces.steps() }

  CommandDeck.HelpSheet {
    id: helpSheet
    parent: host
    width: 900
    height: 620
    visible: false
    theme: theme
    opened: true
    filterDescriptors: [
      { id: "applications", label: "Applications", token: "app:", aliases: [] },
      { id: "files", label: "Files", token: "file:", aliases: ["text:", "files:"] },
      { id: "workspaces", label: "Workspaces", token: "workspace:", aliases: ["ws:", "workspaces:"] }
    ]
  }

  CommandDeck.ConfirmationSurface {
    id: confirmation
    parent: host
    width: 500
    height: 300
    visible: false
    theme: theme
    prompt: "Reset everything?"
    confirmLabel: "Reset"
  }

  CommandDeck.AiController {
    id: answerAiSpecController
  }

  // Standalone inline answer pane bound to the integrated deck's assistant
  // controller: the deck's own pane only exists during the surface pass.
  CommandDeck.AiAnswerPane {
    id: answerPaneSpec
    parent: host
    width: 420
    height: 320
    visible: false
    theme: theme
    assistantController: integratedDeck.agentTestController
    directController: answerAiSpecController
    mode: integratedDeck.answerMode
    query: integratedDeck.answerQuery
    backendLabel: integratedDeck.answerBackendLabel
  }

  CommandDeck.AgentController {
    id: agentSpecController
    property var sentRequests: []
    onRequest: message => sentRequests = sentRequests.concat([
      JSON.parse(JSON.stringify(message))
    ])
  }

  ComponentAgentSurfaceFixture {
    id: agentSpecSurface
    parent: host
    theme: theme
    controller: agentSpecController
  }

  ComponentAgentSettingsFixture {
    id: agentSettingsPanel
    host: root
  }

  function verifyAgentController() { verifyAgentControllerPart.run() }

  function verifyAgentSurface() { verifyAgentSurfacePart.run() }

  CommandDeck.AiProviderSettingsController {
    id: aiSettingsSpec
    property var sent: []
    onRequest: message => sent = sent.concat([message])
  }

  function verifyAiProviderSettings() { verifyAiProviderSettingsPart.run() }

  function verifyAgentSettings() { verifyAgentSettingsPart.run() }

  function verifySearchFieldHints() { verifySearchFieldHintsPart.run() }

  function verifyDiscoverabilitySlice2() { verifyDiscoverabilitySlice2Part.run() }

  function expect(condition, message) {
    assertions++
    if (condition) {
      console.log("PASS " + message)
      return
    }

    failures++
    console.error("FAIL " + message)
  }

  function verifyIntegratedSurface(done) { verifyIntegratedSurfacePart.run(done) }
  function verifyWorkspacePreview() { workspacePreviewAssertions.run() }

  Component.onCompleted: Qt.callLater(function() {
    const context = {}
    stage0.run(context)
    stage1.run(context)
    stage2.run(context)
    stage3.run(context)
    stage4.run(context)
    stage5.run(context)
    stage6.run(context)
    stage7.run(context)
    quicklinkAssertions.run()
    resultListAssertions.run()
    bridgeBackoff.start()
    displayStoreAssertions.start()
  })
}
