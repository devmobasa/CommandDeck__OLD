import QtQuick
import QtTest
import "../../qml/common" as Deck
import "../../qml/common/Protocol.js" as Protocol

// Up on an empty search recalls recent queries from the opt-in history: read
// once per opening, newest first, each query once, and forgotten on clear.
TestCase {
  name: "QueryRecallController"
  property int sequence: 0
  property var sent: []
  property var applied: []
  Deck.QueryRecallController {
    id: controller
    nextRequestId: function() { return ++sequence }
    onRequest: message => sent = sent.concat([message])
    onRecalled: query => applied = applied.concat([query])
  }

  function entry(id, query) {
    return { id: id, query: query, stableId: "app:" + id, actionId: "launch", invokedUnixMilliseconds: id }
  }

  function reply(snapshot) {
    return controller.handleMessage({ requestId: sent[sent.length - 1].requestId,
      type: Protocol.Messages.QueryHistoryResult, queryHistory: snapshot })
  }

  function init() {
    controller.available = true
    controller.clear()
    sent = []
    applied = []
  }

  function test_steps_back_through_distinct_queries_newest_first() {
    controller.step(true)
    compare(sent.length, 1)
    compare(sent[0].operation, Protocol.Operations.QueryHistoryGet)
    compare(sent[0].limit, 256)
    controller.step(true)
    compare(sent.length, 1, "a pending read is not repeated")
    compare(applied.length, 0, "nothing is recalled before history arrives")

    verify(reply({ enabled: true, entries: [entry(4, "app: firefox"), entry(3, " notes "), entry(2, ""),
      entry(1, "app: firefox"), entry(0, "gaps")] }))
    compare(applied, ["app: firefox"], "the pending Up applies the newest query")
    controller.step(false)
    controller.step(false)
    compare(applied, ["app: firefox", "notes", "gaps"])
    controller.step(false)
    compare(applied.length, 3, "the oldest query is the last one")
    compare(controller.current, "gaps")

    controller.step(true)
    compare(applied[applied.length - 1], "app: firefox", "an empty search starts from the newest again")
    compare(sent.length, 1, "history is read once per opening")
  }

  function test_history_off_offline_and_clear_recall_nothing() {
    controller.step(true)
    verify(reply({ enabled: false, entries: [entry(1, "stale")] }))
    compare(applied.length, 0)
    controller.step(true)
    compare(applied.length, 0)
    compare(sent.length, 1)

    controller.clear()
    controller.available = false
    controller.step(true)
    compare(sent.length, 1, "nothing is requested while the daemon is offline")
    controller.available = true

    controller.step(true)
    verify(!controller.handleMessage({ requestId: 999, type: Protocol.Messages.QueryHistoryResult }))
    verify(controller.handleMessage({ requestId: sent[sent.length - 1].requestId, type: Protocol.Messages.Error,
      error: "synthetic failure" }))
    compare(applied.length, 0)
    verify(!controller.loaded)

    controller.step(true)
    verify(reply({ enabled: true, entries: [entry(1, "kept")] }))
    compare(applied, ["kept"])
    controller.clear()
    verify(!controller.loaded)
    compare(controller.queries.length, 0, "closing forgets the recalled queries")
  }

  function test_disabling_recall_discards_cached_queries_and_late_replies() {
    controller.step(true)
    verify(reply({ enabled: true, entries: [entry(1, "old query")] }))
    compare(applied, ["old query"])

    controller.available = false
    controller.step(true)
    compare(applied, ["old query"], "disabled recall never replays its cache")
    compare(controller.queries, [])

    controller.available = true
    controller.step(true)
    const pending = sent[sent.length - 1].requestId
    controller.available = false
    verify(!controller.handleMessage({ requestId: pending, type: Protocol.Messages.QueryHistoryResult,
      queryHistory: { enabled: true, entries: [entry(2, "late query")] } }))
    controller.available = true
    controller.step(true)
    verify(reply({ enabled: true, entries: [entry(3, "current query")] }))
    compare(applied, ["old query", "current query"])
  }
}
