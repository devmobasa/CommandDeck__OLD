import QtQuick
import QtTest
import "../../qml/common" as Deck
import "../../qml/common/Protocol.js" as Protocol
import "../../qml/common/QuicklinkValidation.js" as Validation
import "../fixtures/QuicklinkUrls.js" as UrlCases

Item {
  id: fixture
  width: 1000
  height: 900
  property int sequence: 0
  property var requests: []
  Deck.Theme { id: theme; loadShellTokens: false }
  Deck.QuicklinksController {
    id: controller
    ready: true
    nextRequestId: function() { return ++fixture.sequence }
    onRequestReady: request => fixture.requests = fixture.requests.concat([request])
  }
  Deck.SettingsPanel {
    id: panel
    anchors.fill: parent
    theme: theme
    visible: false
    quicklinksController: controller
    iconResolver: function() { return "" }
    snapshot: ({ preferences: { sources: [] }, shortcuts: [], customizations: [] })
    onDismissed: visible = false
  }
  TestCase {
    name: "QuicklinksEvents"
    when: windowShown
    function typeText(text) {
      for (let index = 0; index < text.length; index++) keyClick(text[index])
    }
    function link(id, source, enabled) {
      return { quicklink: { id: id, title: "Docs " + id, urlTemplate: "https://example.test/?q={query}", alias: id },
        source: source || "configured", enabled: enabled !== false }
    }
    function reply(entries, message) {
      const id = controller.active.request.requestId
      controller.handleMessage({ requestId: id, type: Protocol.Messages.QuicklinksResult,
        quicklinks: { quicklinks: entries, message: message || "" } })
      controller.handleMessage({ requestId: id, type: Protocol.Messages.Done })
    }
    function control(name) { return findChild(panel, name) }
    function openCard() {
      panel.visible = true
      tryCompare(panel.searchControl, "activeFocus", true)
      typeText("quicklinks")
      compare(panel.currentPage, "search")
      reply([link("configured")])
      keyClick(Qt.Key_Return)
      for (let index = 0; index < 20 && !control("quicklinkAdd").activeFocus; index++) keyClick(Qt.Key_Tab)
      verify(control("quicklinkAdd").activeFocus)
    }
    function init() {
      panel.visible = false
      controller.disconnect()
      controller.quicklinks = []
      controller.errorText = ""
      controller.statusText = ""
      fixture.requests = []
      panel.filterAliasInputControl.text = ""
    }
    function cleanup() { panel.visible = false; controller.disconnect() }

    function test_shared_url_validation_data() {
      return UrlCases.cases.map(entry => ({ tag: entry.url + ":" + (entry.alias || ""),
        url: entry.url, alias: entry.alias, error: entry.error || "" }))
    }
    function test_shared_url_validation(data) {
      const error = Validation.validate({ title: "Docs", urlTemplate: data.url, alias: data.alias }, [])
      compare(error, data.error)
    }

    function test_add_preview_validation_save_and_reopen() {
      openCard()
      keyClick(Qt.Key_Return)
      tryCompare(control("quicklinkTitle"), "activeFocus", true)
      typeText("My docs")
      keyClick(Qt.Key_Tab)
      tryCompare(control("quicklinkUrl"), "activeFocus", true)
      typeText("javascript:alert(1)")
      verify(control("quicklinkValidation").text.indexOf("HTTP") >= 0)
      verify(!control("quicklinkSave").enabled)
      keyClick(Qt.Key_A, Qt.ControlModifier)
      typeText("https://[not-ipv6]/")
      compare(control("quicklinkValidation").text, UrlCases.cases.find(entry => entry.url === "https://[not-ipv6]/").error)
      verify(!control("quicklinkSave").enabled)
      keyClick(Qt.Key_A, Qt.ControlModifier)
      typeText("https://example.test/?q={QUERY}")
      compare(control("quicklinkPreview").text, "Preview: https://example.test/?q=hello%20world")
      keyClick(Qt.Key_Tab)
      typeText("app")
      compare(control("quicklinkValidation").text, UrlCases.cases.find(entry => entry.alias === "app").error)
      verify(!control("quicklinkSave").enabled)
      keyClick(Qt.Key_A, Qt.ControlModifier)
      typeText("mine")
      for (const name of ["titleFieldLabel", "urlFieldLabel", "aliasFieldLabel", "iconFieldLabel"]) {
        const label = control(name)
        verify(label !== null && label.visible && label.text.length > 0)
      }
      keyClick(Qt.Key_Tab)
      typeText("help-browser")
      keyClick(Qt.Key_Return)
      compare(fixture.requests[fixture.requests.length - 1].operation, Protocol.Operations.QuicklinksSave)
      const draft = fixture.requests[fixture.requests.length - 1].quicklink
      compare(draft.title, "My docs")
      compare(draft.alias, "mine")
      compare(draft.icon, "help-browser")
      reply([link("configured"), { quicklink: draft, source: "user", enabled: true }], "Quicklink saved.")
      tryCompare(control("quicklinkAdd"), "activeFocus", true)
      keyClick(Qt.Key_Return)
      tryCompare(control("quicklinkTitle"), "activeFocus", true)
      keyClick(Qt.Key_Escape)
      tryCompare(control("quicklinkAdd"), "activeFocus", true)
    }

    function test_refresh_queues_actions_and_preserves_row_focus() {
      openCard()
      keyClick(Qt.Key_Tab)
      const button = control("quicklinkEdit-configured")
      tryCompare(button, "activeFocus", true)
      controller.load()
      const count = fixture.requests.length
      keyClick(Qt.Key_Space)
      compare(fixture.requests.length, count)
      compare(controller.pending.length, 1)
      reply([link("configured")])
      compare(fixture.requests.length, count + 1)
      compare(controller.active.request.operation, Protocol.Operations.QuicklinksDisable)
      reply([link("configured", "configured", false)], "Quicklink disabled.")
      tryCompare(control("quicklinkEdit-configured"), "activeFocus", true)
      compare(control("quicklinkEdit-configured").text, "Enable")
      keyClick(Qt.Key_Return)
      compare(controller.active.request.operation, Protocol.Operations.QuicklinksEnable)
      reply([link("configured")])
      tryCompare(control("quicklinkEdit-configured"), "activeFocus", true)
    }

    function test_older_save_cannot_close_a_newer_edit_of_the_same_link_data() {
      return [{ tag: "successful A", failFirst: false }, { tag: "failed A", failFirst: true }]
    }

    function test_older_save_cannot_close_a_newer_edit_of_the_same_link(data) {
      openCard()
      controller.load()
      reply([link("user", "user")])
      keyClick(Qt.Key_Tab)
      tryCompare(control("quicklinkEdit-user"), "activeFocus", true)
      keyClick(Qt.Key_Return)
      tryCompare(control("quicklinkTitle"), "activeFocus", true)
      keyClick(Qt.Key_A, Qt.ControlModifier)
      typeText("First edit")
      keyClick(Qt.Key_Return)
      const first = controller.active.request.quicklink
      keyClick(Qt.Key_Escape)
      tryCompare(control("quicklinkEdit-user"), "activeFocus", true)
      keyClick(Qt.Key_Return)
      tryCompare(control("quicklinkTitle"), "activeFocus", true)
      keyClick(Qt.Key_A, Qt.ControlModifier)
      typeText("Second edit")
      keyClick(Qt.Key_Return)
      compare(controller.pending.length, 1)
      if (data.failFirst) {
        const id = controller.active.request.requestId
        controller.handleMessage({ requestId: id, type: Protocol.Messages.Error, error: "First save failed." })
        compare(controller.errorText, "")
        verify(!control("quicklinkSave").enabled)
        controller.handleMessage({ requestId: id, type: Protocol.Messages.Done })
      } else reply([{ quicklink: first, source: "user", enabled: true }], "Quicklink saved.")
      verify(control("quicklinkTitle").visible)
      compare(control("quicklinkTitle").text, "Second edit")
      const second = controller.active.request.quicklink
      reply([{ quicklink: second, source: "user", enabled: true }], "Quicklink saved.")
      tryCompare(control("quicklinkEdit-user"), "activeFocus", true)
      compare(controller.errorText, "")
      compare(controller.statusText, "Quicklink saved.")
      compare(control("quicklinksFeedback").text, "Quicklink saved.")
    }

    function test_mouse_toggle_and_refresh_keep_search_ready() {
      openCard()
      mouseClick(control("quicklinkEdit-configured"))
      tryCompare(panel.searchControl, "activeFocus", true)
      typeText(" web")
      compare(panel.searchControl.text, "quicklinks web")
      reply([link("configured", "configured", false)])
      tryCompare(panel.searchControl, "activeFocus", true)
      mouseClick(control("quicklinksRefresh"))
      typeText(" searches")
      compare(panel.searchControl.text, "quicklinks web searches")
      reply([link("configured", "configured", false)])
      tryCompare(panel.searchControl, "activeFocus", true)
    }

    function test_late_save_keeps_focus_in_filters_data() {
      return ["escape", "cancel", "leave search", "close settings"].map(action => ({ tag: action, action: action }))
    }

    function test_late_save_keeps_focus_in_filters(data) {
      openCard()
      keyClick(Qt.Key_Return)
      tryCompare(control("quicklinkTitle"), "activeFocus", true)
      typeText("Delayed docs")
      keyClick(Qt.Key_Tab)
      typeText("https://example.test/?q={query}")
      keyClick(Qt.Key_Return)
      const draft = controller.active.request.quicklink

      if (data.action === "escape") keyClick(Qt.Key_Escape)
      if (data.action === "cancel") mouseClick(control("quicklinkCancel"))
      if (data.action === "close settings") panel.visible = false
      panel.visible = true
      panel.searchControl.text = ""
      panel.showPage("filters")
      wait(0)
      waitForRendering(panel)
      mouseClick(panel.filterAliasInputControl)
      tryCompare(panel.filterAliasInputControl, "activeFocus", true)
      typeText("before")

      reply([{ quicklink: draft, source: "user", enabled: true }], "Quicklink saved.")
      wait(0)
      verify(panel.filterAliasInputControl.activeFocus, "late save leaves the active editor focused")
      typeText("after")
      compare(panel.filterAliasInputControl.text, "beforeafter")
      panel.filterAliasInputControl.text = ""
    }

    function test_deferred_row_focus_cannot_leave_the_current_editor_data() {
      const cases = []
      for (const action of ["refresh", "toggle"]) {
        for (const surface of ["quicklink", "filters"]) {
          cases.push({ tag: action + ": " + surface, action: action, surface: surface })
        }
      }
      return cases
    }

    function test_deferred_row_focus_cannot_leave_the_current_editor(data) {
      openCard()
      keyClick(Qt.Key_Tab)
      tryCompare(control("quicklinkEdit-configured"), "activeFocus", true)
      if (data.action === "refresh") controller.load()
      else keyClick(Qt.Key_Space)
      reply([link("configured", "configured", data.action === "refresh")])

      let field
      if (data.surface === "quicklink") {
        mouseClick(control("quicklinkAdd"))
        field = control("quicklinkTitle")
      } else {
        panel.searchControl.text = ""
        panel.showPage("filters")
        mouseClick(panel.searchControl)
        field = panel.searchControl
      }
      wait(0)
      verify(field.activeFocus, "replacement cannot restore a row over a new editor")
      typeText("draft")
      compare(field.text, "draft")
      field.text = ""
    }

    function test_deferred_dismissal_cannot_leave_a_new_quicklink_editor_data() {
      return ["editor escape", "editor cancel", "remove cancel", "remove accept"].map(action =>
        ({ tag: action, action: action }))
    }

    function test_deferred_dismissal_cannot_leave_a_new_quicklink_editor(data) {
      openCard()
      controller.load()
      reply([link("user", "user")])
      keyClick(Qt.Key_Tab)
      tryCompare(control("quicklinkEdit-user"), "activeFocus", true)
      if (data.action.indexOf("editor") === 0) {
        keyClick(Qt.Key_Return)
        tryCompare(control("quicklinkTitle"), "activeFocus", true)
        if (data.action === "editor escape") keyClick(Qt.Key_Escape)
        else mouseClick(control("quicklinkCancel"))
      } else {
        keyClick(Qt.Key_Tab)
        keyClick(Qt.Key_Return)
        const confirmation = control("quicklinkRemoveConfirmation")
        tryCompare(confirmation.cancelControl, "activeFocus", true)
        if (data.action === "remove cancel") keyClick(Qt.Key_Escape)
        else {
          keyClick(Qt.Key_Right)
          keyClick(Qt.Key_Return)
        }
      }

      mouseClick(control("quicklinkAdd"))
      wait(0)
      verify(control("quicklinkTitle").activeFocus, "dismissal cannot restore the old session over a new editor")
      typeText("New draft")
      compare(control("quicklinkTitle").text, "New draft")
      if (data.action === "remove accept") {
        reply([])
        wait(0)
        verify(control("quicklinkTitle").activeFocus)
        typeText(" after removal")
        compare(control("quicklinkTitle").text, "New draft after removal")
      }
    }

    function test_mouse_add_immediate_typing_edit_and_remove_confirmation() {
      openCard()
      mouseClick(control("quicklinkAdd"))
      tryCompare(control("quicklinkTitle"), "activeFocus", true)
      typeText("Mouse docs")
      keyClick(Qt.Key_Tab)
      typeText("https://example.test/static")
      keyClick(Qt.Key_Return)
      const draft = controller.active.request.quicklink
      reply([{ quicklink: draft, source: "user", enabled: true }], "Quicklink saved.")
      tryCompare(control("quicklinkAdd"), "activeFocus", true)
      keyClick(Qt.Key_Tab)
      tryCompare(control("quicklinkEdit-" + draft.id), "activeFocus", true)
      keyClick(Qt.Key_Return)
      tryCompare(control("quicklinkTitle"), "activeFocus", true)
      keyClick(Qt.Key_A, Qt.ControlModifier)
      typeText("Renamed")
      keyClick(Qt.Key_Escape)
      tryCompare(control("quicklinkEdit-" + draft.id), "activeFocus", true)
      keyClick(Qt.Key_Tab)
      keyClick(Qt.Key_Return)
      const confirmation = control("quicklinkRemoveConfirmation")
      tryCompare(confirmation.cancelControl, "activeFocus", true)
      keyClick(Qt.Key_Backtab)
      tryCompare(confirmation.confirmControl, "activeFocus", true)
      keyClick(Qt.Key_Tab)
      tryCompare(confirmation.cancelControl, "activeFocus", true)
      keyClick(Qt.Key_Escape)
      tryCompare(control("quicklinkRemove-" + draft.id), "activeFocus", true)
      keyClick(Qt.Key_Space)
      tryCompare(confirmation.cancelControl, "activeFocus", true)
      keyClick(Qt.Key_Right)
      keyClick(Qt.Key_Return)
      compare(controller.active.request.operation, Protocol.Operations.QuicklinksRemove)
      verify(controller.active.request.confirmed)
      reply([], "Web fallback reset to the default.")
      tryCompare(control("quicklinkAdd"), "activeFocus", true)
      compare(controller.statusText, "Web fallback reset to the default.")
    }
  }
}
