import QtQuick
import QtTest
import "../../qml/common" as CommandDeck
import "../../qml/common/Protocol.js" as Protocol
import "../../qml/common/UiConstants.js" as Ui

EventPathFixture {
  id: scene
  EventPathTestCase {
    fixture: scene
    name: "CommandDeck_settings_preferences"
    function test_top_matches_choice_with_keyboard_and_mouse() {
      const panel = fixture.part_settingsPanel
      const original = panel.snapshot
      panel.snapshot = Object.assign({}, original, {
        preferences: Object.assign({}, original.preferences, { topMatches: null }),
        defaults: { topMatches: true }
      })
      panel.visible = true
      tryCompare(panel.searchControl, "activeFocus", true)
      typeText("top matches")
      compare(panel.currentPage, "search")
      const toggle = findChild(panel, "topMatchesToggle")
      verify(toggle.visible)
      compare(toggle.checked, true)
      compare(toggle.Accessible.name, "Top Matches")

      keyClick(Qt.Key_Return)
      tryCompare(panel.navigationControl, "activeFocus", true)
      for (let index = 0; index < 10 && !toggle.activeFocus; index++) keyClick(Qt.Key_Tab)
      verify(toggle.activeFocus, "Top Matches is reachable in the Search tab order")
      keyClick(Qt.Key_Space)
      compare(panel.changedPreferences.topMatches, false)
      compare(panel.changedPreferences.sources.length, 2)
      verify(toggle.activeFocus)
      panel.snapshot = Object.assign({}, panel.snapshot, { preferences: panel.changedPreferences })
      verify(toggle.activeFocus, "saving preserves the focused toggle")
      keyClick(Qt.Key_Tab, Qt.ShiftModifier)
      verify(!toggle.activeFocus)
      keyClick(Qt.Key_Tab)
      tryCompare(toggle, "activeFocus", true)
      keyClick(Qt.Key_Space)
      compare(panel.changedPreferences.topMatches, true)

      mouseClick(toggle, toggle.width - toggle.pillWidth / 2, toggle.height / 2)
      compare(panel.changedPreferences.topMatches, false)
      keyClick(Qt.Key_Escape)
      keyClick(Qt.Key_Escape)
      verify(!panel.visible)
      panel.visible = true
      tryCompare(panel.searchControl, "activeFocus", true)
      compare(toggle.checked, false, "reopening reflects the saved choice")
      panel.snapshot = original
    }

    function test_source_rows_fill_the_visible_settings_page() {
      fixture.part_settingsPanel.visible = true
      fixture.part_settingsPanel.showPage("search")
      tryVerify(function() { return fixture.part_settingsPanel.renderedSourceWidthAt(0) > 400 })
    }

    function test_assistant_sign_in_works_with_keyboard_and_mouse() {
      const panel = fixture.part_settingsPanel
      panel.agentPreferenceValues = { enabled: true, backendId: "codex.local", trustLevel: 2 }
      panel.agentBackends = [{ id: "codex.local", displayName: "Codex", kind: "codex-app-server" }]
      panel.agentStatus = {
        backend: { id: "codex.local", displayName: "Codex", authenticationState: "unauthenticated" },
        login: { state: "unauthenticated", supportedMethods: ["device-code"] }
      }
      panel.visible = true
      tryCompare(panel.searchControl, "activeFocus", true)
      typeText("sign in")
      compare(panel.currentPage, "ai")
      compare(panel.part_aiHub.section, "assistant")
      keyClick(Qt.Key_Return)
      tryCompare(panel.navigationControl, "activeFocus", true)
      keyClick(Qt.Key_Tab)
      tryCompare(findChild(panel, "aiAssistantSection"), "activeFocus", true)
      keyClick(Qt.Key_Tab)
      tryCompare(findChild(panel, "aiQuickSection"), "activeFocus", true)
      keyClick(Qt.Key_Tab)
      tryCompare(panel.agentEnableControl, "activeFocus", true)
      keyClick(Qt.Key_Tab)
      tryCompare(panel.agentLoginControl, "activeFocus", true)
      const previous = panel.agentLogins
      keyClick(Qt.Key_Space)
      compare(panel.agentLogins, previous + 1)
      mouseClick(panel.agentLoginControl)
      compare(panel.agentLogins, previous + 2)
    }

    function test_workspace_snapshots_require_capability_and_explicit_choice() {
      fixture.part_settingsPanel.workspaceSnapshotsAvailable = false
      fixture.part_settingsPanel.visible = true
      tryCompare(fixture.part_settingsPanel.searchControl, "activeFocus", true)
      typeText("workspace")
      const toggle = findChild(fixture.part_settingsPanel, "workspaceSnapshotsToggle")
      verify(toggle.visible)
      verify(!toggle.enabled)
      fixture.part_settingsPanel.workspaceSnapshotsAvailable = true
      keyClick(Qt.Key_Return)
      for (let index = 0; index < 14 && !toggle.activeFocus; index++) keyClick(Qt.Key_Tab)
      verify(toggle.activeFocus)
      keyClick(Qt.Key_Space)
      compare(fixture.part_settingsPanel.changedPreferences.workspacePreviewMode, "snapshots")
      compare(fixture.part_settingsPanel.changedPreferences.sources.length, 2)
      mouseClick(toggle, toggle.width - toggle.pillWidth / 2, toggle.height / 2)
      compare(fixture.part_settingsPanel.changedPreferences.workspacePreviewMode, "layout")
      fixture.part_settingsPanel.workspaceSnapshotsAvailable = false
    }
    function test_workspace_preview_setting_keyboard_and_mouse_preserve_other_preferences() {
      fixture.part_settingsPanel.visible = true
      tryCompare(fixture.part_settingsPanel.searchControl, "activeFocus", true)
      typeText("layout previews")
      compare(fixture.part_settingsPanel.currentPage, "search")
      const toggle = findChild(fixture.part_settingsPanel, "workspacePreviewToggle")
      verify(toggle.visible)
      // Filtering re-lays out every page; click where the toggle settles.
      waitForRendering(fixture.part_settingsPanel)
      mouseClick(toggle)
      compare(fixture.part_settingsPanel.changedPreferences.workspacePreviewMode, "off")
      compare(fixture.part_settingsPanel.changedPreferences.sources.length, 2)
      keyClick(Qt.Key_F, Qt.ControlModifier)
      keyClick(Qt.Key_Return)
      for (let index = 0; index < 10 && !toggle.activeFocus; index++) keyClick(Qt.Key_Tab)
      verify(toggle.activeFocus)
      keyClick(Qt.Key_Space)
      compare(fixture.part_settingsPanel.changedPreferences.workspacePreviewMode, "layout")
      verify(toggle.activeFocus)
    }
    function test_settings_close_button_emphasizes_hover_and_keyboard_focus() {
      fixture.part_settingsPanel.visible = true
      tryCompare(fixture.part_settingsPanel.searchControl, "activeFocus", true)
      const close = fixture.part_settingsPanel.closeControl
      compare(close.background.border.width, 1)
      compare(close.background.children[0].visible, false)

      mouseMove(close, close.width / 2, close.height / 2)
      tryCompare(close, "hovered", true)
      compare(close.background.border.width, 3)
      compare(close.background.border.color, fixture.part_theme.text)
      compare(close.background.color, Qt.rgba(fixture.part_theme.accent.r, fixture.part_theme.accent.g, fixture.part_theme.accent.b, 0.38))

      mouseMove(fixture.part_settingsPanel, 2, fixture.part_settingsPanel.height - 2)
      tryCompare(close, "hovered", false)
      close.forceActiveFocus()
      tryCompare(close, "activeFocus", true)
      compare(close.focusRingVisible, true)
      compare(close.background.border.width, 3)
      compare(close.background.border.color, fixture.part_theme.text)
      compare(close.background.color, Qt.rgba(fixture.part_theme.accent.r, fixture.part_theme.accent.g, fixture.part_theme.accent.b, 0.58))
      compare(close.background.children[0].visible, true)
      compare(close.background.children[0].border.width, 4)
      compare(close.background.children[0].border.color, fixture.part_theme.accent)
    }

    function test_settings_search_filters_cards_and_activates_controls() {
      fixture.part_theme.background = "#33090909"
      fixture.part_settingsPanel.visible = true
      tryCompare(fixture.part_settingsPanel.searchControl, "activeFocus", true)
      compare(fixture.part_settingsPanel.backgroundControl.color.a, 1)
      compare(fixture.part_settingsPanel.backgroundControl.color.r, fixture.part_theme.background.r)
      typeText("  TRANSPARENCY  ")
      compare(fixture.part_settingsPanel.filteredPages.length, 1)
      compare(fixture.part_settingsPanel.currentPage, "customization")
      compare(fixture.part_settingsPanel.pageCards.customization.filter(card => card.matchesSearch).length, 1)
      verify(!fixture.part_settingsPanel.displaySizeControl.visible)
      keyClick(Qt.Key_Return)
      compare(fixture.part_settingsPanel.navigationControl.activeFocus, true)
      keyClick(Qt.Key_Tab)
      tryCompare(fixture.part_settingsPanel.panelTransparencyControl, "activeFocus", true)
      const checked = fixture.part_settingsPanel.panelTransparencyControl.checked
      keyClick(Qt.Key_Space)
      compare(fixture.part_settingsPanel.panelTransparencyControl.checked, !checked)
      keyClick(Qt.Key_F, Qt.ControlModifier)
      tryCompare(fixture.part_settingsPanel.searchControl, "activeFocus", true)
      compare(fixture.part_settingsPanel.searchControl.selectedText, "  TRANSPARENCY  ")
      keyClick(Qt.Key_Escape)
      compare(fixture.part_settingsPanel.searchControl.text, "")
      compare(fixture.part_settingsPanel.filteredPages.length, 6)
      verify(fixture.part_settingsPanel.searchControl.activeFocus)
      compare(fixture.part_settingsPanel.dismissals, 0)
      keyClick(Qt.Key_Backtab)
      tryCompare(fixture.part_settingsPanel.closeControl, "activeFocus", true)
      keyClick(Qt.Key_Tab)
      tryCompare(fixture.part_settingsPanel.searchControl, "activeFocus", true)
      keyClick(Qt.Key_Escape)
      compare(fixture.part_settingsPanel.dismissals, 1)
      verify(!fixture.part_settingsPanel.visible)
    }

    function test_settings_search_navigates_matches_and_handles_empty_results() {
      fixture.part_settingsPanel.theme = fixture.part_compactSettingsTheme
      fixture.part_settingsPanel.visible = true
      tryCompare(fixture.part_settingsPanel.searchControl, "activeFocus", true)
      typeText("model")
      compare(fixture.part_settingsPanel.filteredPages.map(page => page.id).join(","), "ai")
      compare(fixture.part_settingsPanel.currentPage, "ai")
      compare(fixture.part_settingsPanel.part_aiHub.section, "assistant")
      keyClick(Qt.Key_Down)
      compare(fixture.part_settingsPanel.navigationControl.activeFocus, true)
      keyClick(Qt.Key_Tab)
      const assistantSection = findChild(fixture.part_settingsPanel, "aiAssistantSection")
      const quickSection = findChild(fixture.part_settingsPanel, "aiQuickSection")
      tryCompare(assistantSection, "activeFocus", true)
      keyClick(Qt.Key_Right)
      tryCompare(quickSection, "activeFocus", true)
      keyClick(Qt.Key_Space)
      compare(fixture.part_settingsPanel.part_aiHub.section, "quick")
      keyClick(Qt.Key_Left)
      tryCompare(assistantSection, "activeFocus", true)
      keyClick(Qt.Key_Return)
      compare(fixture.part_settingsPanel.part_aiHub.section, "assistant")
      mouseClick(quickSection)
      compare(fixture.part_settingsPanel.part_aiHub.section, "quick")
      mouseClick(assistantSection)
      compare(fixture.part_settingsPanel.part_aiHub.section, "assistant")
      keyClick(Qt.Key_F, Qt.ControlModifier)
      typeText("conversation retention")
      compare(fixture.part_settingsPanel.currentPage, "ai")
      compare(fixture.part_settingsPanel.pageCards.assistant.filter(card => card.matchesSearch).length, 1)
      verify(fixture.part_settingsPanel.agentRetentionControl.visible)
      verify(!fixture.part_settingsPanel.agentEnableControl.visible)
      keyClick(Qt.Key_F, Qt.ControlModifier)
      typeText("no-such-setting")
      compare(fixture.part_settingsPanel.filteredPages.length, 0)
      keyClick(Qt.Key_Return)
      verify(fixture.part_settingsPanel.searchControl.activeFocus)
      keyClick(Qt.Key_Tab)
      tryCompare(fixture.part_settingsPanel.closeControl, "activeFocus", true)
      keyClick(Qt.Key_Backtab)
      tryCompare(fixture.part_settingsPanel.searchControl, "activeFocus", true)
      keyClick(Qt.Key_Escape)
      compare(fixture.part_settingsPanel.filteredPages.length, 6)
      verify(fixture.part_settingsPanel.searchControl.activeFocus)
    }

    function test_settings_search_works_with_collapsed_page_picker() {
      fixture.part_settingsPanel.theme = fixture.part_compactSettingsTheme
      fixture.width = 480
      fixture.part_settingsPanel.visible = true
      tryCompare(fixture.part_settingsPanel.searchControl, "activeFocus", true)
      verify(fixture.part_settingsPanel.railCollapsed)
      typeText("model")
      keyClick(Qt.Key_Return)
      tryCompare(fixture.part_settingsPanel.pagePickerControl, "activeFocus", true)
      keyClick(Qt.Key_PageDown, Qt.ControlModifier)
      compare(fixture.part_settingsPanel.currentPage, "ai")
      compare(fixture.part_settingsPanel.pagePickerControl.currentIndex, 0)
      keyClick(Qt.Key_PageUp, Qt.ControlModifier)
      compare(fixture.part_settingsPanel.currentPage, "ai")
      keyClick(Qt.Key_F, Qt.ControlModifier)
      typeText("opacity")
      compare(fixture.part_settingsPanel.currentPage, "customization")
      compare(fixture.part_settingsPanel.pagePickerControl.currentIndex, 0)
      keyClick(Qt.Key_Escape)
      compare(fixture.part_settingsPanel.filteredPages.length, 6)
      keyClick(Qt.Key_Escape)
      verify(!fixture.part_settingsPanel.visible)
      fixture.part_settingsPanel.visible = true
      tryCompare(fixture.part_settingsPanel.searchControl, "activeFocus", true)
      compare(fixture.part_settingsPanel.searchControl.text, "")
      typeText("retention")
      compare(fixture.part_settingsPanel.currentPage, "ai")
      mouseClick(fixture.part_settingsPanel.closeControl)
      verify(!fixture.part_settingsPanel.visible)
      fixture.part_settingsPanel.visible = true
      tryCompare(fixture.part_settingsPanel.searchControl, "activeFocus", true)
      compare(fixture.part_settingsPanel.searchControl.text, "")
    }

    function test_settings_acknowledgement_and_rollback_use_real_events() {
      const baseline = JSON.parse(JSON.stringify(fixture.part_settingsPanel.snapshot))
      fixture.part_settingsPanel.queueChanges = true
      fixture.part_settingsPanel.visible = true
      tryCompare(fixture.part_settingsPanel, "activeFocus", true)
      compare(fixture.part_settingsPanel.Accessible.role, Accessible.Dialog)
      for (let index = 0; index < 20; index++) keyClick(Qt.Key_Tab)
      verify(fixture.part_settingsPanel.activeFocus)
      keyClick(Qt.Key_Backtab)
      verify(fixture.part_settingsPanel.activeFocus)
      mouseClick(fixture, 5, 5)
      compare(fixture.part_backgroundTarget.clicks, 0)
      keyClick(Qt.Key_Escape)
      compare(fixture.part_settingsPanel.dismissals, 1)

      fixture.part_settingsPanel.visible = true
      fixture.part_settingsPanel.showPage("search")
      const firstRow = fixture.part_settingsPanel.renderedSourceAt(0)
      const initialSearchState = firstRow.searchToggleControl.checked
      mouseClick(firstRow.searchToggleControl)
      verify(fixture.part_settingsPersonalization.dispatched !== null)
      compare(fixture.part_settingsPersonalization.dispatched.operation, "preferences.update")
      compare(fixture.part_settingsPersonalization.dispatched.preferences.sources[0].enabledInSearch,
        !initialSearchState)
      const toggleRequestId = fixture.part_settingsPersonalization.dispatched.requestId
      fixture.part_settingsPersonalization.acceptPreferences(fixture.part_settingsPersonalization.dispatched.preferences)
      fixture.part_settingsPersonalization.settle(toggleRequestId)
      compare(fixture.part_settingsPanel.renderedSourceAt(0).searchToggleControl.checked,
        !initialSearchState)

      fixture.part_settingsPersonalization.dispatched = null
      mouseClick(fixture.part_settingsPanel.renderedSourceAt(0).moveDownControl)
      compare(fixture.part_settingsPanel.changedPreferences.sources[0].sourceId, "files")
      compare(fixture.part_settingsPanel.renderedSourceLabelAt(0), "Files")
      const reorderRequestId = fixture.part_settingsPersonalization.dispatched.requestId
      fixture.part_settingsPersonalization.rejectPreferences("reorder rejected")
      fixture.part_settingsPersonalization.settle(reorderRequestId)
      compare(fixture.part_settingsPersonalization.rollbackError, "reorder rejected")
      compare(fixture.part_settingsPanel.renderedSourceLabelAt(0), "Applications")
      fixture.part_settingsPanel.snapshot = baseline
    }

    function test_source_edits_send_revisioned_patches_after_acknowledgement() {
      const baseline = JSON.parse(JSON.stringify(fixture.part_settingsPanel.snapshot))
      const preferences = JSON.parse(JSON.stringify(baseline.preferences))
      preferences.registryRevision = 3
      preferences.preferenceRevision = 7
      fixture.part_settingsPersonalization.resetPreferences(preferences)
      fixture.part_settingsPanel.queueChanges = true
      fixture.part_settingsPanel.visible = true
      tryCompare(fixture.part_settingsPanel.searchControl, "activeFocus", true)
      typeText("motion")
      compare(fixture.part_settingsPanel.currentPage, "search")
      keyClick(Qt.Key_Escape)
      // Clearing the search re-lays out every card; click where the row settles.
      verify(waitForPolish(fixture.Window.window))
      const firstRow = fixture.part_settingsPanel.renderedSourceAt(0)
      verify(waitForRendering(firstRow.searchToggleControl))
      mouseClick(firstRow.searchToggleControl)
      const first = fixture.part_settingsPersonalization.dispatched
      verify(first.sourcePatch !== undefined, "Source toggle dispatched " + JSON.stringify(first))
      verify(first.preferences === undefined)
      compare(first.sourcePatch.registryRevision, 3)
      compare(first.sourcePatch.preferenceRevision, 7)
      compare(first.sourcePatch.changes.length, 1)
      const acknowledged = JSON.parse(JSON.stringify(fixture.part_settingsPersonalization.draft))
      acknowledged.preferenceRevision = 8
      // A second user change is queued while the first save is in flight.
      verify(waitForPolish(fixture.Window.window))
      const secondToggle = fixture.part_settingsPanel.renderedSourceAt(1).searchToggleControl
      verify(waitForRendering(secondToggle))
      mouseClick(secondToggle)
      compare(fixture.part_settingsPersonalization.dispatched.requestId, first.requestId)
      fixture.part_settingsPersonalization.acceptPreferences(acknowledged)
      fixture.part_settingsPersonalization.settle(first.requestId)
      const next = fixture.part_settingsPersonalization.dispatched
      verify(next.requestId !== first.requestId)
      compare(next.sourcePatch.preferenceRevision, 8)
      compare(next.sourcePatch.changes.length, 1)
      compare(next.sourcePatch.changes[0].sourceId, preferences.sources[1].sourceId)
      fixture.part_settingsPersonalization.rejectPreferences("The source registry changed")
      fixture.part_settingsPersonalization.settle(next.requestId)
      compare(fixture.part_settingsPersonalization.rollbackError, "The source registry changed")
      keyClick(Qt.Key_Escape)
      verify(!fixture.part_settingsPanel.visible)
      fixture.part_settingsPanel.snapshot = baseline
    }

    function test_home_folder_scope_persists_and_rolls_back() {
      const baseline = JSON.parse(JSON.stringify(fixture.part_settingsPanel.snapshot))
      fixture.part_settingsPanel.visible = true
      fixture.part_settingsPanel.showPage("search")
      fixture.part_settingsPanel.queueChanges = true
      const toggle = fixture.part_settingsPanel.homeFolderOnlyControl
      wait(0)
      toggle.forceActiveFocus()
      verify(toggle.activeFocus)
      verify(!toggle.checked)
      keyClick(Qt.Key_Space)
      compare(fixture.part_settingsPersonalization.dispatched.operation, "preferences.update")
      compare(fixture.part_settingsPersonalization.dispatched.preferences.searchHomeOnly, true)
      const requestId = fixture.part_settingsPersonalization.dispatched.requestId
      fixture.part_settingsPersonalization.acceptPreferences(fixture.part_settingsPersonalization.dispatched.preferences)
      fixture.part_settingsPersonalization.settle(requestId)
      verify(toggle.checked)
      fixture.part_settingsPersonalization.dispatched = null
      keyClick(Qt.Key_Space)
      compare(fixture.part_settingsPersonalization.dispatched.preferences.searchHomeOnly, false)
      const rejectedId = fixture.part_settingsPersonalization.dispatched.requestId
      fixture.part_settingsPersonalization.rejectPreferences("scope rejected")
      fixture.part_settingsPersonalization.settle(rejectedId)
      verify(toggle.checked)
      compare(fixture.part_settingsPersonalization.rollbackError, "scope rejected")
      fixture.part_settingsPanel.snapshot = baseline
    }

    function test_configured_and_unavailable_shortcuts_render() {
      const baseline = JSON.parse(JSON.stringify(fixture.part_settingsPanel.snapshot))
      fixture.part_settingsPanel.snapshot = {
        preferences: baseline.preferences,
        shortcuts: [
          { label: "Open CommandDeck", status: "configured", chords: ["SUPER + Space"] },
          { label: "Open System Menu", status: "not-configured", chords: [],
            detail: "hyprctl was not found" }
        ],
        customizations: []
      }
      fixture.part_settingsPanel.visible = true
      compare(fixture.part_settingsPanel.renderedShortcutCount, 2)
      compare(fixture.part_settingsPanel.renderedShortcutStatusAt(0), "SUPER + Space")
      verify(fixture.part_settingsPanel.renderedShortcutStatusAt(1).indexOf("hyprctl was not found") >= 0)
      verify(fixture.part_settingsPanel.renderedShortcutAccessibleNameAt(0).indexOf("SUPER + Space") >= 0)
      verify(fixture.part_settingsPanel.renderedShortcutAccessibleNameAt(1)
        .indexOf("Not configured") >= 0)
      fixture.part_settingsPanel.snapshot = baseline
    }

    function test_filter_alias_settings_acknowledge_and_roll_back_with_real_events() {
      fixture.part_settingsPanel.visible = true
      fixture.part_settingsPanel.showPage("filters")
      wait(0)
      fixture.part_settingsPanel.filterAliasInputControl.forceActiveFocus()
      tryVerify(function() { return fixture.part_settingsPanel.filterAliasInputControl.activeFocus })
      typeText("docs")
      fixture.part_settingsPanel.filterAliasTargetControl.forceActiveFocus()
      keyClick(Qt.Key_Down)
      mouseClick(fixture.part_settingsPanel.filterAliasSaveControl)
      compare(fixture.part_settingsPanel.filterAliasSaved, "docs")
      compare(fixture.part_settingsPanel.filterAliasTargetSaved, "files")

      fixture.part_settingsPanel.filterAliases = [{ alias: "docs", filterId: "files" }]
      compare(fixture.part_settingsPanel.renderedFilterAliasCount, 1)
      mouseClick(fixture.part_settingsPanel.renderedFilterAliasAt(0).removeControl)
      compare(fixture.part_settingsPanel.filterAliasRemoved, "docs")

      fixture.part_settingsPanel.filterAliasError = "alias conflict"
      compare(fixture.part_settingsPanel.renderedFilterAliasCount, 1)
      compare(fixture.part_settingsPanel.filterAliases[0].alias, "docs")
    }

    function test_saved_search_settings_use_real_events() {
      fixture.part_settingsPanel.visible = true
      fixture.part_settingsPanel.showPage("filters")
      fixture.part_settingsPanel.savedSearches = []
      fixture.part_settingsPanel.savedSearchNameControl.text = "Roadmap files"
      fixture.part_settingsPanel.savedSearchShortcutControl.text = "Ctrl+Alt+R"
      fixture.part_settingsPanel.savedSearchNameControl.forceActiveFocus()
      keyClick(Qt.Key_Return)
      compare(fixture.part_settingsPanel.savedSearchSaved.name, "Roadmap files")
      compare(fixture.part_settingsPanel.savedSearchSaved.shortcut, "Ctrl+Alt+R")
      compare(fixture.part_settingsPanel.savedSearchSaved.expression.includeFilterIds[0], "files")

      const persisted = {
        id: "saved-search:roadmap",
        name: "Roadmap files",
        shortcut: "CTRL+ALT+R",
        showOnHome: true,
        position: 1,
        expression: fixture.part_settingsPanel.currentExpression
      }
      fixture.part_settingsPanel.savedSearches = [persisted]
      compare(fixture.part_settingsPanel.renderedSavedSearchCount, 1)
      fixture.part_settingsPanel.renderedSavedSearchAt(0).runControl.clicked()
      compare(fixture.part_settingsPanel.savedSearchRan.id, persisted.id)
      fixture.part_settingsPanel.renderedSavedSearchAt(0).editControl.clicked()
      compare(fixture.part_settingsPanel.savedSearchNameControl.text, "Roadmap files")
      fixture.part_settingsPanel.renderedSavedSearchAt(0).deleteControl.clicked()
      compare(fixture.part_settingsPanel.savedSearchDeleted.id, persisted.id)
      fixture.part_settingsPanel.clearSavedSearchEditor()
      fixture.part_settingsPanel.savedSearches = []
    }

    function test_quarantined_saved_search_allows_only_delete() {
      fixture.part_settingsPanel.visible = true
      tryCompare(fixture.part_settingsPanel.searchControl, "activeFocus", true)
      typeText("saved searches")
      fixture.part_settingsPanel.savedSearches = [{
        id: "saved-search:quarantined", name: "Unsupported clipboard saved search",
        unavailable: true, expression: {}, shortcut: null, showOnHome: false, position: 1
      }]
      const row = fixture.part_settingsPanel.renderedSavedSearchAt(0)
      verify(row !== null)
      verify(!row.runControl.enabled)
      verify(!row.editControl.enabled)
      verify(row.deleteControl.enabled)
      keyClick(Qt.Key_Return)
      for (let index = 0; index < 20 && !row.deleteControl.activeFocus; index++)
        keyClick(Qt.Key_Tab)
      verify(row.deleteControl.activeFocus)
      keyClick(Qt.Key_Space)
      compare(fixture.part_settingsPanel.savedSearchDeleted.id, "saved-search:quarantined")
      fixture.part_settingsPanel.savedSearchDeleted = null
      mouseClick(row.deleteControl)
      compare(fixture.part_settingsPanel.savedSearchDeleted.id, "saved-search:quarantined")
      fixture.part_settingsPanel.savedSearches = []
    }

    function test_editor_adapter_settings_acknowledge_and_validate() {
      fixture.part_settingsPanel.visible = true
      fixture.part_settingsPanel.showPage("editors")
      fixture.part_settingsPanel.graphicalEditorExecutableControl.text = "/tmp/fake editor"
      fixture.part_settingsPanel.graphicalEditorArgumentsControl.text =
        '["--goto","{path}:{line}:{column}"]'
      fixture.part_settingsPanel.preferredEditorControl.currentIndex = 0
      fixture.part_settingsPanel.editorSaveControl.clicked()
      compare(fixture.part_settingsPanel.changedPreferences.preferredEditor, "graphical")
      compare(fixture.part_settingsPanel.changedPreferences.graphicalEditor.executable,
        "/tmp/fake editor")
      compare(fixture.part_settingsPanel.changedPreferences.graphicalEditor.arguments[1],
        "{path}:{line}:{column}")

      const acknowledged = fixture.part_settingsPanel.changedPreferences
      fixture.part_settingsPanel.graphicalEditorArgumentsControl.text = "not json"
      verify(!fixture.part_settingsPanel.saveEditors())
      compare(fixture.part_settingsPanel.changedPreferences, acknowledged)
      fixture.part_settingsPanel.syncEditorControls()
    }

    function test_removed_fallback_is_visible_and_normalized_on_edit() {
      const baseline = JSON.parse(JSON.stringify(fixture.part_settingsPanel.snapshot))
      fixture.part_settingsPanel.fallbackQuicklinks = [{ id: "web", title: "Web" }]
      fixture.part_settingsPanel.snapshot = {
        preferences: {
          sources: baseline.preferences.sources,
          reduceMotion: false,
          webFallbackQuicklinkId: "removed-web"
        },
        shortcuts: [],
        customizations: []
      }
      fixture.part_settingsPanel.visible = true
      fixture.part_settingsPanel.showPage("search")
      verify(fixture.part_settingsPanel.fallbackSelectionUnavailable)
      verify(fixture.part_settingsPanel.fallbackDisplayText.indexOf("removed-web") >= 0)
      mouseClick(fixture.part_settingsPanel.renderedSourceAt(0).searchToggleControl)
      compare(fixture.part_settingsPanel.changedPreferences.webFallbackQuicklinkId, null)
      fixture.part_settingsPanel.snapshot = baseline
      fixture.part_settingsPanel.fallbackQuicklinks = []
    }
  }
}
