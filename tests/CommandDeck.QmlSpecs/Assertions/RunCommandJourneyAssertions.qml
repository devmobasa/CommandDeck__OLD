import QtQuick
import "Protocol.js" as Protocol

// `> program arguments` with real keys: Enter with no row runs nothing, the
// daemon's Run row is first, Ctrl+K offers the terminal and copy actions, and
// Enter dispatches the row's own run action. The fixture daemon never launches.
QtObject {
  id: journey
  required property var host
  readonly property var deck: host.part_integratedDeck
  readonly property string query: "> some-fixture-binary --flag"
  readonly property var argv: ["some-fixture-binary", "--flag"]
  property int start: 0

  function launch(id, title, terminal) {
    return { id: id, title: title, kind: Protocol.ActionKinds.LaunchArgv,
      arguments: { argv: JSON.stringify(journey.argv), terminal: terminal ? "true" : "false" } }
  }

  readonly property var runRow: ({
    id: "run-command:fixture", kind: Protocol.ResultKinds.CustomCommand,
    sourceId: Protocol.SourceIds.CommandsQuicklinks, providerId: Protocol.ProviderIds.RunCommand,
    title: "Run some-fixture-binary --flag", subtitle: "Program · runs without a shell",
    invocationReference: "run-reference",
    actions: [launch("run", "Run", false), launch("run-in-terminal", "Run in Terminal", true),
      { id: "copy-command", title: "Copy Command", kind: Protocol.ActionKinds.CopyText,
        arguments: { text: "some-fixture-binary --flag" } }]
  })

  function searchFocused() {
    const input = deck.testSearchInput
    const window = input ? input.Window.window : null
    return window !== null && window.active && input.activeFocus
  }

  function latestSearch(text) {
    const requests = deck.sentRequests.filter(request => request.operation === Protocol.Operations.Search)
    const request = requests[requests.length - 1]
    return request && request.query === text ? request : null
  }

  function invokes() {
    return deck.sentRequests.slice(start).filter(request => request.operation === Protocol.Operations.Invoke)
  }

  function type(text) {
    host.part_hostEvents.keyClick(Qt.Key_A, Qt.ControlModifier)
    for (const character of text) host.part_hostEvents.keyClick(character)
  }

  function done(request, results) {
    for (const result of results) {
      deck.handleMessage({ type: Protocol.Messages.Result, requestId: request.requestId, result: result })
    }
    deck.handleMessage({ type: Protocol.Messages.Done, requestId: request.requestId,
      searchCompletion: { containsPrivateQuery: false } })
  }

  function settle(succeeded) {
    const invoke = invokes()[invokes().length - 1]
    deck.handleMessage({ type: Protocol.Messages.ActionResult, requestId: invoke.requestId,
      actionResult: { succeeded: succeeded, message: "Fixture action settled" } })
  }

  function idle() {
    return searchFocused() && !deck.actionPanelOpen && !deck.part_invocationController.busy
  }

  function restore() {
    deck.close()
  }

  function steps() {
    return [{
      ready: () => true,
      message: "open a fresh search for the Run row",
      action: () => {
        deck.close()
        deck.lastSearch = null
        deck.open("{}")
      }
    }, {
      ready: () => searchFocused() && latestSearch("") !== null,
      message: "type a program that is not on PATH with real keys",
      action: () => type("> missing-program")
    }, {
      ready: () => latestSearch("> missing-program") !== null,
      message: "the daemon offers no Run row for a missing program",
      action: () => done(latestSearch("> missing-program"), [])
    }, {
      ready: () => searchFocused() && deck.currentResult === null && !deck.part_searchSession.inFlight,
      message: "Enter without a Run row runs nothing",
      action: () => {
        start = deck.sentRequests.length
        host.part_hostEvents.keyClick(Qt.Key_Return)
        host.expect(invokes().length === 0, "Enter with no row sends no invocation")
        type(journey.query)
      }
    }, {
      ready: () => latestSearch(journey.query) !== null,
      message: "the daemon answers the typed command with its Run row first",
      action: () => done(latestSearch(journey.query), [journey.runRow, {
        id: "desktop:volume-up", kind: Protocol.ResultKinds.CustomCommand,
        sourceId: Protocol.SourceIds.DesktopControls, title: "Volume Up", invocationReference: "volume",
        actions: [{ id: "run", title: "Run", kind: Protocol.ActionKinds.RunArgv }] }])
    }, {
      ready: () => searchFocused() && deck.currentResult !== null && deck.currentResult.id === journey.runRow.id,
      message: "the first row is Run; Ctrl+K opens its actions",
      action: () => {
        host.expect(deck.currentResult.title === "Run some-fixture-binary --flag", "the selected first row is Run")
        start = deck.sentRequests.length
        host.part_hostEvents.keyClick(Qt.Key_K, Qt.ControlModifier)
      }
    }, {
      ready: () => deck.actionPanelOpen && deck.activeActionPanel.filterControl.activeFocus,
      message: "Run in Terminal from the action menu with real keys",
      action: () => {
        const ids = deck.activeActionPanel.filteredActions.map(action => action.id)
        host.expect(ids.indexOf("run-in-terminal") === 1 && ids.indexOf("copy-command") === 2,
          "the action menu offers Run in Terminal and Copy Command; got " + JSON.stringify(ids))
        host.part_hostEvents.keyClick(Qt.Key_Down)
        host.part_hostEvents.keyClick(Qt.Key_Return)
        const invoke = invokes()[0]
        host.expect(invokes().length === 1 && invoke.actionId === "run-in-terminal" && invoke.resultId === journey.runRow.id,
          "the menu dispatches Run in Terminal for the Run row")
        settle(false)
      }
    }, {
      ready: () => idle(),
      message: "Ctrl+Shift+C copies the command",
      action: () => {
        host.part_hostEvents.keyClick(Qt.Key_C, Qt.ControlModifier | Qt.ShiftModifier)
        host.expect(invokes().length === 2 && invokes()[1].actionId === "copy-command",
          "the copy shortcut dispatches Copy Command")
        settle(false)
      }
    }, {
      ready: () => idle() && deck.currentResult !== null && deck.currentResult.id === journey.runRow.id,
      message: "Enter runs the exact argv of the Run row",
      action: () => {
        host.part_hostEvents.keyClick(Qt.Key_Return)
        const invoke = invokes()[2]
        host.expect(invokes().length === 3 && invoke.actionId === "run" && invoke.resultId === journey.runRow.id
            && invoke.invocationReference === "run-reference"
            && (!invoke.inputValues || Object.keys(invoke.inputValues).length === 0),
          "Enter dispatches the Run row's run action by reference only")
        const action = deck.currentResult.actions.find(candidate => candidate.id === invoke.actionId)
        host.expect(action.kind === Protocol.ActionKinds.LaunchArgv
            && JSON.stringify(JSON.parse(action.arguments.argv)) === JSON.stringify(journey.argv)
            && action.arguments.terminal === "false",
          "the dispatched action launches exactly " + JSON.stringify(journey.argv))
        settle(true)
      }
    }]
  }
}
