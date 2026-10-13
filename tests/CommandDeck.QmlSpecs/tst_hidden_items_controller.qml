import QtQuick
import QtTest
import "../../qml/common" as Deck
import "../../qml/common/Protocol.js" as Protocol

TestCase {
  name: "HiddenItemsController"
  property int sequence: 0
  property var sent: []
  Deck.HiddenItemsController {
    id: controller
    nextRequestId: function() { return ++sequence }
    onRequest: message => sent = sent.concat([message])
  }

  function init() {
    controller.close()
    controller.available = true
    controller.snapshot = null
    sent = []
  }
  function reply(id, items, total, truncated) {
    return controller.handleMessage({ requestId: id, type: Protocol.Messages.HiddenItemsResult,
      hiddenItems: { items: items, total: total, truncated: truncated } })
  }

  function test_query_changes_reject_stale_pages_and_append_contiguously() {
    const first = controller.refresh("note", 0)
    compare(sent[0].operation, Protocol.Operations.HiddenList)
    compare(sent[0].limit, 64)
    verify(reply(first, [{ id: "note1" }], 2, true))
    const page = controller.refresh("note", 1)
    compare(sent[1].offset, 1)
    verify(reply(page, [{ id: "note2" }], 2, false))
    compare(controller.snapshot.items.length, 2)

    const stale = controller.refresh("note", 2)
    const current = controller.refresh("app", 2)
    compare(sent[3].offset, 0)
    verify(!reply(stale, [{ id: "note3" }], 3, false))
    verify(reply(current, [{ id: "app1" }], 1, false))
    compare(controller.snapshot.query, "app")
    compare(controller.snapshot.items.length, 1)
    compare(controller.snapshot.items[0].id, "app1")
    verify(!controller.loading)
    verify(!reply(current, [{ id: "duplicate" }], 1, false))
  }

  function test_close_offline_and_errors_invalidate_pending_responses() {
    const closed = controller.refresh("", 0)
    controller.close()
    verify(!reply(closed, [{ id: "old" }], 1, false))
    const reopened = controller.refresh("", 0)
    verify(reopened > closed)
    verify(reply(reopened, [{ id: "fresh" }], 1, false))
    const offline = controller.refresh("", 0)
    controller.available = false
    verify(!reply(offline, [{ id: "stale" }], 1, false))
    verify(!controller.loading)
    compare(controller.snapshot, null)
    compare(controller.refresh("", 0), 0)
    compare(sent.length, 3)
    verify(controller.error.indexOf("offline") >= 0)

    controller.available = true
    compare(sent.length, 4)
    verify(controller.loading)
    compare(sent[3].offset, 0)
    verify(reply(controller.activeRequestId, [], 0, false))
    compare(controller.error, "")
    const failure = controller.refresh("", 0)
    verify(controller.handleMessage({ requestId: failure, type: Protocol.Messages.Error, error: "synthetic failure" }))
    compare(controller.error, "synthetic failure")
    verify(!controller.loading)
    const retry = controller.refresh("", 0)
    verify(reply(retry, [], 0, false))
    compare(controller.error, "")
    controller.close()
    controller.available = false
    controller.available = true
    compare(sent.length, 6)
    verify(!controller.opened)
  }
}
