pragma ComponentBehavior: Bound
import QtQuick
import Quickshell
import "./DemoData.js" as Data

// A captioned tour of prepared UI states, not a recording of a live session.
ShellRoot {
  id: root

  DemoStage {
    id: stage
    filters: Data.filters
    captionSize: 18
    shots: root.storyboard()
    onShotSaved: {
      if (stage.shotIndex >= 11 && !gallerySettings.verify(["folders", "quicklinks", "quicklink-edit"][stage.shotIndex - 11])) Qt.exit(1)
      if (stage.shotIndex === 0) {
        const selected = stage.deck.part_navigation.selectedResult()
        if (selected.title !== "Cloudflare" || selected.metadata.omarchyChecked !== "true") {
          root.fail("The DNS beat must select the active provider")
        }
      }
      if (stage.shotIndex === 4 && stage.content.part_searchBar.pillsControl.fieldAt(0).text !== "Sam") {
        root.fail("The snippet beat must show the completed name pill")
      }
      if (stage.shotIndex === 6 && stage.deck.part_navigation.selectedResult().section.id !== "top-match") {
        root.fail("The learned abbreviation must appear in Top Matches")
      }
      if (stage.shotIndex === 9 && stage.deck.previewPaneVisible) root.fail("The Ctrl+I beat must hide the preview")
      if (stage.shotIndex === 10 && !stage.deck.compactLayout) root.fail("The compact beat must use compact rows")
    }
    panelOverlays: [
      DemoSettings {
        id: gallerySettings
        galleryStage: stage
        anchors.fill: parent
      }
    ]

    Text {
      anchors.horizontalCenter: parent.horizontalCenter
      anchors.top: parent.top
      anchors.topMargin: 24
      text: "CommandDeck · What's new"
      font.family: stage.theme.fontFamily
      font.pixelSize: 20
      color: stage.theme.text
    }
  }

  function fail(message) {
    console.error(message)
    Qt.exit(1)
  }

  function storyboard() {
    const steps = []
    const add = function(hold, caption, show) {
      steps.push({ hold: hold, caption: caption, show: show })
    }
    add(3200, "Browse Omarchy menus · ✓ marks the active DNS provider", root.showDns)
    const pills = "Fill snippet inputs right in the search bar"
    add(1000, pills, root.showSnippet)
    for (const text of ["S", "Sa", "Sam"]) {
      add(text === "Sam" ? 2000 : 150, pills, function() { root.fillPill(text) })
    }
    add(1300, "Type f to find apps", function() { root.showFirefox("f") })
    add(2800, "Type ff · Firefox becomes a Top Match, learned from use", function() { root.showFirefox("ff") })
    add(600, "Search history · recall a previous search with ↑", root.showHome)
    add(3000, "↑ recalls your previous search · gaps", root.recallSearch)
    add(2000, "Ctrl+I hides the preview pane", function() { stage.deck.togglePreviewPane() })
    add(2400, "Compact layout · one line per result", function() { stage.deck.applyCompactLayout(true) })
    add(3000, "Settings · disable missing folders; disabled folders have no warning", function() { gallerySettings.show("folders") })
    add(2200, "Settings · keep web searches and links in Quicklinks", function() { gallerySettings.show("quicklinks") })
    add(3000, "Edit a quicklink · title, URL template, alias and preview", function() { gallerySettings.show("quicklink-edit") })

    return steps.map(function(step, index) {
      return {
        name: "frame-" + String(index + 1).padStart(4, "0") + "-" + String(step.hold).padStart(5, "0") + ".png",
        frame: { width: 1280, height: 900 },
        caption: step.caption,
        settle: 400,
        show: step.show
      }
    })
  }

  function clearBrowse() {
    stage.deck.part_navigation.clearOmarchyBrowse()
  }

  function showDns() {
    const row = Data.search(stage.icon, "dns")[0]
    stage.deck.part_navigation.browseOmarchyMenu(row, row.actions[0])
    stage.setResults("", Data.dnsMenu(stage.icon), "4 results")
    Qt.callLater(function() { stage.move(1) })
  }

  function showSnippet() {
    root.clearBrowse()
    stage.setResults("follow", Data.snippets(stage.icon, "follow"), "1 snippet")
    Qt.callLater(function() { root.fillPill("") })
  }

  function fillPill(text) {
    const field = stage.content.part_searchBar.pillsControl.fieldAt(0)
    field.forceActiveFocus()
    field.text = text
  }

  function showFirefox(query) {
    stage.setResults(query, Data.learnedFirefox(stage.icon, query), query === "ff" ? "1 result" : "2 results")
    stage.content.part_searchBar.inputControl.forceActiveFocus()
  }

  function showHome() {
    stage.setResults("", Data.home(stage.icon), "Home")
  }

  function recallSearch() {
    // The same result payload as a history recall, without storing any history.
    stage.setResults("gaps", Data.gaps(stage.icon), "4 results")
    Qt.callLater(function() { stage.move(2) })
  }
}
