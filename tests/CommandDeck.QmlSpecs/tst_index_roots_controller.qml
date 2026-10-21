import QtQuick
import QtTest
import "../../qml/common" as Deck
import "../../qml/common/Protocol.js" as Protocol

DeadlineTestCase {
  id: testCase
  name: "IndexRootsController"
  property int sequence: 0
  property var sent: []
  Deck.IndexRootsController {
    id: controller
    nextRequestId: () => ++testCase.sequence
    onRequest: message => testCase.sent = testCase.sent.concat([message])
  }
  function init() {
    controller.opened = false
    controller.available = false
    controller.roots = []
    controller.error = ""
    sequence = 0
    sent = []
    controller.available = true
  }
  function test_mutations_queue_in_order_while_another_mutation_is_pending() {
    const before = sent.length
    const first = controller.change(Protocol.Operations.IndexDisable, "", "configured")
    const second = controller.change(Protocol.Operations.IndexAdd, "/queued", "")
    const third = controller.change(Protocol.Operations.IndexRemove, "", "added")
    verify(first > 0 && second > first && third > second)
    compare(sent.length, before + 1)
    compare(controller.refresh(), 0)
    controller.handleMessage({ requestId: first, type: Protocol.Messages.IndexRootsResult, indexRoots: { roots: [] } })
    compare(sent.length, before + 2)
    compare(sent[before + 1].path, "/queued")
    controller.handleMessage({ requestId: second, type: Protocol.Messages.Error, error: "Folder does not exist." })
    compare(sent.length, before + 3)
    compare(sent[before + 2].rootId, "added")
    compare(controller.error, "Folder does not exist.")
    controller.handleMessage({ requestId: third, type: Protocol.Messages.IndexRootsResult, indexRoots: { roots: [] } })
    compare(controller.error, "Folder does not exist.")
    verify(!controller.busy)
  }
  function test_action_error_survives_polling_until_retry() {
    const add = controller.change(Protocol.Operations.IndexAdd, "/missing", "")
    controller.handleMessage({ requestId: add, type: Protocol.Messages.Error, error: "Folder does not exist." })
    const poll = controller.refresh()
    compare(controller.error, "Folder does not exist.")
    controller.handleMessage({ requestId: poll, type: Protocol.Messages.IndexRootsResult, indexRoots: { roots: [] } })
    compare(controller.error, "Folder does not exist.")
    controller.change(Protocol.Operations.IndexAdd, "/missing", "")
    compare(controller.error, "")
  }
  function test_queued_corrected_add_clears_validation_error_when_dispatched() {
    const invalid = controller.change(Protocol.Operations.IndexAdd, "/missing", "")
    const corrected = controller.change(Protocol.Operations.IndexAdd, "/existing", "")
    controller.handleMessage({ requestId: invalid, type: Protocol.Messages.Error, error: "Folder does not exist." })
    compare(sent[sent.length - 1].requestId, corrected)
    compare(sent[sent.length - 1].path, "/existing")
    compare(controller.error, "")
    controller.handleMessage({ requestId: corrected, type: Protocol.Messages.IndexRootsResult,
      indexRoots: { roots: [{ id: "added", path: "/existing" }] } })
    compare(controller.roots[0].path, "/existing")
    compare(controller.error, "")
    verify(!controller.busy)
  }
  function test_root_action_error_survives_other_roots_and_poll_failure_until_retry() {
    const first = controller.change(Protocol.Operations.IndexDisable, "", "first")
    controller.handleMessage({ requestId: first, type: Protocol.Messages.Error, error: "Could not disable folder." })
    const other = controller.change(Protocol.Operations.IndexDisable, "", "second")
    compare(controller.error, "Could not disable folder.")
    controller.handleMessage({ requestId: other, type: Protocol.Messages.IndexRootsResult, indexRoots: { roots: [] } })
    const poll = controller.refresh()
    controller.handleMessage({ requestId: poll, type: Protocol.Messages.Error, error: "Could not refresh folders." })
    compare(controller.error, "Could not disable folder.")
    controller.change(Protocol.Operations.IndexDisable, "", "first")
    compare(controller.error, "")
  }
  function test_serialization_correlation_errors_and_reconnection() {
    const initial = controller.refresh()
    const before = sent.length - 1
    compare(sent[before].operation, Protocol.Operations.IndexRoots)
    verify(!controller.handleMessage({ requestId: initial + 1, type: Protocol.Messages.IndexRootsResult, indexRoots: { roots: [] } }))
    verify(controller.busy)
    verify(controller.handleMessage({ requestId: initial, type: Protocol.Messages.IndexRootsResult,
      indexRoots: { roots: [{ id: "root", path: "/folder" }] } }))
    compare(controller.roots[0].path, "/folder")
    const add = controller.change(Protocol.Operations.IndexAdd, "/missing", "")
    compare(sent[before + 1].path, "/missing")
    verify(controller.handleMessage({ requestId: add, type: Protocol.Messages.Error, error: "Folder does not exist." }))
    compare(controller.error, "Folder does not exist.")
    verify(!controller.busy)
    const interrupted = controller.change(Protocol.Operations.IndexDisable, "", "root")
    compare(sent[before + 2].rootId, "root")
    controller.available = false
    verify(!controller.handleMessage({ requestId: interrupted, type: Protocol.Messages.IndexRootsResult, indexRoots: { roots: [] } }))
    verify(controller.error.indexOf("offline") >= 0)
    controller.available = true
    compare(sent[before + 3].operation, Protocol.Operations.IndexRoots)
    verify(controller.busy)
    verify(controller.handleMessage({ requestId: controller.activeRequestId, type: Protocol.Messages.IndexRootsResult,
      indexRoots: { roots: [] } }))
    compare(controller.error, "")
    verify(!controller.busy)
  }
}
