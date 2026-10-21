import QtQuick
import "Protocol.js" as Protocol

// Closed launcher surfaces are built on demand. These journeys drive each one
// through the production window with real keys and clicks: first open and its
// focus, dismissal and focus restoration, and a reopen that reuses the surface.
QtObject {
  id: journeys
  required property var host
  // With every closed surface built up front one window held 2,768 items.
  readonly property int closedWindowItemCap: 1000
  property var settings: null
  property var management: null
  property var inspector: null
  property var agent: null
  property var clipboard: null
  readonly property var agentRow: ({
    id: "agent:conversation", kind: Protocol.ResultKinds.Assistant, title: "Assistant conversation",
    actions: [{ id: "open", title: "Open conversation", kind: Protocol.ActionKinds.AgentOpen }]
  })
  readonly property var clipboardRow: ({
    id: "clipboard:history", kind: Protocol.ResultKinds.CustomCommand, title: "Clipboard history",
    actions: [{ id: "open", title: "Open", kind: Protocol.ActionKinds.OpenClipboardHistory }]
  })

  function deck() { return journeys.host.part_integratedDeck }
  function events() { return journeys.host.part_hostEvents }
  function search() { return journeys.deck().testSearchInput }
  function focused() { return journeys.search().Window.activeFocusItem }
  function expect(condition, message) {
    journeys.events().waitForCondition(condition, 30000, message)
    journeys.host.expect(condition(), message)
  }

  function countItems(item) {
    let total = 1
    const children = item.children || []
    for (let index = 0; index < children.length; index++) total += journeys.countItems(children[index])
    return total
  }

  function within(container, item) {
    for (let current = item; current; current = current.parent) {
      if (current === container) return true
    }
    return false
  }

  function searchReady(message) {
    journeys.expect(() => journeys.search().activeFocus && journeys.deck().opened, message)
  }

  function press(key, modifiers) { journeys.events().keyClick(key, modifiers || Qt.NoModifier) }

  function tabUntil(control, limit) {
    for (let step = 0; step < limit && !control.activeFocus; step++) journeys.press(Qt.Key_Tab)
    return control.activeFocus
  }

  function deliverRows() {
    const deck = journeys.deck()
    const requestId = deck.part_searchSession.activeRequestId
    for (const result of [journeys.agentRow, journeys.clipboardRow]) {
      deck.handleMessage({ type: Protocol.Messages.Result, requestId: requestId, result: result })
    }
    deck.handleMessage({ type: Protocol.Messages.Done, requestId: requestId })
  }

  function rowAt(index) { return journeys.deck().testSurfaceWindow.part_resultList.itemAtIndex(index) }

  function steps() {
    return [
      () => {
        const deck = journeys.deck()
        deck.coachSeen = true
        deck.daemonAvailable = true
        deck.reconciliationPending = false
        deck.aiFeaturesEnabled = true
        deck.runtimeStatus = { catalog: { itemCount: 1 }, providers: [{ id: "clipboard-copy",
          label: "Clipboard copy", state: "degraded", detail: "wl-copy is not on PATH.",
          remediation: "Install with: sudo pacman -S --needed wl-clipboard" }] }
        deck.testSurfaceEnabled = true
        const items = journeys.countItems(deck.testSurfaceWindow.contentItem)
        console.log("Launcher window items while its surfaces are closed: " + items)
        journeys.expect(() => items < journeys.closedWindowItemCap,
          "a launcher window stays under " + journeys.closedWindowItemCap + " items while its surfaces are closed")
        journeys.expect(() => deck.testSettingsPanel === null && deck.testExtensionManagementView === null
            && deck.testIndexInspector === null && deck.testAgentSurface === null && deck.testClipboardSurface === null,
          "Settings, extension management, the inspector, the assistant and clipboard history are not built until opened")
        deck.open("{}")
        // As in production, only the window on the focused monitor presents.
        deck.openedScreenName = deck.testSurfaceWindow.screen.name
      },
      () => {
        journeys.searchReady("the launcher opens with search focused")
        journeys.press(Qt.Key_Comma, Qt.ControlModifier)
      },
      () => {
        journeys.settings = journeys.deck().testSettingsPanel
        journeys.expect(() => journeys.settings !== null && journeys.settings.visible
            && journeys.settings.searchControl.activeFocus,
          "Ctrl+, builds Settings on first open and focuses its search")
        journeys.press(Qt.Key_Escape)
      },
      () => {
        journeys.expect(() => !journeys.deck().settingsOpen && !journeys.settings.visible,
          "Escape closes the first-built Settings")
        journeys.searchReady("closing Settings returns focus to launcher search")
        journeys.press(Qt.Key_X)
        journeys.expect(() => journeys.search().text === "x"
            && journeys.settings.currentExpression === journeys.settings.closedExpression,
          "typing in search leaves the closed Settings panel's query parse alone")
        journeys.press(Qt.Key_Backspace)
        journeys.press(Qt.Key_Comma, Qt.ControlModifier)
      },
      () => {
        journeys.expect(() => journeys.deck().testSettingsPanel === journeys.settings && journeys.settings.visible
            && journeys.settings.searchControl.activeFocus,
          "reopening Settings reuses the built panel and focuses its search again")
        journeys.tabUntil(journeys.settings.extensionsControl, 120)
        journeys.expect(() => journeys.settings.extensionsControl.activeFocus,
          "Tab reaches the Extensions button inside Settings")
        journeys.press(Qt.Key_Space)
      },
      () => {
        journeys.management = journeys.deck().testExtensionManagementView
        journeys.expect(() => journeys.management !== null && journeys.management.opened
            && !journeys.settings.visible && journeys.management.searchControl.activeFocus,
          "Space on Extensions builds extension management and focuses its search")
        journeys.press(Qt.Key_Escape)
      },
      () => {
        journeys.expect(() => !journeys.deck().extensionManagementOpen && journeys.settings.visible
            && journeys.settings.extensionsControl.activeFocus,
          "Escape closes extension management and returns focus to the Extensions button")
        journeys.press(Qt.Key_Space)
      },
      () => {
        journeys.expect(() => journeys.deck().testExtensionManagementView === journeys.management
            && journeys.management.opened && journeys.management.searchControl.activeFocus,
          "reopening extension management reuses it and focuses its search again")
        journeys.events().mouseClick(journeys.management.backControl)
      },
      () => {
        journeys.expect(() => !journeys.management.opened && journeys.settings.visible
            && journeys.settings.extensionsControl.activeFocus,
          "clicking Close in extension management returns focus to the Extensions button")
        journeys.events().mouseClick(journeys.settings.extensionsControl)
      },
      () => {
        journeys.expect(() => journeys.management.opened && journeys.management.searchControl.activeFocus,
          "clicking Extensions opens management with its search focused")
        journeys.press(Qt.Key_Escape)
      },
      () => {
        journeys.events().mouseClick(journeys.settings.closeControl)
      },
      () => {
        journeys.expect(() => !journeys.deck().settingsOpen, "clicking Close dismisses Settings")
        journeys.searchReady("closing Settings by mouse returns focus to launcher search")
        journeys.events().mouseClick(journeys.deck().testSettingsButton)
      },
      () => {
        journeys.expect(() => journeys.settings.visible && journeys.settings.searchControl.activeFocus,
          "clicking the settings button opens Settings with its search focused")
        journeys.press(Qt.Key_Escape)
      },
      () => {
        journeys.searchReady("Escape after a mouse open still restores launcher search")
        const status = journeys.deck().testRuntimeStatusSurface
        journeys.tabUntil(status, 3)
        journeys.expect(() => status.activeFocus, "Tab reaches the runtime health banner")
        journeys.press(Qt.Key_Space)
      },
      () => {
        journeys.inspector = journeys.deck().testIndexInspector
        const focus = journeys.focused()
        journeys.expect(() => journeys.inspector !== null && journeys.inspector.opened
            && journeys.within(journeys.inspector, focus) && focus.Accessible.name === "Close index inspector",
          "Space on the health banner builds the inspector and focuses Close")
        journeys.press(Qt.Key_Escape)
      },
      () => {
        journeys.expect(() => !journeys.deck().indexInspectorOpen && !journeys.inspector.opened,
          "Escape closes the index inspector")
        journeys.searchReady("closing the inspector returns focus to launcher search")
        journeys.tabUntil(journeys.deck().testRuntimeStatusSurface, 3)
        journeys.press(Qt.Key_Return)
      },
      () => {
        const focus = journeys.focused()
        journeys.expect(() => journeys.deck().testIndexInspector === journeys.inspector && journeys.inspector.opened
            && focus.Accessible.name === "Close index inspector",
          "reopening the inspector reuses it and focuses Close again")
        journeys.events().mouseClick(focus)
      },
      () => {
        journeys.expect(() => !journeys.inspector.opened, "clicking Close dismisses the inspector")
        journeys.searchReady("closing the inspector by mouse returns focus to launcher search")
        journeys.events().mouseClick(journeys.deck().testRuntimeStatusSurface)
      },
      () => {
        journeys.expect(() => journeys.inspector.opened && journeys.focused().Accessible.name === "Close index inspector",
          "clicking the health banner opens the inspector with Close focused")
        journeys.press(Qt.Key_Escape)
        journeys.deliverRows()
      },
      () => {
        journeys.searchReady("search stays focused while results arrive")
        journeys.expect(() => journeys.deck().currentResult && journeys.deck().currentResult.id === journeys.agentRow.id,
          "the assistant row is selected")
        journeys.press(Qt.Key_Return)
      },
      () => {
        journeys.agent = journeys.deck().testAgentSurface
        journeys.expect(() => journeys.agent !== null && journeys.agent.opened
            && journeys.focused().objectName === "agentComposer",
          "Enter on the assistant row builds the conversation and focuses its composer")
        journeys.press(Qt.Key_Escape)
      },
      () => {
        journeys.expect(() => !journeys.deck().agentOpen && !journeys.agent.opened, "Escape closes the conversation")
        journeys.searchReady("closing the conversation returns focus to launcher search")
        journeys.press(Qt.Key_Return)
      },
      () => {
        journeys.expect(() => journeys.deck().testAgentSurface === journeys.agent && journeys.agent.opened
            && journeys.focused().objectName === "agentComposer",
          "reopening the conversation reuses it and focuses the composer again")
        journeys.events().mouseClick(journeys.agent.part_backButton)
      },
      () => {
        journeys.expect(() => !journeys.agent.opened, "clicking Back to Search closes the conversation")
        journeys.searchReady("closing the conversation by mouse returns focus to launcher search")
        const row = journeys.rowAt(journeys.deck().selectedIndex)
        journeys.events().mouseClick(row, 20, row.height / 2)
      },
      () => {
        journeys.expect(() => journeys.agent.opened && journeys.focused().objectName === "agentComposer",
          "clicking the assistant row opens the conversation with the composer focused")
        journeys.press(Qt.Key_Escape)
      },
      () => {
        journeys.searchReady("Escape after a mouse open restores launcher search")
        journeys.press(Qt.Key_Down)
        journeys.expect(() => journeys.deck().currentResult.id === journeys.clipboardRow.id,
          "Down selects the clipboard history row")
        journeys.press(Qt.Key_Return)
      },
      () => {
        journeys.clipboard = journeys.deck().testClipboardSurface
        journeys.expect(() => journeys.clipboard !== null && journeys.clipboard.visible
            && journeys.clipboard.searchControl.activeFocus,
          "Enter on clipboard history builds the browser and focuses its search")
        journeys.press(Qt.Key_Escape)
      },
      () => {
        journeys.expect(() => !journeys.deck().part_clipboardController.opened && !journeys.clipboard.visible,
          "Escape closes clipboard history")
        journeys.searchReady("closing clipboard history returns focus to launcher search")
        journeys.press(Qt.Key_Return)
      },
      () => {
        journeys.expect(() => journeys.deck().testClipboardSurface === journeys.clipboard && journeys.clipboard.visible
            && journeys.clipboard.searchControl.activeFocus,
          "reopening clipboard history reuses the browser and focuses its search again")
        journeys.events().mouseClick(journeys.clipboard.backControl)
      },
      () => {
        journeys.expect(() => !journeys.clipboard.visible, "clicking Back closes clipboard history")
        journeys.searchReady("closing clipboard history by mouse returns focus to launcher search")
        const row = journeys.rowAt(journeys.deck().selectedIndex)
        journeys.events().mouseClick(row, 20, row.height / 2)
      },
      () => {
        journeys.expect(() => journeys.clipboard.visible && journeys.clipboard.searchControl.activeFocus,
          "clicking the clipboard row opens the browser with its search focused")
        journeys.press(Qt.Key_Escape)
      },
      () => {
        journeys.searchReady("the launcher keeps search ready after every surface journey")
        journeys.deck().close()
        journeys.deck().testSurfaceEnabled = false
      }
    ]
  }
}
