import QtQuick
import Quickshell.Io
import "../common" as Common

Item {
  width: 0
  height: 0

  Common.CommandDeck {
    id: launcher
    desktopIntegration: Common.HyprlandDesktopIntegration { }
  }

  IpcHandler {
    target: "commanddeck"

    function open(payloadJson: string): string {
      launcher.open(payloadJson)
      return "ok"
    }

    function close(): string {
      launcher.close()
      return "ok"
    }

    function toggle(payloadJson: string): string {
      launcher.toggle(payloadJson)
      return "ok"
    }
  }
}
