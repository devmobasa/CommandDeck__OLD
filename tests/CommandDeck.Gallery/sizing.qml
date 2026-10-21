import QtQuick
import Quickshell
import "./components" as Deck
import "./DemoData.js" as Data

ShellRoot {
  id: root
  property string surface: "launcher"

  function show(name) {
    root.surface = name
    stage.deck.settingsOpen = name === "settings" || name === "layout"
    settings.searchControl.text = name === "layout" ? "panel percent" : ""
    stage.setResults("files", Data.corpus(stage.icon), "Sizing acceptance · long labels and preview content")
    if (name === "actions") actions.openFor(Data.gaps(stage.icon)[0])
  }

  function namedItems(parent, name) {
    let found = parent.objectName === name ? [parent] : []
    for (const child of parent.children) found = found.concat(namedItems(child, name))
    return found
  }

  function inside(control, bounds) {
    const point = control.mapToItem(bounds, 0, 0)
    return point.x >= -1 && point.y >= -1
      && point.x + control.width <= bounds.width + 1
      && point.y + control.height <= bounds.height + 1
  }

  function contained(control) {
    if (!control || !control.visible || control.width <= 0 || control.height <= 0) return false
    if (!inside(control, stage.panel)) return false
    for (let parent = control.parent; parent; parent = parent.parent) {
      if (parent.clip && !inside(control, parent)) return false
    }
    return true
  }

  function surfaceControls() {
    switch (root.surface) {
    case "layout": return [settings.layoutWidthControl, settings.layoutWidthResetControl]
    case "settings": return [settings.searchControl, settings.closeControl]
    case "help": return [namedItems(help, "helpHeading")[0], namedItems(help, "helpDismissHint")[0],
      namedItems(help, "helpViewport")[0], namedItems(help, "helpShortcutRow")[0]]
    case "actions": {
      const viewport = namedItems(actions, "actionsViewport")[0]
      const controls = [actions.filterControl, viewport, actions.actionItemAt(0)]
      for (let index = 1; index < actions.visibleActionCount; index++) {
        const row = actions.actionItemAt(index)
        if ((index + 1) * (row.height + 4) <= viewport.height + 4) controls.push(row)
      }
      return controls
    }
    case "confirmation": return [confirmation.cancelControl, confirmation.confirmControl]
    case "extension": return [extension.inputControl, extension.backControl]
    default: return [stage.content.part_searchBar, stage.content.part_footerActions]
    }
  }

  DemoStage {
    id: stage
    defaultCaption: ""
    filters: Data.filters
    shots: [
      { name: "launcher.png", settle: 250, show: function() { root.show("launcher") } },
      { name: "settings.png", settle: 250, show: function() { root.show("settings") } },
      { name: "layout.png", settle: 250, show: function() { root.show("layout") } },
      { name: "help.png", settle: 250, show: function() { root.show("help") } },
      { name: "actions.png", settle: 250, show: function() { root.show("actions") } },
      { name: "confirmation.png", settle: 250, show: function() { root.show("confirmation") } },
      { name: "extension.png", settle: 250, show: function() { root.show("extension") } }
    ]
    onShotSaved: name => {
      console.log("Saved sizing proof", name, stage.view.width, stage.view.height,
        stage.panel.width, stage.panel.height, stage.deck.displaySizePercent, stage.theme.fontBody)
      if (name === "launcher.png") {
        const list = stage.content.part_resultList
        console.log("Saved launcher layout", JSON.stringify({
          text: stage.theme.fontBody, row: stage.theme.rowHeight,
          rows: Math.floor(list.height / (stage.theme.rowHeight + stage.theme.rowGap)),
          preview: stage.content.part_detailPane.width,
          panelWidth: stage.panel.width, panelHeight: stage.panel.height
        }))
      }
      if (name === "settings.png") {
        const maximum = settings.maximumFittingPercent
        const slider = settings.displaySizeControl.sliderControl
        const marker = root.namedItems(slider.background, "displaySizeLimitMarker")[0]
        const expected = maximum === 0 ? "no percentage fits this screen"
          : "maximum that fits this screen: " + maximum + "%"
        const defaultLaptop = stage.view.width === 1280 && stage.view.height === 720
          && (stage.sizingOptions["font-scale"] || 1) === 1
          && (stage.sizingOptions["panel-width"] || 100) === 100
          && (stage.sizingOptions["panel-height"] || 100) === 100
        const fraction = 1 - Math.max(0, (maximum - 75) / 75)
        if ((defaultLaptop && maximum !== 105) || !marker
            || marker.visible !== (maximum < 150)
            || Math.abs(marker.width - marker.parent.width * fraction) > 1
            || slider.Accessible.description.indexOf(expected) < 0) {
          console.error("Gallery fitting maximum, rendered marker or accessible description failed: " + maximum)
          Qt.exit(1)
        }
        console.log("Saved fitting feedback", maximum, marker.width, slider.Accessible.description)
      }
      const controls = root.surfaceControls()
      if (controls.some(control => !root.contained(control))) {
        console.error("Sizing containment failed: " + name)
        Qt.exit(1)
      }
    }
    panelOverlays: [
      Deck.CommandDeckWindowSettings {
        id: settings
        host: stage.content.host
        presented: root.surface === "settings" || root.surface === "layout"
        anchors.fill: parent
        Component.onCompleted: showPage("customization")
      },
      Deck.HelpSheet {
        id: help
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
