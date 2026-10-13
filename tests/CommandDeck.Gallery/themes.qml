import QtQuick
import Quickshell
import "./DemoData.js" as Data

// Home under one Omarchy palette from themes.json. scripts/capture-gallery.cs
// renders it once per theme and tiles the results into themes.png.
ShellRoot {
  id: root

  DemoStage {
    id: stage
    filters: Data.filters
    shots: [{
      name: "theme-" + stage.palette.id + ".png",
      caption: stage.palette.name + " · Omarchy theme colours",
      show: root.showHome
    }]
  }

  function showHome() {
    stage.setResults("", Data.home(stage.icon), "Home")
  }
}
