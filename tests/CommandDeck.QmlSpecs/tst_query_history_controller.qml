import QtQuick
import QtTest
import "../../qml/common" as Deck
import "../../qml/common/Protocol.js" as Protocol

TestCase {
  name: "QueryHistoryController"
  property int sequence: 0
  property var sent: []
  Deck.QueryHistoryController {
    id: controller
    nextRequestId: function() { return ++sequence }
    onRequest: message => sent = sent.concat([message])
  }

  function init() {
    controller.available = true
    controller.clear()
    sent = []
  }

  function test_refresh_rejects_stale_and_closed_responses() {
    controller.open()
    verify(controller.opened)
    verify(controller.loading)
    const stale = controller.activeRequestId
    controller.refresh()
    const current = controller.activeRequestId
    verify(!controller.handleMessage({ requestId: stale, type: Protocol.Messages.QueryHistoryResult, queryHistory: { entries: [1] } }))
    verify(controller.handleMessage({ requestId: current, type: Protocol.Messages.QueryHistoryResult, queryHistory: { entries: [2] } }))
    compare(controller.snapshot.entries[0], 2)
    verify(!controller.loading)
    controller.refresh()
    const closed = controller.activeRequestId
    controller.close()
    verify(!controller.handleMessage({ requestId: closed, type: Protocol.Messages.QueryHistoryResult }))
    controller.open()
    verify(controller.activeRequestId > closed)
  }

  function test_mutations_offline_and_error_recovery() {
    controller.remove(7)
    compare(sent[0].operation, Protocol.Operations.QueryHistoryDelete)
    compare(sent[0].historyId, 7)
    verify(sent[0].confirmed)
    controller.reset()
    compare(sent.length, 1)
    verify(controller.handleMessage({ requestId: controller.activeRequestId, type: Protocol.Messages.Error, error: "synthetic failure" }))
    compare(controller.error, "synthetic failure")
    controller.reset()
    compare(sent[1].operation, Protocol.Operations.QueryHistoryReset)
    controller.available = false
    verify(!controller.loading)
    compare(controller.activeRequestId, 0)
    controller.refresh()
    compare(sent.length, 2)
    verify(controller.error.indexOf("offline") >= 0)
  }
}
