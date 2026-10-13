import QtQuick
import "Protocol.js" as Protocol
import Quickshell
import "." as CommandDeck

// Hidden items through the integrated deck: "Don't show this" removes only the
// confirmed row and arms undo, undo restores by id and searches again, and the
// settings list is fetched in bounded pages that append.
QtObject {
  required property var host

  function rowIds(deck) {
    const ids = []
    for (let index = 0; index < deck.part_rows.count; index++) {
      const row = deck.part_rows.get(index)
      if (!row.isHeader && row.value) ids.push(row.value.id)
    }
    return ids
  }

  function run(context) {
    const deck = host.part_integratedDeck
    const wasOpened = deck.opened
    if (!wasOpened) deck.open("{}")
    deck.lastHidden = null
    deck.requestSearch(true)
    const searchId = deck.part_searchSession.activeRequestId
    const notes = {
      id: "file:notes", kind: "file", title: "notes.txt", subtitle: "/tmp/notes.txt",
      sourceId: "files", providerId: Protocol.ProviderIds.Files, score: 1, canCustomize: true, isFavorite: false,
      aliases: [], metadata: {}, actions: [{ id: "open", title: "Open", kind: "open-path" }]
    }
    const brave = {
      id: "app:brave.desktop", kind: "application", title: "Brave", subtitle: "Browser",
      sourceId: "applications", providerId: Protocol.ProviderIds.Applications, score: 2, canCustomize: true,
      isFavorite: false, aliases: [], metadata: {}, actions: [{ id: "launch", title: "Launch", kind: "launch-desktop" }]
    }
    deck.handleMessage({ type: Protocol.Messages.Result, requestId: searchId, result: notes })
    deck.handleMessage({ type: Protocol.Messages.Result, requestId: searchId, result: brave })
    deck.handleMessage({ type: Protocol.Messages.Done, requestId: searchId })
    host.expect(rowIds(deck).indexOf("file:notes") >= 0 && rowIds(deck).indexOf("app:brave.desktop") >= 0,
      "hidden-items stage starts with both rows visible")

    const beforeHide = deck.sentRequests.length
    deck.setHidden(notes)
    const hideRequest = deck.sentRequests.slice(beforeHide).find(request => request.operation === Protocol.Operations.HiddenSet)
    host.expect(hideRequest !== undefined && hideRequest.resultId === "file:notes" && hideRequest.enabled === true,
      "don't show this sends hidden.set for the daemon-cached result id")
    deck.handleMessage({
      requestId: hideRequest.requestId, type: Protocol.Messages.CustomizationResult,
      customization: { stableId: "file:notes", isHidden: true, isFavorite: false, aliases: [], message: "Hidden notes.txt" }
    })
    host.expect(rowIds(deck).indexOf("file:notes") < 0 && rowIds(deck).indexOf("app:brave.desktop") >= 0
        && deck.lastHidden !== null && deck.lastHidden.customizationId === "hidden:file:notes"
        && deck.lastHidden.title === "notes.txt" && deck.statusText === "Hidden notes.txt",
      "a confirmed hide removes only the hidden row and arms the undo strip")

    const beforeUndo = deck.sentRequests.length
    deck.undoHide()
    const restoreRequest = deck.sentRequests.slice(beforeUndo).find(request => request.operation === Protocol.Operations.HiddenRestore)
    host.expect(restoreRequest !== undefined && restoreRequest.customizationId === "hidden:file:notes"
        && deck.lastHidden === null,
      "undo sends hidden.restore by id and clears the strip at once")
    deck.undoHide()
    host.expect(deck.sentRequests.filter(request => request.operation === Protocol.Operations.HiddenRestore).length
        === deck.sentRequests.slice(0, beforeUndo).filter(request => request.operation === Protocol.Operations.HiddenRestore).length + 1,
      "a second undo without a pending hide sends nothing")
    deck.handleMessage({
      requestId: restoreRequest.requestId, type: Protocol.Messages.CustomizationResult,
      customization: { stableId: "file:notes", isHidden: false, isFavorite: false, aliases: [], message: "Restored notes.txt" }
    })
    host.expect(deck.sentRequests.slice(beforeUndo).some(request => request.operation === Protocol.Operations.Search),
      "a confirmed restore searches again so the item can come back through ranking")

    const beforeList = deck.sentRequests.length
    deck.requestHiddenItems("note", 0)
    const listRequest = deck.sentRequests.slice(beforeList).find(request => request.operation === Protocol.Operations.HiddenList)
    host.expect(listRequest !== undefined && listRequest.query === "note" && listRequest.limit === 64
        && listRequest.offset === 0,
      "the hidden list is requested with the query in bounded pages")
    deck.handleMessage({
      requestId: listRequest.requestId, type: Protocol.Messages.HiddenItemsResult,
      hiddenItems: { items: [{ id: "hidden:file:notes", stableId: "file:notes", title: "notes.txt", sourceId: "files", isAvailable: true }], total: 2, truncated: true }
    })
    host.expect(deck.hiddenItems.items.length === 1 && deck.hiddenItems.truncated,
      "the first hidden page reports that more entries exist")
    deck.requestHiddenItems("note", 1)
    const pageRequest = deck.sentRequests[deck.sentRequests.length - 1]
    deck.handleMessage({
      requestId: pageRequest.requestId, type: Protocol.Messages.HiddenItemsResult,
      hiddenItems: { items: [{ id: "hidden:app:old", stableId: "app:old", title: "Old", sourceId: "applications", isAvailable: false }], total: 2, truncated: false }
    })
    host.expect(deck.hiddenItems.items.length === 2 && !deck.hiddenItems.truncated && deck.hiddenItems.query === "note",
      "later hidden pages append to the list")
    deck.handleMessage({ requestId: 999999, type: Protocol.Messages.HiddenItemsResult, hiddenItems: { items: [], total: 0 } })
    host.expect(deck.hiddenItems.items.length === 2, "a stale hidden-items reply is ignored")

    // Closing Settings while a restore is pending must retire list browsing.
    deck.restoreHidden("hidden:app:old", true)
    const delayedRestore = deck.sentRequests[deck.sentRequests.length - 1]
    deck.part_hiddenItemsController.close()
    const listCount = deck.sentRequests.filter(request => request.operation === Protocol.Operations.HiddenList).length
    deck.handleMessage({
      requestId: delayedRestore.requestId, type: Protocol.Messages.CustomizationResult,
      customization: { stableId: "app:old", isHidden: false, isFavorite: false, aliases: [], message: "Restored Old" }
    })
    host.expect(!deck.part_hiddenItemsController.opened
        && deck.sentRequests.filter(request => request.operation === Protocol.Operations.HiddenList).length === listCount,
      "a delayed restore settles without reopening dismissed hidden-item browsing")

    deck.part_hiddenItemsController.snapshot = null
    if (!wasOpened) deck.close()
  }
}
