pragma ComponentBehavior: Bound
import QtQuick
import "UiConstants.js" as Ui
import QtQuick.Controls
import Quickshell
import Quickshell.Wayland

// Quickshell registers the platform-specific PanelWindow at runtime; its static
// interface is marked uncreatable. The component and event suites instantiate it.
// qmllint disable uncreatable-type
PanelWindow {
  required property var host

  id: launcherWindow
  readonly property alias part_palette: palettePanel
  readonly property alias part_searchBar: launcherContent.part_searchBar
  readonly property alias part_resultList: launcherContent.part_resultList
  readonly property alias part_emptyStateView: launcherContent.part_emptyStateView
  readonly property alias part_noResultsView: launcherContent.part_noResultsView
  readonly property alias part_answerPane: launcherContent.part_answerPane
  readonly property alias part_runtimeStatusSurface: launcherContent.part_runtimeStatusSurface
  readonly property alias part_footerActions: launcherContent.part_footerActions
  readonly property alias part_detailPane: launcherContent.part_detailPane
  readonly property alias part_hud: launcherHud
  readonly property alias part_appTheme: display.theme
  readonly property alias part_settingsTheme: display.theme
  readonly property alias displaySizing: display.sizing
  readonly property bool narrow: display.sizing.narrow
  readonly property string displayDescription: display.description

  WindowDisplay {
    id: display
    host: launcherWindow.host
    width: launcherWindow.width
    height: launcherWindow.height
    legacyHeight: {
      const monitor = (launcherWindow.host.compositor.monitors || [])
        .find(value => value.name === launcherWindow.modelData.name)
      return monitor ? monitor.height : launcherWindow.height
    }
  }

  required property var modelData
  screen: modelData

  // The window that shows the launcher. Closed surfaces are built only here,
  // so the other monitors' windows never pay for them.
  readonly property bool presenting: launcherWindow.host.opened
    && (launcherWindow.host.openedScreenName.length === 0
      || launcherWindow.host.openedScreenName === launcherWindow.modelData.name)
  onPresentingChanged: {
    if (presenting) host.presentingWindow = launcherWindow
    else if (host.presentingWindow === launcherWindow) host.presentingWindow = null
  }
  // Null until Settings first opens in this window.
  readonly property CommandDeckWindowSettings settingsPanel: settingsSurface.item as CommandDeckWindowSettings

  Component.onCompleted: {
    if (!launcherWindow.host.testMode) return

    launcherWindow.host.testSurfaceWindow = launcherWindow
    launcherWindow.publishTestParts()
  }

  // Specs reach the real surfaces through the host. A surface built on demand
  // is published when it loads, and only by the window the specs drive.
  function publishTestParts() {
    if (!launcherWindow.host.testMode || launcherWindow.host.testSurfaceWindow !== launcherWindow) return

    launcherWindow.host.testExtensionView = extensionView
    launcherWindow.host.testExtensionManagementView = managementSurface.item
    launcherWindow.host.testSearchInput = launcherWindow.part_searchBar.inputControl
    launcherWindow.host.testSettingsButton = launcherWindow.part_searchBar.settingsControl
    launcherWindow.host.testSettingsPanel = settingsSurface.item
    launcherWindow.host.testAliasDialog = dialogs.aliasDialogControl
    launcherWindow.host.testRuntimeStatusSurface = launcherWindow.part_runtimeStatusSurface
    launcherWindow.host.testIndexInspector = inspectorSurface.item
    launcherWindow.host.testDetailDialog = dialogs.detailDialogControl
    launcherWindow.host.testQueryHistorySurface = queryHistorySurface
    launcherWindow.host.testAiPreflightSurface = dialogs.aiPreflightSurfaceControl
    launcherWindow.host.testAiStreamingSurface = dialogs.aiStreamingSurfaceControl
    launcherWindow.host.testAgentSurface = dialogs.agentSurfaceControl
    launcherWindow.host.testClipboardSurface = clipboardSurface.item
    launcherWindow.host.answerTestPane = launcherWindow.part_answerPane
    launcherWindow.host.testEmptyState = launcherWindow.part_emptyStateView
    launcherWindow.host.testNoResults = launcherWindow.part_noResultsView
    launcherWindow.host.testFilterPalette = filterPalette
    launcherWindow.host.testFooterActions = launcherWindow.part_footerActions
    launcherWindow.host.testFirstRunCoach = firstRunCoach
  }

  Component.onDestruction: {
    if (launcherWindow.host.presentingWindow === launcherWindow) launcherWindow.host.presentingWindow = null
    if (launcherWindow.host.activeActionPanel === actionPanel) launcherWindow.host.activeActionPanel = null
    if (launcherWindow.host.testSurfaceWindow === launcherWindow) {
      launcherWindow.host.testSurfaceWindow = null
      launcherWindow.host.testSearchInput = null
      launcherWindow.host.testSettingsButton = null
      launcherWindow.host.testSettingsPanel = null
      launcherWindow.host.testExtensionManagementView = null
      launcherWindow.host.testClipboardSurface = null
      launcherWindow.host.testAliasDialog = null
      launcherWindow.host.testRuntimeStatusSurface = null
      launcherWindow.host.testIndexInspector = null
      launcherWindow.host.testDetailDialog = null
      launcherWindow.host.testQueryHistorySurface = null
      launcherWindow.host.testAiPreflightSurface = null
      launcherWindow.host.testAiStreamingSurface = null
      launcherWindow.host.testAgentSurface = null
      launcherWindow.host.answerTestPane = null
      launcherWindow.host.testEmptyState = null
      launcherWindow.host.testNoResults = null
      launcherWindow.host.testFilterPalette = null
      launcherWindow.host.testFooterActions = null
      launcherWindow.host.testFirstRunCoach = null
    }
  }

  // Lives on the surface rather than on root: `actionPanel` is declared in
  // this delegate, so its id is out of scope for a root-level function.
  function openActionPanel() {
    const target = launcherWindow.host.part_navigation.selectedResult()
    if (!target) {
      return
    }

    launcherWindow.host.actionPanelOpen = true
    launcherWindow.host.activeActionPanel = actionPanel
    actionPanel.openFor(target)
  }

  visible: launcherWindow.presenting && !launcherWindow.host.part_extensionController.externalActionPending
  color: "transparent"
  SavedSearchShortcuts {
    searches: launcherWindow.host.savedSearches
    active: launcherWindow.presenting && !launcherWindow.host.anyBlockingSurface
      && !launcherWindow.host.extensionManagementOpen && !launcherWindow.host.part_extensionController.opened
    onRequested: search => launcherWindow.host.applySavedSearch(search)
  }
  anchors {
    top: true
    bottom: true
    left: true
    right: true
  }
  // Cover bars with the scrim; the panel's safe insets leave room inside it.
  exclusionMode: ExclusionMode.Ignore
  WlrLayershell.layer: WlrLayer.Overlay
  // Hyprland reports openlayer>>commanddeck when the launcher maps, which
  // scripts/measure-toggle-latency.cs times; the picker and HUD use their own.
  WlrLayershell.namespace: "commanddeck"
  WlrLayershell.keyboardFocus: launcherWindow.host.opened && !launcherWindow.host.part_extensionController.externalActionPending
    ? WlrKeyboardFocus.Exclusive
    : WlrKeyboardFocus.None

  onVisibleChanged: {
    if (visible) {
      launcherWindow.part_searchBar.inputControl.text = launcherWindow.host.queryText
      Qt.callLater(function() {
        if (launcherWindow && launcherWindow.visible && !launcherWindow.host.part_extensionController.opened
            && !launcherWindow.host.part_clipboardController.opened && launcherWindow.part_searchBar)
          launcherWindow.part_searchBar.inputControl.forceActiveFocus()
      })
    }
  }

  Connections {
    target: launcherWindow.host

    function onQueryInputRequested(value) {
      launcherWindow.part_searchBar.setInputText(value)
    }

    function onSelectedIndexChanged() {
      if (launcherWindow.visible) {
        launcherWindow.part_resultList.positionViewAtIndex(launcherWindow.host.selectedIndex, ListView.Contain)
      }
    }
  }

  // C8 - a strong, tunable scrim (appTheme.overlayScrimColor) so background
  // windows do not bleed through and the launcher reads as clearly modal.
  // When panel transparency is on, the same amount scales this scrim so the
  // wallpaper can show through; clicking it still dismisses the overlay.
  Rectangle {
    objectName: "launcherScrim"
    anchors.fill: parent
    color: launcherWindow.part_appTheme.overlayScrimColor

    MouseArea {
      anchors.fill: parent
      onClicked: launcherWindow.host.dismiss()
    }
  }

  SizingViewport {
    id: palettePanel

    ClipboardOpenShortcut {
      id: clipboardOpenBinding
      preferences: launcherWindow.host.launcherPreferences
      savedSearches: launcherWindow.host.savedSearches || []
      stateReady: launcherWindow.host.clipboardShortcutStateReady
      preferenceUpdateInFlight: launcherWindow.host.preferenceUpdateInFlight
      active: launcherWindow.host.opened && launcherWindow.host.daemonAvailable && !launcherWindow.host.reconciliationPending
        && !launcherWindow.host.anyBlockingSurface && !launcherWindow.host.part_extensionController.opened
        && !launcherWindow.host.extensionManagementOpen && !launcherWindow.host.part_invocationController.herdrEditPending
        && launcherWindow.host.part_invocationController.requestId === 0
      onRequested: launcherWindow.host.part_clipboardController.open("")
    }
    readonly property int topInset: display.sizing.topInset
    readonly property int bottomInset: display.sizing.bottomInset
    width: display.sizing.panelWidth
    height: display.sizing.panelHeight
    requestedWidth: display.sizing.contentWidth
    requestedHeight: display.sizing.contentHeight
    anchors.horizontalCenter: parent.horizontalCenter
    anchors.top: parent.top
    anchors.topMargin: launcherWindow.host.settingsOpen
      ? Math.round((launcherWindow.height - height) / 2)
      : topInset
    radius: launcherWindow.host.settingsOpen ? launcherWindow.part_settingsTheme.radius : launcherWindow.part_appTheme.radius
    color: launcherWindow.part_appTheme.panelBackground
    border.width: 1
    border.color: launcherWindow.part_appTheme.border
    opacity: launcherWindow.host.opened ? 1 : 0
    transform: Translate {
      y: launcherWindow.host.opened ? 0 : -6
      Behavior on y {
        NumberAnimation { duration: launcherWindow.part_appTheme.durationNormal }
      }
    }
    Behavior on opacity {
      NumberAnimation { duration: launcherWindow.part_appTheme.durationNormal }
    }

    MouseArea {
      anchors.fill: parent
      acceptedButtons: Qt.NoButton
    }

    ExtensionViewHost {
      id: extensionView
      anchors.fill: parent
      z: 10
      theme: launcherWindow.part_appTheme
      opened: (launcherWindow.visible || launcherWindow.host.part_extensionController.externalActionPending) && launcherWindow.host.part_extensionController.opened
      snapshot: launcherWindow.host.part_extensionController.snapshot
      busy: launcherWindow.host.part_extensionController.busy || launcherWindow.host.part_extensionController.failed
      completed: launcherWindow.host.part_extensionController.completed
      noView: launcherWindow.host.part_extensionController.noView
      externalError: launcherWindow.host.part_extensionController.error
      hostTitle: launcherWindow.host.part_extensionController.packageTitle
      debugLease: launcherWindow.host.part_extensionController.debugLease
      confirmation: launcherWindow.host.part_extensionController.confirmation
      onRemoteDecided: confirmed => launcherWindow.host.part_extensionController.decideRemote(confirmed)
      recoveryVisible: launcherWindow.host.part_extensionController.failed
      disableAvailable: launcherWindow.host.part_extensionController.packageKey.length > 0 && !launcherWindow.host.part_extensionController.busy
      returnFocus: launcherWindow.part_searchBar.inputControl
      onPresented: revision => launcherWindow.host.part_extensionController.present(revision)
      onActionRequested: (revision, itemId, handle) => launcherWindow.host.part_extensionController.activate(revision, itemId, handle)
      onFormSubmitted: (revision, handle, formRevision, values) => launcherWindow.host.part_extensionController.submit(revision, handle, formRevision, values)
      onBackRequested: launcherWindow.host.part_extensionController.close()
      onRestartRequested: launcherWindow.host.part_extensionController.restart()
      onDisableRequested: launcherWindow.host.part_extensionController.disable()
    }

    CommandDeckContent {
      id: launcherContent
      host: launcherWindow
    }

    OnDemandSurface {
      id: clipboardSurface
      anchors.fill: parent
      z: 11
      wanted: launcherWindow.presenting && launcherWindow.host.part_clipboardController.opened
      onItemChanged: launcherWindow.publishTestParts()
      sourceComponent: ClipboardHistorySurface {
        visible: clipboardSurface.presented && launcherWindow.host.part_clipboardController.opened
        theme: launcherWindow.part_appTheme
        controller: launcherWindow.host.part_clipboardController
        returnFocus: launcherWindow.part_searchBar.inputControl
      }
    }

    ActionPanel {
      id: actionPanel
      theme: launcherWindow.part_appTheme
      shortcuts: launcherWindow.host.shortcutActions(actionPanel.result)
      width: Math.min(Math.round(360 * launcherWindow.part_appTheme.scale), palettePanel.width * 0.45)
      // Size to the action list's natural height, capped to fit inside the
      // palette (the panel clips and scrolls beyond that), so a result with
      // many actions never spills over the detail pane at any display scale.
      height: Math.min(
        palettePanel.height - Math.round(28 * launcherWindow.part_appTheme.scale),
        actionPanel.implicitHeight)
      anchors.right: parent.right
      anchors.top: parent.top
      anchors.margins: 14
      visible: launcherWindow.host.actionPanelOpen
      opacity: visible ? 1 : 0
      Behavior on opacity {
        NumberAnimation { duration: launcherWindow.part_appTheme.durationFast }
      }
      z: 10

      onActionChosen: action => {
        const target = actionPanel.result
        if (!target) return
        if (action.kind === Ui.ResultAction.FavoriteToggle) {
          launcherWindow.host.actionPanelOpen = false
          launcherWindow.part_searchBar.inputControl.forceActiveFocus()
          launcherWindow.host.setFavorite(target, !target.isFavorite)
        } else if (action.kind === Ui.ResultAction.HideResult) {
          launcherWindow.host.actionPanelOpen = false
          launcherWindow.part_searchBar.inputControl.forceActiveFocus()
          launcherWindow.host.setHidden(target)
        } else if (action.kind === Ui.ResultAction.AliasEdit) {
          launcherWindow.host.aliasTarget = target
          launcherWindow.host.aliasDialogError = ""
          launcherWindow.host.aliasDialogOpen = true
        } else if (action.kind === Ui.ResultAction.RankingExplanation) {
          launcherWindow.host.showRankingExplanation(target)
        } else {
          launcherWindow.host.actionPanelOpen = false
          launcherWindow.part_searchBar.inputControl.forceActiveFocus()
          launcherWindow.host.invokeAction(target, action, false)
        }
      }

      onDismissed: {
        launcherWindow.host.actionPanelOpen = false
        launcherWindow.part_searchBar.inputControl.forceActiveFocus()
      }
    }

    HelpSheet {
      id: helpSheet
      anchors.fill: parent
      opened: launcherWindow.host.helpOpen
      returnFocusTarget: launcherWindow.host.helpFocusTarget === Ui.FocusReturnControl.FooterHelp
        ? launcherWindow.part_footerActions.helpControl : null
      focusReturnAllowed: launcherWindow.visible && !launcherWindow.host.anyBlockingSurface
      opacity: opened ? 1 : 0
      Behavior on opacity {
        NumberAnimation { duration: launcherWindow.part_appTheme.durationNormal }
      }
      z: 14
      theme: launcherWindow.part_appTheme
      filterDescriptors: launcherWindow.host.filterDescriptors
      clipboardShortcut: clipboardOpenBinding.availableChord
      onDismissed: launcherWindow.host.closeHelp()
    }

    FilterPalette {
      id: filterPalette
      objectName: "filterPalette"
      anchors.fill: parent
      opened: launcherWindow.host.filterPaletteOpen
      returnFocusTarget: launcherWindow.host.filterFocusTarget === Ui.FocusReturnControl.FooterPrefixes
        ? launcherWindow.part_footerActions.prefixesControl : null
      focusReturnAllowed: launcherWindow.visible && !launcherWindow.host.anyBlockingSurface
      opacity: opened ? 1 : 0
      Behavior on opacity {
        NumberAnimation { duration: launcherWindow.part_appTheme.durationNormal }
      }
      z: 19
      theme: launcherWindow.part_appTheme
      filterDescriptors: launcherWindow.host.filterDescriptors
      onTokenInserted: token => {
        launcherWindow.host.closeFilterPalette(false)
        launcherWindow.host.insertFilterToken(token)
      }
      onDismissed: launcherWindow.host.closeFilterPalette()
    }

    OnDemandSurface {
      id: inspectorSurface
      anchors.fill: parent
      z: 15
      wanted: launcherWindow.presenting && launcherWindow.host.indexInspectorOpen
      onItemChanged: launcherWindow.publishTestParts()
      sourceComponent: IndexInspector {
        theme: launcherWindow.part_appTheme
        opened: inspectorSurface.presented && launcherWindow.host.indexInspectorOpen
        inspection: launcherWindow.host.runtimeStatus ? launcherWindow.host.runtimeStatus.inspection : null
        healthDetails: launcherWindow.part_runtimeStatusSurface.details
        healthSummary: launcherWindow.part_runtimeStatusSurface.issueCount > 0 ? launcherWindow.part_runtimeStatusSurface.summary : ""
        onDismissed: {
          launcherWindow.host.indexInspectorOpen = false
          launcherWindow.host.focusRevision++
        }
      }
    }

    OnDemandSurface {
      id: settingsSurface
      anchors.fill: parent
      z: 12
      wanted: launcherWindow.presenting && launcherWindow.host.settingsOpen
      onItemChanged: launcherWindow.publishTestParts()
      sourceComponent: CommandDeckWindowSettings {
        host: launcherWindow
        presented: settingsSurface.presented
      }
    }

    OnDemandSurface {
      id: managementSurface
      anchors.fill: parent
      z: 17
      wanted: launcherWindow.presenting && launcherWindow.host.extensionManagementOpen
      onItemChanged: launcherWindow.publishTestParts()
      sourceComponent: ExtensionManagementView {
        theme: launcherWindow.part_settingsTheme
        controller: launcherWindow.host.part_extensionManagementController
        opened: managementSurface.presented && launcherWindow.host.opened && launcherWindow.host.extensionManagementOpen
        returnFocus: launcherWindow.settingsPanel ? launcherWindow.settingsPanel.extensionsControl : null
        onDismissed: launcherWindow.host.extensionManagementOpen = false
        onBrowserLoginRequested: launcherWindow.host.dismiss()
        onScopedSearchRequested: (filterId, label) => { launcherWindow.host.extensionManagementOpen = false; launcherWindow.host.open({ filterId: filterId, filterLabel: label }); launcherWindow.host.focusRevision++ }
      }
    }

    QueryHistorySurface {
      id: queryHistorySurface
      anchors.fill: parent
      z: 18
      theme: launcherWindow.part_appTheme
      opened: launcherWindow.host.queryHistoryOpen
      loading: launcherWindow.host.queryHistoryLoading
      snapshot: launcherWindow.host.queryHistorySnapshot
      errorText: launcherWindow.host.queryHistoryError
      onDismissed: launcherWindow.host.closeQueryHistory()
      onReloadRequested: launcherWindow.host.requestQueryHistory()
      onDeleteRequested: historyId => launcherWindow.host.deleteQueryHistory(historyId)
      onResetRequested: launcherWindow.host.resetQueryHistory()
    }

    CommandDeckDialogs {
      id: dialogs
      surface: launcherWindow
      container: palettePanel
      onAgentSurfaceControlChanged: launcherWindow.publishTestParts()
      onBackendSettingsRequested: {
        launcherWindow.host.openSettings()
        const settings = launcherWindow.settingsPanel
        settings.showPage(Ui.SettingsPage.Assistant)
        Qt.callLater(function() { settings.agentBackendControl.forceActiveFocus() })
      }
    }

    FirstRunCoach {
      id: firstRunCoach
      objectName: "firstRunCoach"
      anchors.fill: parent
      z: 40
      theme: launcherWindow.part_appTheme
      opened: launcherWindow.host.opened
      seen: launcherWindow.host.coachSeen
      suppressed: !launcherWindow.host.queryIsEmpty || launcherWindow.host.anyBlockingSurface
      onDismissed: {
        launcherWindow.host.coachSeen = true
        launcherWindow.host.focusRevision++
      }
    }
  }

  // A copy that closes the launcher leaves its message on this screen.
  LauncherHud {
    id: launcherHud
    host: launcherWindow.host
    screen: launcherWindow.modelData
    theme: launcherWindow.part_appTheme
    onLauncherScreen: launcherWindow.host.openedScreenName.length === 0
      || launcherWindow.host.openedScreenName === launcherWindow.modelData.name
  }
}
