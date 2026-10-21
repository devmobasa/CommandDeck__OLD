pragma ComponentBehavior: Bound
import QtQuick
import "DeckSearchActions.js" as DeckSearchActions
import "Protocol.js" as Protocol

QtObject {
  id: lifecycle
  required property var host
  property string frontendInstance: newInstance()
  property double openGeneration: 0

  function newInstance() {
    return "xxxxxxxxxxxx4xxxyxxxxxxxxxxxxxxx".replace(/[xy]/g, function(value) {
      const digit = Math.floor(Math.random() * 16)
      return (value === "x" ? digit : (digit & 3) | 8).toString(16)
    })
  }

  function reconnected() {
    frontendInstance = newInstance()
    openGeneration = host.opened ? 1 : 0
    host.part_clipboardController.close(false)
  }

  function toggle(payload) { return DeckSearchActions.toggle(lifecycle.host, payload) }
  function open(payload) {
    if (openGeneration >= 9007199254740991) {
      host.part_protocol.restart()
      return
    }
    // Publish before any surface becomes visible, on every launcher-open path.
    openGeneration++
    host.part_clipboardController.close(false)
    return DeckSearchActions.open(lifecycle.host, payload)
  }
  function close() {
    // Omarchy's deferred shell.hide can close the same session again.
    if (!host.opened && !host.suspendedForScreen) return

    if (openGeneration > 0 && host.daemonAvailable) {
      host.part_protocol.send({version: Protocol.Version, requestId: ++host.requestSequence,
        operation: Protocol.Operations.LauncherOpenCancel,
        clipboard: {frontendInstance: frontendInstance, openGeneration: openGeneration}})
    }
    host.part_clipboardController.close(false)
    return DeckSearchActions.close(lifecycle.host)
  }
  function dismiss() { return DeckSearchActions.dismiss(lifecycle.host) }
}
