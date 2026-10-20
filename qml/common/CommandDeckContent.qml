pragma ComponentBehavior: Bound
import "WorkspacePreviewPolicy.js" as WorkspacePreviewPolicy
import QtQuick
import "UiConstants.js" as Ui
import "Protocol.js" as Protocol
import QtQuick.Layouts

ColumnLayout {
  required property var host
  readonly property alias part_searchBar: searchBar
  readonly property alias part_resultList: resultList
  readonly property alias part_emptyStateView: emptyStateView
  readonly property alias part_noResultsView: noResultsView
  readonly property alias part_answerPane: answerPane
  readonly property alias part_runtimeStatusSurface: runtimeStatusSurface
  readonly property alias part_footerActions: footerActions
  readonly property alias part_detailPane: detailPane
  readonly property alias part_statusLine: statusLine
  readonly property alias part_pointerGate: resultPointerGate

  id: launcherContent
  // The Herdr or Omarchy menu bar under the search field, first in Tab order
  // after it; null when neither view is open.
  readonly property var scopeBar: herdrNavigation.visible ? herdrNavigation
    : omarchyNavigation.visible ? omarchyNavigation : null

  PointerMoveGate { id: resultPointerGate }
  Connections {
    target: launcherContent.host
    function onVisibleChanged() { resultPointerGate.reset() }
  }
  visible: !launcherContent.host.host.part_extensionController.opened && !launcherContent.host.host.part_clipboardController.opened
  anchors.fill: parent
  anchors.margins: launcherContent.host.host.part_appTheme.panelPadding
  spacing: launcherContent.host.host.part_appTheme.gap

  CommandDeckSearchField {
    id: searchBar
    Layout.fillWidth: true
    theme: launcherContent.host.host.part_appTheme
    controller: launcherContent.host.host.part_navigation
    focusRequestsEnabled: !launcherContent.host.host.part_extensionController.opened && !launcherContent.host.host.anyBlockingSurface
    filterDescriptors: launcherContent.host.host.filterDescriptors
    surfaceVisible: launcherContent.host.visible
    loading: launcherContent.host.host.part_searchSession.pending
    additionalTabStop: launcherContent.scopeBar ? launcherContent.scopeBar.firstControl : fileSelectionBar.visible ? fileSelectionBar.firstControl : runtimeStatusSurface.visible ? runtimeStatusSurface : footerActions.firstControl
    precedingTabStop: detailPane.lastPreviewControl || footerActions.lastControl
      || (runtimeStatusSurface.visible ? runtimeStatusSurface : null)
  onSearchEdited: { if (!launcherContent.host.host.applyingBrowseNavigation) launcherContent.host.host.browseOffset = 0; launcherContent.host.host.part_searchSession.schedule() }
  directActions: launcherContent.host.host.directActions
  onShortcutRequested: actionId => launcherContent.host.host.invokeShortcut(actionId)
  onActionsRequested: launcherContent.host.openActionPanel()
  onHistoryRecallRequested: restart => launcherContent.host.host.part_queryRecall.step(restart)
  onZoomRequested: direction => launcherContent.host.host.stepDisplaySize(direction)
  onFavoriteMoveRequested: direction => launcherContent.host.host.moveFavorite(direction)
  pillTarget: launcherContent.host.host.argumentPillTarget
  onPillsSubmitted: values => launcherContent.host.host.submitArgumentPills(values)
  onPillValueMissing: name => launcherContent.host.host.statusText = "Enter a value for " + name
  onPreviewToggleRequested: launcherContent.host.host.togglePreviewPane()
  undoAvailable: launcherContent.host.host.lastHidden !== null || launcherContent.host.host.lastUndo !== null
  onUndoRequested: launcherContent.host.host.undoLast()
  }

  HerdrNavigationBar {
    id: herdrNavigation
    Layout.fillWidth: true
    visible: launcherContent.host.host.herdrViewOpen
    theme: launcherContent.host.host.part_appTheme
    controller: launcherContent.host.host.part_navigation
    searchInput: searchBar.inputControl
    previousTabStop: searchBar.settingsControl
    nextTabStop: fileSelectionBar.visible ? fileSelectionBar.firstControl : runtimeStatusSurface.visible ? runtimeStatusSurface : footerActions.firstControl
  }

  OmarchyMenuBar {
    id: omarchyNavigation
    Layout.fillWidth: true
    visible: launcherContent.host.host.omarchyMenuOpen && !herdrNavigation.visible
    theme: launcherContent.host.host.part_appTheme
    controller: launcherContent.host.host.part_navigation
    searchInput: searchBar.inputControl
    previousTabStop: searchBar.settingsControl
    nextTabStop: fileSelectionBar.visible ? fileSelectionBar.firstControl : runtimeStatusSurface.visible ? runtimeStatusSurface : footerActions.firstControl
  }

  FileSelectionBar {
    id: fileSelectionBar
    previousTabStop: launcherContent.scopeBar ? launcherContent.scopeBar.lastControl : searchBar.settingsControl
    nextTabStop: runtimeStatusSurface.visible ? runtimeStatusSurface : footerActions.firstControl
    Layout.fillWidth: true
    visible: launcherContent.host.host.fileSelection.length > 0
    theme: launcherContent.host.host.part_appTheme
    entries: launcherContent.host.host.fileSelection
    enabled: launcherContent.host.host.activeFileSelectionRequestId === 0
    onCopyRequested: launcherContent.host.host.runFileSelection(Protocol.FileSelectionActionIds.CopyPaths)
    onOpenRequested: launcherContent.host.host.runFileSelection(Protocol.FileSelectionActionIds.Open)
    onClearRequested: launcherContent.host.host.clearFileSelection()
    onRemoveRequested: entry => launcherContent.host.host.selectFile(entry, false)
  }

  RowLayout {
    Layout.fillWidth: true
    Layout.fillHeight: true
    spacing: 10

    // Left content cell: the result list, with the A2 teaching
    // empty-state and the C9 guiding no-results state overlaid on top.
    Item {
      id: contentColumn
      visible: !launcherContent.host.host.calculatorOnly
      Layout.fillWidth: true
      Layout.fillHeight: true
      Layout.preferredWidth: 0.58

    ListView {
      id: resultList
      anchors.fill: parent
      clip: true
      spacing: launcherContent.host.host.part_appTheme.rowGap
      model: launcherContent.host.host.part_rows
      highlightMoveDuration: launcherContent.host.host.part_appTheme.highlightMoveDuration

      delegate: ResultListDelegate {
        id: listRow
        width: resultList.width
        theme: launcherContent.host.host.part_appTheme
        selected: !listRow.isHeader && listRow.index === launcherContent.host.host.selectedIndex
        actionCount: listRow.selected ? launcherContent.host.host.actionCount : 0
        onActivated: {
          launcherContent.host.host.selectedIndex = listRow.index
          launcherContent.host.host.part_navigation.invokePrimary()
        }
        onActionsRequested: {
          launcherContent.host.host.selectedIndex = listRow.index
          launcherContent.host.openActionPanel()
        }
        pointerGate: resultPointerGate
        compactLayout: launcherContent.host.host.compactLayout
        controlHeld: searchBar.controlHeld
        onHoverSelected: launcherContent.host.host.selectedIndex = listRow.index
      }
    }

      EmptyStateView {
        id: emptyStateView
        objectName: "emptyStateView"
        anchors.fill: parent
        visible: launcherContent.host.host.showEmptyState
        theme: launcherContent.host.host.part_appTheme
        filterDescriptors: launcherContent.host.host.filterDescriptors
        onTokenActivated: token => launcherContent.host.host.insertFilterToken(token)
        assistantAvailable: launcherContent.host.host.aiFeaturesEnabled
        onAssistantRequested: launcherContent.host.host.askAssistant()
        onFiltersRequested: launcherContent.host.host.openFilterPalette()
      }

      NoResultsView {
        id: noResultsView
        objectName: "noResultsView"
        anchors.fill: parent
        visible: launcherContent.host.host.showNoResults
        theme: launcherContent.host.host.part_appTheme
        query: launcherContent.host.host.part_navigation.queryText
        filterDescriptors: launcherContent.host.host.filterDescriptors
        assistantAvailable: launcherContent.host.host.aiFeaturesEnabled
        onAssistantRequested: launcherContent.host.host.askAssistant()
        onFiltersRequested: launcherContent.host.host.openFilterPalette()
      }
    }

    DetailPane {
      id: detailPane
      theme: launcherContent.host.host.part_appTheme
      Layout.fillHeight: true
      Layout.fillWidth: launcherContent.host.host.calculatorOnly
      Layout.preferredWidth: 0.42 * launcherContent.host.part_palette.width
      onCopyRequested: launcherContent.host.host.part_navigation.invokePrimary()
      result: launcherContent.host.host.currentResult
      opened: launcherContent.host.visible
      selected: true
      workspacePreviewEligible: launcherContent.visible && launcherContent.host.visible
        && launcherContent.host.host.desktopCapabilities.workspaceLayouts
        && launcherContent.host.host.daemonAvailable && !launcherContent.host.host.reconciliationPending
        && !launcherContent.host.host.anyBlockingSurface && !launcherContent.host.host.answerOpen
        && !launcherContent.host.host.herdrViewOpen
        && WorkspacePreviewPolicy.filtersAllowWorkspace(launcherContent.host.host.activeFilters,
          launcherContent.host.host.filterDescriptors)
        && WorkspacePreviewPolicy.eligibleForQuery(launcherContent.host.host.currentResult,
          launcherContent.host.host.part_personalization.acknowledged,
          launcherContent.host.host.part_personalization.draft,
          launcherContent.host.host.part_navigation.queryText, launcherContent.host.host.activeFilters)
      workspacePreviewMode: WorkspacePreviewPolicy.displayMode(launcherContent.host.host.part_personalization.acknowledged,
        launcherContent.host.host.part_personalization.draft)
      workspaceSnapshotLifecycle: launcherContent.host.host.workspaceSnapshotLifecycle
      workspacePreviewBackend: launcherContent.host.host.workspacePreviewBackend
      windowPreviewIntegration: launcherContent.host.host.desktopIntegration
      workspaceIssuedRequestId: launcherContent.host.host.part_searchSession.activeRequestId
      workspaceResultRequestId: launcherContent.host.host.part_searchSession.committedRequestId
      filePreview: launcherContent.host.host.filePreview
      previewLoading: launcherContent.host.host.previewLoading
      previewError: launcherContent.host.host.previewError
      typingTarget: searchBar.inputControl
      previousTabStop: footerActions.lastControl
      nextTabStop: searchBar.filterChipControl || searchBar.inputControl
      onPreviewMatchRequested: index => launcherContent.host.host.requestPreview(launcherContent.host.host.currentResult, index)
      onPreviewOpenRequested: launcherContent.host.host.openPreviewMatch()
      visible: !launcherContent.host.host.answerOpen && hasContent
        && (launcherContent.host.host.previewPaneVisible || launcherContent.host.host.calculatorOnly)
    }

    AiAnswerPane {
      id: answerPane
      objectName: "aiAnswerPane"
      visible: launcherContent.host.host.answerOpen
      theme: launcherContent.host.host.part_appTheme
      Layout.fillHeight: true
      Layout.preferredWidth: 0.42 * launcherContent.host.part_palette.width
      mode: launcherContent.host.host.answerMode
      query: launcherContent.host.host.answerQuery
      backendLabel: launcherContent.host.host.answerBackendLabel
      assistantController: launcherContent.host.host.part_agentController
      directController: launcherContent.host.host.part_aiController
      onCancelRequested: {
        if (launcherContent.host.host.answerMode === "agent") launcherContent.host.host.part_agentController.cancelTurn(++launcherContent.host.host.requestSequence)
        else launcherContent.host.host.part_aiController.cancel(++launcherContent.host.host.requestSequence)
      }
      onRetryRequested: {
        if (launcherContent.host.host.answerMode === "agent") {
          launcherContent.host.host.part_agentController.startTurn(++launcherContent.host.host.requestSequence, launcherContent.host.host.answerQuery, launcherContent.host.host.answerBackendId)
        } else {
          launcherContent.host.host.activeAiRequestId = ++launcherContent.host.host.requestSequence
          launcherContent.host.host.part_aiController.retry(launcherContent.host.host.activeAiRequestId)
        }
      }
      onCopyRequested: {
        if (launcherContent.host.host.answerMode === "agent") {
          launcherContent.host.host.part_agentController.outputAction(Ui.OutputAction.Copy, ++launcherContent.host.host.requestSequence, "", "")
        } else {
          launcherContent.host.host.part_aiController.outputAction(Ui.OutputAction.Copy, ++launcherContent.host.host.requestSequence, "", "")
        }
      }
      onExpandRequested: {
        if (launcherContent.host.host.answerMode === "agent") launcherContent.host.host.openAgentSurface()
        else launcherContent.host.host.aiSurfaceExpanded = true
      }
      onCloseRequested: launcherContent.host.host.closeAnswer()
    }
  }

  RuntimeStatusSurface {
    id: runtimeStatusSurface
    Layout.fillWidth: true
    Layout.maximumHeight: Math.max(
      Math.round(48 * launcherContent.host.host.part_appTheme.scale),
      Math.round(launcherContent.host.part_palette.height * 0.28))
    theme: launcherContent.host.host.part_appTheme
    daemonAvailable: launcherContent.host.host.daemonAvailable
    status: launcherContent.host.host.runtimeStatus
    reconciliationPending: launcherContent.host.host.reconciliationPending
    reconciliationWarning: launcherContent.host.host.reconciliationWarning
    KeyNavigation.tab: footerActions.firstControl || searchBar.filterChipControl || searchBar.inputControl
    KeyNavigation.backtab: fileSelectionBar.visible ? fileSelectionBar.lastControl : launcherContent.scopeBar ? launcherContent.scopeBar.lastControl : searchBar.settingsControl
    onInspectorRequested: launcherContent.host.host.indexInspectorOpen = true
  }

  HiddenUndoBar {
    id: undoBar
    objectName: "hiddenUndoBar"
    Layout.fillWidth: true
    theme: launcherContent.host.host.part_appTheme
    active: launcherContent.host.host.lastHidden !== null || launcherContent.host.host.lastUndo !== null
    title: launcherContent.host.host.lastHidden ? launcherContent.host.host.lastHidden.title : ""
    message: launcherContent.host.host.lastUndo ? launcherContent.host.host.lastUndo.message : ""
    onUndoRequested: launcherContent.host.host.undoLast()
    onDismissed: {
      launcherContent.host.host.lastHidden = null
      launcherContent.host.host.lastUndo = null
    }
  }

  RowLayout {
    Layout.fillWidth: true
    spacing: 8

    Text {
      id: statusLine
      Layout.fillWidth: true
      // Daemon and action messages are shown as written, never as markup.
      textFormat: Text.PlainText
      text: launcherContent.host.host.statusText
      color: launcherContent.host.host.daemonAvailable ? launcherContent.host.host.part_appTheme.textFaint : launcherContent.host.host.part_appTheme.urgent
      font.family: launcherContent.host.host.part_appTheme.fontFamily
      font.pixelSize: launcherContent.host.host.part_appTheme.fontSmall
      elide: Text.ElideRight
      Accessible.role: Accessible.StaticText
      Accessible.name: launcherContent.host.host.announcementText
      Accessible.description: "CommandDeck status"
    }

    FooterHintBar {
      id: footerActions
      objectName: "footerHintBar"
      compact: launcherContent.host.host.compactLayout
      Layout.alignment: Qt.AlignVCenter
      theme: launcherContent.host.host.part_appTheme
      primaryActionTitle: launcherContent.host.host.primaryActionTitle
      actionCount: launcherContent.host.host.actionCount
      directActions: launcherContent.host.host.directActions.slice(0, 2)
      onShortcutRequested: actionId => launcherContent.host.host.invokeShortcut(actionId)
      actionsEnabled: launcherContent.host.host.daemonAvailable && !launcherContent.host.host.reconciliationPending
      onPrefixesRequested: () => launcherContent.host.host.openFilterPalette(Ui.FocusReturnControl.FooterPrefixes)
      onHelpRequested: () => launcherContent.host.host.toggleHelp(Ui.FocusReturnControl.FooterHelp)
      previousTabStop: runtimeStatusSurface.visible ? runtimeStatusSurface : fileSelectionBar.visible ? fileSelectionBar.lastControl : launcherContent.scopeBar ? launcherContent.scopeBar.lastControl : searchBar.settingsControl
      nextTabStop: detailPane.firstPreviewControl || searchBar.filterChipControl || searchBar.inputControl
      shortcutTarget: searchBar.inputControl
      shortcutHandler: searchBar.handleActionShortcut
      availableWidth: launcherContent.host.part_palette.width - 2 * launcherContent.host.host.part_appTheme.panelPadding
      onPrimaryRequested: launcherContent.host.host.part_navigation.invokePrimary()
      onActionsRequested: launcherContent.host.openActionPanel()
    }
  }
}
