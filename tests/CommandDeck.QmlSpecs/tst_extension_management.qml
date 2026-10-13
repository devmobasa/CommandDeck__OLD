import QtQuick
import "../../qml/common/Protocol.js" as Protocol
import QtQuick.Controls
import QtTest
import "../../qml/common" as CommandDeck

Item {
  width: 1000; height: 700
  property int sequence: 0
  property var requests: []
  property int browserHandoffs: 0
  property string scopedToken: ""
  property string scopedLabel: ""
  CommandDeck.Theme { id: theme; loadShellTokens: false }
  CommandDeck.ExtensionManagementController {
    id: controller
    nextRequestId: () => ++sequence
    onRequest: value => requests.push(value)
  }
  Button { id: opener; text: "Manage extensions"; onClicked: panel.opened = true }
  CommandDeck.ExtensionManagementView {
    id: panel
    anchors.fill: parent
    theme: theme; controller: controller; returnFocus: opener
    onDismissed: opened = false
    onBrowserLoginRequested: { browserHandoffs++; opened = false }
    onScopedSearchRequested: (token, label) => { scopedToken = token; scopedLabel = label }
  }
  LiteralTextProbe { id: literal }
  TestCase {
    name: "ExtensionManagement"
    when: windowShown
    function inventory() { return { revision: 7, enabled: true, packages: [{ key: "draft", name: "Local Draft", id: "org.example.draft", version: "1.0.0", digest: "current", retainedDigests: ["current", "previous"], grants: ["storage.read"], enabled: true }], offset: 0, total: 1 } }
    function respond(action, value) {
      const id = controller.activeRequestId
      controller.handleMessage({ requestId: id, type: Protocol.Messages.ExtensionsResult, extensions: value })
      controller.handleMessage({ requestId: id, type: Protocol.Messages.Done })
      // Rows shown by the response keep stale positions until the layout
      // polishes; a real click before that can land on a neighbouring control.
      waitForRendering(panel)
    }
    function init() {
      panel.opened = false; controller.disconnected(); requests = []; sequence = 0; browserHandoffs = 0
      panel.searchControl.text = ""
      mouseClick(opener)
      respond("list", inventory())
    }
    function detail() {
      panel.inspect(inventory().packages[0])
      respond("inspect", Object.assign(inventory(), { manifest: { name: "Local Draft", version: "1.0.0", preferences: [{ id: "title", type: "text", title: "Default title", required: true, default: "Draft" }], capabilities: { "storage.read": { reason: "Load draft" }, "storage.write": { reason: "Save draft" } } }, settings: { title: "Saved default" } }))
    }
    function test_management_pages_use_the_launcher_theme() {
      compare(panel.searchControl.font.family, theme.fontFamily)
      verify(Qt.colorEqual(panel.searchControl.background.color, theme.surfaceStrong), "search uses the launcher field")
      verify(Qt.colorEqual(panel.palette.highlight, theme.withAlpha(theme.accent, 0.28)),
        "the current package row uses the launcher accent, not the platform highlight")
      verify(Qt.colorEqual(panel.palette.window, theme.popupBackground), "stock controls inherit the launcher palette")
    }

    function test_search_scope_and_global_query_consent() {
      detail()
      panel.selected = Object.assign({}, panel.selected, { key: "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef" })
      panel.manifest = Object.assign({}, panel.manifest, { searchProviders: [{ id: "notes", title: "Notes", commandId: "edit", cacheKey: "cache" }] })
      waitForRendering(panel)
      mouseClick(panel.scopedSearchControl)
      compare(scopedToken, "extension-search:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef:notes")
      compare(scopedLabel, "Local Draft · Notes")
      mouseClick(panel.globalSearchControl)
      tryCompare(panel.cancelControl, "activeFocus", true)
      keyClick(Qt.Key_Escape)
      verify(requests[requests.length - 1].extension.action !== "global-search")
      mouseClick(panel.globalSearchControl)
      tryCompare(panel.cancelControl, "activeFocus", true)
      keyClick(Qt.Key_Tab)
      keyClick(Qt.Key_Space)
      const change = requests[requests.length - 1].extension
      compare(change.action, "global-search")
      compare(change.confirmed, true)
      compare(change.globalSearchEnabled, true)
    }
    function test_search_keyboard_inspect_and_configure() {
      tryCompare(panel.searchControl, "activeFocus", true)
      keyClick(Qt.Key_D)
      compare(requests[requests.length - 1].query, "d")
      respond("list", inventory())
      keyClick(Qt.Key_Down)
      keyClick(Qt.Key_Return)
      compare(requests[requests.length - 1].extension.action, "inspect")
      respond("inspect", Object.assign(inventory(), { manifest: { name: "Draft", preferences: [{ id: "title", type: "text", title: "Title", required: true, default: "Default" }] }, settings: { title: "Saved" } }))
      panel.configureSettings()
      const editor = panel.settingsControl.fieldsControl.itemAt(0).editor
      tryCompare(editor, "activeFocus", true)
      compare(editor.text, "Saved")
      keyClick(Qt.Key_A, Qt.ControlModifier)
      keyClick(Qt.Key_N)
      mouseClick(panel.settingsControl.submitControl)
      compare(requests[requests.length - 1].extension.action, "configure")
      compare(requests[requests.length - 1].extension.settings.title, "n")
      compare(requests[requests.length - 1].extension.expectedRevision, 7)
    }
    // Package names and permission reasons come from the extension's manifest;
    // markup in them cannot restyle or hide what is being granted.
    function test_package_names_and_permission_reasons_are_shown_literally() {
      const name = "<b>Draft</b> &amp; notes"
      const reason = "<span style='font-size:1px'>Reads every file</span>"
      const listed = inventory()
      listed.packages[0].name = name
      tryCompare(panel.searchControl, "activeFocus", true)
      keyClick(Qt.Key_D)
      respond("list", listed)
      compare(literal.problem(panel.packagesControl.itemAtIndex(0), name + " · 1.0.0 · Enabled"), "")

      keyClick(Qt.Key_Down)
      keyClick(Qt.Key_Return)
      compare(requests[requests.length - 1].extension.action, "inspect")
      respond("inspect", Object.assign(listed, { manifest: { name: name, version: "1.0.0",
        capabilities: { "files.read": { reason: reason } } } }))
      compare(literal.problem(panel, "files.read — " + reason), "")
    }
    function test_diagnostics_mouse_keyboard_refresh_and_back() {
      detail()
      mouseClick(panel.logsControl)
      compare(requests[requests.length - 1].extension.action, "logs")
      compare(requests[requests.length - 1].extension.includeStderr, false)
      const value = Object.assign(inventory(), { diagnostics: [
        { at: "now", sessionId: "s1", commandId: "edit", digest: "build1", status: "failed" },
        { at: "now", sessionId: "s2", commandId: "copy", digest: "build2", status: "closed" }
      ], debug: { commandId: "edit", consumed: false, expiresAt: "later", socketPath: "/private/debug.sock" } })
      respond("logs", value)
      const view = panel.diagnosticsControl
      tryCompare(view.searchControl, "activeFocus", true)
      compare(view.listControl.count, 2)
      mouseClick(view.listControl.itemAtIndex(0))
      keyClick(Qt.Key_E)
      keyClick(Qt.Key_D)
      keyClick(Qt.Key_I)
      compare(view.searchControl.text, "edi")
      compare(view.listControl.count, 1)
      keyClick(Qt.Key_Down)
      keyClick(Qt.Key_T)
      compare(view.searchControl.text, "edit")
      verify(view.searchControl.activeFocus)
      mouseClick(panel.reloadControl)
      respond("logs", value)
      verify(view.searchControl.activeFocus)
      compare(view.searchControl.text, "edit")
      keyClick(Qt.Key_Tab)
      tryCompare(view.stderrControl, "activeFocus", true)
      keyClick(Qt.Key_Space)
      compare(requests[requests.length - 1].extension.includeStderr, true)
      respond("logs", value)
      verify(view.stderrControl.activeFocus)
      keyClick(Qt.Key_Tab)
      tryCompare(view.stopControl, "activeFocus", true)
      keyClick(Qt.Key_Space)
      compare(requests[requests.length - 1].extension.action, "debug-stop")
      respond("debug-stop", Object.assign(inventory(), { diagnostics: [], debug: null }))
      tryCompare(view.searchControl, "activeFocus", true)
      keyClick(Qt.Key_Escape)
      compare(view.searchControl.text, "")
      keyClick(Qt.Key_Escape)
      compare(panel.page, "detail")
      tryCompare(panel.logsControl, "activeFocus", true)
    }
    function test_oauth_approval_releases_surface_before_sending_login() {
      detail()
      mouseClick(panel.connectionsButtonControl)
      respond("connections", Object.assign(inventory(), { connections: [{ name: "tasks", kind: "http", scope: "https://example.test/tasks", credentialStatus: "disconnected" }] }))
      const view = panel.connectionsControl
      tryCompare(view.searchControl, "activeFocus", true)
      mouseClick(view.listControl.itemAtIndex(0))
      mouseClick(view.loginControl)
      tryCompare(panel.cancelControl, "activeFocus", true)
      keyClick(Qt.Key_Escape)
      compare(browserHandoffs, 0)
      mouseClick(view.loginControl)
      tryCompare(panel.cancelControl, "activeFocus", true)
      keyClick(Qt.Key_Tab)
      keyClick(Qt.Key_Space)
      compare(browserHandoffs, 1)
      compare(panel.opened, false)
      compare(requests[requests.length - 1].extension.action, "connection-login")
      respond("connection-login", inventory())
      compare(panel.opened, false)
    }
    function test_remote_history_reset_requires_confirmation_and_restores_focus() {
      detail()
      mouseClick(panel.connectionsButtonControl)
      respond("connections", Object.assign(inventory(), { connections: [], effectAttempts: 12 }))
      const view = panel.connectionsControl
      compare(view.effectAttempts, 12)
      mouseClick(view.resetEffectsControl)
      tryCompare(panel.cancelControl, "activeFocus", true)
      keyClick(Qt.Key_Escape)
      compare(panel.page, "connections")
      tryCompare(view.resetEffectsControl, "activeFocus", true)
      verify(requests[requests.length - 1].extension.action !== "effects-reset")
      keyClick(Qt.Key_Space)
      tryCompare(panel.cancelControl, "activeFocus", true)
      keyClick(Qt.Key_Tab)
      keyClick(Qt.Key_Space)
      const input = requests[requests.length - 1].extension
      compare(input.action, "effects-reset")
      compare(input.confirmed, true)
      respond("effects-reset", Object.assign(inventory(), { connections: [], effectAttempts: 0 }))
      compare(view.effectAttempts, 0)
      tryCompare(view.resetEffectsControl, "activeFocus", true)
    }
    function test_connection_setup_confirmation_typing_and_delayed_back() {
      detail()
      mouseClick(panel.connectionsButtonControl)
      respond("connections", Object.assign(inventory(), { connections: [{ name: "notes", kind: "files", scope: "/tmp/notes", credentialStatus: "none" }] }))
      const view = panel.connectionsControl
      tryCompare(view.searchControl, "activeFocus", true)
      mouseClick(view.listControl.itemAtIndex(0))
      keyClick(Qt.Key_N)
      compare(view.searchControl.text, "n")
      verify(view.searchControl.activeFocus)
      mouseClick(view.addControl)
      tryCompare(view.nameControl, "activeFocus", true)
      keyClick(Qt.Key_N)
      waitForRendering(view)
      mouseClick(view.pathControl)
      for (const character of "/TMP/NOTES") keyClick(character.charCodeAt(0))
      mouseClick(view.saveControl)
      compare(panel.page, "confirm")
      tryCompare(panel.cancelControl, "activeFocus", true)
      keyClick(Qt.Key_Escape)
      compare(panel.page, "connections")
      compare(view.editing, true)
      compare(view.pathControl.text, "/tmp/notes")
      mouseClick(view.saveControl)
      tryCompare(panel.cancelControl, "activeFocus", true)
      keyClick(Qt.Key_Tab)
      keyClick(Qt.Key_Space)
      const change = requests[requests.length - 1].extension
      compare(change.action, "connection-set")
      compare(change.connection.path, "/tmp/notes")
      compare(change.confirmed, true)
      respond("connection-set", Object.assign(inventory(), { connections: [] }))
      compare(panel.page, "connections")
      tryCompare(view.searchControl, "activeFocus", true)
      mouseClick(panel.reloadControl)
      const pending = controller.activeRequestId
      keyClick(Qt.Key_Escape)
      compare(view.searchControl.text, "")
      keyClick(Qt.Key_Escape)
      compare(panel.page, "detail")
      controller.handleMessage({ requestId: pending, type: Protocol.Messages.ExtensionsResult, extensions: inventory() })
      compare(panel.page, "detail")
    }
    function test_diagnostics_arrows_navigate_before_typing_returns_to_search() {
      detail()
      mouseClick(panel.logsControl)
      respond("logs", Object.assign(inventory(), { diagnostics: [
        { at: "now", sessionId: "s1", commandId: "edit", digest: "build1", status: "closed" },
        { at: "now", sessionId: "s2", commandId: "copy", digest: "build2", status: "closed" },
        { at: "now", sessionId: "s3", commandId: "save", digest: "build3", status: "closed" }
      ] }))
      const view = panel.diagnosticsControl
      tryCompare(view.searchControl, "activeFocus", true)
      keyClick(Qt.Key_Down)
      compare(view.listControl.currentIndex, 0)
      keyClick(Qt.Key_Down)
      compare(view.listControl.currentIndex, 1)
      keyClick(Qt.Key_Down)
      compare(view.listControl.currentIndex, 2)
      keyClick(Qt.Key_Up)
      compare(view.listControl.currentIndex, 1)
      verify(view.listControl.activeFocus)
      keyClick(Qt.Key_C)
      compare(view.searchControl.text, "c")
      tryCompare(view.searchControl, "activeFocus", true)
      keyClick(Qt.Key_O)
      compare(view.listControl.count, 1)
      compare(view.listControl.model[0].commandId, "copy")
    }
    function test_diagnostics_delayed_response_does_not_undo_back_data() {
      return [{ tag: "initial", reload: false }, { tag: "reload", reload: true }]
    }
    function test_diagnostics_delayed_response_does_not_undo_back(data) {
      detail()
      mouseClick(panel.logsControl)
      if (data.reload) {
        respond("logs", inventory())
        tryCompare(panel.diagnosticsControl.searchControl, "activeFocus", true)
        mouseClick(panel.reloadControl)
      }
      const pendingId = controller.activeRequestId
      keyClick(Qt.Key_Escape)
      const destination = data.reload ? "detail" : "list"
      compare(panel.page, destination)
      controller.handleMessage({ requestId: pendingId, type: Protocol.Messages.ExtensionsResult, extensions: inventory() })
      controller.handleMessage({ requestId: pendingId, type: Protocol.Messages.Done })
      wait(0)
      compare(panel.page, destination)
      if (data.reload) {
        tryCompare(panel.logsControl, "activeFocus", true)
        mouseClick(panel.logsControl)
        respond("logs", inventory())
        compare(panel.page, "logs")
        tryCompare(panel.diagnosticsControl.searchControl, "activeFocus", true)
      }
    }
    function test_remove_confirmation_traps_focus_and_cancel_restores() {
      detail()
      mouseClick(panel.removeControl)
      tryCompare(panel.cancelControl, "activeFocus", true)
      const count = requests.length
      keyClick(Qt.Key_Tab)
      keyClick(Qt.Key_Tab)
      keyClick(Qt.Key_Tab)
      verify(panel.cancelControl.activeFocus)
      keyClick(Qt.Key_Escape)
      compare(panel.page, "detail")
      tryCompare(panel.removeControl, "activeFocus", true)
      compare(requests.length, count)
      mouseClick(panel.removeControl)
      tryCompare(panel.cancelControl, "activeFocus", true)
      keyClick(Qt.Key_Backtab)
      keyClick(Qt.Key_Space)
      compare(requests[requests.length - 1].extension.confirmed, true)
      compare(requests[requests.length - 1].extension.eraseSettings, false)
    }
    function test_stale_list_and_disconnect_cannot_mutate() {
      controller.load("old", 0)
      const old = controller.activeRequestId
      controller.load("new", 0)
      controller.handleMessage({ requestId: old, type: Protocol.Messages.ExtensionsResult, extensions: { revision: 999 } })
      compare(controller.inventory.revision, 7)
      respond("list", inventory())
      controller.disconnected()
      const count = requests.length
      controller.change({ action: "disable", key: "draft" })
      compare(requests.length, count)
      verify(controller.error.indexOf("Disconnected") >= 0)
    }
    function test_mouse_reload_keeps_search_ready_for_typing() {
      tryCompare(panel.searchControl, "activeFocus", true)
      mouseClick(panel.reloadControl)
      respond("list", inventory())
      keyClick(Qt.Key_D)
      compare(panel.searchControl.text, "d")
      compare(requests[requests.length - 1].query, "d")
      verify(panel.searchControl.activeFocus)
    }
    function test_archive_review_pins_digest_before_confirmation() {
      panel.page = "archive"
      panel.archiveControl.text = "/tmp/draft.cdext"
      panel.archiveControl.accepted()
      compare(requests[requests.length - 1].extension.archivePath, "/tmp/draft.cdext")
      respond("inspect", Object.assign(inventory(), { manifest: { name: "Draft", capabilities: {} }, archiveDigest: "reviewed" }))
      compare(panel.page, "archive-review")
      compare(panel.archiveDigest, "reviewed")
      compare(panel.grants.length, 0)
      compare(panel.inspectedPath, "/tmp/draft.cdext")
    }
    function test_inventory_reload_preserves_settings_revision_data() {
      return [{ tag: "while editing", beforeEdit: false }, { tag: "before editing", beforeEdit: true }]
    }
    function test_inventory_reload_preserves_settings_revision(data) {
      detail()
      if (!data.beforeEdit) panel.configureSettings()
      mouseClick(panel.reloadControl)
      respond("list", Object.assign(inventory(), { revision: 8 }))
      if (data.beforeEdit) panel.configureSettings()
      const editor = panel.settingsControl.fieldsControl.itemAt(0).editor
      tryCompare(editor, "activeFocus", true)
      compare(editor.text, "Saved default")
      keyClick(Qt.Key_A, Qt.ControlModifier)
      keyClick(Qt.Key_N)
      mouseClick(panel.settingsControl.submitControl)
      const request = requests[requests.length - 1]
      compare(request.extension.expectedRevision, 7, "Reload must not give old settings a newer revision")
      controller.handleMessage({ requestId: request.requestId, type: Protocol.Messages.Error, error: "Settings changed elsewhere" })
      controller.handleMessage({ requestId: request.requestId, type: Protocol.Messages.Done })
      compare(editor.text, "n", "conflict preserves the unsaved draft")
      keyClick(Qt.Key_Escape)
      keyClick(Qt.Key_Escape)
      respond("list", Object.assign(inventory(), { revision: 8 }))
      tryCompare(panel.searchControl, "activeFocus", true)
      keyClick(Qt.Key_Return)
      respond("inspect", Object.assign(inventory(), { revision: 8, manifest: panel.manifest, settings: { title: "New saved value" } }))
      panel.configureSettings()
      const refreshed = panel.settingsControl.fieldsControl.itemAt(0).editor
      tryCompare(refreshed, "activeFocus", true)
      compare(refreshed.text, "New saved value")
      mouseClick(panel.settingsControl.submitControl)
      compare(requests[requests.length - 1].extension.expectedRevision, 8)
    }
    function test_number_settings_data() {
      return [{ tag: "optional unset", saved: undefined, value: "", required: false, omit: true },
        { tag: "optional cleared", saved: 12, value: "", required: false, omit: true },
        { tag: "optional whitespace", saved: undefined, value: " ", required: false, omit: true },
        { tag: "zero", saved: undefined, value: "0", required: false, omit: false },
        { tag: "required blank", saved: undefined, value: "", required: true, blocked: true },
        { tag: "invalid number", saved: undefined, value: "x", required: false, blocked: true }]
    }
    function test_number_settings(data) {
      panel.inspect(inventory().packages[0])
      respond("inspect", Object.assign(inventory(), { manifest: { name: "Draft", preferences: [
        { id: "amount", type: "number", title: "Amount", required: data.required }
      ] }, settings: data.saved === undefined ? {} : { amount: data.saved } }))
      panel.configureSettings()
      const editor = panel.settingsControl.fieldsControl.itemAt(0).editor
      tryCompare(editor, "activeFocus", true)
      keyClick(Qt.Key_A, Qt.ControlModifier)
      keyClick(Qt.Key_Backspace)
      if (data.value === " ") keyClick(Qt.Key_Space)
      else if (data.value === "0") keyClick(Qt.Key_0)
      else if (data.value === "x") keyClick(Qt.Key_X)
      const count = requests.length
      keyClick(Qt.Key_Tab)
      verify(panel.settingsControl.submitControl.activeFocus)
      keyClick(Qt.Key_Space)
      if (data.blocked) { compare(requests.length, count); return }
      compare(requests.length, count + 1, "valid optional settings must be saved")
      const values = requests[requests.length - 1].extension.settings
      if (data.omit) verify(!Object.prototype.hasOwnProperty.call(values, "amount"), "blank optional number must be absent")
      else { compare(values.amount, 0); compare(typeof values.amount, "number") }
    }
  }
}
