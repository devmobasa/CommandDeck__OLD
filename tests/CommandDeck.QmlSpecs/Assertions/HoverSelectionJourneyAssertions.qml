import QtQuick
import "Protocol.js" as Protocol

// Hovering selects a production result row only after the pointer really
// moves. The launcher opening under the pointer, and a hover update while the
// pointer rests, leave the keyboard selection alone; search keeps focus.
QtObject {
  id: journey
  required property var host
  readonly property var deck: host.part_integratedDeck

  function latestSearch(query) {
    const searches = deck.sentRequests.filter(request => request.operation === Protocol.Operations.Search)
    const request = searches[searches.length - 1]
    return request && request.query === query ? request : null
  }

  function deliver(request, titles) {
    for (const title of titles) {
      deck.handleMessage({ type: Protocol.Messages.Result, requestId: request.requestId, result: {
        id: "file:hover-" + title, kind: Protocol.ResultKinds.File, sourceId: Protocol.SourceIds.Files,
        title: title, invocationReference: "hover-" + title, actions: [] } })
    }
    deck.handleMessage({ type: Protocol.Messages.Done, requestId: request.requestId })
  }

  function row(index) {
    return deck.testSurfaceWindow ? deck.testSurfaceWindow.part_resultList.itemAtIndex(index) : null
  }

  function steps() {
    return [
      {
        ready: () => true,
        message: "hover selection starts on a reopened launcher",
        action: () => {
          deck.close()
          deck.open("{}")
        }
      },
      {
        ready: () => deck.testSurfaceWindow.visible && deck.testSearchInput.activeFocus
          && latestSearch("") !== null,
        message: "the reopened launcher searches Home before rows arrive",
        action: () => deliver(latestSearch(""), ["alpha.txt", "beta.txt", "gamma.txt"])
      },
      {
        ready: () => row(3) !== null && row(3).height > 0 && deck.currentResult !== null,
        message: "three rows are laid out for pointer events",
        action: () => {
          const events = host.part_hostEvents
          const first = deck.selectedIndex
          const target = row(3)
          events.mouseMove(target, 30, target.height / 2)
          host.expect(deck.selectedIndex === first,
            "the first pointer position after opening selects nothing")
          events.mouseMove(target, 60, target.height / 2)
          host.expect(deck.selectedIndex === 3 && deck.currentResult.title === "gamma.txt",
            "moving the pointer over a row selects it")
          host.expect(deck.testSearchInput.activeFocus, "hover selection leaves search focused")

          events.keyClick(Qt.Key_Up)
          host.expect(deck.currentResult.title === "beta.txt", "Up moves on from the hovered row")
          events.mouseMove(target, 60, target.height / 2)
          host.expect(deck.currentResult.title === "beta.txt",
            "a hover update without movement keeps the keyboard selection")

          events.keyClick(Qt.Key_X)
          host.expect(deck.testSearchInput.text === "x" && deck.queryText === "x",
            "typing right after hovering edits the query")
        }
      }
    ]
  }
}
