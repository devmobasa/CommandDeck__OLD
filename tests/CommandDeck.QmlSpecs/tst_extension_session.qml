import QtQuick
import "../../qml/common/Protocol.js" as Protocol
import QtTest
import "../../qml/common" as CommandDeck

Item {
  width: 800
  height: 600
  property int requestId: 0
  property var sent: []
  CommandDeck.ExtensionSessionController {
    id: controller
    nextRequestId: function() { return ++requestId }
    onRequest: value => sent.push(value)
  }
  SignalSpy { id: externalActions; target: controller; signalName: "externalActionCompleted" }
  CommandDeck.Theme { id: theme; loadShellTokens: false }
  CommandDeck.ExtensionViewHost {
    id: host
    anchors.fill: parent
    theme: theme
    opened: controller.opened && !controller.externalActionPending
    snapshot: controller.snapshot
    busy: controller.busy
    debugLease: controller.debugLease
    confirmation: controller.confirmation
    onRemoteDecided: confirmed => controller.decideRemote(confirmed)
    onActionRequested: (revision, itemId, handle) => controller.activate(revision, itemId, handle)
  }
  DeadlineTestCase {
    name: "ExtensionSessionLifecycle"
    when: windowShown
    function open() { controller.open({ id: "extension:package:uuids", title: "UUID Tools" }); return requestId }
    function test_dynamic_search_activation_preserves_host_lease() {
      controller.open({ id: "extension-item:package:notes:item", title: "Meeting", metadata: { extensionSearchLease: "lease-one" } })
      compare(sent[sent.length - 1].extensionInput.searchLease, "lease-one")
      compare(sent[sent.length - 1].resultId, "extension-item:package:notes:item")
      controller.close()
      controller.open({ id: "extension-item:package:notes:next", title: "Next", metadata: { extensionSearchLease: "lease-two" } })
      controller.handleMessage({ requestId: controller.closingRequestId, type: Protocol.Messages.Done })
      compare(sent[sent.length - 1].extensionInput.searchLease, "lease-two")
    }
    function state(request, session, status, revision) {
      controller.handleMessage({ requestId: request, type: Protocol.Messages.ExtensionSession, extensionSession: {
        sessionId: session, packageKey: "package", title: "UUID Tools", status: status,
        revision: revision, content: status === "view" ? { title: "UUIDs", items: [] } : null
      } })
    }
    function init() {
      controller.close()
      controller.disconnected()
      requestId = 0
      sent = []
      externalActions.clear()
    }
    function test_url_handoff_waits_for_matching_success() {
      const request = open()
      state(request, "session", "ready", 1)
      controller.snapshot = { revision: 1, content: { actions: [{ kind: Protocol.ExtensionActionKinds.OpenUrl, handle: "url" }] } }
      controller.activate(1, "", "url")
      const action = requestId
      compare(controller.externalActionPending, true)
      compare(controller.opened, true, "keep session alive while the desktop effect runs")
      controller.handleMessage({ requestId: action - 1, type: Protocol.Messages.Done })
      compare(controller.externalActionPending, true)
      compare(externalActions.count, 0)
      controller.handleMessage({ requestId: action, type: Protocol.Messages.Done })
      compare(externalActions.count, 1)
      compare(controller.externalActionPending, false)
    }
    function test_remote_confirmation_cancel_focus_keyboard_and_exact_identity() {
      const request = open()
      state(request, "session", "view", 1)
      state(request, "session", "ready", 1)
      controller.handleMessage({ requestId: request, type: Protocol.Messages.Done })
      waitForValue(host.inputControl, "activeFocus", true)
      controller.handleMessage({ requestId: request, type: Protocol.Messages.ExtensionSession, extensionSession: {
        sessionId: "session", packageKey: "package", title: "Tasks", status: "confirmation",
        confirmation: { id: "exact-request", connection: "tasks", method: "POST", destination: "https://example.com/tasks", body: "create", idempotencyKey: "one-request" }
      } })
      waitForValue(host.remoteCancelControl, "activeFocus", true)
      keyClick(Qt.Key_Tab)
      waitForValue(host.remoteApproveControl, "activeFocus", true)
      keyClick(Qt.Key_Tab)
      waitForValue(host.remoteBodyControl, "activeFocus", true)
      keyClick(Qt.Key_Tab)
      waitForValue(host.remoteCancelControl, "activeFocus", true)
      keyClick(Qt.Key_Escape)
      compare(sent[sent.length - 1].extensionInput.confirmationId, "exact-request")
      compare(sent[sent.length - 1].extensionInput.confirmed, false)
      waitForValue(host.inputControl, "activeFocus", true)
      controller.confirmation = { id: "another-request", connection: "tasks", method: "POST", destination: "https://example.com/tasks", body: "second", idempotencyKey: "another-key" }
      waitForValue(host.remoteCancelControl, "activeFocus", true)
      mouseClick(host.remoteApproveControl)
      compare(sent[sent.length - 1].extensionInput.confirmationId, "another-request")
      compare(sent[sent.length - 1].extensionInput.confirmed, true)
      controller.disconnected()
      compare(host.remoteDialogControl.visible, false)
    }
    function test_debug_lease_is_visible_during_startup_and_clears_on_reopen() {
      const request = open()
      controller.handleMessage({ requestId: request, type: Protocol.Messages.ExtensionSession, extensionSession: {
        sessionId: "debug", packageKey: "package", title: "Development", status: "starting",
        debug: { commandId: "uuids", digest: "build", expiresAt: "later", socketPath: "/private/debug.sock", consumed: true }
      } })
      compare(host.debugLease.socketPath, "/private/debug.sock")
      verify(host.listControl.y >= 64)
      controller.close()
      controller.disconnected()
      open()
      compare(controller.debugLease, null)
      compare(host.listControl.y, 0)
    }
    function test_url_handoff_from_keyboard_and_mouse_data() {
      return [{ tag: "keyboard", mouse: false }, { tag: "mouse", mouse: true }]
    }
    function test_url_handoff_from_keyboard_and_mouse(data) {
      const request = open()
      state(request, "session", "ready", 1)
      controller.snapshot = { revision: 1, content: { kind: "list", id: "urls", title: "Links", items: [
        { id: "row", title: "Specification", actions: [{ id: "open", title: "Open specification", kind: Protocol.ExtensionActionKinds.OpenUrl, handle: "url" }] }
      ] } }
      waitForValue(host.inputControl, "activeFocus", true)
      if (data.mouse) mouseClick(host.primaryControl)
      else keyClick(Qt.Key_Return)
      compare(sent[sent.length - 1].operation, "extension.action")
      compare(controller.externalActionPending, true)
      compare(host.opened, false)
      const action = requestId
      controller.handleMessage({ requestId: action, type: Protocol.Messages.Error, error: "Denied" })
      controller.handleMessage({ requestId: action, type: Protocol.Messages.Done })
      waitForValue(host.inputControl, "activeFocus", true)
      keyClick(Qt.Key_S)
      compare(host.inputControl.text, "s", "failed handoff restores typing without a click")
    }
    function test_rejected_url_restores_overlay_without_dismissing() {
      const request = open()
      state(request, "session", "ready", 1)
      controller.snapshot = { revision: 1, content: { items: [{ id: "row", actions: [{ kind: Protocol.ExtensionActionKinds.OpenUrl, handle: "url" }] }] } }
      controller.activate(1, "row", "url")
      const action = requestId
      compare(controller.externalActionPending, true)
      controller.handleMessage({ requestId: action, type: Protocol.Messages.Error, error: "Permission denied" })
      compare(controller.externalActionPending, false)
      controller.handleMessage({ requestId: action, type: Protocol.Messages.Done })
      compare(externalActions.count, 0)
      compare(controller.opened, true)
      compare(controller.error, "Permission denied")
    }
    function test_copy_keeps_overlay_and_disconnect_recovers_url() {
      const request = open()
      state(request, "session", "ready", 1)
      controller.snapshot = { revision: 1, content: { actions: [{ kind: Protocol.ExtensionActionKinds.CopyText, handle: "copy" }, { kind: Protocol.ExtensionActionKinds.OpenUrl, handle: "url" }] } }
      controller.activate(1, "", "copy")
      compare(controller.externalActionPending, false)
      controller.handleMessage({ requestId: requestId, type: Protocol.Messages.Done })
      compare(externalActions.count, 0)
      controller.activate(1, "", "url")
      compare(controller.externalActionPending, true)
      controller.disconnected()
      compare(controller.externalActionPending, false)
      compare(controller.failed, true)
    }
    function test_action_stays_busy_until_its_own_completion() {
      const request = open()
      state(request, "session", "ready", 1)
      controller.activate(1, "uuid", "copy-1")
      const first = requestId
      state(request, "session", "ready", 2)
      compare(controller.busy, true, "ready is not the action request completion")
      controller.activate(2, "uuid", "copy-2")
      compare(requestId, first)
      controller.handleMessage({ requestId: first, type: Protocol.Messages.Error, error: "Copy failed" })
      compare(controller.busy, true, "an error still awaits the matching done")
      controller.handleMessage({ requestId: first, type: Protocol.Messages.Done })
      controller.activate(2, "uuid", "copy-2")
      const second = requestId
      verify(second > first)
      controller.handleMessage({ requestId: first, type: Protocol.Messages.Done })
      controller.handleMessage({ requestId: first, type: Protocol.Messages.Error, error: "old error" })
      compare(controller.busy, true)
      compare(controller.error, "")
      controller.activate(2, "uuid", "copy-2")
      compare(requestId, second)
      controller.handleMessage({ requestId: second, type: Protocol.Messages.Done })
      compare(controller.busy, false)
    }
    function test_no_view_completion_does_not_fail_or_cancel_finished_work() {
      const request = open()
      state(request, "session", "running", 0)
      compare(controller.noView, true)
      state(request, "session", "completed", 0)
      compare(controller.completed, true)
      compare(controller.failed, false)
      compare(controller.busy, false)
      compare(controller.sessionId, "")
      const count = sent.length
      controller.close()
      compare(sent.length, count)
      open()
      compare(controller.completed, false)
    }
    function test_reopen_waits_for_close_completion() {
      const request = open()
      state(request, "old", "ready", 1)
      controller.close()
      const close = requestId
      open()
      compare(requestId, close, "do not open while daemon cleanup owns the connection")
      state(request, "old", "closed", 1)
      compare(requestId, close, "terminal notification precedes ownership release")
      controller.handleMessage({ requestId: close, type: Protocol.Messages.Done })
      compare(sent[sent.length - 1].operation, "extension.open")
      verify(requestId > close)
      state(requestId, "new", "ready", 1)
      compare(controller.sessionId, "new")
      compare(controller.failed, false)
    }
    function test_startup_reopen_waits_for_cancelled_open_not_cancel_ack() {
      const request = open()
      controller.close()
      const cancel = requestId
      open()
      compare(requestId, cancel)
      controller.handleMessage({ requestId: cancel, type: Protocol.Messages.Done })
      compare(requestId, cancel)
      controller.handleMessage({ requestId: request, type: Protocol.Messages.Error, error: "Cancelled" })
      controller.handleMessage({ requestId: request, type: Protocol.Messages.Done })
      verify(requestId > cancel)
      compare(sent[sent.length - 1].operation, "extension.open")
      compare(controller.error, "")
    }
    function test_close_or_disconnect_cancels_queued_reopen() {
      const request = open()
      state(request, "old", "ready", 1)
      controller.close()
      const close = requestId
      open()
      controller.close()
      controller.handleMessage({ requestId: close, type: Protocol.Messages.Done })
      compare(requestId, close)
      compare(controller.opened, false)
      const next = open()
      state(next, "next", "ready", 1)
      controller.close()
      const closing = requestId
      open()
      controller.disconnected()
      controller.handleMessage({ requestId: closing, type: Protocol.Messages.Done })
      compare(requestId, closing)
      compare(controller.failed, true)
    }
    function test_disconnect_retires_stream_before_reopen() {
      const old = open()
      state(old, "old-session", "view", 1)
      state(old, "old-session", "ready", 1)
      controller.disconnected()
      state(old, "old-session", "view", 2)
      compare(controller.snapshot.revision, 1)
      compare(controller.sessionId, "")
      compare(controller.failed, true)
      controller.restart()
      const next = requestId
      state(old, "old-session", "failed", 2)
      compare(controller.failed, false)
      state(next, "new-session", "view", 1)
      state(next, "new-session", "ready", 1)
      state(next, "foreign-session", "view", 99)
      compare(controller.sessionId, "new-session")
      compare(controller.snapshot.revision, 1)
    }
    function test_close_starting_cancels_and_ignores_late_open() {
      const request = open()
      controller.close()
      compare(sent[sent.length - 1].operation, "cancel")
      compare(sent[sent.length - 1].targetRequestId, request)
      state(request, "late-session", "view", 1)
      compare(controller.opened, false)
      compare(controller.sessionId, "")
    }
    function test_busy_prevents_duplicate_action_and_disable_refreshes_revision() {
      const request = open()
      state(request, "session", "view", 1)
      state(request, "session", "ready", 1)
      controller.present(1)
      controller.activate(1, "uuid", "copy")
      const action = requestId
      controller.activate(1, "uuid", "copy")
      compare(requestId, action)
      compare(sent[sent.length - 1].extensionInput.sequence, 2)
      controller.handleMessage({ requestId: action, type: Protocol.Messages.Error, error: "Grant required" })
      controller.handleMessage({ requestId: action, type: Protocol.Messages.Done })
      compare(controller.busy, false)
      compare(controller.error, "Grant required")
      state(request, "session", "failed", 1)
      controller.disable()
      const list = requestId
      controller.handleMessage({ requestId: list, type: Protocol.Messages.ExtensionsResult, extensions: { revision: 42 } })
      compare(sent[sent.length - 1].extension.expectedRevision, 42)
      compare(sent[sent.length - 1].extension.key, "package")
      compare(sent[sent.length - 1].extension.action, "disable")
      controller.handleMessage({ requestId: requestId, type: Protocol.Messages.ExtensionsResult, extensions: { revision: 43 } })
      compare(controller.opened, false)
    }
  }
}
