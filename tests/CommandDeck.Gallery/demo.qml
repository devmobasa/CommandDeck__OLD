import QtQuick
import Quickshell
import "./components" as Deck
import "./DemoData.js" as Data

// Frames for the animated demo: Home, typing `gaps`, moving to a Hyprland key,
// its action menu, browsing the Omarchy DNS submenu, filling a snippet's inputs
// as pills, and the window list. Each frame is named frame-<index>-<hold ms>.png;
// scripts/capture-gallery.cs encodes them.
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
    type("dns", 160, root.showSearch)
    add(1300, root.openDnsMenu)
    add(1700, function() { stage.move(1) })
    add(600, root.showHome)
    type("follow", 110, root.showSnippetSearch)
    add(600, function() { root.fillPill("") })
    type("Sam", 160, root.fillPill)
    add(1200, function() { })
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
    stage.deck.part_navigation.clearOmarchyBrowse()
    stage.setResults("", Data.home(stage.icon), "Home")
    stage.content.part_searchBar.inputControl.forceActiveFocus()
  }

  function showSearch(text) {
    const rows = Data.search(stage.icon, text)
    stage.setResults(text, rows, rows.length + " result" + (rows.length === 1 ? "" : "s"))
  }

  function openActions() {
    actions.openFor(stage.deck.part_navigation.selectedResult())
    root.actionsOpen = true
  }

  // Enter on the DNS row: the path bar opens and lists the submenu's entries.
  function openDnsMenu() {
    const row = stage.deck.part_navigation.selectedResult()
    stage.deck.part_navigation.browseOmarchyMenu(row, row.actions[0])
    stage.setResults("", Data.dnsMenu(stage.icon), "4 results")
  }

  // A snippet is one more search source; its inputs show as pills.
  function showSnippetSearch(text) {
    const rows = Data.snippets(stage.icon, text).concat(Data.search(stage.icon, text))
    stage.setResults(text, rows, rows.length + " result" + (rows.length === 1 ? "" : "s"))
  }

  // Tab from the query reaches the first pill; typing fills it.
  function fillPill(text) {
    const field = stage.content.part_searchBar.pillsControl.fieldAt(0)
    field.forceActiveFocus()
    field.text = text
  }

  function showWindowSearch(text) {
    if (text !== "win:") {
      root.showSearch(text)
      return
    }

    stage.setResults(text, Data.windows(stage.icon), "5 windows · 1 workspace")
  }
}
