import QtQuick
import Quickshell
import "./DemoData.js" as Data

// The 1280 × 640 repository social preview: name, summary, and Home.
ShellRoot {
  id: root

  DemoStage {
    id: stage
    logicalWorkspaceSize: Qt.size(1920, 1080)
    filters: Data.filters
    shots: [{ name: "social-preview.png", frame: { width: 640, height: 320 }, caption: "", show: root.showHome }]
    onShotSaved: {
      if (stage.panel.requestedWidth > stage.panel.width || stage.panel.requestedHeight > stage.panel.height) {
        console.error("The social preview must show the complete launcher")
        Qt.exit(1)
      }
    }

    Column {
      x: 30
      width: 230
      anchors.verticalCenter: parent.verticalCenter
      spacing: 10

      Text {
        text: "CommandDeck"
        color: "#e6ebf5"
        font.family: stage.theme.fontFamily
        font.pixelSize: 28
        font.weight: Font.Bold
      }

      Text {
        width: parent.width
        text: "A keyboard-first command palette for Omarchy and Hyprland."
        wrapMode: Text.WordWrap
        color: "#aab4c6"
        font.family: stage.theme.fontFamily
        font.pixelSize: 13
        lineHeight: 1.15
      }

      Text {
        width: parent.width
        topPadding: 6
        text: "apps · files · config · windows\nworkspaces · Omarchy · optional AI"
        color: stage.theme.accent
        font.family: stage.theme.fontFamily
        font.pixelSize: 10
        lineHeight: 1.3
      }

      Text {
        topPadding: 10
        text: "Open with Ctrl+Space"
        color: "#8492a8"
        font.family: stage.theme.fontFamily
        font.pixelSize: 10
      }
    }
  }

  function showHome() {
    stage.panel.scale = 0.36
    stage.panel.anchors.horizontalCenterOffset = 138
    stage.setResults("", Data.home(stage.icon), "Home")
  }
}
