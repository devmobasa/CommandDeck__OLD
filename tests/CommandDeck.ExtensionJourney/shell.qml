import QtQuick
import Quickshell
import Quickshell.Io
import "plugin" as CommandDeck

ShellRoot {
  CommandDeck.CommandDeck {
    id: launcher
    testMode: true
    testSurfaceEnabled: true
    coachSeen: true
    onProtocolRequestSent: value => bridge.send(value)
  }
  CommandDeck.ProtocolClient {
    id: bridge
    executable: Quickshell.env("COMMANDDECK_TEST_HOST")
    socketPath: Quickshell.env("COMMANDDECK_TEST_SOCKET")
    onMessage: value => launcher.handleMessage(value)
    onDisconnected: launcher.handleProtocolDisconnected()
    onReconnected: launcher.handleProtocolReconnected()
  }
  IpcHandler {
    target: "extension-journey"
    function open(): void {
      launcher.open("{}")
      launcher.openedScreenName = launcher.testSurfaceWindow.screen.name
    }
    function close(): void { launcher.close() }
    function state(): string {
      const view = launcher.testExtensionView
      const session = launcher.extensionTestController
      function point(item) {
        if (!item) return null
        const position = item.mapToGlobal(item.width / 2, item.height / 2)
        return { x: Math.round(position.x), y: Math.round(position.y) }
      }
      const actions = view ? view.viewActionsControl : null
      function action(id) {
        if (!actions) return null
        for (let index = 0; index < actions.count; index++) {
          if (actions.model[index].action.id === id) return actions.itemAt(index)
        }
        return null
      }
      const regenerate = action("regenerate")
      const details = action("details")
      return JSON.stringify({ opened: launcher.opened, query: launcher.queryText,
        searchFocused: launcher.testSearchInput ? launcher.testSearchInput.activeFocus : false,
        result: launcher.currentResult, extensionOpen: session.opened, sessionId: session.sessionId,
        revision: session.snapshot.revision, busy: session.busy, error: session.error,
        viewFocused: view ? view.inputControl.activeFocus : false,
        viewQuery: view ? view.inputControl.text : "", selectedId: view ? view.selectedId : "",
        count: view ? view.filtered.length : 0,
        primaryFocused: view ? view.primaryControl.activeFocus : false,
        restartFocused: view ? view.restartControl.activeFocus : false,
        disableFocused: view ? view.disableControl.activeFocus : false,
        callbackFocused: regenerate ? regenerate.activeFocus : false,
        detailsFocused: details ? details.activeFocus : false,
        kind: session.snapshot.content.kind || "",
        detailFocused: view ? view.detailControl.bodyControl.activeFocus : false,
        detailCopy: view && view.detailControl.actionsControl.count ? point(view.detailControl.actionsControl.itemAt(0)) : null,
        detailBack: view ? point(view.detailControl.backControl) : null,
        details: point(details),
        nextPage: view ? point(view.listControl.nextPageControl) : null,
        previousPage: view ? point(view.listControl.previousPageControl) : null,
        copy: view ? point(view.primaryControl) : null,
        regenerate: point(regenerate),
        back: view ? point(view.backControl) : null })
    }
  }
}
