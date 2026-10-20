import QtQuick
import Quickshell
import "./components" as Deck
import "./DemoData.js" as Data

ShellRoot {
  id: root
  property string surface: "launcher"

  function show(name) {
    root.surface = name
    stage.deck.settingsOpen = name === "settings"
    stage.setResults("files", Data.corpus(stage.icon), "Sizing acceptance · long labels and preview content")
    if (name === "actions") actions.openFor(Data.gaps(stage.icon)[0])
  }

  function contained(control) {
    const point = control.mapToItem(stage.panel, 0, 0)
    return point.x >= -1 && point.y >= -1
      && point.x + control.width <= stage.panel.width + 1
      && point.y + control.height <= stage.panel.height + 1
  }

  DemoStage {
    id: stage
    defaultCaption: ""
    filters: Data.filters
    shots: [
      { name: "launcher.png", settle: 250, show: function() { root.show("launcher") } },
      { name: "settings.png", settle: 250, show: function() { root.show("settings") } },
      { name: "help.png", settle: 250, show: function() { root.show("help") } },
      { name: "actions.png", settle: 250, show: function() { root.show("actions") } },
      { name: "confirmation.png", settle: 250, show: function() { root.show("confirmation") } },
      { name: "extension.png", settle: 250, show: function() { root.show("extension") } }
    ]
    onShotSaved: name => {
      console.log("Saved sizing proof", name, stage.view.width, stage.view.height,
        stage.panel.width, stage.panel.height, stage.deck.displaySizePercent, stage.theme.fontBody)
      const controls = root.surface === "settings" ? [settings.searchControl, settings.closeControl]
        : root.surface === "confirmation" ? [confirmation.cancelControl, confirmation.confirmControl]
        : root.surface === "extension" ? [extension.inputControl, extension.backControl]
        : [content.part_searchBar, content.part_footerActions]
      if (controls.some(control => !root.contained(control))) {
        console.error("Sizing containment failed: " + name)
        Qt.exit(1)
      }
    }
    panelOverlays: [
      Deck.CommandDeckWindowSettings {
        id: settings
        host: stage.content.host
        presented: root.surface === "settings"
        anchors.fill: parent
        Component.onCompleted: showPage("customization")
      },
      Deck.HelpSheet {
        anchors.fill: parent
        theme: stage.theme
        opened: root.surface === "help"
        filterDescriptors: Data.filters
      },
      Deck.ActionPanel {
        id: actions
        anchors.right: parent.right
        anchors.top: parent.top
        width: Math.min(380 * stage.theme.scale, parent.width)
        height: Math.min(implicitHeight, parent.height)
        theme: stage.theme
        visible: root.surface === "actions"
      },
      Deck.ConfirmationSurface {
        id: confirmation
        anchors.fill: parent
        theme: stage.theme
        opened: root.surface === "confirmation"
        prompt: "Remove this item? This long confirmation still needs readable, reachable controls."
        confirmLabel: "Remove"
        destructive: true
      },
      Deck.ExtensionViewHost {
        id: extension
        anchors.fill: parent
        theme: stage.theme
        opened: root.surface === "extension"
        snapshot: ({ revision: 1, content: { id: "sizing", kind: "list", title: "Extension sizing",
          searchText: "", items: [
            { id: "first", title: "A long extension result title that must remain readable", subtitle: "A description with more detail", actions: [] },
            { id: "second", title: "Another result", subtitle: "Extension content uses the same fit factor", actions: [] }
          ] } })
      }
    ]
  }
}
