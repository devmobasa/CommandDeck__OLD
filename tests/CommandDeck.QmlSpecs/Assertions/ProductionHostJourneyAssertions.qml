import QtQuick
import Quickshell
import Quickshell.Wayland
import "Protocol.js" as Protocol

Item {
  id: journey
  required property var host
  readonly property var deck: host.part_integratedDeck
  readonly property alias pointerIsolation: pointerIsolation
  Region { id: pointerIsolation }
  Connections {
    target: journey.deck
    function onTestSurfaceWindowChanged() {
      if (!journey.deck.testSurfaceWindow) return
      journey.deck.testSurfaceWindow.mask = pointerIsolation
      const strip = journey.host.part_hostEvents.findChild(journey.deck.testSurfaceWindow, "hiddenUndoBar")
      if (strip) strip.holdMs = 300000
    }
  }
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
  property var originalSizing: null
  property var originalScreens: []
  property int baselineWorkAreaHeight: 0

  // A real exclusive zone, plus a non-exclusive witness that confirms the
  // compositor applied it before we inspect the launcher's full-screen scrim.
  // qmllint disable uncreatable-type
  PanelWindow {
    id: reservedBar
    visible: false
    screen: journey.deck.desktopIntegration.screens[0] || null
    anchors { top: true; left: true; right: true }
    implicitHeight: 48
    exclusiveZone: 48
    color: "transparent"
    WlrLayershell.layer: WlrLayer.Top
    WlrLayershell.keyboardFocus: WlrKeyboardFocus.None
  }
  // qmllint disable uncreatable-type
  PanelWindow {
    id: workArea
    visible: false
    screen: reservedBar.screen
    anchors { top: true; bottom: true; left: true; right: true }
    exclusionMode: ExclusionMode.Normal
    color: "transparent"
    WlrLayershell.layer: WlrLayer.Overlay
    WlrLayershell.keyboardFocus: WlrKeyboardFocus.None
  }

  SavedSearchShortcutJourneyAssertions { id: savedShortcuts; host: journey.host }
  HoverSelectionJourneyAssertions { id: hoverSelection; host: journey.host }
  KeptSearchJourneyAssertions { id: keptSearch; host: journey.host }
  OmarchyCloseJourneyAssertions { id: omarchyClose; host: journey.host }

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
    const width = surface.displaySizing.panelWidth
    const height = surface.displaySizing.panelHeight
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
    host.expect(deck.testSurfaceWindow.mask === host.pointerIsolation,
      "production fixture windows reject native pointer input while accepting QtTest events")
    workArea.visible = true

    steps = [
      {
        ready: () => workArea.backingWindowVisible && workArea.width === reservedBar.screen.width
          && workArea.height > 48,
        message: "the work-area witness is configured before adding an exclusive zone",
        action: () => {
          baselineWorkAreaHeight = workArea.height
          host.expect(baselineWorkAreaHeight <= reservedBar.screen.height,
            "the baseline work area respects existing desktop reservations")
          reservedBar.visible = true
          deck.open("{}")
        }
      },
      {
        ready: () => deck.testSurfaceWindow !== null && deck.testSearchInput.activeFocus
          && workArea.height === baselineWorkAreaHeight - 48,
        message: "window sizing is ready before another monitor receives focus",
        action: () => {
          host.expect(deck.testSurfaceWindow.height === reservedBar.screen.height
              && deck.testSurfaceWindow.width === reservedBar.screen.width,
            "the backdrop covers the whole screen even with an exclusive top bar")
          host.expect(deck.testSurfaceWindow.part_palette.y >= 48,
            "the launcher panel leaves room for the top bar inside its full-screen backdrop")
          deck.applyDisplaySizePercent(105)
          host.part_hostEvents.keyClick(Qt.Key_Equal, Qt.ControlModifier)
          host.expect(deck.displaySizePercent === 115
              && /^Display size: 115%(; limited to fit \(max \d+% here\))?(; narrow layout)?(; scroll to view)?$/.test(deck.statusText),
            "real zoom keys keep the concise percentage status in a presenting window")
          host.part_hostEvents.keyClick(Qt.Key_0, Qt.ControlModifier)
          host.expect(deck.displaySizePercent === 100
              && /^Display size: 100% \(Recommended\)(; limited to fit \(max \d+% here\))?(; narrow layout)?(; scroll to view)?$/.test(deck.statusText),
            "real zoom reset keys identify Recommended in a presenting window")
          const store = deck.part_displayScaleStore
          store.legacy = true
          store.legacyPercent = 115
          deck.applyDisplaySizePercent(125)
          host.part_hostEvents.keyClick(Qt.Key_0, Qt.ControlModifier)
          host.expect(deck.displaySizePercent === 100 && store.legacy && store.legacyPercent === 115
              && /^Display size: 100%(; limited to fit \(max \d+% here\))?(; narrow layout)?(; scroll to view)?$/.test(deck.statusText),
            "Ctrl+0 resets only the percentage and preserves the previous sizing profile")
          store.useRecommended()
          originalSizing = deck.testSurfaceWindow.displaySizing
          deck.desktopIntegration.focusedMonitor = { name: "another-monitor", width: 7680, height: 4320 }
        }
      },
      {
        ready: () => deck.testSearchInput.activeFocus,
        message: "another monitor's focus leaves the presenting window stable",
        action: () => {
          host.expect(JSON.stringify(deck.testSurfaceWindow.displaySizing) === JSON.stringify(originalSizing),
            "focusing a different-resolution monitor cannot resize the open launcher")
          deck.desktopIntegration.focusedMonitor = Qt.binding(function() {
            return deck.desktopIntegration.screens.length > 0 ? deck.desktopIntegration.screens[0] : null
          })
          typePrompt("retained draft")
          originalScreens = deck.desktopIntegration.screens.slice()
          deck.desktopIntegration.screens = []
        }
      },
      {
        ready: () => !deck.opened && deck.testSurfaceWindow === null,
        message: "removing the presenting output unmaps its exclusive-input window",
        action: () => {
          host.expect(deck.queryText === "retained draft",
            "removing the output preserves the pending search draft")
          deck.desktopIntegration.screens = originalScreens
          deck.open("{}")
        }
      },
      {
        ready: () => deck.testSurfaceWindow !== null && deck.testSearchInput.activeFocus,
        message: "reopening after output removal restores a usable search",
        action: () => {
          host.expect(deck.testSearchInput.text === "retained draft",
            "reopening on an available output restores the interrupted draft")
          host.part_hostEvents.keyClick(Qt.Key_A, Qt.ControlModifier)
          host.part_hostEvents.keyClick(Qt.Key_Backspace)
        }
      },
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
          host.expect(host.part_hostEvents.waitForFrame(deck.testAgentSurface.part_retryButton),
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
    steps = steps.concat(savedShortcuts.steps()).concat(hoverSelection.steps()).concat(keptSearch.steps())
      .concat(omarchyClose.steps())
    stepIndex = 0
    deadline = Date.now() + 30000
    stepTimer.start()
  }

  function diagnostics(step) {
    const window = deck.testSurfaceWindow
    const input = deck.testSearchInput
    const session = deck.part_searchSession
    const palette = window ? window.part_palette : null
    return "predicate=" + step.ready.toString() + "; " + JSON.stringify({
      step: stepIndex, opened: deck.opened, windowVisible: window ? window.visible : null,
      windowActive: window ? window.active : null, query: deck.queryText,
      inputText: input ? input.text : null, inputFocus: input ? input.activeFocus : null,
      selectedIndex: deck.selectedIndex, result: deck.currentResult,
      requestId: session.activeRequestId, inFlight: session.inFlight,
      debounceRunning: session.debounce.running, lastRequests: deck.sentRequests.slice(-3),
      workAreaHeight: workArea.height, baselineWorkAreaHeight: baselineWorkAreaHeight,
      palette: palette ? [palette.x, palette.y, palette.width, palette.height, palette.opacity] : null,
      undo: deck.lastUndo, helpOpen: deck.helpOpen, actionPanelOpen: deck.actionPanelOpen
    }) + (step.diagnostics ? "; " + step.diagnostics() : "")
  }

  function advance() {
    const step = steps[stepIndex]
    try {
      if (!step.ready()) {
        if (Date.now() < deadline) {
          stepTimer.start()
          return
        }

        host.expect(false, step.message + " (readiness deadline exceeded); " + diagnostics(step))
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

      deadline = Date.now() + 30000
      stepTimer.start()
    } catch (error) {
      host.expect(false, step.message + ": " + error)
      finish()
    }
  }

  function finish() {
    stepTimer.stop()
    deliveryTimer.stop()
    reservedBar.visible = false
    workArea.visible = false
    savedShortcuts.restore()
    keptSearch.restore()
    omarchyClose.restore()
    deck.close()
    deck.part_searchSession.debounce.interval = originalDebounce
    deck.testSurfaceEnabled = false
    completed()
  }
}
