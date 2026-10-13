import QtQuick
import "Protocol.js" as Protocol
import "." as CommandDeck

// HEY rows that change the search: Enter on a box row and "Search again" in
// the action panel replace the query and its chip, search at once, never reach
// the daemon as an action, and leave the search field ready for typing.
QtObject {
  required property var host

  function run() {
    const deck = host.part_integratedDeck
    const box = { id: "hey:box:imbox", sourceId: Protocol.SourceIds.Hey, kind: Protocol.ResultKinds.Mail, title: "Imbox",
      actions: [{ id: "browse", title: "Browse Imbox", kind: Protocol.ActionKinds.ApplyQuery,
        arguments: { query: "box:imbox", filterId: "hey" } }] }
    const retry = { id: "hey:state:offline", sourceId: Protocol.SourceIds.Hey, kind: Protocol.ResultKinds.Status,
      title: "HEY is unreachable",
      actions: [{ id: "retry", title: "Search Again", kind: Protocol.ActionKinds.ApplyQuery,
        arguments: { query: "@hey invoice", filterId: "" } }] }
    const deliver = values => {
      const requestId = deck.part_searchSession.activeRequestId
      for (const value of values) deck.handleMessage({ type: Protocol.Messages.Result, requestId: requestId, result: value })
      deck.handleMessage({ type: Protocol.Messages.Done, requestId: requestId })
    }
    const sentSince = start => deck.sentRequests.slice(start)
    const lastSearch = requests => requests.filter(value => value.operation === Protocol.Operations.Search).pop()

    if (!deck.opened) deck.open("{}")
    deck.daemonAvailable = true
    deck.reconciliationPending = false
    deck.activeFilters = [{ id: "hey", excluded: false }]
    deck.queryText = ""
    deck.requestSearch()
    deliver([box])
    deck.selectedIndex = deck.part_navigation.indexOfResultId(box.id)

    let start = deck.sentRequests.length
    let focus = deck.focusRevision
    deck.invokeSelectedPrimary()
    let sent = sentSince(start)
    let search = lastSearch(sent)
    host.expect(sent.every(value => value.operation !== Protocol.Operations.Invoke),
      "a HEY box row is handled by the launcher, not invoked in the daemon")
    host.expect(deck.activeFilters.length === 1 && deck.activeFilters[0].id === "hey"
        && deck.activeFilters[0].excluded === false && deck.queryText === "box:imbox",
      "Enter on a HEY box browses it under the HEY chip")
    host.expect(search !== undefined && search.query === "box:imbox"
        && search.expression.includeFilterIds.length === 1 && search.expression.includeFilterIds[0] === "hey",
      "browsing a HEY box searches at once")
    host.expect(deck.focusRevision > focus && deck.routedInputs[deck.routedInputs.length - 1] === "box:imbox",
      "the search field shows the new query and is ready for typing")

    deliver([retry])
    start = deck.sentRequests.length
    focus = deck.focusRevision
    deck.actionPanelOpen = true
    deck.invokeAction(retry, retry.actions[0], false)
    sent = sentSince(start)
    search = lastSearch(sent)
    host.expect(!deck.actionPanelOpen && deck.activeFilters.length === 0 && deck.queryText === "@hey invoice"
        && search !== undefined && search.query === "@hey invoice" && deck.focusRevision > focus,
      "Search again from the action panel reruns the same query and closes the panel")

    // A retained row from an older search cannot change the query.
    deck.queryText = "@hey receipts"
    start = deck.sentRequests.length
    deck.invokeAction(retry, retry.actions[0], false)
    host.expect(deck.queryText === "@hey receipts" && deck.statusText.indexOf("current search") >= 0,
      "a stale HEY row waits for results matching the current search")

    deck.activeFilters = []
    deck.queryText = ""
    runTriage()
  }

  // One unseen HEY thread and the same thread seen: each offers the mark that
  // applies and its hidden undo, the way a box listing does.
  function triageFixture() {
    const deck = host.part_integratedDeck
    const triage = (id, title, change) => ({ id: id, title: title, kind: Protocol.ActionKinds.HeyTriage,
      arguments: { change: change, postingId: "601" } })
    const markSeen = triage("mark-seen", "Mark Seen", "seen")
    const undoSeen = Object.assign(triage(Protocol.UndoActions.Prefix + "mark-seen", "Undo Mark Seen", "unseen"), { hidden: true })
    const markUnseen = triage("mark-unseen", "Mark Unseen", "unseen")
    const undoUnseen = Object.assign(triage(Protocol.UndoActions.Prefix + "mark-unseen", "Undo Mark Unseen", "seen"), { hidden: true })
    const open = { id: "open", title: "Open in HEY", kind: Protocol.ActionKinds.HeyOpen, arguments: {} }
    const thread = (reference, seen) => ({ id: "hey:topic:9101", sourceId: Protocol.SourceIds.Hey, kind: Protocol.ResultKinds.Mail,
      title: "Lunch?", invocationReference: reference, actions: seen ? [open, markUnseen, undoUnseen] : [open, markSeen, undoSeen] })
    return {
      markSeen: markSeen,
      undoSeen: undoSeen,
      thread: thread,
      deliver: values => {
        const requestId = deck.part_searchSession.activeRequestId
        for (const value of values) deck.handleMessage({ type: Protocol.Messages.Result, requestId: requestId, result: value })
        deck.handleMessage({ type: Protocol.Messages.Done, requestId: requestId })
      },
      invokes: () => deck.sentRequests.filter(value => value.operation === Protocol.Operations.Invoke),
      searches: () => deck.sentRequests.filter(value => value.operation === Protocol.Operations.Search).length,
      // Marks the thread seen; the daemon answers with the undo's reference.
      change: (reference, undoReference) => {
        const before = deck.sentRequests.length
        deck.invokeAction(thread(reference, false), markSeen, false)
        const sent = deck.sentRequests.slice(before).filter(value => value.operation === Protocol.Operations.Invoke)
        if (sent.length !== 1) return sent
        deck.handleMessage({ type: Protocol.Messages.ActionResult, requestId: sent[0].requestId,
          actionResult: undoReference
            ? { succeeded: true, message: "Marked “Lunch?” seen", undoInvocationReference: undoReference }
            : { succeeded: true, message: "Marked “Lunch?” seen" } })
        return sent
      }
    }
  }

  // A HEY change offers Undo in the strip. The refresh replaces the row, and
  // now that the thread is seen it offers Mark Unseen and no undo:mark-seen;
  // Undo still reverses the change, through the undo reference the daemon
  // returned with it, even after the query moved on.
  function runTriage() {
    const deck = host.part_integratedDeck
    const fixture = triageFixture()

    deck.lastUndo = null
    deck.lastHidden = null
    deck.activeFilters = [{ id: "hey", excluded: false }]
    deck.queryText = "box:imbox"
    deck.requestSearch()
    fixture.deliver([fixture.thread("ref-1", false)])
    deck.selectedIndex = deck.part_navigation.indexOfResultId("hey:topic:9101")
    host.expect(deck.actionCount === 2, "the hidden undo is not counted as a listed action")

    let before = fixture.invokes().length
    deck.invokeAction(fixture.thread("ref-1", false), fixture.markSeen, false)
    let sent = fixture.invokes().slice(before)
    host.expect(sent.length === 1 && sent[0].actionId === "mark-seen" && deck.pendingUndo !== null,
      "a HEY change is invoked with its undo held back")
    const searchesBefore = fixture.searches()
    deck.handleMessage({ type: Protocol.Messages.ActionResult, requestId: sent[0].requestId,
      actionResult: { succeeded: true, message: "Marked “Lunch?” seen", undoInvocationReference: "undo-ref-1" } })
    host.expect(deck.lastUndo !== null && deck.lastUndo.message === "Marked “Lunch?” seen" && deck.pendingUndo === null
        && deck.opened && fixture.searches() === searchesBefore + 1,
      "a successful change offers Undo and refreshes the list without closing the launcher")

    fixture.deliver([fixture.thread("ref-2", true)])
    deck.queryText = "box:imbox lunch"
    before = fixture.invokes().length
    deck.undoLast()
    sent = fixture.invokes().slice(before)
    host.expect(sent.length === 1 && sent[0].resultId === "hey:topic:9101" && sent[0].actionId === fixture.undoSeen.id
        && sent[0].invocationReference === "undo-ref-1" && deck.lastUndo === null && deck.pendingUndo === null,
      "Undo runs the change's own undo once, after the refresh flipped the row")
    if (sent.length === 1) {
      deck.handleMessage({ type: Protocol.Messages.ActionResult, requestId: sent[0].requestId,
        actionResult: { succeeded: true, message: "Marked “Lunch?” unseen" } })
    }
    host.expect(deck.lastUndo === null, "undoing does not offer another undo")

    // Without an undo reference from the daemon there is nothing to undo with.
    deck.queryText = "box:imbox"
    deck.requestSearch()
    fixture.deliver([fixture.thread("ref-3", false)])
    sent = fixture.change("ref-3", null)
    host.expect(sent.length === 1 && deck.lastUndo === null && deck.pendingUndo === null,
      "a change the daemon returned no undo reference for offers no undo")

    // A failed change offers nothing to undo.
    fixture.deliver([fixture.thread("ref-4", false)])
    before = fixture.invokes().length
    deck.invokeAction(fixture.thread("ref-4", false), fixture.markSeen, false)
    sent = fixture.invokes().slice(before)
    if (sent.length === 1) {
      deck.handleMessage({ type: Protocol.Messages.ActionResult, requestId: sent[0].requestId,
        actionResult: { succeeded: false, message: "Not logged in" } })
    }
    host.expect(sent.length === 1 && deck.lastUndo === null && deck.pendingUndo === null, "a failed change offers no undo")

    deck.lastUndo = null
    deck.activeFilters = []
    deck.queryText = ""
  }

  // The same undo on the production surface: a real Ctrl+Z in search, then a
  // real click on the strip's Undo button. Live steps let the surface take
  // focus after it opens and lay the strip out before it is clicked.
  function openForUndoKeys() {
    const deck = host.part_integratedDeck
    deck.lastUndo = null
    deck.lastHidden = null
    deck.daemonAvailable = true
    deck.reconciliationPending = false
    deck.open("{}")
  }

  function verifyUndoKeys() {
    const deck = host.part_integratedDeck
    const input = deck.testSearchInput
    const events = host.part_hostEvents
    const fixture = triageFixture()
    host.expect(input !== null && input.activeFocus, "the HEY undo journey starts in production search")
    if (!input || !input.activeFocus) return

    deck.activeFilters = [{ id: "hey", excluded: false }]
    for (const character of "box:imbox") events.keyClick(character)
    deck.requestSearch()
    fixture.deliver([fixture.thread("ref-1", false)])
    let sent = fixture.change("ref-1", "undo-ref-keys")
    fixture.deliver([fixture.thread("ref-2", true)])
    host.expect(sent.length === 1 && deck.lastUndo !== null && deck.lastUndo.message === "Marked “Lunch?” seen",
      "the strip offers Undo after the refreshed row shows the thread seen")

    const before = fixture.invokes().length
    events.keyClick(Qt.Key_Z, Qt.ControlModifier)
    sent = fixture.invokes().slice(before)
    host.expect(sent.length === 1 && sent[0].actionId === fixture.undoSeen.id && sent[0].invocationReference === "undo-ref-keys"
        && deck.lastUndo === null && input.activeFocus && deck.queryText === "box:imbox",
      "Ctrl+Z in search undoes the change and leaves search ready")
    if (sent.length === 1) {
      deck.handleMessage({ type: Protocol.Messages.ActionResult, requestId: sent[0].requestId,
        actionResult: { succeeded: true, message: "Marked “Lunch?” unseen" } })
    }

    fixture.deliver([fixture.thread("ref-3", false)])
    fixture.change("ref-3", "undo-ref-click")
    fixture.deliver([fixture.thread("ref-4", true)])
  }

  function verifyUndoClick() {
    const deck = host.part_integratedDeck
    const input = deck.testSearchInput
    const events = host.part_hostEvents
    const fixture = triageFixture()
    const strip = deck.testSurfaceWindow ? events.findChild(deck.testSurfaceWindow, "hiddenUndoBar") : null
    host.expect(strip !== null && strip.visible && strip.displayText === "Marked “Lunch?” seen",
      "the strip shows the newer change")
    if (strip === null || !strip.visible || !input) {
      deck.close()
      return
    }

    host.expect(events.waitForRendering(strip), "the production undo strip renders before its button is clicked")

    const before = fixture.invokes().length
    events.mouseClick(strip.undoControl, strip.undoControl.width / 2, strip.undoControl.height / 2)
    const sent = fixture.invokes().slice(before)
    host.expect(sent.length === 1 && sent[0].actionId === fixture.undoSeen.id && sent[0].invocationReference === "undo-ref-click"
        && deck.lastUndo === null,
      "clicking Undo in the strip undoes the change after the refresh")
    events.keyClick(Qt.Key_L)
    host.expect(input.activeFocus && input.text === "box:imboxl" && deck.queryText === "box:imboxl",
      "typing right after clicking Undo edits the query")
    if (sent.length === 1) {
      deck.handleMessage({ type: Protocol.Messages.ActionResult, requestId: sent[0].requestId,
        actionResult: { succeeded: true, message: "Marked “Lunch?” unseen" } })
    }

    deck.close()
  }
}
