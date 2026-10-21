pragma ComponentBehavior: Bound
import QtQuick
import QtQuick.Window
import Quickshell
import "./components" as Deck

// One offscreen stage for the documentation gallery. It renders the shared
// production launcher content with demonstration data and saves each shot.
//
// A scene lists `shots`: { name, frame, show, settle }. Every shot uses one
// 980 × 680 frame unless `frame` gives { width, height } in logical pixels;
// `show` prepares the launcher; `settle` is the wait before the grab in ms;
// an optional `caption` replaces the stage caption for that shot.
// scripts/capture-gallery.cs copies qml/common into ./components, resolves
// icon files, and passes an optional Omarchy palette through the environment.
Scope {
  id: stage

  property var shots: []
  property string defaultCaption: "CommandDeck · demonstration data"
  property string caption: stage.defaultCaption
  property int captionSize: 12
  // A miniature can use desktop geometry before scaling down for composition.
  property size logicalWorkspaceSize: Qt.size(view.width, view.height)
  property var filters: []
  property int shotIndex: -1
  readonly property var palette: stage.parseJson(Quickshell.env("COMMANDDECK_GALLERY_THEME"), null)
  readonly property var icons: stage.parseJson(Quickshell.env("COMMANDDECK_GALLERY_ICONS"), ({}))
  readonly property string outputDirectory: Quickshell.env("COMMANDDECK_SCREENSHOT_OUTPUT")
  readonly property var sizingOptions: stage.parseJson(Quickshell.env("COMMANDDECK_GALLERY_SIZING"), ({}))

  readonly property alias deck: deck
  readonly property alias content: content
  readonly property alias panel: panel
  readonly property alias view: view
  readonly property alias theme: display.theme
  // Children of the stage render above the whole window; panelOverlays render
  // inside the launcher panel, above its content.
  default property alias overlays: overlayLayer.data
  property alias panelOverlays: panelLayer.data

  signal shotSaved(string name)

  function parseJson(text, fallback) {
    if (!text || String(text).length === 0) return fallback
    return JSON.parse(text)
  }

  // Resolved icon file for a freedesktop icon name. The offscreen platform has
  // no icon theme, so the capture script looks files up ahead of time.
  function icon(name) {
    return stage.icons[name] || name
  }

  // Mirrors qml/omarchy/ThemeTokens.qml defaults for a theme without
  // [launcher] overrides, using the colours from the theme's colors.toml.
  function omarchyTokens(colors) {
    const alpha = function(hex, amount) {
      const value = Math.round(amount * 255).toString(16).padStart(2, "0")
      return "#" + value + hex.slice(1)
    }

    return {
      scrim: alpha(colors.background, 0.5),
      background: alpha(colors.background, 0.95),
      border: colors.foreground,
      selectedBackground: alpha(colors.foreground, 0.08),
      selectedBorder: alpha(colors.foreground, 0.25),
      text: colors.foreground,
      selectedText: colors.accent,
      accent: colors.accent,
      urgent: colors.red,
      scale: 1,
      radius: 0,
      fontFamily: "monospace",
      fontCaption: 10, fontSmall: 11, fontBody: 12,
      fontSubtitle: 13, fontTitle: 14, fontHeading: 16,
      panelPadding: 18, rowPaddingX: 12, gap: 8
    }
  }

  function applyFrame(frame) {
    const size = frame && frame.width ? frame : { width: 980, height: 680 }
    view.width = stage.sizingOptions.width || size.width
    view.height = stage.sizingOptions.height || size.height
  }

  function setResults(query, results, status) {
    deck.activeFilters = []
    deck.queryText = query
    content.part_searchBar.setInputText(query)
    content.part_searchBar.inputControl.cursorPosition = query.length
    deck.part_navigation.replaceResults(results, true)
    deck.previewLoading = false
    deck.activePreviewRequestId = 0
    deck.statusText = status
  }

  // Moves the selection; with no daemon, file previews would stay "loading".
  function move(delta) {
    deck.part_navigation.moveSelection(delta)
    deck.previewLoading = false
    deck.activePreviewRequestId = 0
  }

  function advance() {
    stage.shotIndex++
    if (stage.shotIndex >= stage.shots.length) {
      Qt.quit()
      return
    }

    const shot = stage.shots[stage.shotIndex]
    stage.applyFrame(shot.frame)
    stage.caption = shot.caption !== undefined ? shot.caption : stage.defaultCaption
    if (shot.show) shot.show()
    settle.interval = shot.settle || 900
    settle.restart()
  }

  function grab() {
    const name = stage.shots[stage.shotIndex].name
    view.contentItem.grabToImage(function(image) {
      if (!image.saveToFile(stage.outputDirectory + "/" + name)) {
        console.error("Could not save " + name)
        Qt.exit(1)
        return
      }

      console.log("Saved " + name)
      stage.shotSaved(name)
      stage.advance()
    })
  }

  Deck.CommandDeck {
    id: deck
    testMode: true
    testSurfaceEnabled: false
    visible: false
    opened: true
    daemonAvailable: true
    filterDescriptors: stage.filters
    Component.onCompleted: {
      part_appTheme.loadShellTokens = false
      part_appTheme.reduceMotion = true
      part_searchSession.available = false
      part_clipboardController.available = false
      if (stage.palette) part_appTheme.tokens = stage.omarchyTokens(stage.palette)
      const fontScale = stage.sizingOptions["font-scale"] || 1
      if (fontScale !== 1) {
        const tokens = stage.omarchyTokens(stage.palette || {
          background: "#151a21", foreground: "#f1f1f1", accent: "#8ab4f8", red: "#ff8d8d"
        })
        tokens.scale = fontScale
        for (const key of ["fontCaption", "fontSmall", "fontBody", "fontSubtitle", "fontTitle", "fontHeading",
            "panelPadding", "rowPaddingX", "gap"]) tokens[key] = Math.round(tokens[key] * fontScale)
        part_appTheme.tokens = tokens
      }
      applyDisplaySizePercent(stage.sizingOptions.percent || 100)
      part_layoutStore.setSize(stage.sizingOptions["panel-width"] || 100, stage.sizingOptions["panel-height"] || 100)
    }
  }

  // The minimal surface CommandDeckContent needs outside a layer-shell window.
  QtObject {
    id: surface
    readonly property var host: deck
    readonly property var part_appTheme: display.theme
    readonly property var part_settingsTheme: display.theme
    readonly property int maximumFittingPercent: display.maximumFittingPercent
    readonly property string displayDescription: display.description
    readonly property bool narrow: display.sizing.narrow
    readonly property var part_palette: panel
    readonly property bool visible: true
    function openActionPanel() { }
  }

  Deck.WindowDisplay {
    id: display
    host: deck
    width: stage.logicalWorkspaceSize.width
    height: stage.logicalWorkspaceSize.height
  }

  Window {
    id: view
    visible: true
    width: 980
    height: 680
    color: stage.palette ? stage.palette.backdrop : "#0c111b"
    title: "CommandDeck gallery"

    Rectangle {
      anchors.fill: parent
      color: view.color
    }

    Deck.SizingViewport {
      id: panel
      anchors.centerIn: parent
      width: display.sizing.panelWidth
      height: display.sizing.panelHeight
      requestedWidth: display.sizing.contentWidth
      requestedHeight: display.sizing.contentHeight
      color: display.theme.panelBackground
      radius: display.theme.radius
      border.width: 1
      border.color: deck.part_appTheme.border

      Deck.CommandDeckContent {
        id: content
        host: surface
      }

      Item {
        id: panelLayer
        anchors.fill: parent
        z: 2
      }
    }

    Item {
      id: overlayLayer
      anchors.fill: parent
      z: 3
    }

    Text {
      anchors.horizontalCenter: parent.horizontalCenter
      anchors.bottom: parent.bottom
      anchors.bottomMargin: 20
      visible: stage.caption.length > 0
      text: stage.caption
      color: stage.palette ? stage.palette.caption : "#8492a8"
      font.family: deck.part_appTheme.fontFamily
      font.pixelSize: stage.captionSize
      z: 4
    }
  }

  Timer {
    id: settle
    onTriggered: stage.grab()
  }

  Component.onCompleted: Qt.callLater(stage.advance)
}
