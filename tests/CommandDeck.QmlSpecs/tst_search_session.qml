import QtQuick
import QtTest
import "../../qml/common" as Deck
import "../../qml/common/Protocol.js" as Protocol

DeadlineTestCase {
  name: "SearchSessionController"

  property int sequence: 0
  property var sent: []
  property var published: []

  Deck.SearchSessionController {
    id: session
    nextRequestId: function() { return ++sequence }
    replaceResults: function(results, reset, completed) {
      if (!reset && !completed) return false
      published = published.concat([{ results: results, reset: reset, completed: completed }])
      return true
    }
    opened: true
    onRequest: message => sent = sent.concat([message])
  }

  SignalSpy { id: finishedSpy; target: session; signalName: "finished" }
  SignalSpy { id: failedSpy; target: session; signalName: "failed" }
  SignalSpy { id: divertedSpy; target: session; signalName: "divertRequested" }

  function init() {
    session.clear()
    session.available = true
    session.opened = true
    session.paused = false
    session.diverted = false
    session.query = ""
    session.filters = []
    sequence = 0
    sent = []
    published = []
    finishedSpy.clear()
    failedSpy.clear()
    divertedSpy.clear()
  }

  function searches() { return sent.filter(message => message.operation === Protocol.Operations.Search) }
  function cancels() { return sent.filter(message => message.operation === Protocol.Operations.Cancel) }

  function reply(requestId, results) {
    const consumed = results.map(result => session.handleMessage(
      { type: Protocol.Messages.Result, requestId: requestId, result: result }))
    return consumed.concat([session.handleMessage({ type: Protocol.Messages.Done, requestId: requestId })])
  }

  function test_stale_replies_are_ignored_and_one_identity_is_committed() {
    session.query = "first"
    verify(session.search())
    const first = session.activeRequestId
    session.query = "second"
    verify(session.search())
    const second = session.activeRequestId

    compare(reply(first, [{ id: "stale" }]), [false, false])
    compare(published.length, 0)
    compare(reply(second, [{ id: "current" }]), [true, true])
    compare(published.length, 1)
    compare(published[0].results.map(result => result.id), ["current"])
    verify(published[0].reset && published[0].completed)
    compare(session.committedRequestId, second)
    compare(session.committedIdentity, session.currentIdentity())
    compare(finishedSpy.count, 1)

    verify(session.search())
    reply(session.activeRequestId, [{ id: "refresh" }])
    verify(!published[1].reset, "a refresh of the committed search keeps the selection")
  }

  function test_can_activate_only_rows_answering_the_current_query() {
    verify(session.canActivate(), "nothing committed yet has nothing to refuse")
    session.query = "w"
    session.search()
    reply(session.activeRequestId, [{ id: "saved-search" }])
    verify(session.canActivate())

    session.query = "we"
    verify(!session.canActivate(), "a row found for the previous query waits")
    session.search()
    verify(!session.canActivate(), "the longer query in flight still waits")
    reply(session.activeRequestId, [{ id: "extension" }])
    verify(session.canActivate())

    session.filters = [{ id: "files", excluded: false }]
    verify(!session.canActivate(), "a filter changes the question too")
  }

  function test_only_a_running_search_is_cancelled() {
    session.query = "report"
    session.search()
    const finished = session.activeRequestId
    reply(finished, [])
    session.query = "reports"
    session.search()
    compare(cancels().length, 0)

    const running = session.activeRequestId
    session.query = "reportsx"
    session.search()
    compare(cancels().length, 1)
    compare(cancels()[0].targetRequestId, running)
    verify(sent.indexOf(cancels()[0]) < sent.indexOf(searches()[2]), "the cancel precedes its replacement")
  }

  function test_reconnect_drops_the_interrupted_search() {
    session.query = "notes"
    session.search()
    const interrupted = session.activeRequestId
    session.handleMessage({ type: Protocol.Messages.Result, requestId: interrupted, result: { id: "partial" } })
    session.clear()
    verify(!session.inFlight && !session.pending && !session.searching)
    verify(!session.commitDelay.running && !session.pendingDelay.running)
    compare(reply(interrupted, [{ id: "late" }]), [false, false])

    session.available = false
    verify(!session.search(true), "no search is sent while the daemon is away")
    session.available = true
    session.opened = false
    verify(!session.search(), "a closed launcher does not search")
    verify(session.search(true), "reconciliation searches even while closed")
    compare(cancels().length, 0, "the interrupted search is never cancelled after reconnecting")
    compare(published.length, 0)
    reply(session.activeRequestId, [{ id: "reconciled" }])
    compare(published[0].results[0].id, "reconciled")
    verify(published[0].reset, "the first search after reconnecting replaces the list")
  }

  function test_loading_feedback_waits_and_failure_releases_it() {
    session.search()
    verify(!session.pending, "a fast search never dims the list")
    waitForCondition(() => session.pending, 1000)

    session.handleMessage({ type: Protocol.Messages.Error, requestId: session.activeRequestId, error: "Search interrupted" })
    verify(!session.pending && !session.inFlight)
    compare(session.activeRequestId, 0)
    compare(failedSpy.count, 1)
    compare(failedSpy.signalArguments[0][0], "Search interrupted")
  }

  function test_typing_searches_once_after_the_debounce() {
    session.query = "n"
    session.schedule()
    session.query = "no"
    session.schedule()
    verify(session.searching)
    compare(searches().length, 0)
    waitForCondition(() => searches().length === 1, 1000)
    compare(searches()[0].query, "no")

    session.schedule()
    session.search()
    verify(!session.debounce.running, "a search sent now replaces the scheduled one")
    observeFor(session.debounce.interval * 2, () => searches().length === 2, "an immediate search leaves no duplicate debounce")
    compare(searches().length, 2)
  }

  function test_streamed_rows_are_published_in_batches() {
    session.query = "batch"
    session.search()
    session.handleMessage({ type: Protocol.Messages.Result, requestId: session.activeRequestId, result: { id: "one" } })
    verify(session.commitDelay.running)
    waitForCondition(() => published.length === 1, 1000)
    verify(published[0].reset && !published[0].completed)
    verify(session.inFlight, "a batch does not finish the search")
  }

  function test_results_under_settings_are_searched_once_when_it_closes() {
    session.paused = true
    verify(!session.refresh())
    verify(!session.refresh())
    verify(session.stale)
    compare(searches().length, 0, "preference changes under Settings do not search")

    session.paused = false
    compare(searches().length, 1, "closing Settings searches the stale results once")
    verify(!session.stale)
    session.paused = true
    session.paused = false
    compare(searches().length, 1, "fresh results are not searched again")

    session.paused = true
    verify(session.refresh(true), "reconciliation searches while Settings is open")
    compare(searches().length, 2)
    session.paused = false
  }

  function test_home_alone_is_searched_again_after_a_personal_change() {
    verify(session.refreshHome())
    session.query = "notes"
    verify(!session.refreshHome())
    session.query = ""
    session.filters = [{ id: "files", excluded: false }]
    verify(!session.refreshHome())
    compare(searches().length, 1)
  }

  function test_local_edits_reach_the_buffered_rows() {
    session.search()
    const requestId = session.activeRequestId
    for (const id of ["kept", "edited", "hidden"]) {
      session.handleMessage({ type: Protocol.Messages.Result, requestId: requestId, result: { id: id } })
    }
    session.updateResults(result => result.id === "edited" ? Object.assign({}, result, { isFavorite: true }) : result)
    session.removeResults(result => result.id === "hidden")
    session.handleMessage({ type: Protocol.Messages.Done, requestId: requestId })
    compare(published.slice(-1)[0].results.map(result => result.id), ["kept", "edited"])
    verify(published.slice(-1)[0].results[1].isFavorite)
  }

  function test_a_diverted_query_opens_its_surface_instead() {
    session.diverted = true
    verify(!session.search())
    compare(divertedSpy.count, 1)
    compare(sent.length, 0)
  }

  function test_keep_search_requires_the_current_query_to_be_classified_nonprivate() {
    session.query = "ordinary"
    session.search()
    const ordinary = session.activeRequestId
    verify(!session.canKeepSearch, "an unfinished search cannot be kept")
    session.handleMessage({ type: Protocol.Messages.Done, requestId: ordinary,
      searchCompletion: { containsPrivateQuery: false } })
    verify(session.canKeepSearch, "the daemon classified this query as nonprivate")

    session.query = "@hey private-token"
    verify(!session.canKeepSearch, "the previous query's classification never applies to an edit")
    session.search()
    verify(!session.handleMessage({ type: Protocol.Messages.Done, requestId: ordinary,
      searchCompletion: { containsPrivateQuery: false } }))
    session.handleMessage({ type: Protocol.Messages.Done, requestId: session.activeRequestId,
      searchCompletion: { containsPrivateQuery: true } })
    verify(!session.canKeepSearch, "a private query is never kept")

    session.query = "ordinary"
    session.search()
    reply(session.activeRequestId, [])
    verify(!session.canKeepSearch, "a reply without classification cannot permit retention")
    session.clear()
    verify(!session.canKeepSearch)
  }
}
