import QtQuick
import Quickshell
import "./components" as Deck
import "./DemoData.js" as Data

// Home, Omarchy and Hyprland search, Herdr, the action menu, the help sheet,
// and two of the small built-in tools.
ShellRoot {
  id: root
  property string overlay: ""
  property var actionResult: null

  DemoStage {
    id: stage
    filters: Data.filters
    shots: [
      { name: "overview.png", show: root.showHome },
      { name: "config.png", show: root.showGaps },
      { name: "windows.png", show: root.showWindows },
      { name: "omarchy.png", show: root.showOmarchy },
      { name: "herdr.png", show: root.showHerdr },
      { name: "actions.png", show: root.showActions },
      { name: "help.png", show: root.showHelp },
      { name: "time.png", show: root.showTime },
      { name: "emoji.png", show: root.showEmoji }
    ]
    panelOverlays: [
      Deck.ActionPanel {
        id: actions
        theme: stage.theme
        width: Math.round(380 * stage.theme.scale)
        height: Math.min(parent.height - 28, implicitHeight)
        anchors.right: parent.right
        anchors.top: parent.top
        anchors.margins: 14
        visible: root.overlay === "actions"
      },
      Deck.HelpSheet {
        anchors.fill: parent
        theme: stage.theme
        opened: root.overlay === "help"
        filterDescriptors: Data.filters
      }
    ]
  }

  function reset() {
    root.overlay = ""
  }

  // Result replacement settles its own selection first.
  function select(index) {
    Qt.callLater(function() { stage.move(index) })
  }

  function showHome() {
    root.reset()
    stage.setResults("", Data.home(stage.icon), "Home")
  }

  function showGaps() {
    root.reset()
    stage.setResults("gaps", Data.gaps(stage.icon), "4 results")
    root.select(2)
  }

  function showWindows() {
    root.reset()
    stage.setResults("win:", Data.windows(stage.icon), "5 windows · 1 workspace")
    stage.content.part_searchBar.inputControl.cursorPosition = 0
  }

  function showOmarchy() {
    root.reset()
    stage.setResults("oma:", Data.omarchyControls(stage.icon), "6 Omarchy controls")
    stage.content.part_searchBar.inputControl.cursorPosition = 0
  }

  function showHerdr() {
    root.reset()
    stage.setResults("htab:", Data.herdrTabs(stage.icon), "4 Herdr tabs · 1 needs attention")
    stage.content.part_searchBar.inputControl.cursorPosition = 0
  }

  function showActions() {
    root.showGaps()
    root.actionResult = Data.gaps(stage.icon)[2]
    actions.openFor(root.actionResult)
    root.overlay = "actions"
  }

  function showHelp() {
    root.showHome()
    root.overlay = "help"
  }

  function showTime() {
    root.reset()
    stage.setResults("time: 3pm london to tokyo", Data.timezone(stage.icon), "1 result")
  }

  function showEmoji() {
    root.reset()
    stage.setResults("emoji: rocket", Data.emoji(stage.icon), "1 result")
  }
}
