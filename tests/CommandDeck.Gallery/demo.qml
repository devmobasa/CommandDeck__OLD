import QtQuick
import Quickshell
import "./components" as Deck
import "./components/Protocol.js" as Protocol
import "./DemoData.js" as Data

// Frames for the animated demo: Home, typing `gaps`, moving to a Hyprland key,
// its action menu, a unit conversion, and the window list. Each frame is named
// frame-<index>-<hold ms>.png; scripts/capture-gallery.cs encodes them.
ShellRoot {
  id: root
  property bool actionsOpen: false

  DemoStage {
    id: stage
    filters: Data.filters
    shots: root.storyboard()
    panelOverlays: [
      Deck.ActionPanel {
        id: actions
        theme: stage.theme
        width: Math.round(380 * stage.theme.scale)
        height: Math.min(parent.height - 28, implicitHeight)
        anchors.right: parent.right
        anchors.top: parent.top
        anchors.margins: 14
        visible: root.actionsOpen
      }
    ]
  }

  function pad(value, width) {
    return String(value).padStart(width, "0")
  }

  function storyboard() {
    const steps = []
    const add = function(hold, show) { steps.push({ hold: hold, show: show }) }
    const type = function(text, hold, show) {
      for (let length = 1; length <= text.length; length++) {
        const typed = text.slice(0, length)
        add(length === text.length ? 900 : hold, function() { show(typed) })
      }
    }

    add(1600, root.showHome)
    type("gaps", 140, root.showSearch)
    add(450, function() { stage.move(1) })
    add(1400, function() { stage.move(1) })
    add(2000, root.openActions)
    add(600, root.showHome)
    type("= 100 km to miles", 70, root.showCalculator)
    add(1400, function() { })
    add(600, root.showHome)
    type("win:", 140, root.showWindowSearch)
    add(450, function() { stage.move(1) })
    add(450, function() { stage.move(1) })
    add(2200, function() { stage.move(1) })

    return steps.map(function(step, index) {
      return {
        name: "frame-" + root.pad(index + 1, 4) + "-" + root.pad(step.hold, 5) + ".png",
        settle: 300,
        show: step.show
      }
    })
  }

  function showHome() {
    root.actionsOpen = false
    stage.setResults("", Data.home(stage.icon), "Home")
  }

  function showSearch(text) {
    const rows = Data.search(stage.icon, text)
    stage.setResults(text, rows, rows.length + " results")
  }

  function openActions() {
    actions.openFor(stage.deck.part_navigation.selectedResult())
    root.actionsOpen = true
  }

  function showCalculator(text) {
    const expression = text.replace(/^=\s*/, "")
    if (expression === "100 km to miles") {
      stage.setResults(text, Data.conversion(stage.icon), "1 result")
      return
    }

    const partial = expression.match(/^(\d+)(\s+km)?/)
    const rows = partial ? [{
      id: "calculator:" + expression, kind: Protocol.ResultKinds.Calculator, sourceId: "calculator",
      icon: stage.icon("accessories-calculator"), title: partial[2] ? partial[1] + " km" : partial[1],
      subtitle: expression,
      actions: [{ id: "copy", title: "Copy Result", kind: Protocol.ActionKinds.CopyText, arguments: {} }]
    }] : []
    stage.setResults(text, rows, rows.length + " result" + (rows.length === 1 ? "" : "s"))
  }

  function showWindowSearch(text) {
    if (text !== "win:") {
      root.showSearch(text)
      return
    }

    stage.setResults(text, Data.windows(stage.icon), "5 windows · 1 workspace")
  }
}
