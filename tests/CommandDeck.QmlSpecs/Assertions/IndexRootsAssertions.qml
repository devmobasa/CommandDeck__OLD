import QtQuick
import "Protocol.js" as Protocol

QtObject {
  required property var host

  function run(context) {
    const settings = host.part_settings
    const previous = settings.indexRoots
    const card = settings.part_indexRootsCard
    settings.indexRoots = [
      { id: "configured", path: "/folders/missing", source: "configuration", enabled: true,
        status: "Needs attention", issue: "Folder does not exist." },
      { id: "added", path: "/folders/notes", source: "added", enabled: true, status: "Watching" }
    ]
    host.expect(card.renderedCount === 2 && card.actionControlAt(0).text === "Disable"
        && card.actionControlAt(1).text === "Remove",
      "indexed folders render configuration and added actions")
    host.expect(card.pathControl.Accessible.name === "Add folder path"
        && card.actionControlAt(0).Accessible.name.indexOf("/folders/missing") >= 0,
      "indexed folder inputs and row actions expose their purpose and path")
    settings.indexRoots = previous

    const deck = host.part_integratedDeck
    const controller = deck.part_indexRootsController
    const wasAvailable = deck.daemonAvailable
    deck.daemonAvailable = true
    const requestId = controller.change(Protocol.Operations.IndexAdd, "/folders/notes", "")
    const request = deck.sentRequests.find(value => value.requestId === requestId)
    host.expect(request && request.operation === Protocol.Operations.IndexAdd && request.path === "/folders/notes",
      "indexed folder changes cross the integrated bridge as typed requests")
    deck.handleMessage({ requestId: requestId, type: Protocol.Messages.IndexRootsResult,
      indexRoots: { roots: [{ id: "added", path: "/folders/notes", source: "added", enabled: true, status: "Watching" }] } })
    host.expect(controller.roots.length === 1 && !controller.busy && controller.error === "",
      "the integrated deck publishes the acknowledged indexed folders")
    controller.opened = false
    controller.roots = []
    deck.daemonAvailable = wasAvailable
  }
}
