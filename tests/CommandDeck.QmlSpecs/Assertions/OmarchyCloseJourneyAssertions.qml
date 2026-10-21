import QtQuick
import QtTest
import "common" as Common
import "omarchy" as Omarchy
import "Protocol.js" as Protocol

Item {
  id: journey
  required property var host
  property int hideCount: 0

  Common.DesktopIntegration {
    id: injectedDesktop
    screens: journey.host.part_integratedDeck.desktopIntegration.screens
    focusedMonitor: screens.length > 0 ? screens[0] : null
  }

  Omarchy.CommandDeck {
    id: deck
    visible: false
    testMode: true
    desktopIntegration: injectedDesktop
    themeTokenSource: journey.host.part_integratedDeck.themeTokenSource
    property var sentRequests: []
    onProtocolRequestSent: request => sentRequests = sentRequests.concat([JSON.parse(JSON.stringify(request))])
    shell: QtObject {
      function hide(id) {
        journey.host.expect(id === deck.integrationIds.Plugin, "deferred hide targets the Omarchy plugin")
        journey.host.expect(!deck.opened, "the Omarchy shell calls close after Escape already closed the plugin")
        deck.close()
        journey.hideCount++
      }
    }
  }

  TestCase { id: events; parent: deck.testSearchInput; when: false }

  function focused() {
    const input = deck.testSearchInput
    const window = input ? input.Window.window : null
    return window !== null && window.active && input.activeFocus
  }

  function latestSearch(query) {
    const requests = deck.sentRequests.filter(request => request.operation === Protocol.Operations.Search)
    const request = requests[requests.length - 1]
    return request && request.query === query ? request : null
  }

  function deliver(query) {
    const request = latestSearch(query)
    deck.handleMessage({ type: Protocol.Messages.Result, requestId: request.requestId, result: {
      id: "file:kept", kind: Protocol.ResultKinds.File, sourceId: Protocol.SourceIds.Files,
      title: "kept.txt", invocationReference: "kept",
      actions: [{ id: "open", title: "Open", kind: Protocol.ActionKinds.OpenPath }] } })
    deck.handleMessage({ type: Protocol.Messages.Done, requestId: request.requestId,
      searchCompletion: { containsPrivateQuery: false } })
  }

  function type(query) {
    events.keyClick(Qt.Key_A, Qt.ControlModifier)
    for (const character of query) events.keyClick(character)
  }

  function restore() {
    deck.close()
    deck.testSurfaceEnabled = false
  }

  function steps() {
    const steps = [{
      ready: () => true,
      message: "open the production Omarchy plugin for deferred-close retention",
      action: () => {
        host.expect(deck.desktopIntegration === injectedDesktop,
          "the Omarchy plugin uses its injected desktop fixture")
        host.part_integratedDeck.close()
        deck.testSurfaceEnabled = true
        host.expect(!focused(), "the unopened Omarchy input waits safely for its window")
        deck.coachSeen = true
        deck.daemonAvailable = true
        deck.reconciliationPending = false
        deck.applyLauncherBehavior(false, 30)
        deck.part_personalization.resetPreferences({ hideAfterAction: false })
        deck.open("{}")
      }
    }, {
      ready: () => focused() && latestSearch("") !== null,
      message: "type alpha in the Omarchy plugin with real keys",
      action: () => type("alpha")
    }, {
      ready: () => latestSearch("alpha") !== null,
      message: "deliver alpha in the Omarchy plugin",
      action: () => deliver("alpha")
    }, {
      ready: () => focused() && deck.currentResult !== null,
      message: "accept alpha and edit to beta while the Omarchy plugin stays open",
      action: () => {
        const start = deck.sentRequests.length
        events.keyClick(Qt.Key_Return)
        const invoke = deck.sentRequests.slice(start).find(request => request.operation === Protocol.Operations.Invoke)
        host.expect(invoke !== undefined, "Enter invokes alpha through the Omarchy plugin")
        if (invoke === undefined) return

        deck.handleMessage({ type: Protocol.Messages.ActionResult, requestId: invoke.requestId,
          actionResult: { succeeded: true } })
        host.expect(deck.opened && focused(), "accepted action keeps the Omarchy search ready")
        type("beta")
      }
    }]
    for (const query of ["beta", "gamma"]) {
      const callbackCount = query === "beta" ? 1 : 2
      steps.push({
        ready: () => latestSearch(query) !== null,
        message: "complete edited Omarchy search " + query,
        action: () => deliver(query)
      }, {
        ready: () => focused() && deck.currentResult !== null,
        message: "Escape closes the Omarchy session for " + query,
        action: () => {
          events.keyClick(Qt.Key_Escape)
          host.expect(!deck.opened, "Escape closes the plugin before deferred shell.hide")
        }
      }, {
        ready: () => hideCount === callbackCount && !deck.testSurfaceWindow.visible,
        message: "reopen after deferred shell.hide calls close again for " + query,
        action: () => {
          deck.open("{}")
          host.expect(deck.queryText === query,
            "the Omarchy callback preserves " + query + "; got " + JSON.stringify(deck.queryText))
        }
      }, {
        ready: () => focused() && latestSearch(query) !== null,
        message: "the reopened Omarchy search is keyboard ready for " + query,
        action: () => {
          host.expect(deck.testSearchInput.activeFocus, "reopening restores focus without a preparatory click")
          if (query === "beta") type("gamma")
        }
      })
    }
    return steps
  }
}
