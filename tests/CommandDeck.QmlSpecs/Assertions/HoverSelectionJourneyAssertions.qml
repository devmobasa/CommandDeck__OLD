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

          const list = deck.testSurfaceWindow.part_resultList
          events.mouseMove(list, 30, target.y + target.height + list.spacing + target.height / 2)
          host.expect(deck.currentResult.title === "beta.txt",
            "moving into empty results space keeps the keyboard selection")
          deck.requestSearch()
          deliver(latestSearch(""), ["alpha.txt", "beta.txt", "gamma.txt", "delta.txt", "epsilon.txt"])
        }
      },
      {
        ready: () => row(4) !== null && row(4).height > 0,
        message: "new rows arrive under the pointer resting in empty results space",
        action: () => {
          host.expect(deck.currentResult.title === "beta.txt",
            "a row appearing under a resting pointer keeps the keyboard selection")
          host.expect(deck.testSearchInput.activeFocus, "row delivery keeps search focused")
          host.part_hostEvents.keyClick(Qt.Key_X)
          host.expect(deck.testSearchInput.text === "x" && deck.queryText === "x",
            "typing right after hovering edits the query")
        }
      },
      {
        ready: () => latestSearch("x") !== null,
        message: "the typed search is sent before a copy row arrives",
        action: () => {
          const request = latestSearch("x")
          deck.handleMessage({ type: Protocol.Messages.Result, requestId: request.requestId, result: {
            id: "file:hover-copy", kind: Protocol.ResultKinds.File, sourceId: Protocol.SourceIds.Files,
            title: "copy.txt", invocationReference: "hover-copy",
            actions: [{ id: "copy-path", title: "Copy Path", kind: Protocol.ActionKinds.CopyText }] } })
          deck.handleMessage({ type: Protocol.Messages.Done, requestId: request.requestId })
        }
      },
      {
        ready: () => deck.currentResult !== null && deck.currentResult.title === "copy.txt",
        message: "a copy row is selected in production",
        action: () => {
          const start = deck.sentRequests.length
          host.part_hostEvents.keyClick(Qt.Key_Return)
          const invoke = deck.sentRequests.slice(start).find(request => request.operation === Protocol.Operations.Invoke)
          deck.handleMessage({ type: Protocol.Messages.ActionResult, requestId: invoke.requestId,
            actionResult: { succeeded: true, message: "Copied." } })
          const hud = deck.testSurfaceWindow.part_hud
          host.expect(!deck.opened && hud.visible && hud.text === "Copied.",
            "Enter on a copy closes the launcher and shows its message window")
          deck.part_hudTimer.triggered()
          host.expect(!hud.visible, "the message window hides when its time is up")
        }
      }
    ]
  }
}
