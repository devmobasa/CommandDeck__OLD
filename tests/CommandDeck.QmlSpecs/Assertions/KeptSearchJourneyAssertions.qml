import QtQuick
import "Protocol.js" as Protocol

QtObject {
  id: journey
  required property var host
  readonly property var deck: host.part_integratedDeck
  property var originalPreferences: null
  property int originalKeepSeconds: 0
  property bool originalEscapeClears: false
  property int searchBeforeEdit: 0
  property var pendingInvoke: null

  function searchFocused() {
    const input = deck.testSearchInput
    const window = input ? input.Window.window : null
    return window !== null && window.active && input.activeFocus
  }

  function latestSearch(query) {
    const requests = deck.sentRequests.filter(request => request.operation === Protocol.Operations.Search)
    const request = requests[requests.length - 1]
    return request && request.query === query ? request : null
  }

  function deliver(request, collectFiles = false) {
    const actions = [{ id: "open", title: "Open", kind: Protocol.ActionKinds.OpenPath }]
    if (collectFiles) actions.push({ id: "collect", title: "Collect File", kind: Protocol.ActionKinds.CollectFile,
      arguments: { path: "/tmp/keep.txt" } })

    deck.handleMessage({ type: Protocol.Messages.Result, requestId: request.requestId, result: {
      id: "file:keep-action", kind: Protocol.ResultKinds.File, sourceId: Protocol.SourceIds.Files,
      title: "keep.txt", invocationReference: "keep-action",
      actions: actions } })
    deck.handleMessage({ type: Protocol.Messages.Done, requestId: request.requestId,
      searchCompletion: { containsPrivateQuery: false } })
  }

  function restore() {
    if (originalPreferences === null) return
    deck.part_personalization.resetPreferences(originalPreferences)
    deck.applyLauncherBehavior(originalEscapeClears, originalKeepSeconds)
    originalPreferences = null
  }

  function steps() {
    const steps = [{
      ready: () => true,
      message: "kept-search action journeys use the keep-open preference",
      action: () => {
        originalPreferences = deck.part_personalization.acknowledged
        originalKeepSeconds = deck.keepSearchSeconds
        originalEscapeClears = deck.escapeClearsSearch
        deck.part_personalization.resetPreferences(Object.assign({}, originalPreferences, { hideAfterAction: false }))
        deck.applyLauncherBehavior(false, 30)
      }
    }]
    for (const scenario of ["accepted", "query edited", "filter edited", "failed", "delayed acceptance"]) {
      const initialQuery = scenario === "delayed acceptance" ? "alpha" : "k"
      const restoredQuery = scenario === "accepted" ? "" : scenario === "query edited" ? "kx"
        : scenario === "delayed acceptance" ? "beta" : "k"
      steps.push({
        ready: () => true,
        message: "open a fresh search for " + scenario,
        action: () => {
          deck.close()
          deck.lastSearch = null
          deck.open(scenario === "filter edited" ? JSON.stringify({ filterId: Protocol.SourceIds.Files }) : "{}")
        }
      }, {
        ready: () => searchFocused() && latestSearch("") !== null,
        message: "type the search with real key events for " + scenario,
        action: () => {
          for (const character of initialQuery) host.part_hostEvents.keyClick(character)
        }
      }, {
        ready: () => latestSearch(initialQuery) !== null,
        message: "complete the searched results for " + scenario,
        action: () => deliver(latestSearch(initialQuery))
      }, {
        ready: () => searchFocused() && deck.currentResult !== null && deck.currentResult.id === "file:keep-action",
        message: "invoke with Enter and keep the launcher open for " + scenario,
        action: () => {
          const start = deck.sentRequests.length
          host.part_hostEvents.keyClick(Qt.Key_Return)
          const invoke = deck.sentRequests.slice(start).find(request => request.operation === Protocol.Operations.Invoke)
          host.expect(invoke !== undefined, "Enter requests the selected action; status " + JSON.stringify(deck.statusText))
          if (invoke === undefined) return
          pendingInvoke = invoke
          if (scenario !== "delayed acceptance") {
            deck.handleMessage({ type: Protocol.Messages.ActionResult, requestId: invoke.requestId,
              actionResult: { succeeded: scenario !== "failed" } })
          }
          host.expect(deck.opened && deck.testSearchInput.activeFocus,
            "the action leaves the launcher open and search focused")
          searchBeforeEdit = latestSearch(initialQuery).requestId
          if (scenario === "query edited") host.part_hostEvents.keyClick(Qt.Key_X)
          if (scenario === "filter edited") {
            host.part_hostEvents.keyClick(Qt.Key_Home)
            host.part_hostEvents.keyClick(Qt.Key_Backspace)
            host.expect(deck.activeFilters.length === 0 && deck.queryText === "k",
              "Backspace removes the existing filter chip without changing the query")
          }
          if (scenario === "delayed acceptance") {
            host.part_hostEvents.keyClick(Qt.Key_A, Qt.ControlModifier)
            for (const character of "beta") host.part_hostEvents.keyClick(character)
          }
        }
      }, {
        ready: () => {
          const request = latestSearch(scenario === "accepted" ? initialQuery : restoredQuery)
          return request !== null && (scenario !== "filter edited" || request.requestId !== searchBeforeEdit)
        },
        message: "close with Escape and reopen after " + scenario,
        action: () => {
          deliver(latestSearch(scenario === "accepted" ? initialQuery : restoredQuery))
          if (scenario === "delayed acceptance") {
            deck.handleMessage({ type: Protocol.Messages.ActionResult, requestId: pendingInvoke.requestId,
              actionResult: { succeeded: true } })
            host.expect(deck.opened && searchFocused(), "delayed acceptance keeps the newer search ready")
          }
          host.part_hostEvents.keyClick(Qt.Key_Escape)
          host.expect(!deck.opened, "Escape closes the launcher")
          deck.open("{}")
          host.expect(deck.queryText === restoredQuery,
            "reopening restores only an unused or newly edited search: " + scenario
              + "; restored query " + JSON.stringify(deck.queryText))
          host.expect(deck.activeFilters.length === 0,
            "reopening restores the edited filters: " + scenario)
        }
      }, {
        ready: () => searchFocused(),
        message: "reopening restores search focus after " + scenario,
        action: () => host.expect(deck.testSearchInput.activeFocus, "reopened search is ready for typing")
      })
    }
    return steps.concat(lateReplySteps()).concat(batchSteps())
  }

  function lateReplySteps() {
    const steps = []
    const arrivals = ["before Escape", "after Escape", "after reopen", "after second close",
      "after second reopen", "after edit"]
    const cases = arrivals.map((label, arrival) => ({ arrival: arrival, label: label, edit: "query" }))
      .concat([{ arrival: 5, label: "after filter edit", edit: "filter" },
        { arrival: 5, label: "after edit back to same text", edit: "same text" }])
    for (const succeeded of [true, false]) {
      for (const ordering of cases) {
        const arrival = ordering.arrival
        const scenario = (succeeded ? "success " : "failure ") + ordering.label
        const expected = arrival === 5 ? ordering.edit === "query" ? "beta" : "alpha" : succeeded ? "" : "alpha"
        const reply = checkpoint => {
          if (checkpoint !== arrival) return
          const detached = !deck.part_invocationController.busy
          const opened = deck.opened
          const status = deck.statusText
          const focusRevision = deck.focusRevision
          const requests = deck.sentRequests.length
          deck.handleMessage({ type: Protocol.Messages.ActionResult, requestId: pendingInvoke.requestId,
            actionResult: { succeeded: succeeded, message: "Ordered reply", detailMarkdown: detached ? "Output" : "" } })
          if (detached) {
            host.expect(deck.opened === opened && !deck.detailDialogRequested && deck.statusText === status
                && deck.focusRevision === focusRevision && deck.hudText === "" && deck.sentRequests.length === requests,
              "a detached reply changes only search retention: " + scenario)
          }
        }
        steps.push({
          ready: () => true,
          message: "start ordered action journey: " + scenario,
          action: () => {
            deck.close()
            deck.lastSearch = null
            deck.part_personalization.resetPreferences(Object.assign({}, originalPreferences, { hideAfterAction: false }))
            deck.open(ordering.edit === "filter" ? JSON.stringify({ filterId: Protocol.SourceIds.Files }) : "{}")
          }
        }, {
          ready: () => searchFocused() && latestSearch("") !== null,
          message: "type alpha with real keys: " + scenario,
          action: () => {
            for (const character of "alpha") host.part_hostEvents.keyClick(character)
          }
        }, {
          ready: () => latestSearch("alpha") !== null,
          message: "complete alpha search: " + scenario,
          action: () => deliver(latestSearch("alpha"))
        }, {
          ready: () => searchFocused() && deck.currentResult !== null && deck.currentResult.id === "file:keep-action",
          message: "invoke alpha with Enter: " + scenario,
          action: () => {
            const start = deck.sentRequests.length
            host.part_hostEvents.keyClick(Qt.Key_Return)
            pendingInvoke = deck.sentRequests.slice(start).find(request => request.operation === Protocol.Operations.Invoke)
            host.expect(pendingInvoke !== undefined && deck.part_invocationController.busy,
              "Enter starts the pending action: " + scenario)
            reply(0)
          }
        })
        for (let cycle = 0; cycle < 2; cycle++) {
          steps.push({
            ready: () => searchFocused(),
            message: "Escape closes cycle " + cycle + ": " + scenario,
            action: () => {
              host.part_hostEvents.keyClick(Qt.Key_Escape)
              host.expect(!deck.opened, "Escape closes pending action journey: " + scenario)
            }
          }, {
            ready: () => !deck.testSurfaceWindow.visible,
            message: "reply while closed and reopen cycle " + cycle + ": " + scenario,
            action: () => {
              reply(1 + cycle * 2)
              host.expect(!deck.opened && !deck.testSurfaceWindow.visible,
                "a closed reply leaves the launcher hidden: " + scenario)
              deck.open("{}")
            }
          }, {
            ready: () => searchFocused() && latestSearch(deck.queryText) !== null,
            message: "complete restored search cycle " + cycle + ": " + scenario,
            action: () => {
              deliver(latestSearch(deck.queryText))
              reply(2 + cycle * 2)
            }
          })
        }
        if (arrival === 5) {
          steps.push({
            ready: () => searchFocused(),
            message: "edit restored search before reply: " + scenario,
            action: () => {
              searchBeforeEdit = latestSearch("alpha").requestId
              if (ordering.edit === "filter") {
                host.part_hostEvents.keyClick(Qt.Key_Home)
                host.part_hostEvents.keyClick(Qt.Key_Backspace)
              } else {
                host.part_hostEvents.keyClick(Qt.Key_A, Qt.ControlModifier)
                for (const character of "beta") host.part_hostEvents.keyClick(character)
                if (ordering.edit === "same text") {
                  host.part_hostEvents.keyClick(Qt.Key_A, Qt.ControlModifier)
                  for (const character of "alpha") host.part_hostEvents.keyClick(character)
                }
              }
            }
          }, {
            ready: () => latestSearch(expected) !== null && latestSearch(expected).requestId !== searchBeforeEdit,
            message: "complete edited search then reply: " + scenario,
            action: () => { deliver(latestSearch(expected)); reply(5) }
          })
        }
        steps.push({
          ready: () => searchFocused(),
          message: "final Escape and reopen: " + scenario,
          action: () => {
            host.part_hostEvents.keyClick(Qt.Key_Escape)
            host.expect(!deck.opened, "final Escape closes: " + scenario)
            deck.open("{}")
            host.expect(deck.queryText === expected && deck.activeFilters.length === 0,
              "final reopen restores " + JSON.stringify(expected) + ": " + scenario
                + "; got " + JSON.stringify(deck.queryText))
          }
        }, {
          ready: () => searchFocused(),
          message: "final reopen is keyboard ready: " + scenario,
          action: () => host.expect(deck.testSearchInput.activeFocus, "final search focus: " + scenario)
        })
      }
    }
    return steps
  }

  function batchSteps() {
    const steps = []
    for (const hideAfterAction of [true, false]) {
      for (const actionId of [Protocol.FileSelectionActionIds.CopyPaths, Protocol.FileSelectionActionIds.Open]) {
        const label = actionId === Protocol.FileSelectionActionIds.CopyPaths ? "Copy paths" : "Open all"
        const scenario = label + " with hide-after-action " + (hideAfterAction ? "on" : "off")
        steps.push({
          ready: () => true,
          message: "open a fresh batch search for " + scenario,
          action: () => {
            deck.close()
            deck.lastSearch = null
            deck.part_personalization.resetPreferences(Object.assign({}, originalPreferences, { hideAfterAction: hideAfterAction }))
            deck.open("{}")
          }
        }, {
          ready: () => searchFocused() && latestSearch("") !== null,
          message: "type the file search with real keys for " + scenario,
          action: () => {
            for (const character of "batch") host.part_hostEvents.keyClick(character)
          }
        }, {
          ready: () => latestSearch("batch") !== null,
          message: "deliver a collectable file for " + scenario,
          action: () => deliver(latestSearch("batch"), true)
        }, {
          ready: () => searchFocused() && deck.currentResult !== null && deck.currentResult.id === "file:keep-action",
          message: "open the file action menu with real keys for " + scenario,
          action: () => host.part_hostEvents.keyClick(Qt.Key_K, Qt.ControlModifier)
        }, {
          ready: () => deck.actionPanelOpen && deck.activeActionPanel.filterControl.activeFocus,
          message: "collect the file through the action menu for " + scenario,
          action: () => {
            host.part_hostEvents.keyClick(Qt.Key_Down)
            host.part_hostEvents.keyClick(Qt.Key_Return)
            const select = deck.sentRequests[deck.sentRequests.length - 1]
            host.expect(select.operation === Protocol.Operations.FilesSelect && select.resultId === "file:keep-action"
                && select.enabled === true, "the keyboard action requests collection of the searched file")
            deck.handleMessage({ type: Protocol.Messages.FileSelection, requestId: select.requestId,
              fileSelection: [{ id: "file:keep-action", title: "keep.txt", path: "/tmp/keep.txt" }] })
          }
        }, {
          ready: () => searchFocused() && deck.fileSelection.length === 1 && !deck.actionPanelOpen,
          message: "Shift+Tab to the batch button and activate it for " + scenario,
          action: () => {
            const window = deck.testSearchInput.Window.window
            for (let tab = 0; tab < 20 && (!window.activeFocusItem || window.activeFocusItem.text !== label); tab++) {
              host.part_hostEvents.keyClick(Qt.Key_Backtab, Qt.ShiftModifier)
            }
            host.expect(window.activeFocusItem && window.activeFocusItem.text === label,
              "Shift+Tab reaches " + label + " from search")
            const start = deck.sentRequests.length
            host.part_hostEvents.keyClick(Qt.Key_Return)
            pendingInvoke = deck.sentRequests.slice(start).find(request => request.operation === Protocol.Operations.FilesBatch)
            host.expect(pendingInvoke !== undefined && pendingInvoke.actionId === actionId,
              "Enter sends the selected batch action")
          }
        }, {
          ready: () => pendingInvoke !== undefined && deck.part_invocationController.busy,
          message: "complete the batch and reopen for " + scenario,
          action: () => {
            deck.handleMessage({ type: Protocol.Messages.ActionResult, requestId: pendingInvoke.requestId,
              actionResult: { succeeded: true, message: "Done" } })
            host.expect(deck.opened === !hideAfterAction, "the batch respects hide-after-action")
            if (!hideAfterAction) {
              for (let tab = 0; tab < 20 && !deck.testSearchInput.activeFocus; tab++) {
                host.part_hostEvents.keyClick(Qt.Key_Backtab, Qt.ShiftModifier)
              }
              host.expect(deck.testSearchInput.activeFocus, "Shift+Tab returns from the batch button to search")
              host.part_hostEvents.keyClick(Qt.Key_Escape)
              host.expect(!deck.opened, "Escape closes after the batch stays open")
            }
            deck.open("{}")
            host.expect(deck.queryText === "" && deck.activeFilters.length === 0,
              "reopening forgets the search used by " + scenario + "; restored query " + JSON.stringify(deck.queryText))
          }
        }, {
          ready: () => searchFocused(),
          message: "reopening restores search focus after " + scenario,
          action: () => host.expect(deck.testSearchInput.activeFocus, "reopened search is ready after a batch action")
        })
      }
    }
    return steps
  }
}
