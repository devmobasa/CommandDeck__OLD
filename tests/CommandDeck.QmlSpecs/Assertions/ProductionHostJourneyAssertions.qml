import QtQuick
import "Protocol.js" as Protocol

Item {
  id: journey
  required property var host
  readonly property var deck: host.part_integratedDeck
  property var steps: []
  property int stepIndex: 0
  property double deadline: 0
  property var completed: null
  property var pendingRequest: null
  property var pendingResult: null
  property int menuRefreshFrom: 0
  readonly property var menuActions: [
    { id: "open", title: "Open", kind: Protocol.ActionKinds.OpenPath, arguments: {} },
    { id: "reveal", title: "Reveal in File Manager", kind: Protocol.ActionKinds.RevealPath, arguments: {} }
  ]
  property int originalDebounce: 0

  SavedSearchShortcutJourneyAssertions { id: savedShortcuts; host: journey.host }

  Timer {
    id: stepTimer
    interval: 10
    onTriggered: journey.advance()
  }

  Timer {
    id: deliveryTimer
    interval: 250
    onTriggered: {
      journey.deck.handleMessage({ type: Protocol.Messages.Result,
        requestId: journey.pendingRequest.requestId, result: journey.pendingResult })
      journey.deck.handleMessage({ type: Protocol.Messages.Done,
        requestId: journey.pendingRequest.requestId })
    }
  }

  function latestSearch(query) {
    const searches = deck.sentRequests.filter(request => request.operation === Protocol.Operations.Search)
    const request = searches[searches.length - 1]
    return request && request.query === query ? request : null
  }

  function rowReady(title) {
    return deck.currentResult && deck.currentResult.title === title
      && deck.testSurfaceWindow.part_resultList.itemAtIndex(deck.selectedIndex) !== null
  }

  function undoReady() {
    const surface = deck.testSurfaceWindow
    const strip = surface ? host.part_hostEvents.findChild(surface, "hiddenUndoBar") : null
    if (strip === null || !strip.visible) return false
    const palette = surface.part_palette
    const width = Math.min(deck.part_appTheme.paletteWidth, surface.width - 80)
    const height = Math.min(deck.part_appTheme.paletteHeight,
      surface.height - palette.topInset - palette.bottomInset)
    // A positive button size can still belong to a palette animating out of
    // Settings. Wait for its final bounds before QtTest maps the click.
    return Math.abs(palette.width - width) < 0.1 && Math.abs(palette.height - height) < 0.1
      && palette.opacity === 1 && strip.undoControl.width > 0 && strip.undoControl.height > 0
  }

  function deliverLater(query, title, reference = "", actions = []) {
    pendingRequest = latestSearch(query)
    pendingResult = {
      id: "file:host-journey", kind: Protocol.ResultKinds.File, sourceId: Protocol.SourceIds.Files,
      title: title, invocationReference: reference, actions: actions
    }
    deliveryTimer.start()
  }

  function searchCount() {
    return deck.sentRequests.filter(request => request.operation === Protocol.Operations.Search).length
  }

  function lastInvoke() {
    const invokes = deck.sentRequests.filter(request => request.operation === Protocol.Operations.Invoke)
    return invokes.length > 0 ? invokes[invokes.length - 1] : null
  }

  function menuRow(actionId) {
    const panel = deck.activeActionPanel
    const index = panel ? panel.filteredActions.findIndex(action => action.id === actionId) : -1
    return index >= 0 ? panel.actionItemAt(index) : null
  }

  function settleInvoke(request) {
    deck.handleMessage({ type: Protocol.Messages.ActionResult, requestId: request.requestId,
      actionResult: { succeeded: false, message: "Fixture action settled" } })
  }

  function agentComposer() {
    return host.part_hostEvents.findChild(deck.testAgentSurface, "agentComposer")
  }

  function turnRequests() {
    return deck.sentRequests.filter(request => request.operation === Protocol.Operations.AgentTurnStart)
  }

  function typePrompt(text) {
    for (let index = 0; index < text.length; index++) host.part_hostEvents.keyClick(text.charAt(index))
  }

  function refuseLastTurn() {
    const request = turnRequests().slice(-1)[0]
    deck.handleMessage({ type: Protocol.Messages.Error, requestId: request.requestId,
      error: "Assistant permissions changed while starting; send again." })
    deck.handleMessage({ type: Protocol.Messages.Done, requestId: request.requestId })
  }

  function completeLastTurn() {
    const controller = deck.part_agentController
    const request = turnRequests().slice(-1)[0]
    deck.handleMessage({ type: Protocol.Messages.AgentEvent, requestId: request.requestId,
      agentEvent: { kind: Protocol.AgentEventKinds.Done, generation: 1,
        sequence: controller.lastSequence + 1, done: { outcome: Protocol.AgentTurnOutcomes.Completed } } })
    deck.handleMessage({ type: Protocol.Messages.Done, requestId: request.requestId })
  }

  function run(done) {
    completed = done
    originalDebounce = deck.part_searchSession.debounce.interval
    // Longer than the old fixed-step wait: readiness, not elapsed time, admits each step.
    deck.part_searchSession.debounce.interval = 250
    deck.sentRequests = []
    deck.testSurfaceEnabled = true
    deck.coachSeen = true
    deck.daemonAvailable = true
    deck.reconciliationPending = false
    host.expect(!deck.opened && !deck.testSurfaceWindow.part_searchBar.placeholderRotating,
      "a closed launcher leaves its search placeholder timer stopped")
    deck.open("{}")

    steps = [
      {
        ready: () => deck.testSurfaceWindow !== null && deck.testSurfaceWindow.visible
          && deck.testSearchInput !== null && deck.testSearchInput.activeFocus,
        message: "HEY undo starts with production search focused",
        action: () => host.openHeyUndoKeys()
      },
      {
        ready: () => deck.testSearchInput.activeFocus,
        message: "HEY undo accepts real shortcut keys",
        action: () => host.verifyHeyUndoKeys()
      },
      {
        ready: () => undoReady(),
        message: "HEY undo button is laid out for a real click",
        action: () => host.verifyHeyUndoClick()
      },
      {
        ready: () => !deck.opened,
        message: "stale activation journey starts after HEY closes",
        action: () => host.part_staleActivationJourney.start()
      },
      {
        ready: () => deck.testSurfaceWindow.visible && deck.testSearchInput.activeFocus
          && latestSearch("") !== null,
        message: "Home search owns a request before delivering the saved search",
        action: () => host.part_staleActivationJourney.typeOverSavedSearch()
      },
      {
        ready: () => latestSearch("w") !== null && deck.part_searchSession.inFlight,
        message: "the first typed query is in flight before replacing Home",
        action: () => host.part_staleActivationJourney.typeOverExtensionCommand()
      },
      {
        ready: () => latestSearch("we") !== null && deck.part_searchSession.inFlight,
        message: "the replacement query is in flight before activating its result",
        action: () => host.part_staleActivationJourney.openCurrentExtensionCommand()
      },
      {
        ready: () => deck.part_extensionController.opened,
        message: "current extension result opens after stale rows were refused",
        action: () => {
          host.part_staleActivationJourney.finish()
          deck.testSurfaceEnabled = true
          deck.sentRequests = []
          deck.open("{}")
        }
      },
      {
        ready: () => deck.testSurfaceWindow !== null && deck.testSurfaceWindow.visible
          && deck.testSearchInput !== null && deck.testSearchInput.activeFocus,
        message: "production root open focuses the real search input",
        action: () => {
          host.part_hostEvents.keyClick(Qt.Key_N)
          host.expect(deck.testSearchInput.text === "n" && deck.queryText === "n",
            "a real key event searches immediately after production open")
          host.expect(deck.testSearchInput.activeFocus && deck.part_searchSession.debounce.running,
            "production search stays focused while its request is debounced")
        }
      },
      {
        ready: () => latestSearch("n") !== null,
        message: "typing in the production root sends the edited search",
        action: () => deliverLater("n", "notes.txt")
      },
      {
        ready: () => rowReady("notes.txt"),
        message: "the production result row uses the delayed protocol search result",
        action: () => {
          const row = deck.testSurfaceWindow.part_resultList.itemAtIndex(deck.selectedIndex)
          host.part_hostEvents.mouseClick(row, 20, row.height / 2)
          host.expect(deck.testSearchInput.activeFocus, "clicking a result leaves production search ready")
          host.part_hostEvents.keyClick(Qt.Key_O)
          host.expect(deck.testSearchInput.text === "no" && deck.queryText === "no",
            "typing immediately after a result click edits the query")
        }
      },
      {
        ready: () => latestSearch("no") !== null,
        message: "typing after a result click sends the replacement search",
        action: () => {
          host.expect(deck.currentResult.title === "notes.txt",
            "the previous row remains visible while replacement results are delayed")
          deliverLater("no", "notes updated.txt")
        }
      },
      {
        ready: () => rowReady("notes updated.txt"),
        message: "asynchronous row replacement reaches the production list",
        action: () => {
          host.expect(deck.testSearchInput.activeFocus,
            "asynchronous row replacement preserves search focus")
          host.part_hostEvents.mouseClick(deck.testFooterActions.helpControl)
          host.expect(deck.helpOpen, "a mouse click opens Help from the production footer")
        }
      },
      {
        ready: () => deck.helpOpen,
        message: "Help remains open until a real Escape event",
        action: () => {
          host.part_hostEvents.keyClick(Qt.Key_Escape)
          host.expect(!deck.helpOpen && deck.opened, "Escape dismisses Help but keeps the launcher open")
        }
      },
      {
        ready: () => deck.testFooterActions.helpControl.activeFocus,
        message: "dismissing Help returns focus to its current footer control",
        action: () => {
          host.part_hostEvents.keyClick(Qt.Key_Tab)
          host.expect(deck.testSearchInput.activeFocus,
            "deliberate Tab navigation moves focus from Help back to search")
          host.part_hostEvents.keyClick(Qt.Key_Escape)
          host.expect(!deck.opened, "Escape dismisses the production root")
          deck.open("{}")
        }
      },
      {
        ready: () => deck.opened && deck.testSearchInput !== null && deck.testSearchInput.activeFocus,
        message: "reopening the production root restores search focus",
        action: () => {
          host.part_hostEvents.keyClick(Qt.Key_A)
          host.expect(deck.testSearchInput.text === "a" && deck.queryText === "a",
            "search accepts a real key event after reopen")
        }
      },
      {
        ready: () => latestSearch("a") !== null,
        message: "the reopened search is sent",
        action: () => deliverLater("a", "menu.txt", "ref-1", menuActions)
      },
      {
        ready: () => rowReady("menu.txt"),
        message: "an invocable row reaches the production list",
        action: () => {
          host.part_hostEvents.keyClick(Qt.Key_K, Qt.ControlModifier)
          host.expect(deck.actionPanelOpen, "Ctrl+K opens the action menu for the selected row")
        }
      },
      {
        ready: () => deck.activeActionPanel !== null && deck.activeActionPanel.filterControl.activeFocus,
        message: "the action menu focuses its filter",
        action: () => {
          host.part_hostEvents.keyClick(Qt.Key_Down)
          menuRefreshFrom = searchCount()
          deck.requestSearch()
        }
      },
      {
        ready: () => searchCount() > menuRefreshFrom && latestSearch("a") !== null,
        message: "an asynchronous refresh searches again under the open menu",
        action: () => deliverLater("a", "menu.txt", "ref-2", menuActions)
      },
      {
        ready: () => deck.currentResult && deck.currentResult.invocationReference === "ref-2",
        message: "the refreshed row replaces its revoked invocation reference",
        action: () => {
          const panel = deck.activeActionPanel
          host.expect(deck.actionPanelOpen && panel.filterControl.activeFocus
              && panel.filteredActions[panel.selectedIndex].id === "reveal",
            "a refresh keeps the menu open with its filter focus and selected action")
          host.part_hostEvents.keyClick(Qt.Key_Return)
          const invoke = lastInvoke()
          host.expect(invoke !== null && invoke.actionId === "reveal" && invoke.invocationReference === "ref-2",
            "Enter in the open menu invokes the refreshed row's reference, not the revoked one")
          host.expect(!deck.actionPanelOpen && deck.testSearchInput.activeFocus,
            "invoking from the menu returns focus to search")
          settleInvoke(invoke)
          host.part_hostEvents.keyClick(Qt.Key_K, Qt.ControlModifier)
        }
      },
      {
        ready: () => deck.actionPanelOpen && deck.activeActionPanel.filterControl.activeFocus,
        message: "the action menu reopens on its filter",
        action: () => {
          host.part_hostEvents.keyClick(Qt.Key_Tab)
          host.expect(menuRow("open") !== null && menuRow("open").activeFocus,
            "Tab moves from the menu filter to its first action")
          menuRefreshFrom = searchCount()
          deck.requestSearch()
        }
      },
      {
        ready: () => searchCount() > menuRefreshFrom && latestSearch("a") !== null,
        message: "a second refresh searches again under the focused action",
        action: () => deliverLater("a", "menu.txt", "ref-3", menuActions)
      },
      {
        ready: () => deck.currentResult && deck.currentResult.invocationReference === "ref-3"
          && menuRow("open") !== null && menuRow("open").activeFocus,
        message: "a refresh restores focus to the same action row",
        action: () => {
          const row = menuRow("open")
          host.part_hostEvents.mouseClick(row, 20, row.height / 2)
          const invoke = lastInvoke()
          host.expect(invoke !== null && invoke.actionId === "open" && invoke.invocationReference === "ref-3",
            "clicking an action after a refresh invokes the refreshed row's reference")
          settleInvoke(invoke)
          host.part_hostEvents.keyClick(Qt.Key_B)
          host.expect(deck.testSearchInput.activeFocus && deck.queryText === "ab",
            "typing right after a menu click edits the search")

          const controller = deck.part_agentController
          controller.resetConversation()
          controller.enabled = true
          controller.backend = { id: Protocol.AgentBackendIds.Codex,
            displayName: "Codex", authenticationState: Protocol.AgentAuthenticationStates.Authenticated }
          deck.openAgentSurface()
        }
      },
      {
        ready: () => deck.testAgentSurface !== null && deck.testAgentSurface.opened
          && agentComposer() !== null && agentComposer().activeFocus,
        message: "the production assistant opens with its composer focused",
        action: () => {
          typePrompt("Retry this prompt")
          host.part_hostEvents.keyClick(Qt.Key_Return)
          host.expect(turnRequests().length === 1 && agentComposer().text === "",
            "Enter submits and consumes the assistant draft")
          refuseLastTurn()
          host.expect(agentComposer().text === "Retry this prompt" && agentComposer().activeFocus,
            "a refused production start restores the prompt and composer focus")
        }
      },
      {
        ready: () => deck.testAgentSurface.part_retryButton.visible,
        message: "a refused start offers Retry in the production dialog",
        action: () => {
          host.part_hostEvents.keyClick(Qt.Key_Tab)
          host.part_hostEvents.keyClick(Qt.Key_Tab)
          host.expect(deck.testAgentSurface.part_retryButton.activeFocus,
            "Tab reaches Retry from the restored composer")
          host.part_hostEvents.keyClick(Qt.Key_Space)
          host.expect(turnRequests().length === 2 && turnRequests()[1].query === "Retry this prompt",
            "keyboard Retry sends the refused prompt once")
          host.expect(agentComposer().text === "", "Retry consumes the matching restored draft")
          completeLastTurn()
          host.part_hostEvents.mouseClick(agentComposer())
          host.part_hostEvents.keyClick(Qt.Key_Return)
          host.expect(turnRequests().length === 2,
            "Enter after a successful retry does not submit the same prompt again")

          typePrompt("Another prompt")
          host.part_hostEvents.keyClick(Qt.Key_Return)
          refuseLastTurn()
          host.expect(agentComposer().text === "Another prompt", "a second refusal restores its own prompt")
          typePrompt(" edited")
        }
      },
      {
        ready: () => deck.testAgentSurface.part_retryButton.visible,
        message: "Retry remains available after editing the restored draft",
        action: () => {
          host.expect(host.part_hostEvents.waitForRendering(deck.testAgentSurface.part_retryButton),
            "the retry button renders before its real mouse click")
          host.part_hostEvents.mouseClick(deck.testAgentSurface.part_retryButton)
          host.expect(turnRequests().length === 4 && turnRequests()[3].query === "Another prompt",
            "mouse Retry resends the refused prompt")
          host.expect(agentComposer().text === "Another prompt edited", "Retry preserves later composer edits")
          completeLastTurn()
          host.part_hostEvents.mouseClick(agentComposer())
          host.part_hostEvents.keyClick(Qt.Key_Return)
          host.expect(turnRequests().length === 5 && turnRequests()[4].query === "Another prompt edited"
              && agentComposer().text === "",
            "the preserved draft can be submitted after retry completion")
        }
      }
    ]
    steps = steps.concat(savedShortcuts.steps())
    stepIndex = 0
    deadline = Date.now() + 2000
    stepTimer.start()
  }

  function advance() {
    const step = steps[stepIndex]
    try {
      if (!step.ready()) {
        if (Date.now() < deadline) {
          stepTimer.start()
          return
        }

        host.expect(false, step.message + " (readiness deadline exceeded)")
        finish()
        return
      }

      host.expect(true, step.message)
      const failures = host.failures
      step.action()
      if (host.failures !== failures || ++stepIndex === steps.length) {
        finish()
        return
      }

      deadline = Date.now() + 2000
      stepTimer.start()
    } catch (error) {
      host.expect(false, step.message + ": " + error)
      finish()
    }
  }

  function finish() {
    stepTimer.stop()
    deliveryTimer.stop()
    savedShortcuts.restore()
    deck.close()
    deck.part_searchSession.debounce.interval = originalDebounce
    deck.testSurfaceEnabled = false
    completed()
  }
}
