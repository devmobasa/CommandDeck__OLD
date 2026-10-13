import QtQuick
import QtTest
import "../../qml/common" as CommandDeck
import "../../qml/common/Protocol.js" as Protocol
import "../../qml/common/UiConstants.js" as Ui

EventPathFixture {
  id: scene
  Component {
    id: inspectorComponent
    CommandDeck.IndexInspector { }
  }
  EventPathTestCase {
    fixture: scene
    name: "CommandDeck_search_actions"
    function test_help_sheet_traps_focus_and_handles_its_shortcuts() {
      fixture.part_searchField.inputControl.forceActiveFocus()
      verify(fixture.part_searchField.inputControl.activeFocus)

      fixture.part_eventHelpSheet.opened = true
      tryVerify(function() { return fixture.part_eventHelpSheet.activeFocus })

      keyClick(Qt.Key_Tab)
      verify(fixture.part_eventHelpSheet.activeFocus)
      keyClick(Qt.Key_Backtab)
      verify(fixture.part_eventHelpSheet.activeFocus)

      keyClick(Qt.Key_Slash, Qt.ControlModifier)
      compare(fixture.part_eventHelpSheet.dismissals, 1)
      verify(!fixture.part_eventHelpSheet.opened)

      fixture.part_eventHelpSheet.opened = true
      tryVerify(function() { return fixture.part_eventHelpSheet.activeFocus })
      keyClick(Qt.Key_F1)
      compare(fixture.part_eventHelpSheet.dismissals, 2)

      fixture.part_eventHelpSheet.opened = true
      tryVerify(function() { return fixture.part_eventHelpSheet.activeFocus })
      keyClick(Qt.Key_Escape)
      compare(fixture.part_eventHelpSheet.dismissals, 3)
    }

    function test_filter_palette_traps_navigation_and_inserts_with_real_events() {
      fixture.part_searchField.inputControl.forceActiveFocus()
      verify(fixture.part_searchField.inputControl.activeFocus)

      fixture.part_eventFilterPalette.inserted = ""
      fixture.part_eventFilterPalette.dismissals = 0
      fixture.part_eventFilterPalette.selectedIndex = 0
      fixture.part_eventFilterPalette.opened = true
      tryVerify(function() { return fixture.part_eventFilterPalette.activeFocus })
      compare(fixture.part_eventFilterPalette.selectedIndex, 0)

      // Arrow keys move the highlight and stay trapped inside the palette.
      keyClick(Qt.Key_Down)
      compare(fixture.part_eventFilterPalette.selectedIndex, 1)
      compare(fixture.part_eventFilterPalette.selectedToken(), "app:")
      verify(fixture.part_eventFilterPalette.activeFocus)
      keyClick(Qt.Key_Up)
      compare(fixture.part_eventFilterPalette.selectedIndex, 0)

      // Enter inserts the highlighted token.
      keyClick(Qt.Key_Down)
      keyClick(Qt.Key_Return)
      compare(fixture.part_eventFilterPalette.inserted, "app:")

      // Esc closes the palette.
      fixture.part_eventFilterPalette.opened = true
      tryVerify(function() { return fixture.part_eventFilterPalette.activeFocus })
      keyClick(Qt.Key_Escape)
      compare(fixture.part_eventFilterPalette.dismissals, 1)
      verify(!fixture.part_eventFilterPalette.opened)
    }

    function test_first_run_coach_dismisses_on_any_key_with_real_events() {
      fixture.part_searchField.inputControl.forceActiveFocus()
      fixture.part_eventFirstRunCoach.dismissals = 0
      fixture.part_eventFirstRunCoach.seen = false
      fixture.part_eventFirstRunCoach.suppressed = false
      fixture.part_eventFirstRunCoach.opened = true
      verify(fixture.part_eventFirstRunCoach.shouldShow)
      tryVerify(function() { return fixture.part_eventFirstRunCoach.activeFocus })
      keyClick(Qt.Key_A)
      compare(fixture.part_eventFirstRunCoach.dismissals, 1)
      verify(!fixture.part_eventFirstRunCoach.opened)
    }

    function test_action_search_abbreviations_accents_and_identity() {
      const target = {
        title: "Report",
        actions: [
          { id: "copy-name", title: "Copy Name" },
          { id: "copy-path", title: "Copy Path" },
          { id: "reveal", title: "Reveal in File Manager" },
          { id: "resume", title: "Résumé" },
          { id: "copy", title: "Copy" }
        ]
      }
      fixture.part_actionPanel.openFor(target)
      fixture.part_actionPanel.filterControl.forceActiveFocus()
      typeText("cpy pth")
      compare(fixture.part_actionPanel.visibleActionCount, 1)
      compare(fixture.part_actionPanel.filteredActions[0].id, "copy-path")
      keyClick(Qt.Key_Return)
      compare(fixture.part_actionPanel.chosen, "copy-path")
      fixture.part_actionPanel.filterText = "rfm"
      compare(fixture.part_actionPanel.filteredActions[0].id, "reveal")
      fixture.part_actionPanel.filterText = "RESUME"
      compare(fixture.part_actionPanel.visibleActionCount, 1)
      compare(fixture.part_actionPanel.filteredActions[0].id, "resume")
      fixture.part_actionPanel.filterText = "copy"
      compare(fixture.part_actionPanel.filteredActions[0].id, "copy")
      fixture.part_actionPanel.selectedIndex = 2
      const selected = fixture.part_actionPanel.filteredActions[2].id
      fixture.part_actionPanel.filterText = "cpy"
      compare(fixture.part_actionPanel.filteredActions[fixture.part_actionPanel.selectedIndex].id, selected)
      fixture.part_actionPanel.filterText = "zzzz"
      compare(fixture.part_actionPanel.visibleActionCount, 0)
      fixture.part_actionPanel.chosen = ""
      keyClick(Qt.Key_Return)
      compare(fixture.part_actionPanel.chosen, "")
      fixture.part_actionPanel.filterText = ""
      compare(fixture.part_actionPanel.visibleActionCount, target.actions.length)
      compare(fixture.part_actionPanel.filteredActions[0].id, "copy-name")
    }

    function test_direct_shortcuts_do_not_open_menu_or_replace_text_editing() {
      fixture.part_searchField.directActions = [
        { id: "reveal" }, { id: "copy-path" }, { id: "favorite" }
      ]
      fixture.part_searchField.inputControl.forceActiveFocus()
      const menus = fixture.part_searchField.actionRequests
      const primaries = fixture.part_controller.primaryCalls
      keyClick(Qt.Key_Return, Qt.ControlModifier)
      compare(fixture.part_searchField.shortcutId, "reveal")
      keyClick(Qt.Key_C, Qt.ControlModifier | Qt.ShiftModifier)
      compare(fixture.part_searchField.shortcutId, "copy-path")
      keyClick(Qt.Key_D, Qt.ControlModifier)
      compare(fixture.part_searchField.shortcutId, "favorite")
      compare(fixture.part_searchField.actionRequests, menus)
      compare(fixture.part_controller.primaryCalls, primaries)
      fixture.part_searchField.shortcutId = ""
      keyClick(Qt.Key_C, Qt.ControlModifier)
      compare(fixture.part_searchField.shortcutId, "")
      keyClick(Qt.Key_K, Qt.ControlModifier)
      compare(fixture.part_searchField.actionRequests, menus + 1)
      fixture.part_searchField.directActions = []
      keyClick(Qt.Key_D, Qt.ControlModifier)
      compare(fixture.part_searchField.shortcutId, "")
    }

    function test_footer_direct_actions_click_tab_and_forward_shortcuts() {
      const actions = [
        { id: "reveal", title: "Reveal", keyLabel: "Ctrl+↵" },
        { id: "copy-path", title: "Copy Path", keyLabel: "Ctrl+Shift+C" }
      ]
      fixture.part_footerActions.directActions = actions
      fixture.part_searchField.directActions = actions
      wait(30)
      mouseClick(fixture.part_footerActions.firstShortcutControl)
      compare(fixture.part_footerActions.shortcutId, "reveal")
      fixture.part_footerActions.primaryControl.forceActiveFocus()
      keyClick(Qt.Key_Tab)
      verify(fixture.part_footerActions.firstShortcutControl.activeFocus)
      keyClick(Qt.Key_Tab)
      verify(fixture.part_footerActions.secondShortcutControl.activeFocus)
      keyClick(Qt.Key_Return)
      compare(fixture.part_footerActions.shortcutId, "copy-path")
      keyClick(Qt.Key_Return, Qt.ControlModifier)
      compare(fixture.part_searchField.shortcutId, "reveal")
      keyClick(Qt.Key_Tab)
      verify(fixture.part_footerActions.actionsControl.activeFocus)
      fixture.part_footerActions.availableWidth = fixture.part_footerActions.primaryControl.implicitWidth
        + fixture.part_footerActions.firstShortcutControl.implicitWidth + fixture.part_footerActions.actionsControl.implicitWidth
        + fixture.part_footerActions.spacing * 3 + fixture.part_footerActions.discoveryWidth + 1
      wait(30)
      verify(fixture.part_footerActions.firstShortcutControl.visible)
      verify(!fixture.part_footerActions.secondShortcutControl.visible)
      verify(fixture.part_footerActions.width <= fixture.part_footerActions.availableWidth)
    }

    function test_health_indicator_counts_missing_roots_once() {
      const banner = createTemporaryObject(fixture.part_runtimeBannerComponent, fixture, {
        theme: fixture.part_theme,
        status: { inspection: { failedItemCount: 2, staleItemCount: 0, pendingItemCount: 0 },
          indexing: { unwatchedRoots: ["/missing"], roots: [
            { path: "/missing", watched: false, lastError: "Directory is missing" }
          ] } }
      })
      compare(banner.summary, "3 index issues")
      verify(banner.details.indexOf("Directory is missing") >= 0)
      mouseClick(banner)
      compare(banner.inspectorRequests, 1)
    }

    function test_inspector_explains_file_and_root_issue_total() {
      const inspection = { stats: { itemCount: 18259 }, totalExclusionDiagnosticCount: 21,
        failedItemCount: 2, staleItemCount: 0, pendingItemCount: 0,
        workItems: [{ path: "/one.zip", state: "failed", reason: "archive-invalid-or-limit" },
          { path: "/two.zip", state: "failed", reason: "archive-invalid-or-limit" }] }
      const banner = createTemporaryObject(fixture.part_runtimeBannerComponent, fixture, {
        theme: fixture.part_theme,
        status: { inspection: inspection, indexing: { unwatchedRoots: ["/missing"], roots: [
          { path: "/missing", watched: false, lastError: "Directory is missing" }
        ] } }
      })
      const inspector = createTemporaryObject(inspectorComponent, fixture, {
        theme: fixture.part_theme, width: 900, height: 700, inspection: inspection,
        healthSummary: Qt.binding(() => banner.summary), healthDetails: Qt.binding(() => banner.details)
      })
      banner.inspectorRequested.connect(() => inspector.opened = true)
      inspector.dismissed.connect(() => inspector.opened = false)
      waitForRendering(banner)
      mouseClick(banner)
      tryCompare(inspector, "opened", true)
      compare(inspector.heading, "Index inspector · 3 index issues")
      compare(inspector.visibleWorkItemCount, 2)
      verify(inspector.healthDetails.indexOf("Index root unavailable") >= 0)
      tryVerify(() => inspector.activeFocus)
      keyClick(Qt.Key_Escape)
      tryCompare(inspector, "opened", false)
      mouseClick(banner)
      tryCompare(inspector, "opened", true)
      compare(inspector.heading, "Index inspector · 3 index issues")
    }

    function test_search_navigation_and_action_shortcuts() {
      fixture.part_searchField.inputControl.forceActiveFocus()
      keyClick(Qt.Key_Down)
      keyClick(Qt.Key_N, Qt.ControlModifier)
      compare(fixture.part_controller.selectedResult().id, "app:terminal")
      keyClick(Qt.Key_Return, Qt.ControlModifier)
      compare(fixture.part_searchField.actionRequests, 1)
    }

    function test_filter_chip_and_backspace_use_real_input_events() {
      fixture.part_filterChip.visible = true
      fixture.part_filterChip.forceActiveFocus()
      keyClick(Qt.Key_Space)
      compare(fixture.part_filterChip.removals, 1)
      mouseClick(fixture.part_filterChip)
      compare(fixture.part_filterChip.removals, 2)

      fixture.part_controller.activeFilters = [{ id: "files", excluded: false }]
      fixture.part_searchField.inputControl.text = ""
      fixture.part_searchField.inputControl.cursorPosition = 0
      fixture.part_searchField.inputControl.forceActiveFocus()
      keyClick(Qt.Key_Backspace)
      compare(fixture.part_controller.activeFilterId, "")
    }

    function test_herdr_tab_prefix_becomes_a_chip_from_real_key_events() {
      fixture.part_searchField.inputControl.text = ""
      fixture.part_searchField.inputControl.forceActiveFocus()
      typeText("htab: tests")
      compare(fixture.part_searchField.filterChipCount, 1)
      compare(fixture.part_controller.activeFilters[0].id, "herdr-tabs")
      compare(fixture.part_controller.activeFilters[0].excluded, false)
      compare(fixture.part_searchField.filterChipAt(0).Accessible.name, "Herdr tabs filter, remove")
      compare(fixture.part_searchField.inputControl.text.trim(), "tests")
      verify(fixture.part_searchField.inputControl.activeFocus)

      fixture.part_searchField.filterChipAt(0).forceActiveFocus()
      keyClick(Qt.Key_Space)
      tryCompare(fixture.part_searchField, "filterChipCount", 0)
      fixture.part_searchField.inputControl.text = ""
      fixture.part_searchField.inputControl.forceActiveFocus()
      typeText("-htabs: release")
      compare(fixture.part_searchField.filterChipCount, 1)
      compare(fixture.part_controller.activeFilters[0].id, "herdr-tabs")
      compare(fixture.part_controller.activeFilters[0].excluded, true)
      compare(fixture.part_searchField.inputControl.text.trim(), "release")

      fixture.part_searchField.filterChipAt(0).forceActiveFocus()
      keyClick(Qt.Key_Space)
      tryCompare(fixture.part_searchField, "filterChipCount", 0)
      fixture.part_searchField.inputControl.text = ""
      compare(fixture.part_controller.activeFilterId, "")
    }

    function test_multiple_positive_and_negative_chips_use_real_input_events() {
      fixture.part_searchField.inputControl.forceActiveFocus()
      typeText("file: ")
      typeText("-ws: roadmap")
      compare(fixture.part_searchField.filterChipCount, 2)
      compare(fixture.part_controller.activeFilters[0].id, "files")
      compare(fixture.part_controller.activeFilters[0].excluded, false)
      compare(fixture.part_controller.activeFilters[1].id, "workspaces")
      compare(fixture.part_controller.activeFilters[1].excluded, true)
      compare(fixture.part_searchField.filterChipAt(1).Accessible.name, "Not Workspaces filter, remove")

      fixture.part_searchField.filterChipAt(0).forceActiveFocus()
      keyClick(Qt.Key_Space)
      tryCompare(fixture.part_searchField, "filterChipCount", 1)
      fixture.part_searchField.inputControl.text = ""
      fixture.part_searchField.inputControl.cursorPosition = 0
      fixture.part_searchField.inputControl.forceActiveFocus()
      keyClick(Qt.Key_Backspace)
      tryCompare(fixture.part_searchField, "filterChipCount", 0)
    }

    function test_search_focus_order_and_accessibility_states() {
      fixture.part_controller.activeFilters = [{ id: "files", excluded: false }]
      fixture.part_controller.selectedIndex = -1
      fixture.part_searchField.inputControl.forceActiveFocus()
      compare(fixture.part_searchField.inputControl.Accessible.role, Accessible.EditableText)
      keyClick(Qt.Key_Tab)
      compare(fixture.part_searchField.settingsControl.activeFocus, true)
      keyClick(Qt.Key_Tab)
      compare(fixture.part_searchField.filterChipControl.activeFocus, true)
      compare(fixture.part_searchField.filterChipControl.Accessible.role, Accessible.Button)
      keyClick(Qt.Key_Tab)
      compare(fixture.part_searchField.inputControl.activeFocus, true)

      fixture.part_actionPanel.openFor({
        title: "Browser",
        canCustomize: false,
        actions: [{ id: "launch", title: "Launch", kind: "launch-desktop" }]
      })
      compare(fixture.part_actionPanel.Accessible.role, Accessible.Pane)
      const actionRow = fixture.part_actionPanel.actionItemAt(0)
      compare(actionRow.Accessible.role, Accessible.ListItem)
      compare(actionRow.Accessible.selected, true)

      // The panel clips and grows with its action count, so a result with many
      // actions scrolls inside the panel instead of spilling over other UI.
      verify(fixture.part_actionPanel.clip)
      const oneActionHeight = fixture.part_actionPanel.implicitHeight
      fixture.part_actionPanel.openFor({
        title: "Image",
        canCustomize: false,
        actions: [
          { id: "a1", title: "Open" }, { id: "a2", title: "Reveal" },
          { id: "a3", title: "Copy Path" }, { id: "a4", title: "Copy URL" },
          { id: "a5", title: "Open With A" }, { id: "a6", title: "Open With B" },
          { id: "a7", title: "Open With C" }, { id: "a8", title: "Open With D" },
          { id: "a9", title: "Open With E" }, { id: "a10", title: "Open With F" },
          { id: "a11", title: "Open With G" }, { id: "a12", title: "Open With H" }
        ]
      })
      compare(fixture.part_actionPanel.visibleActionCount, 12)
      verify(fixture.part_actionPanel.implicitHeight > oneActionHeight)
      verify(fixture.part_actionPanel.implicitHeight >= 12 * fixture.part_actionPanel.theme.actionRowHeight)
    }
  }
}
