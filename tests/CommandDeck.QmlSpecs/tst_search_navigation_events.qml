import QtQuick
import QtTest
import "../../qml/common" as CommandDeck
import "../../qml/common/Protocol.js" as Protocol
import "../../qml/common/UiConstants.js" as Ui

EventPathFixture {
  id: scene
  EventPathTestCase {
    fixture: scene
    name: "CommandDeck_search_navigation"
    function test_open_action_menu_keeps_its_result_during_promotion_refresh() {
      const signal = { id: "signal", title: "Signal", kind: "application",
        section: { id: "top-match", title: "Top Matches" },
        actions: [{ id: "launch", title: "Launch", kind: "launch-desktop" }] }
      const install = { id: "install", title: "Signal", kind: "omarchy",
        actions: [{ id: "install", title: "Install", kind: "run-argv" }] }
      fixture.part_controller.replaceResults([signal, install], true)
      fixture.part_actionPanel.openFor(fixture.part_controller.selectedResult())
      tryVerify(function() { return fixture.part_actionPanel.filterControl.activeFocus })
      fixture.part_controller.replaceResults([install], false)
      compare(fixture.part_controller.selectedResult().id, "install")
      compare(fixture.part_actionPanel.result.id, "signal")
      keyClick(Qt.Key_Return)
      compare(fixture.part_actionPanel.chosen, "launch")
      compare(fixture.part_actionPanel.chosenResultId, "signal")
      const dismissals = fixture.part_actionPanel.dismissals
      keyClick(Qt.Key_Escape)
      compare(fixture.part_actionPanel.dismissals, dismissals + 1)
    }

    function test_same_query_partial_refresh_waits_for_done_with_keyboard_selection() {
      const signal = { id: "signal", title: "Signal", kind: "application",
        section: { id: "top-match", title: "Top Matches" } }
      const install = { id: "install", title: "Signal", kind: "omarchy" }
      fixture.part_controller.replaceResults([install, signal], true)
      fixture.part_searchField.inputControl.forceActiveFocus()
      keyClick(Qt.Key_Down)
      compare(fixture.part_controller.selectedResult().id, "signal")
      compare(fixture.part_controller.replaceResults([install], false, false), false)
      compare(fixture.part_controller.selectedResult().id, "signal")
      keyClick(Qt.Key_Return)
      compare(fixture.part_controller.invokedId, "signal")
      fixture.part_controller.replaceResults([install, signal], false, true)
      compare(fixture.part_controller.selectedResult().id, "signal")
      keyClick(Qt.Key_Return)
      compare(fixture.part_controller.invokedId, "signal")

      // Deliberate navigation during the pending refresh remains authoritative.
      fixture.part_controller.replaceResults([install], false, false)
      keyClick(Qt.Key_Up)
      fixture.part_controller.replaceResults([signal, install], false, true)
      compare(fixture.part_controller.selectedResult().id, "install")
      // Only completion establishes that the selected result was removed.
      fixture.part_controller.replaceResults([signal], false, false)
      compare(fixture.part_controller.selectedResult().id, "install")
      fixture.part_controller.replaceResults([signal], false, true)
      compare(fixture.part_controller.selectedResult().id, "signal")
      fixture.part_controller.replaceResults([install, signal], true, false)
      compare(fixture.part_controller.selectedResult().id, "install")
    }

    function test_top_matches_keyboard_reorder_preserves_invocation_identity() {
      const signal = { id: "signal", title: "Signal", kind: "application", section: { id: "top-match", title: "Top Matches" } }
      const install = { id: "install", title: "Signal", kind: "omarchy", section: { id: "omarchy", title: "Omarchy" } }
      const file = { id: "notes", title: "Signal Notes", kind: "file", section: { id: "files", title: "Files" } }
      fixture.part_controller.replaceResults([signal, install, file], true)
      fixture.part_searchField.inputControl.forceActiveFocus()
      compare(fixture.part_controller.selectedResult().id, "signal")
      compare(fixture.part_controllerRows.get(0).ordinal, 0)
      keyClick(Qt.Key_Down)
      compare(fixture.part_controller.selectedResult().id, "install")
      keyClick(Qt.Key_Down)
      compare(fixture.part_controller.selectedResult().id, "notes")
      keyClick(Qt.Key_Up)
      compare(fixture.part_controller.selectedResult().id, "install")
      fixture.part_controller.replaceResults([file, signal, install], false)
      compare(fixture.part_controller.selectedResult().id, "install")
      keyClick(Qt.Key_Return)
      compare(fixture.part_controller.invokedId, "install")
      keyClick(Qt.Key_Home, Qt.ControlModifier)
      compare(fixture.part_controller.selectedResult().id, "notes")
      keyClick(Qt.Key_End, Qt.ControlModifier)
      compare(fixture.part_controller.selectedResult().id, "install")
      keyClick(Qt.Key_Space)
      compare(fixture.part_searchField.inputControl.text, " ")
      fixture.part_controller.replaceResults([signal, file], false)
      compare(fixture.part_controller.selectedResult().id, "signal")
      keyClick(Qt.Key_Return)
      compare(fixture.part_controller.invokedId, "signal")
      const dismissed = fixture.part_controller.dismissCalls
      keyClick(Qt.Key_Escape)
      compare(fixture.part_controller.dismissCalls, dismissed + 1)
    }

    function test_typed_query_uses_main_result_navigation() {
      fixture.part_searchField.inputControl.forceActiveFocus()
      typeText("ri")
      keyClick(Qt.Key_Down)
      compare(fixture.part_controller.selectedResult().id, "app:editor")
      keyClick(Qt.Key_Up)
      compare(fixture.part_controller.selectedResult().id, "app:browser")
      keyClick(Qt.Key_Return)
      compare(fixture.part_controller.primaryCalls, 1)
      compare(fixture.part_searchField.inputControl.text, "ri")
      const previousDismissals = fixture.part_controller.dismissCalls
      keyClick(Qt.Key_Escape)
      compare(fixture.part_controller.dismissCalls, previousDismissals + 1)
    }

    // Ctrl+Down reaches the first result of the next section; Ctrl+Up returns
    // to the start of the current section, then of the previous one. Neither
    // moves past the ends, and plain arrows still move one row.
    function test_control_arrows_jump_between_sections() {
      const row = (id, section) => ({ id: id, kind: "application", title: id,
        section: { id: section, title: section }, actions: [] })
      fixture.part_controller.replaceResults([row("a1", "first"), row("a2", "first"), row("b1", "second"),
        row("b2", "second"), row("b3", "second"), row("c1", "third")], true)
      fixture.part_searchField.inputControl.forceActiveFocus()
      compare(fixture.part_controller.selectedResult().id, "a1")

      keyClick(Qt.Key_Down, Qt.ControlModifier)
      compare(fixture.part_controller.selectedResult().id, "b1")
      keyClick(Qt.Key_Down)
      keyClick(Qt.Key_Down)
      compare(fixture.part_controller.selectedResult().id, "b3")
      keyClick(Qt.Key_Down, Qt.ControlModifier)
      compare(fixture.part_controller.selectedResult().id, "c1")
      keyClick(Qt.Key_Down, Qt.ControlModifier)
      compare(fixture.part_controller.selectedResult().id, "c1", "the last section stays put")

      keyClick(Qt.Key_Up)
      compare(fixture.part_controller.selectedResult().id, "b3")
      keyClick(Qt.Key_Up, Qt.ControlModifier)
      compare(fixture.part_controller.selectedResult().id, "b1", "start of the current section first")
      keyClick(Qt.Key_Up, Qt.ControlModifier)
      compare(fixture.part_controller.selectedResult().id, "a1")
      keyClick(Qt.Key_Up, Qt.ControlModifier)
      compare(fixture.part_controller.selectedResult().id, "a1", "the first section stays put")
      verify(fixture.part_searchField.inputControl.activeFocus)
      compare(fixture.part_controller.primaryCalls, 0)
    }

    // Up from the first result of an empty search asks for the newest recent
    // query; on a query left as recalled it asks for an older one. Anywhere
    // else Up still moves the selection, and edited text never recalls.
    function test_up_on_an_empty_or_recalled_search_recalls_history() {
      fixture.part_controller.replaceResults([{ id: "a", kind: "file", title: "A", actions: [] },
        { id: "b", kind: "file", title: "B", actions: [] }], true)
      fixture.part_searchField.inputControl.forceActiveFocus()
      keyClick(Qt.Key_Down)
      keyClick(Qt.Key_Up)
      compare(fixture.part_controller.selectedResult().id, "a", "Up below the first row moves")
      compare(fixture.part_searchField.recallRequests, [])
      keyClick(Qt.Key_Up)
      compare(fixture.part_searchField.recallRequests, [true], "the first row of an empty search recalls")
      compare(fixture.part_controller.selectedResult().id, "a")

      fixture.part_controller.recalledText = "notes"
      typeText("notes")
      keyClick(Qt.Key_Up)
      compare(fixture.part_searchField.recallRequests, [true, false], "a recalled query steps back")
      typeText("x")
      keyClick(Qt.Key_Up)
      compare(fixture.part_searchField.recallRequests, [true, false], "an edited query does not recall")
      keyClick(Qt.Key_Up, Qt.ShiftModifier)
      compare(fixture.part_searchField.recallRequests.length, 2, "Shift+Up stays with the text field")
      verify(fixture.part_searchField.inputControl.activeFocus)
      fixture.part_searchField.inputControl.text = ""
    }

    // Ctrl+= and Ctrl++ zoom in, Ctrl+- zooms out and Ctrl+0 returns to the
    // automatic size, from the main or keypad keys; none of them types text.
    function test_zoom_keys_ask_for_display_size_steps() {
      fixture.part_searchField.inputControl.forceActiveFocus()
      keyClick(Qt.Key_Equal, Qt.ControlModifier)
      keyClick(Qt.Key_Plus, Qt.ControlModifier | Qt.ShiftModifier)
      keyClick(Qt.Key_Minus, Qt.ControlModifier)
      keyClick(Qt.Key_Plus, Qt.ControlModifier | Qt.KeypadModifier)
      keyClick(Qt.Key_Minus, Qt.ControlModifier | Qt.KeypadModifier)
      keyClick(Qt.Key_0, Qt.ControlModifier)
      compare(fixture.part_searchField.zoomRequests, [1, 1, -1, 1, -1, 0])
      keyClick(Qt.Key_Equal)
      keyClick(Qt.Key_0)
      compare(fixture.part_searchField.zoomRequests.length, 6, "plain keys type")
      compare(fixture.part_searchField.inputControl.text, "=0")
      compare(fixture.part_controller.primaryCalls, 0)
      fixture.part_searchField.inputControl.text = ""
    }

    // Ctrl+U deletes from the start of the query to the caret, leaving the
    // rest and any filter chip, and the field keeps focus for typing.
    function test_control_u_deletes_the_query_before_the_caret() {
      fixture.part_controller.activeFilters = [{ id: "files", excluded: false }]
      fixture.part_searchField.inputControl.forceActiveFocus()
      typeText("old notes")
      compare(fixture.part_controller.queryText, "old notes")
      for (let step = 0; step < 5; step++) keyClick(Qt.Key_Left)
      keyClick(Qt.Key_U, Qt.ControlModifier)
      compare(fixture.part_searchField.inputControl.text, "notes")
      compare(fixture.part_controller.queryText, "notes")
      compare(fixture.part_searchField.inputControl.cursorPosition, 0)

      keyClick(Qt.Key_End)
      keyClick(Qt.Key_U, Qt.ControlModifier)
      compare(fixture.part_controller.queryText, "")
      compare(fixture.part_controller.activeFilters.length, 1, "the filter chip stays")
      typeText("new")
      compare(fixture.part_controller.queryText, "new")
      fixture.part_searchField.inputControl.text = ""
      fixture.part_controller.activeFilters = []
    }

    // A HEY refinement row names the whole query Tab produces; typing goes on
    // from there. Ordinary rows still complete to their title.
    function test_a_row_can_name_what_tab_completes() {
      fixture.part_searchField.inputControl.forceActiveFocus()
      typeText("invoice in:")
      const metadata = {}
      metadata[Protocol.ResultMetadataKeys.CompletionText] = "invoice in:imbox "
      fixture.part_controller.replaceResults([{ id: "hey:complete:in:imbox", sourceId: Protocol.SourceIds.Hey,
        kind: Protocol.ResultKinds.Status, title: "in:imbox", metadata: metadata, actions: [] }], true)
      keyClick(Qt.Key_Tab)
      compare(fixture.part_searchField.inputControl.text, "invoice in:imbox ")
      typeText("lunch")
      compare(fixture.part_controller.queryText, "invoice in:imbox lunch")
      verify(fixture.part_searchField.inputControl.activeFocus)

      fixture.part_controller.replaceResults([{ id: "app:browser", sourceId: "applications", kind: "application",
        title: "Browser", actions: [] }], true)
      keyClick(Qt.Key_Tab)
      compare(fixture.part_searchField.inputControl.text, "Browser")
      fixture.part_searchField.inputControl.text = ""
    }

    // The undo of a HEY change is hidden: the action menu never lists it, and
    // arrow keys cannot reach it.
    function test_the_action_menu_never_lists_hidden_actions() {
      const thread = { id: "hey:topic:9101", title: "Lunch?", kind: Protocol.ResultKinds.Mail, sourceId: Protocol.SourceIds.Hey,
        actions: [
          { id: "open", title: "Open in HEY", kind: Protocol.ActionKinds.HeyOpen },
          { id: "mark-seen", title: "Mark Seen", kind: Protocol.ActionKinds.HeyTriage },
          { id: Protocol.UndoActions.Prefix + "mark-seen", title: "Undo Mark Seen", kind: Protocol.ActionKinds.HeyTriage, hidden: true }
        ] }
      fixture.part_controller.replaceResults([thread], true)
      fixture.part_actionPanel.openFor(fixture.part_controller.selectedResult())
      tryVerify(function() { return fixture.part_actionPanel.filterControl.activeFocus })
      compare(fixture.part_actionPanel.allActions().map(action => action.id).join(","), "open,mark-seen")
      for (let step = 0; step < 4; step++) keyClick(Qt.Key_Down)
      keyClick(Qt.Key_Return)
      verify(fixture.part_actionPanel.chosen !== Protocol.UndoActions.Prefix + "mark-seen")
      keyClick(Qt.Key_Escape)
    }

    function test_inline_filter_hint_completes_with_tab() {
      fixture.part_searchField.filterDescriptors = fixture.part_controller.filterDescriptors
      fixture.part_searchField.inputControl.forceActiveFocus()
      typeText("ap")
      compare(fixture.part_searchField.prefixHint.token, "app:")
      keyClick(Qt.Key_Tab)
      compare(fixture.part_controller.activeFilterId, "applications")
      compare(fixture.part_searchField.inputControl.text, "")
      fixture.part_searchField.filterDescriptors = []
    }

    function test_scoped_filter_label_before_and_after_descriptor_refresh() {
      const previous = fixture.part_controller.filterDescriptors
      const id = "extension-search:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef:notes"
      fixture.part_controller.activeFilters = [{ id: id, label: "Search Notebook · Saved note", excluded: false }]
      try {
        compare(fixture.part_searchField.filterChipControl.label, "Search Notebook · Saved note")
        verify(fixture.part_controller.searchAccessibilityDescription().indexOf("Search Notebook · Saved note") >= 0)
        mouseClick(fixture.part_searchField.inputControl)
        typeText("Purple")
        compare(fixture.part_controller.queryExpression().includeFilterIds, [id])
        compare(fixture.part_controller.queryText, "Purple")
        fixture.part_controller.filterDescriptors = previous.concat([{ id: id, label: "Renamed notebook · Saved note", token: "ext0123456789-notes:" }])
        compare(fixture.part_searchField.filterChipControl.label, "Renamed notebook · Saved note")
        typeText(" telescope")
        compare(fixture.part_controller.queryText, "Purple telescope")
        verify(fixture.part_searchField.inputControl.activeFocus)
      } finally {
        fixture.part_controller.filterDescriptors = previous
      }
    }

    function test_generated_extension_prefix_builds_scoped_expression() {
      const previous = fixture.part_controller.filterDescriptors
      const id = "extension-search:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef:notes"
      fixture.part_controller.filterDescriptors = previous.concat([{ id: id, token: "ext0123456789-notes:", aliases: ["notes:"] }])
      try {
        mouseClick(fixture.part_searchField.inputControl)
        typeText("ext0123456789-notes:Meeting")
        compare(fixture.part_controller.queryText, "Meeting")
        compare(fixture.part_controller.queryExpression().includeFilterIds, [id])
        compare(fixture.part_controller.queryExpression().clauses, [{ field: null, text: "Meeting", excluded: false }])
        verify(fixture.part_searchField.inputControl.activeFocus)
      } finally {
        fixture.part_controller.filterDescriptors = previous
      }
    }
  }
}
