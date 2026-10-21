import QtQuick
import QtQuick.Controls
import QtTest
import "../../qml/common" as CommandDeck
import "../../qml/common/Protocol.js" as Protocol
import "../../qml/common/DeckInvocationActions.js" as Invocations

Item {
  id: scene
  width: 1000
  height: 740
  property int sequence: 0
  property var sent: []
  CommandDeck.Theme { id: theme; loadShellTokens: false }
  TextField { id: invoker; width: 200; text: "parent search" }
  CommandDeck.DeckButton {
    id: navigation
    x: 220
    theme: theme
    text: "Clipboard History"
    focusPolicy: Qt.TabFocus
    onClicked: Invocations.invokeAction(navigationHost, {kind: Protocol.ResultKinds.CustomCommand},
      {kind: Protocol.ActionKinds.OpenClipboardHistory, arguments: {query: "must not become a history search"}}, false)
  }
  QtObject {
    id: navigationHost
    property bool daemonAvailable: true
    property bool reconciliationPending: false
    property bool actionPanelOpen: false
    property var part_invocationController: invocationController
    property var part_clipboardController: controller
  }
  CommandDeck.InvocationController { id: invocationController }
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
  TextEdit {
    id: matchTarget
    x: -1000
    width: 600
    wrapMode: TextEdit.WrapAnywhere
  }
  CommandDeck.ClipboardMatchOverlay {
    id: matchOverlay
    target: matchTarget
    start: 0
    length: 0
  }
  TextArea {
    id: rtlMatchTarget
    x: -1000
    width: 60
    padding: 6
    wrapMode: TextEdit.WrapAnywhere
  }
  CommandDeck.ClipboardMatchOverlay {
    id: rtlMatchOverlay
    target: rtlMatchTarget
    start: 0
    length: 0
  }
  DeadlineTestCase {
    name: "CommandDeck_clipboard"
    when: windowShown
    function entry(index, revision, presentation) {
      return {id: "entry" + index, incarnation: "inc" + index, revision: revision || 1,
        summary: "clipboard " + index, copiedUnixSeconds: 1000, pinned: false, keywords: [],
        presentation: presentation || {kind: "text", imageWidth: null, imageHeight: null, destinationCount: 0, hasOpenDestination: false, reducedFidelity: false}}
    }
    function emptySlice() {
      return {text: "", sliceStartUtf16: 0, ranges: [], matchCount: 0, matchCountLimited: false, matchIndex: null,
        rangeCountLimited: false, hasTextBefore: false, hasTextAfter: false, sliceReason: null}
    }
    function reply(key, response) {
      verify(controller.pending[key] > 0, "request exists: " + key)
      controller.handleMessage({requestId: controller.pending[key], type: Protocol.Messages.ClipboardResult, clipboard: response})
    }
    function page(entries, cursor) {
      return {page: {entries: entries, cursor: cursor || "", storeRevision: 1, newerAvailable: false}}
    }
    function openResponse(sessionId) {
      return {sessionId: sessionId, capabilities: {browse: true, copy: true,
        inspectOpenHandlers: true, open: false, paste: false, effectsUnavailableReason: "ReleaseBarrierUnavailable"}}
    }
    function typeText(value) { for (let i = 0; i < value.length; i++) keyClick(value.charAt(i)) }
    function init() {
      scene.sent = []
      controller.available = true
      controller.open("")
      reply("open", openResponse("session"))
      reply("settings", {policy: {revision: 1, consent: "None", paused: false, maximumEntries: 200, retentionHours: 168, quotaMiB: 256, clearOnStartup: false},
        migration: {required: false, storeRevision: 1, entryCount: 3, pinnedCount: 0}, status: "Disabled"})
      reply("query", page([entry(0), entry(1), entry(2)], "cursor1"))
      waitForValue(surface.searchControl, "activeFocus", true)
    }
    function cleanup() {
      matchOverlay.start = 0
      matchOverlay.length = 0
      matchTarget.text = ""
      matchTarget.width = 600
      rtlMatchOverlay.start = 0
      rtlMatchOverlay.length = 0
      rtlMatchTarget.text = ""
      controller.close(false)
      nextTurn()
    }

    function test_capture_status_poll_preserves_button_focus_and_stops_on_close() {
      for (let index = 0; index < 30 && !surface.copyControl.activeFocus; index++) keyClick(Qt.Key_Tab)
      verify(surface.copyControl.activeFocus)
      waitForCondition(function() { return Boolean(controller.pending.settings) }, 2000)
      reply("settings", Object.assign({}, controller.settings, {status: "Capturing text"}))
      compare(controller.settings.status, "Capturing text")
      verify(surface.copyControl.activeFocus)
      keyClick(Qt.Key_Space)
      verify(Boolean(controller.pending.copy))
      controller.close(false)
      const count = scene.sent.length
      observeFor(1100, () => scene.sent.length === count, "a closed clipboard sends no polling requests")
      compare(scene.sent.length, count)
    }

    function test_query_free_navigation_opens_search_with_keyboard_and_mouse() {
      keyClick(Qt.Key_Escape)
      waitForValue(invoker, "activeFocus", true)
      keyClick(Qt.Key_Tab)
      waitForValue(navigation, "activeFocus", true)
      keyClick(Qt.Key_Return)
      waitForValue(surface.searchControl, "activeFocus", true)
      compare(controller.query, "")
      typeText("keyboard")
      compare(controller.query, "keyboard")
      keyClick(Qt.Key_Escape)
      keyClick(Qt.Key_Escape)
      waitForValue(invoker, "activeFocus", true)
      mouseClick(navigation, navigation.width / 2, navigation.height / 2)
      waitForValue(surface.searchControl, "activeFocus", true)
      compare(controller.query, "")
      typeText("mouse")
      compare(controller.query, "mouse")
    }

    function test_copy_enter_and_mouse_keep_typing_ready() {
      keyClick(Qt.Key_Return)
      verify(Boolean(controller.pending.copy))
      const request = scene.sent[scene.sent.length - 1]
      compare(request.clipboard.action, "copy")
      compare(request.clipboard.entryId, "entry0")
      compare(request.clipboard.entryRevision, 1)
      reply("copy", {action: {succeeded: true}})
      compare(controller.notice, "Copied")
      compare(controller.opened, true)
      typeText("after")
      compare(controller.query, "after")
      waitForCondition(function() { return Boolean(controller.pending.query) })
      reply("query", page([entry(1)]))
      mouseClick(surface.listControl.itemAtIndex(0), 20, 20)
      mouseClick(surface.copyControl)
      reply("copy", {action: {succeeded: true}})
      typeText("x")
      compare(controller.query, "afterx")
      compare(surface.searchControl.activeFocus, true)
    }

    function test_copy_button_keyboard_focus_and_stale_replies() {
      for (let index = 0; index < 30 && !surface.copyControl.activeFocus; index++) keyClick(Qt.Key_Tab)
      verify(surface.copyControl.activeFocus)
      keyClick(Qt.Key_Space)
      const pending = controller.pending.copy
      verify(Boolean(pending))
      compare(surface.copyControl.activeFocus, true)
      keyClick(Qt.Key_Return)
      compare(controller.pending.copy, pending)
      reply("copy", {action: {succeeded: false, message: "synthetic failure"}})
      compare(surface.copyControl.activeFocus, true)
      compare(controller.error, "synthetic failure")
      keyClick(Qt.Key_Space)
      const stale = controller.pending.copy
      controller.close()
      controller.open("")
      reply("open", openResponse("reopened"))
      reply("query", page([entry(2)]))
      controller.handleMessage({requestId: stale, type: Protocol.Messages.ClipboardResult, clipboard: {action: {succeeded: true}}})
      compare(controller.notice, "")
      waitForValue(surface.searchControl, "activeFocus", true)
    }

    function test_copy_waits_for_new_query_and_preserves_input_editing() {
      typeText("new")
      keyClick(Qt.Key_Return)
      verify(!controller.pending.copy)
      waitForCondition(function() { return Boolean(controller.pending.query) })
      keyClick(Qt.Key_Return)
      verify(!controller.pending.copy)
      reply("query", page([entry(2)]))
      keyClick(Qt.Key_Return)
      verify(Boolean(controller.pending.copy))
      reply("copy", {action: {succeeded: true}})
      keyClick(Qt.Key_A, Qt.ControlModifier)
      keyClick(Qt.Key_C, Qt.ControlModifier)
      verify(!controller.pending.copy)
      compare(surface.searchControl.selectedText, "new")
    }

    function test_row_click_typing_filter_paging_and_refresh_preserve_search() {
      const row = surface.listControl.itemAtIndex(1)
      verify(row !== null)
      mouseClick(row, 20, 20)
      compare(controller.selectedIndex, 1)
      typeText("cafe")
      compare(surface.searchControl.text, "cafe")
      waitForCondition(function() { return Boolean(controller.pending.query) })
      reply("query", page([entry(1)], "next"))
      compare(controller.entries.length, 1)
      compare(surface.searchControl.activeFocus, true)
      mouseClick(surface.filterControl)
      compare(controller.pinnedOnly, true)
      reply("query", page([entry(1)], "next"))
      typeText("x")
      compare(controller.query, "cafex")
      waitForCondition(function() { return Boolean(controller.pending.query) })
      reply("query", page([entry(1)], "next"))
      mouseClick(surface.moreControl)
      reply("more", page([entry(2)]))
      compare(controller.entries.length, 2)
      typeText("y")
      compare(controller.query, "cafexy")
      waitForCondition(function() { return Boolean(controller.pending.query) })
      reply("query", page([entry(1)]))
      mouseClick(surface.refreshControl)
      reply("query", page([entry(1)]))
      typeText("z")
      compare(controller.query, "cafexyz")
      compare(surface.searchControl.activeFocus, true)
    }

    function test_keyboard_button_focus_survives_replacement_and_keywords_restore() {
      keyClick(Qt.Key_Tab)
      waitForValue(surface.kindControl, "activeFocus", true)
      keyClick(Qt.Key_Right)
      compare(controller.kindFilter, "text")
      compare(scene.sent[scene.sent.length - 1].clipboard.kindFilter, "text")
      reply("query", page([entry(0)]))
      compare(surface.kindControl.activeFocus, true)
      compare(surface.kindControl.segmentAt(1).activeFocus, true)
      keyClick(Qt.Key_Tab)
      compare(surface.filterControl.activeFocus, true)
      keyClick(Qt.Key_Space)
      compare(controller.pinnedOnly, true)
      reply("query", page([entry(0)]))
      compare(surface.filterControl.activeFocus, true)
      keyClick(Qt.Key_Tab)
      compare(surface.refreshControl.activeFocus, true)
      keyClick(Qt.Key_Return)
      reply("query", page([entry(0, 2)]))
      compare(surface.refreshControl.activeFocus, true)
      keyClick(Qt.Key_Escape)
      reply("query", page([entry(0, 2)]))
      // Escape resets both active filters, preserving the deliberately focused button.
      compare(controller.pinnedOnly, false)
      compare(controller.kindFilter, "")
      compare(scene.sent[scene.sent.length - 1].clipboard.kindFilter, undefined)
      mouseClick(surface.listControl.itemAtIndex(0), 20, 20)
      keyClick(Qt.Key_E, Qt.ControlModifier)
      waitForValue(surface.dialogControl.editorControl, "activeFocus", true)
      typeText("work")
      keyClick(Qt.Key_Tab)
      compare(surface.dialogControl.cancelControl.activeFocus, true)
      keyClick(Qt.Key_Tab)
      compare(surface.dialogControl.acceptControl.activeFocus, true)
      keyClick(Qt.Key_Space)
      waitForValue(surface.searchControl, "activeFocus", true)
      const request = scene.sent[scene.sent.length - 1]
      compare(request.clipboard.action, "keywords")
      compare(request.clipboard.keywords[0], "work")
      reply("action", {})
      reply("query", page([entry(0, 3)]))
      typeText("ready")
      compare(controller.query, "ready")
    }

    function test_image_entries_show_dimensions_and_inert_thumbnail_preview() {
      const image = entry(5, 1, {kind: "images", imageWidth: 32, imageHeight: 16, destinationCount: 0, hasOpenDestination: false, reducedFidelity: false})
      const link = entry(6, 1, {kind: "links", imageWidth: null, imageHeight: null, destinationCount: 1, hasOpenDestination: true, reducedFidelity: false})
      typeText("img")
      waitForCondition(function() { return Boolean(controller.pending.query) })
      reply("query", page([image, link]))
      compare(controller.describe(image), "Image 32×16")
      compare(controller.describe(link), "Link")
      compare(surface.listControl.itemAtIndex(0).descriptionControl.text, "Image 32×16")
      waitForCondition(function() { return Boolean(controller.pending.preview) })
      reply("preview", {preview: {id: "entry5", incarnation: "inc5", revision: 1, queryGeneration: controller.queryGeneration, slice: emptySlice(),
        presentation: image.presentation, openDestination: null, extractedText: false, truncated: false,
        imageSource: "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAASElEQVR4nGNwi7r0fyAxw6gDRkNgNARGQ4BQCFAKKC6IiDGEEr2jDhgNgdEQGA2B0RAYGiEwoJXRaLN8NARGQ2A0BNxo3D0HAMBAGv92W3CjAAAAAElFTkSuQmCC"}})
      waitForValue(surface.previewImageControl, "status", Image.Ready)
      compare(surface.previewImageControl.visible, true)
      compare(surface.previewControl.visible, false)
      keyClick(Qt.Key_Down)
      waitForCondition(function() { return Boolean(controller.pending.preview) })
      reply("preview", {preview: {id: "entry6", incarnation: "inc6", revision: 1, queryGeneration: controller.queryGeneration,
        slice: Object.assign(emptySlice(), {text: "https://example.invalid/page"}), presentation: link.presentation,
        openDestination: "https://example.invalid/page", extractedText: false, truncated: false, imageSource: null}})
      compare(surface.previewControl.visible, true)
      compare(surface.previewControl.text, "https://example.invalid/page")
      typeText("x")
      compare(controller.query, "imgx")
      compare(surface.searchControl.activeFocus, true)
    }

    function test_open_preparation_lists_handlers_without_effects_and_aborts_on_close() {
      const link = entry(7, 1, {kind: "links", imageWidth: null, imageHeight: null, destinationCount: 1, hasOpenDestination: true, reducedFidelity: false})
      compare(surface.openControl.enabled, false)
      typeText("link")
      waitForCondition(function() { return Boolean(controller.pending.query) })
      reply("query", page([link]))
      waitForCondition(function() { return Boolean(controller.pending.preview) })
      reply("preview", {preview: {id: "entry7", incarnation: "inc7", revision: 1, queryGeneration: controller.queryGeneration,
        slice: Object.assign(emptySlice(), {text: "https://example.invalid/page"}), presentation: link.presentation,
        openDestination: "https://example.invalid/page", extractedText: false, truncated: false, imageSource: null}})
      compare(surface.openControl.enabled, true)
      compare(surface.openControl.text, "Inspect Open…")
      const capabilities = controller.capabilities
      controller.capabilities = Object.assign({}, capabilities, {inspectOpenHandlers: false})
      compare(surface.openControl.enabled, false)
      keyClick(Qt.Key_K, Qt.ControlModifier)
      compare(Boolean(controller.pending.prepare), false)
      controller.capabilities = capabilities
      compare(surface.openControl.enabled, true)
      keyClick(Qt.Key_K, Qt.ControlModifier)
      verify(Boolean(controller.pending.prepare))
      const request = scene.sent[scene.sent.length - 1]
      compare(request.operation, Protocol.Operations.ClipboardActionsPrepare)
      compare(request.clipboard.action, "open")
      compare(request.clipboard.entryId, "entry7")

      controller.select(0)
      controller.handleMessage({requestId: request.requestId, type: Protocol.Messages.ClipboardResult, clipboard: {actions: {
        preparationId: "stale", releaseId: "11111111111111111111111111111111", status: "Resolved", handlers: [],
        total: 0, moreAvailable: false, expiresInSeconds: 30, effectsAvailable: false}}})
      compare(surface.dialogControl.opened, false)
      compare(controller.openPreparation, null)

      controller.select(1)
      controller.prepareOpen()
      verify(Boolean(controller.pending.prepare))
      const queryBoundRequest = scene.sent[scene.sent.length - 1]
      controller.query = "changed"
      controller.refresh(false)
      controller.handleMessage({requestId: queryBoundRequest.requestId, type: Protocol.Messages.ClipboardResult, clipboard: {actions: {
        preparationId: "stale-query", releaseId: "22222222222222222222222222222222", status: "Resolved", handlers: [],
        total: 0, moreAvailable: false, expiresInSeconds: 30, effectsAvailable: false}}})
      compare(surface.dialogControl.opened, false)
      compare(controller.openPreparation, null)
      reply("query", page([link]))

      controller.prepareOpen()
      verify(Boolean(controller.pending.prepare))
      reply("prepare", {actions: {preparationId: "prep", releaseId: "abcdefabcdefabcdefabcdefabcdefab", status: "Resolved",
        handlers: [{desktopId: "browser.desktop", label: "Browser", rank: "default", available: true, reason: null},
          {desktopId: "term.desktop", label: "Terminal", rank: "declared", available: false, reason: "UnsupportedTerminalLaunch"}],
        total: 2, moreAvailable: false, expiresInSeconds: 30, effectsAvailable: false, unavailableReason: "ReleaseBarrierUnavailable"}})
      waitForValue(surface.dialogControl, "opened", true)
      waitForValue(surface.dialogControl.cancelControl, "activeFocus", true)
      compare(surface.dialogControl.handlersControl.count, 2)
      compare(surface.dialogControl.handlersControl.itemAt(0).text, "• Browser (default)")
      compare(surface.dialogControl.handlersControl.itemAt(1).text, "• Terminal · terminal launch unsupported")
      compare(surface.dialogControl.acceptControl.visible, false)
      verify(surface.dialogControl.explanation.indexOf("not available in this build") > 0)
      keyClick(Qt.Key_Escape)
      waitForValue(surface.dialogControl, "opened", false)
      const abort = scene.sent[scene.sent.length - 1]
      compare(abort.operation, Protocol.Operations.ClipboardReleaseAbort)
      compare(abort.clipboard.releaseId, "abcdefabcdefabcdefabcdefabcdefab")
      reply("abort", {abort: {frontendInstance: controller.frontendInstance, openGeneration: 1, releaseId: "abcdefabcdefabcdefabcdefabcdefab", outcome: "RevokedBeforeDispatch"}})
      waitForValue(surface.searchControl, "activeFocus", true)
      typeText("x")
      compare(controller.query, "linkx")
      compare(controller.openPreparation, null)
    }

    function test_copy_capability_gates_button_and_enter() {
      compare(surface.copyControl.enabled, true)
      const capabilities = controller.capabilities
      controller.capabilities = Object.assign({}, capabilities, {copy: false})
      compare(surface.copyControl.enabled, false)
      const before = scene.sent.length
      keyClick(Qt.Key_Return)
      compare(scene.sent.length, before)
      controller.capabilities = capabilities
      compare(surface.copyControl.enabled, true)
      keyClick(Qt.Key_Return)
      verify(Boolean(controller.pending.copy))
      reply("copy", {action: {succeeded: true}})
    }

    function test_preview_is_inert_stale_replies_are_ignored_and_reopen_focuses_search() {
      const old = controller.pending.preview
      controller.select(1)
      controller.handleMessage({requestId: old, type: Protocol.Messages.ClipboardResult,
        clipboard: {preview: {incarnation: "inc0", revision: 1, queryGeneration: controller.queryGeneration, slice: {text: "stale"}}}})
      compare(controller.preview, null)
      reply("preview", {preview: {incarnation: "inc1", revision: 1, queryGeneration: controller.queryGeneration,
        slice: {text: "<a href='file:///secret'>literal</a>", ranges: [{startUtf16: 25, lengthUtf16: 7}], matchCount: 1, matchIndex: 0, selectedRange: 0}}})
      compare(surface.previewControl.textFormat, TextEdit.PlainText)
      compare(surface.previewControl.text, "<a href='file:///secret'>literal</a>")
      waitForValue(surface.previewHighlightControl, "count", 1)
      const overlay = surface.previewHighlightControl.itemAt(0)
      waitForValue(overlay, "segmentCount", 1)
      const expected = surface.previewControl.positionToRectangle(25)
      fuzzyCompare(overlay.segmentAt(0).x, expected.x, 1)
      fuzzyCompare(overlay.segmentAt(0).y, expected.y, 1)
      verify(overlay.segmentAt(0).width > 4)
      verify(overlay.selected)
      compare(surface.previewControl.cursorPosition, 25)
      controller.preview = {incarnation: "inc1", revision: 1, queryGeneration: controller.queryGeneration,
        slice: {text: "first line\nsecond line", ranges: [{startUtf16: 6, lengthUtf16: 11}], matchCount: 1, matchIndex: 0, selectedRange: 0}}
      waitForValue(surface.previewHighlightControl, "count", 1)
      waitForCondition(function() {
        const wrappedOverlay = surface.previewHighlightControl.itemAt(0)
        return wrappedOverlay !== null && wrappedOverlay.segmentCount >= 2
      })
      controller.preview = {incarnation: "inc1", revision: 1, queryGeneration: controller.queryGeneration,
        slice: {text: "אבג", ranges: [{startUtf16: 0, lengthUtf16: 3}], matchCount: 1, matchIndex: 0, selectedRange: 0}}
      waitForValue(surface.previewHighlightControl, "count", 1)
      const rtlOverlay = surface.previewHighlightControl.itemAt(0)
      waitForValue(rtlOverlay, "segmentCount", 1)
      verify(rtlOverlay.segmentAt(0).width > 20, "RTL range uses both visual endpoints")
      controller.preview = {incarnation: "inc1", revision: 1, queryGeneration: controller.queryGeneration,
        slice: {text: "abc אבג xyz", ranges: [{startUtf16: 2, lengthUtf16: 4}], matchCount: 1, matchIndex: 0, selectedRange: 0}}
      waitForValue(surface.previewHighlightControl, "count", 1)
      waitForCondition(function() {
        const mixedOverlay = surface.previewHighlightControl.itemAt(0)
        return mixedOverlay !== null && mixedOverlay.segmentCount === 2
      })
      controller.preview = null
      waitForValue(surface.previewHighlightControl, "count", 0)
      keyClick(Qt.Key_Escape)
      compare(controller.opened, false)
      waitForValue(invoker, "activeFocus", true)
      controller.open("")
      reply("open", openResponse("second"))
      reply("query", page([entry(0)]))
      waitForValue(surface.searchControl, "activeFocus", true)
      typeText("new")
      compare(controller.query, "new")
    }

    function test_match_overlay_reflows_and_keeps_soft_wrap_boundary_characters() {
      matchTarget.text = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ"
      matchOverlay.start = 30
      matchOverlay.length = 5
      waitForValue(matchOverlay, "segmentCount", 1)
      const originalY = matchOverlay.segmentAt(0).y

      matchTarget.width = 80
      waitForCondition(function() {
        return matchOverlay.segmentCount > 0 && Math.abs(matchOverlay.segmentAt(0).y - originalY) >= 1
      })

      let boundary = -1
      for (let index = 0; index < matchTarget.text.length; index++) {
        const head = matchTarget.positionToRectangle(index)
        const tail = matchTarget.positionToRectangle(index + 1)
        if (Math.abs(head.y - tail.y) >= 1) {
          boundary = index
          break
        }
      }
      verify(boundary >= 0, "fixture contains a visible character immediately before a soft wrap")

      matchOverlay.start = boundary
      matchOverlay.length = 1
      waitForValue(matchOverlay, "segmentCount", 1)
      verify(matchOverlay.segmentAt(0).width > 2, "the boundary character receives its visual width")
    }

    function test_match_overlay_uses_rtl_glyph_bounds_at_a_soft_wrap() {
      rtlMatchTarget.text = "ממממממממממממ"

      let glyphWidth = 0
      let boundary = -1
      for (let index = 0; index < rtlMatchTarget.text.length; index++) {
        const head = rtlMatchTarget.positionToRectangle(index)
        const tail = rtlMatchTarget.positionToRectangle(index + 1)
        if (Math.abs(head.y - tail.y) < 1 && glyphWidth === 0)
          glyphWidth = Math.abs(head.x - tail.x)
        else if (Math.abs(head.y - tail.y) >= 1 && boundary < 0)
          boundary = index
      }
      verify(glyphWidth > 2, "fixture exposes the repeated glyph's visual width")
      verify(boundary >= 0, "fixture contains an RTL character immediately before a soft wrap")

      rtlMatchOverlay.start = boundary
      rtlMatchOverlay.length = 1
      waitForValue(rtlMatchOverlay, "segmentCount", 1)
      const boundaryHead = rtlMatchTarget.positionToRectangle(boundary)
      fuzzyCompare(rtlMatchOverlay.segmentAt(0).x, boundaryHead.x - glyphWidth, 1)
      fuzzyCompare(rtlMatchOverlay.segmentAt(0).width, glyphWidth, 1)
    }

    function test_list_typing_reaches_search_and_clear_confirmation_traps_focus() {
      for (let index = 0; index < 10 && !surface.listControl.activeFocus; index++) keyClick(Qt.Key_Tab)
      verify(surface.listControl.activeFocus)
      typeText("list")
      compare(controller.query, "list")
      compare(surface.searchControl.activeFocus, true)
      mouseClick(surface.clearControl)
      waitForValue(surface.dialogControl.cancelControl, "activeFocus", true)
      keyClick(Qt.Key_Backtab)
      compare(surface.dialogControl.acceptControl.activeFocus, true)
      keyClick(Qt.Key_Tab)
      compare(surface.dialogControl.cancelControl.activeFocus, true)
      keyClick(Qt.Key_Escape)
      waitForValue(surface.searchControl, "activeFocus", true)
      verify(!scene.sent.some(value => value.clipboard && value.clipboard.action === "clear"))
    }

    function test_limits_preview_is_explicit_and_restores_search_after_apply() {
      mouseClick(surface.limitsControl)
      const dialog = surface.limitsDialogControl
      waitForValue(dialog.countControl.contentItem, "activeFocus", true)
      keyClick(Qt.Key_A, Qt.ControlModifier)
      typeText("100")
      keyClick(Qt.Key_Tab)
      compare(dialog.countControl.value, 100)
      // Follow the real tab chain through age, quota, startup, Cancel and Review.
      for (let index = 0; index < 8 && !dialog.saveControl.activeFocus; index++) keyClick(Qt.Key_Tab)
      verify(dialog.saveControl.activeFocus)
      keyClick(Qt.Key_Space)
      const prepare = scene.sent[scene.sent.length - 1]
      compare(prepare.clipboard.patch.maximumEntries, 100)
      verify(!prepare.confirmed)
      const snapshot = controller.settings
      reply("settingsSet", {policy: snapshot.policy, migration: snapshot.migration, status: "Disabled",
        policyReview: {token: "review-token", policyRevision: 1, storeRevision: 1,
          proposed: {maximumEntries: 100, retentionHours: 168, quotaMiB: 256, clearOnStartup: false}, deleteCount: 5, pinnedCount: 1}})
      waitForValue(dialog.cancelControl, "activeFocus", true)
      keyClick(Qt.Key_Tab)
      compare(dialog.saveControl.activeFocus, true)
      keyClick(Qt.Key_Return)
      const confirm = scene.sent[scene.sent.length - 1]
      compare(confirm.confirmationToken, "review-token")
      compare(confirm.clipboard.storeRevision, 1)
      verify(confirm.confirmed)
      reply("settingsSet", {policy: {revision: 2, consent: "None", maximumEntries: 100, retentionHours: 168, quotaMiB: 256},
        migration: snapshot.migration, status: "Disabled"})
      waitForValue(surface.searchControl, "activeFocus", true)
      typeText("after limits")
      compare(controller.query, "after limits")
    }
  }
}
