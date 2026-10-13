import QtQuick
import "Protocol.js" as Protocol
import Quickshell
import "." as CommandDeck

QtObject {
  required property var host
  function run(){
    const tab = { id: "herdr:tab:w1:t1", sourceId: "herdr", kind: "tab", title: "tests", actions: [] }
    const other = { id: "herdr:tab:w1:t2", sourceId: "herdr", kind: "tab", title: "other", actions: [] }
    let requestId = 0
    let count = 0
    let focus = 0
    const searches = () => host.part_integratedDeck.sentRequests.filter(value => value.operation === Protocol.Operations.Search).length
    const deliver = values => {
      requestId = host.part_integratedDeck.part_searchSession.activeRequestId
      for (const value of values) host.part_integratedDeck.handleMessage({ type: Protocol.Messages.Result, requestId: requestId, result: value })
      host.part_integratedDeck.handleMessage({ type: Protocol.Messages.Done, requestId: requestId })
    }
    host.part_integratedDeck.open("{}")
    host.part_integratedDeck.detailDialogRequested = false
    host.part_integratedDeck.daemonAvailable = true
    host.part_integratedDeck.reconciliationPending = false
    host.part_integratedDeck.activeFilters = [{ id: "herdr-tabs", excluded: false }]
    host.part_integratedDeck.requestSearch()
    deliver([tab, other])
    host.part_integratedDeck.selectedIndex = 2
    host.part_integratedDeck.herdrRefreshInterval = 20
    count = searches()
    focus = host.part_integratedDeck.focusRevision
    host.liveSteps = [
      () => {
        host.expect(searches() === count + 1 && host.part_integratedDeck.part_searchSession.inFlight,
          "an open Herdr view refreshes automatically through the search protocol")
        host.expect(host.part_integratedDeck.currentResult.id === other.id && host.part_integratedDeck.focusRevision === focus,
          "starting a live refresh keeps selection and search focus")
        count = searches()
      },
      () => {
        host.expect(searches() === count, "live polling never cancels an unfinished search")
        host.part_integratedDeck.handleMessage({ type: Protocol.Messages.Result, requestId: host.part_integratedDeck.part_searchSession.activeRequestId,
          result: Object.assign({}, other, { title: "renamed", metadata: { herdrAgentStatus: "blocked" } }) })
      },
      () => {
        host.expect(host.part_integratedDeck.currentResult.title === "other", "partial live updates leave the previous inventory visible")
        host.part_integratedDeck.handleMessage({ type: Protocol.Messages.Done, requestId: host.part_integratedDeck.part_searchSession.activeRequestId })
        host.expect(host.part_integratedDeck.currentResult.id === other.id && host.part_integratedDeck.currentResult.title === "renamed"
          && host.part_integratedDeck.currentResult.metadata.herdrAgentStatus === "blocked", "live updates refresh names and status while preserving stable selection")
        host.part_integratedDeck.helpOpen = true
      },
      () => {
        host.expect(searches() === count, "live polling pauses behind a dialog")
        host.part_integratedDeck.helpOpen = false
        host.part_integratedDeck.part_invocationController.requestId = 987654
      },
      () => {
        host.expect(searches() === count, "live polling pauses during an action")
        host.part_integratedDeck.part_invocationController.requestId = 0
      },
      () => {
        host.expect(searches() === count + 1, "live polling resumes after the action settles")
        host.part_integratedDeck.handleMessage({ type: Protocol.Messages.Done, requestId: host.part_integratedDeck.part_searchSession.activeRequestId - 1 })
        host.expect(host.part_integratedDeck.part_searchSession.inFlight, "a stale completion cannot enable another live search")
        host.part_integratedDeck.close()
        count = searches()
      },
      () => {
        host.expect(searches() === count && !host.part_integratedDeck.part_searchSession.inFlight, "closing stops Herdr live updates")
        host.part_integratedDeck.open("{}")
        host.part_integratedDeck.daemonAvailable = true
        host.part_integratedDeck.reconciliationPending = false
        host.part_integratedDeck.activeFilters = [{ id: "files", excluded: false }]
        deliver([])
        count = searches()
      },
      () => {
        host.expect(searches() === count, "ordinary searches do not poll Herdr")
        host.part_integratedDeck.activeFilters = [{ id: "herdr-attention", excluded: false }]
        host.part_integratedDeck.daemonAvailable = false
      },
      () => {
        host.expect(searches() === count, "offline Herdr views do not poll")
        host.part_integratedDeck.close()
      }
    ]
    host.liveStep = 0
    host.part_liveCheckTimer.start()
  }
}
