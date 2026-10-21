pragma ComponentBehavior: Bound
import QtQuick
import "./components" as Deck
import "./DemoData.js" as Data

// The real Settings pages with prepared data and no protocol connection.
Deck.SettingsPanel {
  id: settings
  required property var galleryStage
  theme: galleryStage.theme
  visible: galleryStage.deck.settingsOpen
  sourceDescriptors: [
    { id: "applications", label: "Applications", icon: "applications-other" },
    { id: "files", label: "Files", icon: "folder" }
  ]
  snapshot: ({ preferences: {
    sources: [
      { sourceId: "applications", enabledInSearch: true, enabledOnHome: true, position: 1 },
      { sourceId: "files", enabledInSearch: true, enabledOnHome: true, position: 2 }
    ], reduceMotion: true, rememberSearches: true, learnFromUse: true,
    topMatches: true, hideAfterAction: true, omarchyMenuState: true, workspacePreviewMode: "layout"
  }, customizations: [], shortcuts: [] })
  indexRoots: Data.indexRoots()
  quicklinksController: links
  displaySizePercent: galleryStage.deck.displaySizePercent
  maximumFittingPercent: galleryStage.content.host.maximumFittingPercent
  displaySizeDescription: galleryStage.content.host.displayDescription
  launcherWidthPercent: galleryStage.deck.launcherWidthPercent
  launcherHeightPercent: galleryStage.deck.launcherHeightPercent

  Deck.QuicklinksController {
    id: links
    ready: false
    nextRequestId: () => 1
    quicklinks: Data.quicklinks()
  }

  function show(section) {
    galleryStage.deck.settingsOpen = true
    const customization = section === "display" || section === "layout"
    showPage(customization ? "customization" : "search")
    searchControl.text = section === "display" ? "display size"
      : section === "layout" ? "layout"
      : section === "folders" ? "indexed folders"
      : section === "search" ? "ranking" : "web searches"
    Qt.callLater(function() {
      if (section === "quicklink-edit") {
        const edit = settings.namedItem(settings.part_quicklinksCard, "quicklinkEdit-demo-docs")
        edit.clicked()
      } else {
        settings.searchControl.forceActiveFocus()
      }
    })
  }

  function namedItem(parent, name) {
    if (parent.objectName === name) return parent
    for (const child of parent.children) {
      const match = namedItem(child, name)
      if (match) return match
    }
    return null
  }

  // Fail capture if a card's task controls fall outside the rendered viewport.
  function verify(section) {
    let controls = [searchControl, closeControl]
    if (section === "display") controls.push(displaySizeControl)
    if (section === "layout") controls.push(layoutWidthControl, layoutHeightControl)
    if (section === "search") controls.push(namedItem(part_behaviorCard, "topMatchesToggle"))
    if (section === "folders") {
      if (part_indexRootsCard.renderedCount !== indexRoots.length) {
        return reject("The gallery must render every indexed folder")
      }
      let missing = false
      let disabled = false
      for (let index = 0; index < indexRoots.length; index++) {
        const root = indexRoots[index]
        const action = part_indexRootsCard.actionControlAt(index)
        if (!root.enabled && (root.issue || root.status !== "Disabled")) {
          return reject("Disabled gallery folders must have no warning")
        }
        if (root.source === "configuration" && root.issue === "Folder does not exist.") {
          missing = root.enabled && root.status === "Needs attention" && action.text === "Disable"
        }
        if (!root.enabled && action.text === "Enable") disabled = true
        controls.push(action)
      }
      if (!missing || !disabled) {
        return reject("The gallery must show an enabled missing folder with Disable and a disabled folder with Enable")
      }
      controls.push(part_indexRootsCard.pathControl)
    }
    if (section === "quicklinks") controls.push(namedItem(part_quicklinksCard, "quicklinkEdit-demo-docs"))
    if (section === "quicklink-edit") {
      controls.push(namedItem(part_quicklinksCard, "quicklinkTitle"), namedItem(part_quicklinksCard, "quicklinkSave"))
    }

    for (const control of controls) {
      if (!control || !control.visible || control.width <= 0 || control.height <= 0) {
        return reject("Missing gallery Settings control: " + section)
      }
      for (let parent = control.parent; parent; parent = parent.parent) {
        if (parent !== settings && !parent.clip) continue
        const point = control.mapToItem(parent, 0, 0)
        if (point.x < -1 || point.y < -1 || point.x + control.width > parent.width + 1
            || point.y + control.height > parent.height + 1) {
          return reject("Clipped gallery Settings control: " + section)
        }
      }
    }
    return true
  }

  function reject(message) {
    console.error(message)
    return false
  }
}
