import "SettingsNavigationActions.js" as SettingsNavigationActions
import "SettingsCustomizationActions.js" as SettingsCustomizationActions
import "SettingsProviderActions.js" as SettingsProviderActions
import "SettingsAiActions.js" as SettingsAiActions
import "SettingsAgentActions.js" as SettingsAgentActions
import QtQuick
import "UiConstants.js" as Ui
import QtQuick.Layouts

// Settings dialog: a rail of pages on the left, one page of cards on the
// right. Every control the daemon can act on keeps its alias so the specs
// can drive it with real events.
SettingsPanelState {
  id: root
  owner: root
  parts: ({
    aiHub: pageStack.aiHub,
    aiProviderEditor: aiProviderEditor,
    aiRemoveConfirmation: aiRemoveConfirmation,
    aiScroll: pageStack.aiPage,
    assistantScroll: pageStack.assistantPage,
    closeButton: closeButton,
    customizationScroll: pageStack.customizationPage,
    editorsScroll: pageStack.editorsPage,
    filtersScroll: pageStack.filtersPage,
    pagePicker: pagePicker,
    searchScroll: pageStack.searchPage,
    settingsBackground: settingsBackground,
    settingsSearch: settingsSearch,
    shortcutsScroll: pageStack.shortcutsPage
  })

  function pageDescriptor(pageId) {
    for (let index = 0; index < root.pages.length; index++) {
      if (root.pages[index].id === pageId) return root.pages[index]
    }
    return root.pages[0]
  }

  function pageIndex(pageId) {
    for (let index = 0; index < root.pages.length; index++) {
      if (root.pages[index].id === pageId) return index
    }
    return 0
  }

  function showPage(pageId) {
    if (pageId === Ui.SettingsPage.Assistant) {
      pageStack.aiHub.section = "assistant"
      root.currentPage = Ui.SettingsPage.Ai
    } else {
      root.currentPage = root.pageDescriptor(pageId).id
    }
    pageStack.aiHub.selectSearchSection()
    root.maybeRefreshAgentModels()
    return true
  }

  function agentLocalityLabel(backend) { return SettingsAgentActions.agentLocalityLabel(root, backend) }

  function agentBackendOptions() { return SettingsAgentActions.agentBackendOptions(root) }

  function agentModelOptions() { return SettingsAgentActions.agentModelOptions(root) }

  function agentEffortTitle(id) { return SettingsAgentActions.agentEffortTitle(root, id) }

  function agentSelectedModel() { return SettingsAgentActions.agentSelectedModel(root) }

  function agentReasoningEfforts() { return SettingsAgentActions.agentReasoningEfforts(root) }

  function agentEffortOptions() { return SettingsAgentActions.agentEffortOptions(root) }

  function cloneAgentPreferences() { return SettingsAgentActions.cloneAgentPreferences(root) }

  // Trust is deliberately absent here. A level change is an authority change
  // and only the challenge path may carry it.
  function mutateAgentPreference(field, value) { return SettingsAgentActions.mutateAgentPreference(root, field, value) }

  function confirmAgentChallenge() { return SettingsAgentActions.confirmAgentChallenge(root) }

  function synchronizeAgentChoices() { return SettingsAgentActions.synchronizeAgentChoices(root) }

  // The saved effort wins; otherwise the model's own default is shown, so the
  // control never claims "low" for a model that reasons at "medium".
  function synchronizeAgentEffort() { return SettingsAgentActions.synchronizeAgentEffort(root) }

  function maybeRefreshAgentModels() { return SettingsAgentActions.maybeRefreshAgentModels(root) }

  onAgentPreferenceValuesChanged: Qt.callLater(root.synchronizeAgentChoices)
  onAgentBackendsChanged: Qt.callLater(root.synchronizeAgentChoices)
  onAgentModelsChanged: {
    if ((root.agentModels || []).length > 0) root.agentModelsAutoRequested = false
    Qt.callLater(root.synchronizeAgentChoices)
  }
  onAgentStatusChanged: Qt.callLater(root.maybeRefreshAgentModels)
  onCurrentPageChanged: Qt.callLater(root.maybeRefreshAgentModels)

  focus: visible
  readonly property bool settingsTabFence: true
  Accessible.role: Accessible.Dialog
  Accessible.name: "CommandDeck settings"
  readonly property alias closeControl: closeButton
  readonly property alias navigationControl: navigation
  readonly property alias saveErrorControl: saveErrorText

  onVisibleChanged: {
    if (!visible) settingsSearch.text = ""
    if (visible) Qt.callLater(function() {
      root.synchronizeAiChoices()
      root.maybeRefreshAgentModels()
      settingsSearch.forceActiveFocus()
    })
  }
  onAiProvidersChanged: Qt.callLater(root.synchronizeAiChoices)
  onAiModelsChanged: Qt.callLater(root.synchronizeAiChoices)
  onAiSelectionChanged: Qt.callLater(root.synchronizeAiChoices)

  function indexOfId(values, id) { return SettingsAiActions.indexOfId(root, values, id) }

  function synchronizeAiChoices() { return SettingsAiActions.synchronizeAiChoices(root) }

  function editAiTemplate(template) { return SettingsAiActions.editAiTemplate(root, template) }

  function saveAiTemplate() { return SettingsAiActions.saveAiTemplate(root) }

  function containsFocusItem(item) { return SettingsNavigationActions.containsFocusItem(root, item) }

  function focusBoundary(forward) { return SettingsNavigationActions.focusBoundary(root, forward) }

  function cycleFocus(forward) {
    const window = root.Window.window
    const current = window ? window.activeFocusItem : null
    if (!root.containsFocusItem(current)) {
      root.focusBoundary(forward)
      return
    }
    const next = current.nextItemInFocusChain(forward)
    if (!next || !root.containsFocusItem(next)) root.focusBoundary(forward)
    else next.forceActiveFocus()
  }

  Keys.priority: Keys.BeforeItem
  Keys.onPressed: event => {
    if (event.key === Qt.Key_Tab && !(event.modifiers & Qt.ShiftModifier)) {
      root.cycleFocus(true)
      event.accepted = true
    } else if (event.key === Qt.Key_Backtab
        || (event.key === Qt.Key_Tab && (event.modifiers & Qt.ShiftModifier))) {
      root.cycleFocus(false)
      event.accepted = true
    } else if (event.key === Qt.Key_F && event.modifiers === Qt.ControlModifier) {
      settingsSearch.forceActiveFocus()
      settingsSearch.selectAll()
      event.accepted = true
    } else if (event.key === Qt.Key_PageDown && (event.modifiers & Qt.ControlModifier)) {
      navigation.step(1)
      event.accepted = true
    } else if (event.key === Qt.Key_PageUp && (event.modifiers & Qt.ControlModifier)) {
      navigation.step(-1)
      event.accepted = true
    }
  }

  // Settings fills the rounded palette, so it repeats the palette's frame
  // instead of painting square corners over it.
  Rectangle {
    id: settingsBackground
    anchors.fill: parent
    radius: root.theme.radius
    color: root.theme.popupBackground
    border.width: 1
    border.color: root.theme.border
  }

  MouseArea {
    anchors.fill: parent
    acceptedButtons: Qt.AllButtons
    hoverEnabled: true
  }

  // Test observables. The source rows previously rendered with a collapsed
  // width and a blank label, and no spec could see it.
  readonly property int renderedSourceCount: root.part_sourceRepeater.count
  readonly property int renderedShortcutCount: root.part_shortcutRepeater.count
  readonly property int renderedFilterAliasCount: root.part_filterAliasRepeater.count
  readonly property alias filterAliasInputControl: root.part_filterAliasInput
  readonly property alias filterAliasTargetControl: root.part_filterAliasTarget
  readonly property alias filterAliasSaveControl: root.part_filterAliasSave
  readonly property alias savedSearchNameControl: root.part_savedSearchName
  readonly property alias savedSearchShortcutControl: root.part_savedSearchShortcut
  readonly property alias savedSearchSaveControl: root.part_savedSearchSave
  readonly property alias preferredEditorControl: root.part_preferredEditorChoice
  readonly property alias displaySizeControl: root.part_displaySizeChoice
  readonly property alias panelTransparencyControl: root.part_panelTransparencyCheck
  readonly property alias compactLayoutControl: root.part_compactLayoutCheck
  readonly property alias previewPaneControl: root.part_previewPaneCheck
  readonly property alias escapeClearsControl: root.part_escapeClearsCheck
  readonly property alias keepSearchControl: root.part_keepSearchChoice
  readonly property alias homeFolderOnlyControl: root.part_homeFolderOnlyCheck
  readonly property alias panelTransparencyAmountControl: root.part_panelTransparencySlider
  readonly property alias graphicalEditorExecutableControl: root.part_graphicalEditorExecutable
  readonly property alias graphicalEditorArgumentsControl: root.part_graphicalEditorArguments
  readonly property alias editorSaveControl: root.part_editorSave
  readonly property int renderedSavedSearchCount: root.part_savedSearchRepeater.count
  readonly property int renderedAiTemplateCount: root.part_aiTemplateRepeater.count
  readonly property alias aiProviderControl: root.part_aiProviderChoice
  readonly property alias aiModelControl: root.part_aiModelChoice
  readonly property alias aiSelectionSaveControl: root.part_aiSelectionSave
  readonly property alias aiStatusControl: root.part_aiStatus
  readonly property alias aiTemplateIdControl: root.part_aiTemplateId
  readonly property alias aiTemplateNameControl: root.part_aiTemplateName
  readonly property alias aiTemplateUserControl: root.part_aiTemplateUser
  readonly property alias aiEnableControl: root.part_aiEnableToggle
  readonly property alias aiFeaturesControl: root.part_aiFeaturesToggle
  readonly property alias aiFeaturesCardControl: root.part_aiFeaturesCard
  readonly property alias aiManagementCardControl: root.part_aiManagementCard
  readonly property alias aiProvidersCardControl: root.part_aiProvidersCard
  readonly property alias templatesCardControl: root.part_templatesCard
  readonly property alias aiAddProviderControl: root.part_aiAddProvider
  readonly property alias aiReloadControl: root.part_aiReload
  readonly property alias aiManagementStatusControl: root.part_aiManagementStatus
  readonly property alias aiProviderEditorControl: aiProviderEditor
  readonly property alias aiRemoveConfirmationControl: aiRemoveConfirmation
  readonly property int renderedAiProviderCount: root.part_aiProviderRepeater.count

  function renderedAiProviderAt(index) { return SettingsProviderActions.renderedAiProviderAt(root, index) }

  function aiProviderLocality(provider) { return SettingsProviderActions.aiProviderLocality(root, provider) }

  function aiProviderDestination(provider) { return SettingsProviderActions.aiProviderDestination(root, provider) }

  function aiProviderRowSummary(provider) { return SettingsProviderActions.aiProviderRowSummary(root, provider) }

  function openAiProviderEditor(provider, invoker) { return SettingsProviderActions.openAiProviderEditor(root, provider, invoker) }

  function confirmAiProviderRemoval(provider, invoker) { return SettingsProviderActions.confirmAiProviderRemoval(root, provider, invoker) }

  function toggleAiProviderEnabled(provider) { return SettingsProviderActions.toggleAiProviderEnabled(root, provider) }

  function restoreAiFocus() { return SettingsProviderActions.restoreAiFocus(root) }

  function synchronizeAiManagement() { return SettingsProviderActions.synchronizeAiManagement(root) }

  onAiSettingsChanged: Qt.callLater(root.synchronizeAiManagement)
  onAiSettingsStatusChanged: {
    if (root.aiSettingsStatus !== Ui.ProviderSettingsState.Saved && root.aiSettingsStatus !== Ui.ProviderSettingsState.SavedSelectionFailed) return
    if (aiProviderEditor.opened) {
      aiProviderEditor.close()
    } else if (root.aiRemovalTarget !== null) {
      root.aiRemovalTarget = null
      Qt.callLater(root.restoreAiFocus)
    }
  }
  onAiTestResultChanged: {
    if (!root.aiTestResult || !aiProviderEditor.opened) return
    const id = aiProviderEditor.idControl.text.trim()
    const states = root.aiRowTestStates
    states[id] = String(root.aiTestResult.outcome || "")
    root.aiRowTestStates = states
  }
  readonly property int renderedAgentGrantCount: root.part_agentGrantRepeater.count
  readonly property int renderedAgentWarningCount: root.part_agentWarningRepeater.count
  readonly property alias agentEnableControl: root.part_agentEnabledCheck
  readonly property alias agentBackendControl: root.part_agentBackendChoice
  readonly property alias agentModelControl: root.part_agentModelChoice
  readonly property alias agentEffortControl: root.part_agentEffortChoice
  readonly property alias agentModelsRefreshControl: root.part_agentModelsRefresh
  readonly property alias agentTrustControl: root.part_agentTrustChoice
  readonly property alias agentChallengeCardControl: root.part_agentChallengeCard
  readonly property alias agentChallengePromptControl: root.part_agentChallengePrompt
  readonly property alias agentPhraseControl: root.part_agentPhraseField
  readonly property alias agentDurationControl: root.part_agentDurationChoice
  readonly property alias agentChallengeConfirmControl: root.part_agentChallengeConfirm
  readonly property alias agentChallengeCancelControl: root.part_agentChallengeCancel
  readonly property alias agentContextPolicyControl: root.part_agentContextPolicyChoice
  readonly property alias agentRetentionControl: root.part_agentRetentionSpin
  readonly property alias agentTurnTimeoutControl: root.part_agentTurnTimeoutSpin
  readonly property alias agentWebSearchControl: root.part_agentWebSearchCheck
  readonly property alias agentHiddenModelsControl: root.part_agentHiddenModelsCheck
  readonly property alias agentGrantsClearControl: root.part_agentGrantsClear
  readonly property alias agentLogoutControl: root.part_agentLogout
  readonly property alias agentLoginControl: root.part_agentLogin
  readonly property alias agentStatusControl: root.part_agentStatusText

  function renderedAgentGrantAt(index) { return SettingsCustomizationActions.renderedAgentGrantAt(root, index) }

  function renderedFilterAliasAt(index) { return SettingsCustomizationActions.renderedFilterAliasAt(root, index) }

  function renderedSavedSearchAt(index) { return SettingsCustomizationActions.renderedSavedSearchAt(root, index) }

  function renderedSourceAt(index) { return SettingsCustomizationActions.renderedSourceAt(root, index) }

  function renderedSourceLabelAt(index) { return SettingsCustomizationActions.renderedSourceLabelAt(root, index) }

  function renderedSourceWidthAt(index) { return SettingsCustomizationActions.renderedSourceWidthAt(root, index) }

  function renderedSourceIconAt(index) { return SettingsCustomizationActions.renderedSourceIconAt(root, index) }

  function renderedShortcutStatusAt(index) { return SettingsCustomizationActions.renderedShortcutStatusAt(root, index) }

  function renderedShortcutAccessibleNameAt(index) {
    const item = index >= 0 && index < root.part_shortcutRepeater.count
      ? root.part_shortcutRepeater.itemAt(index)
      : null
    return item ? item.Accessible.name : ""
  }

  function sourceDescriptor(sourceId) { return SettingsCustomizationActions.sourceDescriptor(root, sourceId) }

  function sourceLabel(sourceId) { return SettingsCustomizationActions.sourceLabel(root, sourceId) }

  function sourceIcon(sourceId) { return SettingsCustomizationActions.sourceIcon(root, sourceId) }

  function sourceHomeAvailable(sourceId) { return SettingsCustomizationActions.sourceHomeAvailable(root, sourceId) }

  function shortcutStatus(binding) { return SettingsCustomizationActions.shortcutStatus(root, binding) }

  function hasFallback(fallbackId) { return SettingsCustomizationActions.hasFallback(root, fallbackId) }

  function filterAliasTokenIsBuiltIn(value) { return SettingsCustomizationActions.filterAliasTokenIsBuiltIn(root, value) }

  function saveFilterAlias() { return SettingsCustomizationActions.saveFilterAlias(root) }

  function editSavedSearch(search) { return SettingsCustomizationActions.editSavedSearch(root, search) }

  function clearSavedSearchEditor() { return SettingsCustomizationActions.clearSavedSearchEditor(root) }

  function saveSavedSearch() { return SettingsCustomizationActions.saveSavedSearch(root) }

  onSavedSearchesChanged: {
    if (!savedSearchAwaitingAck) return
    savedSearchAwaitingAck = false
    clearSavedSearchEditor()
  }

  onSavedSearchErrorChanged: {
    if (savedSearchError.length > 0) savedSearchAwaitingAck = false
  }

  function fallbackChoices() { return SettingsCustomizationActions.fallbackChoices(root) }

  function removeUnavailable(customizationId) { return SettingsCustomizationActions.removeUnavailable(root, customizationId) }

  function clonePreferences() { return SettingsCustomizationActions.clonePreferences(root) }

  function mutateSource(index, field, value) { return SettingsCustomizationActions.mutateSource(root, index, field, value) }

  function moveSource(index, delta) { return SettingsCustomizationActions.moveSource(root, index, delta) }

  function moveSourceTo(from, to) { return SettingsCustomizationActions.moveSourceTo(root, from, to) }

  function moveFavorite(index, delta) { return SettingsCustomizationActions.moveFavorite(root, index, delta) }

  function syncEditorControls() { return SettingsCustomizationActions.syncEditorControls(root) }

  function saveEditors() { return SettingsCustomizationActions.saveEditors(root) }

  onSnapshotChanged: syncEditorControls()

  // Escape ends the current interaction first: a source drag, then a search.
  Keys.onEscapePressed: {
    if (root.part_sourceList.cancelDragByKeyboard()) return

    if (settingsSearch.text.length > 0) {
      settingsSearch.text = ""
      settingsSearch.forceActiveFocus()
    } else {
      root.dismissed()
    }
  }

  readonly property int outerPad: Math.round(root.theme.panelPadding * 1.3)
  readonly property bool railCollapsed: root.width < Math.round(760 * root.theme.scale)

  ColumnLayout {
    anchors.fill: parent
    anchors.margins: root.outerPad
    spacing: Math.round(14 * root.theme.scale)

    RowLayout {
      Layout.fillWidth: true
      spacing: Math.round(14 * root.theme.scale)

      ColumnLayout {
        Layout.fillWidth: true
        spacing: Math.round(2 * root.theme.scale)

        Text {
          Layout.fillWidth: true
          text: "Settings"
          color: root.theme.text
          font.family: root.theme.fontFamily
          font.pixelSize: Math.round(root.theme.fontHeading * 1.45)
          font.weight: Font.DemiBold
        }

        Text {
          Layout.fillWidth: true
          text: root.pageDescriptor(root.currentPage).title
            + " · " + root.pageDescriptor(root.currentPage).subtitle
          color: root.theme.textMuted
          elide: Text.ElideRight
          font.family: root.theme.fontFamily
          font.pixelSize: root.theme.fontBody
        }
      }

      DeckButton {
        id: closeButton
        theme: root.theme
        text: "Close"
        Accessible.name: "Close settings"
        onClicked: root.dismissed()
      }
    }

    SettingsField {
      id: settingsSearch
      theme: root.theme
      Layout.fillWidth: true
      placeholderText: "Search settings…"
      Accessible.name: "Search settings"
      Accessible.description: "Filter settings by name or description. Enter moves to matching pages. Escape clears the search."
      onTextChanged: {
        pageStack.resetScroll()
      }
      onAccepted: {
        if (root.filteredPages.length === 0) return
        if (root.railCollapsed) pagePicker.forceActiveFocus()
        else navigation.forceActiveFocus()
      }
      Keys.onDownPressed: {
        if (root.filteredPages.length === 0) return
        if (root.railCollapsed) pagePicker.forceActiveFocus()
        else navigation.forceActiveFocus()
      }
    }

    Text {
      Layout.fillWidth: true
      text: root.filteredPages.length === 0
        ? "No settings found. Try a different search."
        : "Ctrl+F Search · Enter Pages · Tab Controls · Esc " + (settingsSearch.text.length > 0 ? "Clear search" : "Close")
      color: root.theme.textMuted
      font.family: root.theme.fontFamily
      font.pixelSize: root.theme.fontSmall
      wrapMode: Text.Wrap
      Accessible.role: Accessible.StaticText
      Accessible.name: text
    }

    // A rejected save puts its control back; say why here, not only in the
    // launcher footer that Settings covers.
    Text {
      id: saveErrorText
      Layout.fillWidth: true
      visible: root.preferencesSaveError.length > 0
      text: "Couldn't save that change: " + root.preferencesSaveError
      color: root.theme.urgent
      font.family: root.theme.fontFamily
      font.pixelSize: root.theme.fontSmall
      wrapMode: Text.Wrap
      Accessible.role: Accessible.AlertMessage
      Accessible.name: text
    }

    RowLayout {
      visible: root.filteredPages.length > 0
      Layout.fillWidth: true
      Layout.fillHeight: true
      spacing: Math.round(18 * root.theme.scale)

      SettingsNav {
        id: navigation
        theme: root.theme
        visible: !root.railCollapsed
        Layout.alignment: Qt.AlignTop
        Layout.preferredWidth: implicitWidth
        pages: root.filteredPages
        currentId: root.currentPage
        onSelected: pageId => root.showPage(pageId)
      }

      Rectangle {
        visible: !root.railCollapsed
        Layout.fillHeight: true
        Layout.preferredWidth: 1
        color: root.theme.separator
      }

      // Collapsed rail: a compact page picker keeps every page reachable on
      // narrow surfaces.
      SettingsChoice {
        id: pagePicker
        theme: root.theme
        visible: root.railCollapsed
        Layout.alignment: Qt.AlignTop
        Layout.preferredWidth: Math.round(170 * root.theme.scale)
        showCurrentDescription: false
        options: root.filteredPages.map(page => ({ id: page.id, title: page.title, description: page.subtitle }))
        currentIndex: root.filteredPages.findIndex(page => page.id === root.currentPage)
        Accessible.name: "Settings page"
        onActivated: index => root.showPage(root.filteredPages[index].id)
      }

      SettingsPageHost {
        id: pageStack
        host: root
        Layout.fillWidth: true
        Layout.minimumWidth: 0
        Layout.fillHeight: true
      }
    }
  }

  AiProviderEditor {
    id: aiProviderEditor
    anchors.fill: parent
    z: 40
    theme: root.theme
    presets: root.aiPresets
    reservedIds: root.aiReservedIds
    existingIds: root.aiManagedProviders.map(value => String(value.id))
    directAiEnabled: root.aiDirectEnabled
    busy: root.aiSettingsBusy
    testBusy: root.aiTestBusy
    testResult: root.aiTestResult
    errorText: root.aiSettingsStatus === Ui.ProviderSettingsState.Failed || root.aiSettingsStatus === Ui.ProviderSettingsState.Conflict
      ? root.aiSettingsErrorText : ""
    onSaveRequested: (provider, create, selectAfterSave) =>
      root.aiProviderSaveRequested(provider, create, root.aiSettingsRevision, selectAfterSave)
    onTestRequested: provider => root.aiProviderTestRequested(provider)
    onTestCancelRequested: root.aiProviderTestCancelRequested()
    onDraftInvalidated: root.aiDraftInvalidated()
    onClosed: Qt.callLater(root.restoreAiFocus)
  }

  ConfirmationSurface {
    id: aiRemoveConfirmation
    anchors.fill: parent
    z: 41
    theme: root.theme
    prompt: root.aiRemovalTarget
      ? "Remove AI provider \u201c" + String(root.aiRemovalTarget.displayName || root.aiRemovalTarget.id) + "\u201d?"
        + (root.aiSelection && root.aiSelection.providerId === root.aiRemovalTarget.id
          ? " It is the selected provider; the selection will become unavailable." : "")
        + " External credentials are not deleted."
      : "Remove this provider?"
    confirmLabel: "Remove"
    destructive: true
    onCanceled: {
      aiRemoveConfirmation.opened = false
      root.aiRemovalTarget = null
      Qt.callLater(root.restoreAiFocus)
    }
    onAccepted: {
      aiRemoveConfirmation.opened = false
      if (root.aiRemovalTarget) root.aiProviderDeleteRequested(String(root.aiRemovalTarget.id), root.aiSettingsRevision)
    }
  }
}
