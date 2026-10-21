import QtQuick
import "." as CommandDeck
import "Protocol.js" as Protocol

QtObject {
  id: spec
  required property var host
  property int stage: 0
  property double deadline: 0
  property int beforePong: 0
  readonly property var entries: [{ source: "user", enabled: true,
    quicklink: { id: "recovered", title: "Recovered", urlTemplate: "https://example.test/" } }]
  property CommandDeck.CommandDeck deckFixture: CommandDeck.CommandDeck {
    id: deck
    visible: false
    testMode: true
    property var sentRequests: []
    onProtocolRequestSent: request => sentRequests = sentRequests.concat([request])
  }
  property Timer poll: Timer {
    interval: 10
    repeat: true
    onTriggered: spec.advance()
  }

  function start() {
    host.pendingAsyncSpecs++
    stage = 1
    deadline = Date.now() + 5000
    const protocol = deck.part_protocol
    // Keep the real bridge lifecycle in a private echo process, away from any daemon.
    protocol.bridge.command = ["/bin/cat"]
    protocol.awaitingReconnect = true
    protocol.enabled = true
    poll.start()
  }

  function advance() {
    const protocol = deck.part_protocol
    const controller = deck.part_quicklinksController
    if (Date.now() >= deadline) {
      host.expect(false, "quicklink reconnect reaches readiness and drains its pending reload")
      finish()
      return
    }
    if (stage === 1) {
      if (!protocol.bridge.running) return
      protocol.disconnected()
      deck.settingsOpen = true
      const before = deck.sentRequests.length
      controller.load()
      controller.load()
      const earlyId = controller.active ? controller.active.request.requestId : -1
      reply(earlyId)
      host.expect(deck.sentRequests.slice(before).every(request => request.operation !== Protocol.Operations.QuicklinksGet)
          && controller.active === null,
        "opening Settings and refreshing during reconnection wait for protocol readiness")
      beforePong = deck.sentRequests.length
      stage = 2
      protocol.handleBridgeMessage({ requestId: protocol.reconnectProbeRequestId, type: Protocol.Messages.Pong })
      protocol.handleBridgeMessage({ requestId: protocol.reconnectProbeRequestId, type: Protocol.Messages.Done })
      return
    }
    const dispatched = deck.sentRequests.slice(beforePong)
      .filter(request => request.operation === Protocol.Operations.QuicklinksGet)
    if (!protocol.ready || dispatched.length === 0) return
    for (const request of dispatched) reply(request.requestId)
    if (controller.busy) return
    host.expect(controller.quicklinks.length === entries.length && controller.quicklinks[0].quicklink.id === "recovered",
      "probe Pong recovers quicklink loading through production protocol and controller routing")
    controller.submit("disable", { quicklinkId: "recovered" })
    const change = controller.active
    host.expect(change !== null && change.request.operation === Protocol.Operations.QuicklinksDisable,
      "quicklink management dispatches a change after reconnect recovery")
    if (change) reply(change.request.requestId, "Quicklink disabled.")
    host.expect(!controller.busy && controller.statusText === "Quicklink disabled.",
      "quicklink management completes a change after reconnect recovery")
    finish()
  }

  function reply(requestId, message) {
    deck.part_protocol.handleBridgeMessage({ requestId: requestId, type: Protocol.Messages.QuicklinksResult,
      quicklinks: { quicklinks: entries, message: message || "" } })
    deck.part_protocol.handleBridgeMessage({ requestId: requestId, type: Protocol.Messages.Done })
  }

  function finish() {
    poll.stop()
    deck.settingsOpen = false
    deck.part_protocol.enabled = false
    deck.part_quicklinksController.disconnect()
    stage = 0
    host.pendingAsyncSpecs--
  }
}
