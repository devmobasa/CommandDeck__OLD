import QtQuick
import "Protocol.js" as Protocol

// Enter never runs a row from an older query. Real keys in the production root:
// a Home saved search and then an extension command arrive, the user types on
// and presses Return, and neither row runs until results for the new query
// arrive. The production journey schedules each step by request readiness.
QtObject {
  id: journey

  required property var host
  property int requestStart: 0

  readonly property var savedSearch: ({
    id: "saved-search:inbox", kind: Protocol.ResultKinds.SavedSearch,
    sourceId: Protocol.SourceIds.CommandsQuicklinks, title: "Inbox",
    savedSearch: {
      id: "saved-search:inbox", name: "Inbox", shortcut: null, showOnHome: true, position: 1,
      expression: { includeFilterIds: ["files"], excludeFilterIds: [],
        clauses: [{ field: "title", text: "inbox", excluded: false }] }
    },
    actions: [{ id: "apply", title: "Apply Search", kind: Protocol.ActionKinds.ApplySavedSearch }]
  })
  readonly property var weather: ({
    id: "extension:weather:forecast", kind: Protocol.ResultKinds.ExtensionCommand,
    sourceId: "extension:weather", title: "Weather",
    actions: [{ id: "open", title: "Open command", kind: Protocol.ActionKinds.ExtensionOpen }],
    metadata: { extensionSearchLease: "lease-weather" }
  })

  function deliver(result) {
    const deck = host.part_integratedDeck
    const requestId = deck.part_searchSession.activeRequestId
    deck.handleMessage({ type: Protocol.Messages.Result, requestId: requestId, result: result })
    deck.handleMessage({ type: Protocol.Messages.Done, requestId: requestId })
  }

  function extensionOpens() {
    return host.part_integratedDeck.sentRequests.slice(journey.requestStart)
      .filter(request => request.operation === Protocol.Operations.ExtensionOpen)
  }

  function start() {
    const deck = host.part_integratedDeck
    deck.testSurfaceEnabled = true
    deck.coachSeen = true
    deck.daemonAvailable = true
    deck.reconciliationPending = false
    deck.activeFilters = []
    deck.open("{}")
  }

  function typeOverSavedSearch() {
    const deck = host.part_integratedDeck
    const input = deck.testSearchInput
    journey.deliver(journey.savedSearch)
    host.expect(input !== null && input.activeFocus && deck.currentResult
        && deck.currentResult.id === journey.savedSearch.id,
      "Home shows a saved search with the production search focused")
    if (!input || !input.activeFocus) return

    host.part_hostEvents.keyClick(Qt.Key_W)
    host.part_hostEvents.keyClick(Qt.Key_Return)
    host.expect(deck.queryText === "w" && input.text === "w" && deck.activeFilters.length === 0,
      "Return after typing does not apply the saved search from Home")
    host.expect(deck.statusText.indexOf("current search") >= 0 && input.activeFocus,
      "Return waits for results matching the typed query and search stays ready")
  }

  function typeOverExtensionCommand() {
    const deck = host.part_integratedDeck
    const input = deck.testSearchInput
    const searches = deck.sentRequests.filter(request => request.operation === Protocol.Operations.Search)
    host.expect(searches.length > 0 && searches[searches.length - 1].query === "w",
      "the typed query is searched after the debounce")
    journey.deliver(journey.weather)
    host.expect(deck.currentResult && deck.currentResult.id === journey.weather.id,
      "the extension command answers the typed query")

    journey.requestStart = deck.sentRequests.length
    host.part_hostEvents.keyClick(Qt.Key_E)
    host.part_hostEvents.keyClick(Qt.Key_Return)
    host.expect(journey.extensionOpens().length === 0 && !deck.part_extensionController.opened,
      "Return after typing more does not open the extension command from the older query")
    host.expect(deck.queryText === "we" && input.activeFocus,
      "typing continues after the refused Return")
  }

  function openCurrentExtensionCommand() {
    const deck = host.part_integratedDeck
    const searches = deck.sentRequests.filter(request => request.operation === Protocol.Operations.Search)
    host.expect(deck.part_searchSession.inFlight && searches[searches.length - 1].query === "we",
      "the longer query is in flight")
    host.part_hostEvents.keyClick(Qt.Key_Return)
    host.expect(journey.extensionOpens().length === 0,
      "Return while the longer query is in flight still opens nothing")

    journey.deliver(journey.weather)
    host.part_hostEvents.keyClick(Qt.Key_Return)
    const opens = journey.extensionOpens()
    host.expect(opens.length === 1 && opens[0].resultId === journey.weather.id
        && opens[0].extensionInput.searchLease === "lease-weather" && deck.part_extensionController.opened,
      "Return opens the extension command once results match the query")
  }

  function finish() {
    const deck = host.part_integratedDeck
    deck.part_extensionController.close()
    deck.close()
    deck.testSurfaceEnabled = false
  }
}
