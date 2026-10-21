import QtQuick
import "Protocol.js" as Protocol

QtObject {
  id: assertions
  required property var host
  property QuicklinkReconnectAssertions reconnectSpec: QuicklinkReconnectAssertions { host: assertions.host }
  function find(item, name) {
    if (item.objectName === name) return item
    for (const child of item.children || []) {
      const found = find(child, name)
      if (found) return found
    }
    return null
  }
  function run() {
    const panel = host.part_settings
    const controller = panel.quicklinksController
    panel.visible = true
    panel.showPage("search")
    panel.searchControl.text = "quicklinks"
    const entries = [
      { source: "configured", enabled: false, quicklink: { id: "configured", title: "Config docs", urlTemplate: "https://example.test/{query}" } },
      { source: "user", enabled: true, quicklink: { id: "user", title: "User docs", urlTemplate: "https://example.test/{query}", alias: "docs" } }
    ]
    const request = controller.active.request
    controller.handleMessage({ requestId: request.requestId, type: Protocol.Messages.QuicklinksResult,
      quicklinks: { quicklinks: entries, webFallbackQuicklinkId: "user" } })
    controller.handleMessage({ requestId: request.requestId, type: Protocol.Messages.Done })
    const configured = find(panel, "quicklinkEdit-configured")
    const user = find(panel, "quicklinkEdit-user")
    host.expect(panel.part_quicklinksCard.visible && configured && configured.text === "Enable",
      "Quicklinks card renders a disabled configured row with Enable")
    host.expect(user && user.text === "Edit" && find(panel, "quicklinkRemove-user").visible,
      "Quicklinks card renders edit and removal for a user row")
    host.expect(!find(panel, "quicklinkRemove-configured").visible,
      "Quicklinks card does not offer removal for a configured row")
    host.expect(controller.fallbackId === "user", "Quicklinks component receives the daemon fallback identity")
    const deck = host.part_integratedDeck
    const productionController = deck.part_quicklinksController
    productionController.load()
    const productionId = productionController.active.request.requestId
    deck.handleMessage({ requestId: productionId, type: Protocol.Messages.QuicklinksResult,
      quicklinks: { quicklinks: entries, webFallbackQuicklinkId: "user" } })
    host.expect(deck.fallbackQuicklinks.length === 1 && deck.fallbackQuicklinks[0].id === "user",
      "production message routing updates the fallback choices from effective quicklinks")
    deck.handleMessage({ requestId: productionId, type: Protocol.Messages.Done })
    productionController.disconnect()
    reconnectSpec.start()
    panel.visible = false
    controller.disconnect()
  }

}
