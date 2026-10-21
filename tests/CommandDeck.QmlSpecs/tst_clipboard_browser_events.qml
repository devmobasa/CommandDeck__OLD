import QtQuick
import QtQuick.Controls
import QtTest
import "../../qml/common" as CommandDeck
import "../../qml/common/Protocol.js" as Protocol
import "../../qml/common/ClipboardFormat.js" as Format

// Real-event journeys for the clipboard browser layout: selection visibility,
// the kind filter, paging keys, match steps, empty states, and focus recovery.
Item {
  id: scene
  width: 1000
  height: 740
  property int sequence: 0
  property var sent: []
  CommandDeck.Theme { id: theme; loadShellTokens: false }
  TextField { id: invoker; width: 200; text: "parent search" }
  CommandDeck.ClipboardHistoryController {
    id: controller
    nextRequestId: function() { return ++scene.sequence }
    frontendInstance: "123456781234423482341234567890ab"
    openGeneration: 1
    onRequest: message => scene.sent = scene.sent.concat([message])
  }
  CommandDeck.ClipboardHistorySurface {
    id: surface
    anchors.fill: parent
    theme: theme
    controller: controller
    returnFocus: invoker
  }

  DeadlineTestCase {
    name: "CommandDeck_clipboard_browser"
    when: windowShown

    function entry(index, extra) {
      return Object.assign({id: "entry" + index, incarnation: "inc" + index, revision: 1,
        summary: "clipboard " + index, copiedUnixSeconds: 1000, pinned: false, keywords: [],
        presentation: {kind: "text", imageWidth: null, imageHeight: null, destinationCount: 0, hasOpenDestination: false, reducedFidelity: false}},
        extra || {})
    }
    function settings(policy) {
      return {policy: Object.assign({revision: 1, consent: "Text", paused: false, maximumEntries: 200, retentionHours: 168,
        quotaMiB: 256, clearOnStartup: false}, policy || {}),
        migration: {required: false, storeRevision: 1, entryCount: 3, pinnedCount: 0}, status: "Capturing text"}
    }
    function page(entries, cursor) {
      return {page: {entries: entries, cursor: cursor || "", storeRevision: 1, newerAvailable: false}}
    }
    function openResponse(sessionId) {
      return {sessionId: sessionId, capabilities: {browse: true, copy: true,
        inspectOpenHandlers: true, open: false, paste: false, effectsUnavailableReason: "ReleaseBarrierUnavailable"}}
    }
    function previewWithMatches(index, count) {
      const ranges = []
      for (let offset = 0; offset < count; offset++) ranges.push({startUtf16: offset * 10, lengthUtf16: 4})
      return {preview: {id: "entry0", incarnation: "inc0", revision: 1, queryGeneration: controller.queryGeneration,
        slice: {text: "clip one, clip two, clip three", sliceStartUtf16: 0, ranges: ranges, matchCount: count,
          matchCountLimited: false, matchIndex: index, selectedRange: index, rangeCountLimited: false,
          hasTextBefore: false, hasTextAfter: false, sliceReason: null},
        presentation: entry(0).presentation, openDestination: null, extractedText: false, truncated: false, imageSource: null}}
    }
    function reply(key, response) {
      verify(controller.pending[key] > 0, "request exists: " + key)
      controller.handleMessage({requestId: controller.pending[key], type: Protocol.Messages.ClipboardResult, clipboard: response})
    }
    function sentOf(operation) {
      return scene.sent.filter(value => value.operation === operation)
    }
    function lastOf(operation) {
      const matching = sentOf(operation)
      verify(matching.length > 0, "sent " + operation)
      return matching[matching.length - 1]
    }
    function typeText(value) { for (let index = 0; index < value.length; index++) keyClick(value.charAt(index)) }
    function tabTo(control, limit) {
      for (let index = 0; index < (limit || 30) && !control.activeFocus; index++) keyClick(Qt.Key_Tab)
      verify(control.activeFocus, "Tab reaches the control")
    }
    function openBrowser(entries, cursor, policy) {
      scene.sent = []
      controller.available = true
      controller.open("")
      reply("open", openResponse("session"))
      reply("settings", settings(policy))
      reply("query", page(entries, cursor))
      waitForValue(surface.searchControl, "activeFocus", true)
    }
    function cleanup() {
      controller.close(false)
      nextTurn()
    }

    function test_selection_is_marked_while_search_or_list_has_focus() {
      openBrowser([entry(0), entry(1), entry(2)])
      const first = surface.listControl.itemAtIndex(0)
      const second = surface.listControl.itemAtIndex(1)
      verify(first.selected && first.railControl.visible)
      verify(!second.selected && !second.railControl.visible)
      compare(first.cardControl.border.width, 1)
      verify(!Qt.colorEqual(first.cardControl.color, second.cardControl.color), "the selection has its own fill while search is focused")

      keyClick(Qt.Key_Down)
      compare(controller.selectedIndex, 1)
      verify(second.railControl.visible)
      verify(!first.railControl.visible)
      compare(surface.searchControl.activeFocus, true)

      tabTo(surface.listControl, 10)
      verify(second.listFocused)
      compare(second.cardControl.border.width, 2)

      mouseClick(surface.listControl.itemAtIndex(2), 20, 20)
      compare(controller.selectedIndex, 2)
      verify(surface.listControl.itemAtIndex(2).railControl.visible)
      compare(surface.searchControl.activeFocus, true)
      typeText("x")
      compare(controller.query, "x")
    }

    function test_kind_filter_follows_ctrl_tab_arrows_and_mouse_without_losing_search() {
      openBrowser([entry(0)])
      keyClick(Qt.Key_Tab, Qt.ControlModifier)
      compare(controller.kindFilter, "text")
      compare(lastOf(Protocol.Operations.ClipboardQuery).clipboard.kindFilter, "text")
      compare(surface.searchControl.activeFocus, true)
      reply("query", page([entry(0)]))
      keyClick(Qt.Key_Backtab, Qt.ControlModifier | Qt.ShiftModifier)
      compare(controller.kindFilter, "")
      reply("query", page([entry(0)]))

      mouseClick(surface.kindControl.segmentAt(3))
      compare(controller.kindFilter, "images")
      compare(surface.searchControl.activeFocus, true)
      reply("query", page([entry(0)]))
      typeText("a")
      compare(controller.query, "a")
      waitForCondition(function() { return Boolean(controller.pending.query) })
      reply("query", page([entry(0)]))

      keyClick(Qt.Key_Escape)
      compare(controller.query, "")
      reply("query", page([entry(0)]))
      tabTo(surface.kindControl, 5)
      verify(surface.kindControl.segmentAt(3).activeFocus, "Tab enters the group on the selected segment")
      keyClick(Qt.Key_Right)
      compare(controller.kindFilter, "files")
      keyClick(Qt.Key_Right)
      compare(controller.kindFilter, "", "Right wraps to All")
      compare(surface.kindControl.segmentAt(0).activeFocus, true)
      keyClick(Qt.Key_End)
      compare(controller.kindFilter, "files")
      keyClick(Qt.Key_Home)
      compare(controller.kindFilter, "")
      keyClick(Qt.Key_Tab, Qt.ControlModifier)
      compare(controller.kindFilter, "text")
      compare(surface.kindControl.segmentAt(1).activeFocus, true, "Ctrl+Tab keeps focus on the newly selected segment")
      reply("query", page([entry(0)]))
      keyClick(Qt.Key_Escape)
      compare(controller.kindFilter, "")
      compare(surface.kindControl.segmentAt(0).current, true)
    }

    function test_page_keys_and_down_on_the_last_entry_load_the_next_page() {
      openBrowser([entry(0), entry(1), entry(2)], "cursor1")
      keyClick(Qt.Key_End, Qt.ControlModifier)
      compare(controller.selectedIndex, 2)
      keyClick(Qt.Key_Down)
      verify(Boolean(controller.pending.more), "Down on the last entry requests the next page")
      compare(lastOf(Protocol.Operations.ClipboardQuery).clipboard.cursor, "cursor1")
      reply("more", page([entry(3), entry(4)]))
      compare(controller.entries.length, 5)
      compare(controller.selectedIndex, 2)
      keyClick(Qt.Key_Down)
      compare(controller.selectedIndex, 3)

      keyClick(Qt.Key_Home, Qt.ControlModifier)
      compare(controller.selectedIndex, 0)
      keyClick(Qt.Key_PageDown)
      verify(controller.selectedIndex > 1, "PageDown moves by a page")
      keyClick(Qt.Key_PageUp)
      compare(controller.selectedIndex, 0)

      keyClick(Qt.Key_End, Qt.ControlModifier)
      const queries = sentOf(Protocol.Operations.ClipboardQuery).length
      keyClick(Qt.Key_Down)
      compare(sentOf(Protocol.Operations.ClipboardQuery).length, queries, "the last page sends no further request")
      compare(controller.selectedIndex, 4)
      compare(surface.searchControl.activeFocus, true)
    }

    // Like every list: Home and End reach the ends from the focused entry
    // list, while search keeps them for its cursor. Ctrl+P pins here, so
    // Ctrl+N does not move either.
    function test_home_and_end_reach_the_ends_from_the_entry_list() {
      openBrowser([entry(0), entry(1), entry(2)])
      typeText("cl")
      waitForCondition(function() { return Boolean(controller.pending.query) })
      reply("query", page([entry(0), entry(1), entry(2)]))
      keyClick(Qt.Key_Home)
      keyClick(Qt.Key_End)
      compare(controller.selectedIndex, 0, "search keeps Home and End")
      compare(surface.searchControl.cursorPosition, 2)
      keyClick(Qt.Key_N, Qt.ControlModifier)
      compare(controller.selectedIndex, 0, "Ctrl+N leaves the selection")

      tabTo(surface.listControl, 10)
      keyClick(Qt.Key_End)
      compare(controller.selectedIndex, 2)
      keyClick(Qt.Key_Home)
      compare(controller.selectedIndex, 0)
      verify(surface.listControl.activeFocus)
    }

    function test_match_shortcuts_step_and_wrap_while_search_keeps_focus() {
      openBrowser([entry(0)])
      verify(!surface.previewPaneControl.nextMatchControl.visible, "match steps stay hidden without a search")
      typeText("clip")
      waitForCondition(function() { return Boolean(controller.pending.query) })
      reply("query", page([entry(0)]))
      reply("preview", previewWithMatches(0, 3))
      verify(surface.previewPaneControl.nextMatchControl.visible)
      compare(surface.previewPaneControl.matchLabelControl.text, "Match 1 of 3")

      keyClick(Qt.Key_G, Qt.ControlModifier)
      compare(lastOf(Protocol.Operations.ClipboardPreview).clipboard.matchIndex, 1)
      reply("preview", previewWithMatches(1, 3))
      keyClick(Qt.Key_G, Qt.ControlModifier | Qt.ShiftModifier)
      compare(lastOf(Protocol.Operations.ClipboardPreview).clipboard.matchIndex, 0)
      reply("preview", previewWithMatches(0, 3))
      keyClick(Qt.Key_G, Qt.ControlModifier | Qt.ShiftModifier)
      compare(lastOf(Protocol.Operations.ClipboardPreview).clipboard.matchIndex, 2, "Ctrl+Shift+G wraps to the last match")
      reply("preview", previewWithMatches(2, 3))
      mouseClick(surface.previewPaneControl.nextMatchControl)
      compare(lastOf(Protocol.Operations.ClipboardPreview).clipboard.matchIndex, 0, "Next wraps to the first match")

      compare(surface.searchControl.activeFocus, true)
      typeText("s")
      compare(controller.query, "clips")
    }

    function test_empty_states_explain_loading_capture_search_and_filters() {
      scene.sent = []
      controller.open("")
      compare(surface.emptyStateControl.visible, true)
      compare(surface.emptyStateControl.titleControl.text, "Loading clipboard history…")
      reply("open", openResponse("session"))
      reply("settings", settings({consent: "None"}))
      reply("query", page([]))
      waitForValue(surface.searchControl, "activeFocus", true)
      compare(surface.emptyStateControl.titleControl.text, "Clipboard history is off")

      const action = surface.emptyStateControl.actionControl
      tabTo(action)
      keyClick(Qt.Key_Space)
      waitForValue(surface.dialogControl, "opened", true)
      compare(surface.dialogControl.purpose, "enable")
      waitForValue(surface.dialogControl.cancelControl, "activeFocus", true)
      keyClick(Qt.Key_Escape)
      waitForValue(action, "activeFocus", true)
      mouseClick(surface.searchControl)
      mouseClick(action)
      waitForValue(surface.dialogControl, "opened", true)
      keyClick(Qt.Key_Escape)
      waitForValue(surface.searchControl, "activeFocus", true)

      controller.send("settings", Protocol.Operations.ClipboardSettingsGet)
      reply("settings", settings({paused: true}))
      compare(surface.emptyStateControl.titleControl.text, "Capture is paused")
      mouseClick(surface.emptyStateControl.actionControl)
      compare(lastOf(Protocol.Operations.ClipboardSettingsSet).clipboard.paused, false)
      compare(surface.searchControl.activeFocus, true)

      typeText("zz")
      waitForCondition(function() { return Boolean(controller.pending.query) })
      reply("query", page([]))
      compare(surface.emptyStateControl.titleControl.text, "No entries match “zz”")
      verify(!surface.emptyStateControl.actionControl.visible)
      keyClick(Qt.Key_Escape)
      reply("query", page([]))
      mouseClick(surface.filterControl)
      reply("query", page([]))
      compare(surface.emptyStateControl.titleControl.text, "No pinned entries")
      keyClick(Qt.Key_Escape)
      compare(controller.pinnedOnly, false)
      compare(surface.searchControl.activeFocus, true)
    }

    function test_load_more_keeps_keyboard_focus_until_it_disappears() {
      openBrowser([entry(0), entry(1)], "cursor1")
      tabTo(surface.moreControl)
      keyClick(Qt.Key_Space)
      verify(Boolean(controller.pending.more))
      compare(surface.moreControl.activeFocus, true, "loading keeps focus on Load more")
      reply("more", page([entry(2)], "cursor2"))
      compare(surface.moreControl.activeFocus, true)
      keyClick(Qt.Key_Space)
      compare(lastOf(Protocol.Operations.ClipboardQuery).clipboard.cursor, "cursor2")
      reply("more", page([entry(3)]))
      compare(surface.moreControl.visible, false)
      waitForValue(surface.searchControl, "activeFocus", true)
      typeText("k")
      compare(controller.query, "k")
    }

    function test_double_click_copies_and_confirms_with_a_toast() {
      openBrowser([entry(0), entry(1)])
      mouseDoubleClickSequence(surface.listControl.itemAtIndex(1), 20, 20)
      compare(controller.selectedIndex, 1)
      verify(Boolean(controller.pending.copy))
      compare(lastOf(Protocol.Operations.ClipboardAction).clipboard.entryId, "entry1")
      reply("copy", {action: {succeeded: true}})
      verify(surface.toastControl.shown)
      compare(surface.toastControl.message, "Copied")
      compare(surface.searchControl.activeFocus, true)
      typeText("n")
      compare(controller.query, "n")
    }

    function test_shift_delete_removes_selected_entry_and_refreshes_the_page() {
      openBrowser([entry(0), entry(1)])
      compare(surface.deleteControl.keyHint, "Shift+Del")

      keyClick(Qt.Key_Down)
      compare(controller.selected.id, "entry1")
      keyClick(Qt.Key_Delete, Qt.ShiftModifier)

      const request = lastOf(Protocol.Operations.ClipboardAction)
      compare(request.clipboard.action, "delete")
      compare(request.clipboard.entryId, "entry1")
      compare(request.clipboard.incarnation, "inc1")
      compare(request.clipboard.entryRevision, 1)
      reply("action", {action: {succeeded: true}})
      verify(Boolean(controller.pending.query), "deletion refreshes the current page")
      reply("query", page([entry(0)]))

      compare(controller.selected.id, "entry0")
      compare(surface.searchControl.activeFocus, true)
    }

    function test_shift_delete_preserves_search_cut_with_selected_text() {
      openBrowser([entry(0)])
      typeText("query")
      waitForCondition(function() { return Boolean(controller.pending.query) })
      reply("query", page([entry(0)]))
      surface.searchControl.selectAll()

      const actionCount = sentOf(Protocol.Operations.ClipboardAction).length
      keyClick(Qt.Key_Delete, Qt.ShiftModifier)

      compare(sentOf(Protocol.Operations.ClipboardAction).length, actionCount)
      compare(surface.searchControl.text, "")
    }

    function test_start_sequence_follows_ctrl_s_tab_and_mouse_without_losing_search() {
      openBrowser([entry(0), entry(1)])
      compare(surface.sequenceControl.keyHint, "Ctrl+S")
      keyClick(Qt.Key_Down)
      keyClick(Qt.Key_S, Qt.ControlModifier)

      const request = lastOf(Protocol.Operations.ClipboardAction)
      compare(request.clipboard.action, Protocol.ClipboardActionIds.SequenceStart)
      compare(request.clipboard.entryId, "entry1")
      compare(request.clipboard.incarnation, "inc1")
      compare(request.clipboard.entryRevision, 1)
      compare(request.clipboard.queryGeneration, controller.queryGeneration)
      const count = sentOf(Protocol.Operations.ClipboardAction).length
      keyClick(Qt.Key_S, Qt.ControlModifier)
      compare(sentOf(Protocol.Operations.ClipboardAction).length, count, "one start request at a time")
      reply("sequence", {action: {succeeded: true, message: "Clipboard sequence starts here with 1 entry."}})
      verify(surface.toastControl.shown)
      compare(surface.toastControl.message, "Clipboard sequence starts here with 1 entry.")
      verify(!controller.pending.query, "starting a sequence keeps the current page")
      compare(surface.searchControl.activeFocus, true)

      tabTo(surface.sequenceControl)
      keyClick(Qt.Key_Space)
      compare(lastOf(Protocol.Operations.ClipboardAction).clipboard.entryId, "entry1")
      controller.handleMessage({requestId: controller.pending.sequence, type: Protocol.Messages.Error,
        error: "Enable clipboard capture before using clipboard history here."})
      compare(controller.error, "Enable clipboard capture before using clipboard history here.")
      compare(surface.sequenceControl.activeFocus, true, "the focused button keeps focus across the reply")

      mouseClick(surface.listControl.itemAtIndex(0), 20, 20)
      mouseClick(surface.sequenceControl)
      compare(lastOf(Protocol.Operations.ClipboardAction).clipboard.entryId, "entry0")
      compare(surface.searchControl.activeFocus, true)
      typeText("q")
      compare(controller.query, "q")
    }

    // Capture controls sit in the header, so long statuses and the sequence action never widen the browser.
    function test_controls_stay_inside_the_default_palette_in_every_capture_state() {
      const original = scene.width
      scene.width = theme.paletteWidth
      try {
        openBrowser([entry(0)])
        const states = [
          {consent: "Rich", status: "Capturing text and rich content"},
          {consent: "Rich", status: "Capture unavailable: native helper stopped; no wl-paste fallback was started", long: true},
          {consent: "Text", status: "MigrationReviewRequired", migration: true},
          {consent: "Text", status: "Paused"},
          {consent: "None", status: "Disabled"}
        ]
        for (const state of states) {
          const value = settings({consent: state.consent})
          value.status = state.status
          value.migration.required = state.migration === true
          controller.settings = value
          nextTurn()
          for (const control of [surface.backControl, surface.captureControl, surface.deleteControl, surface.sequenceControl, surface.refreshControl]) {
            verify(control.visible && control.mapToItem(surface, control.width, 0).x <= surface.width, state.status + ": " + control.text)
          }
          compare(surface.captureLabelControl.truncated, state.long === true, state.status + " label elides only past its cap")
        }
      } finally {
        scene.width = original
      }
    }

    function test_start_sequence_needs_capture_consent() {
      openBrowser([entry(0)], "", {consent: "None"})
      compare(surface.sequenceControl.enabled, false)
      const count = sentOf(Protocol.Operations.ClipboardAction).length
      keyClick(Qt.Key_S, Qt.ControlModifier)
      mouseClick(surface.sequenceControl)
      compare(sentOf(Protocol.Operations.ClipboardAction).length, count)
      compare(surface.searchControl.activeFocus, true)
    }

    function test_pinned_and_recent_labels_appear_only_for_a_split_page() {
      openBrowser([entry(0, {pinned: true}), entry(1), entry(2)])
      verify(surface.listControl.itemAtIndex(0).sectionControl.visible)
      verify(surface.listControl.itemAtIndex(1).sectionControl.visible)
      verify(!surface.listControl.itemAtIndex(2).sectionControl.visible)

      mouseClick(surface.filterControl)
      reply("query", page([entry(0, {pinned: true})]))
      waitForCondition(function() { return surface.listControl.itemAtIndex(0) !== null })
      verify(!surface.listControl.itemAtIndex(0).sectionControl.visible, "a pinned-only page needs no labels")

      mouseClick(surface.filterControl)
      reply("query", page([entry(1), entry(0, {pinned: true})]))
      waitForCondition(function() { return surface.listControl.itemAtIndex(1) !== null })
      verify(!surface.listControl.itemAtIndex(0).sectionControl.visible)
      verify(!surface.listControl.itemAtIndex(1).sectionControl.visible)
    }

    function test_format_helpers_use_compact_ages_and_plain_capture_labels() {
      const now = new Date(2026, 8, 15, 10, 0, 0).getTime() / 1000
      compare(Format.compactAge(now - 30, now), "now")
      compare(Format.compactAge(now - 125, now), "2m")
      compare(Format.compactAge(now - 3 * 3600, now), "3h")
      compare(Format.compactAge(new Date(2026, 8, 14, 20, 0, 0).getTime() / 1000, now), "Yesterday")
      compare(Format.compactAge(new Date(2026, 8, 12, 9, 0, 0).getTime() / 1000, now), "Sat")
      compare(Format.compactAge(new Date(2026, 8, 1, 9, 0, 0).getTime() / 1000, now), "Sep 1")
      compare(Format.compactAge(new Date(2025, 11, 31, 9, 0, 0).getTime() / 1000, now), "Dec 31, 2025")
      compare(Format.copiedLabel(now - 60, now).indexOf("Today, "), 0)
      compare(Format.copiedLabel(new Date(2026, 8, 14, 20, 0, 0).getTime() / 1000, now).indexOf("Yesterday, "), 0)
      compare(Format.entryCountLabel(1, false), "1 entry")
      compare(Format.entryCountLabel(50, true), "50+ entries")
      compare(Format.captureTone("Capturing text and rich content"), "active")
      compare(Format.captureTone("Paused"), "idle")
      compare(Format.captureTone("Unavailable"), "warning")
      compare(Format.captureLabel("Disabled"), "Capture off")
      compare(Format.captureLabel("MigrationReviewRequired"), "Review required")
    }
  }
}
