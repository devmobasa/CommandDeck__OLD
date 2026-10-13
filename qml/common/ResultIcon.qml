import QtQuick
import "Protocol.js" as Protocol
import Quickshell

// Icon tile for a result row.
//
// The daemon already ships freedesktop icon names on every result
// (`brave-browser`, `accessories-calculator`, `application-x-executable`), so
// this resolves them against the active icon theme and only falls back to a
// glyph when nothing matches.
Rectangle {
  id: root

  required property var theme
  required property var result
  property int size: 34

  readonly property string kind: root.result && root.result.kind
    ? root.result.kind
    : ""
  readonly property string iconName: root.result && root.result.icon
    ? String(root.result.icon)
    : ""
  readonly property int glyphSize: Math.round(root.size * 0.66)
  // Bound to the name and kind rather than the result, so a refreshed result
  // with the same icon does not repeat the icon-theme lookup.
  readonly property string iconSource: root.resolve(root.iconName, root.kind)

  function candidates(iconName, kind) {
    const names = []

    if (iconName) {
      names.push(iconName)
    }

    return names.concat(root.genericNames(kind))
  }

  // Icon themes disagree about which generic names they ship, so each kind
  // lists more than one and anything unresolved falls through to a glyph.
  function genericNames(kind) {
    switch (kind) {
      case Protocol.ResultKinds.Application: return ["application-x-executable", "system-run"]
      case Protocol.ResultKinds.File: return ["text-x-generic"]
      case Protocol.ResultKinds.Configuration: return ["preferences-system", "preferences-desktop"]
      case Protocol.ResultKinds.Window:
      case Protocol.ResultKinds.Workspace: return ["preferences-desktop", "user-desktop"]
      case Protocol.ResultKinds.Tab: return ["utilities-terminal", "preferences-desktop"]
      case Protocol.ResultKinds.Calculator: return ["accessories-calculator"]
      case Protocol.ResultKinds.Omarchy: return ["applications-system", "system-run"]
      case Protocol.ResultKinds.Quicklink: return ["internet-web-browser"]
      case Protocol.ResultKinds.CustomCommand: return ["utilities-terminal", "system-run"]
      default: return []
    }
  }

  function resolve(iconName, kind) {
    const names = root.candidates(iconName, kind)

    for (let index = 0; index < names.length; index++) {
      const name = names[index]
      if (!name || name.length === 0) {
        continue
      }

      // Desktop entries may point at an absolute file instead of a theme name.
      if (name.charAt(0) === "/") {
        return "file://" + name
      }

      const path = Quickshell.iconPath(name, true)
      if (path && path.length > 0) {
        return path
      }
    }

    return ""
  }

  function glyph(kind) {
    switch (kind) {
      case Protocol.ResultKinds.Application: return "A"
      case Protocol.ResultKinds.Configuration: return "⚙"
      case Protocol.ResultKinds.Window: return "▣"
      case Protocol.ResultKinds.Workspace: return "◫"
      case Protocol.ResultKinds.Tab: return "⧉"
      case Protocol.ResultKinds.Calculator: return "="
      case Protocol.ResultKinds.AiCommand: return "✦"
      case Protocol.ResultKinds.Omarchy: return "O"
      default: return "•"
    }
  }

  // Tinted toward the theme accent for kinds that launch or focus something,
  // neutral for kinds that are content.
  function tint(kind) {
    switch (kind) {
      case Protocol.ResultKinds.Calculator:
      case Protocol.ResultKinds.AiCommand:
        return root.theme.withAlpha(root.theme.accent, 0.22)
      case Protocol.ResultKinds.Application:
      case Protocol.ResultKinds.Omarchy:
      case Protocol.ResultKinds.CustomCommand:
      case Protocol.ResultKinds.Quicklink:
        return root.theme.withAlpha(root.theme.accent, 0.16)
      case Protocol.ResultKinds.Window:
      case Protocol.ResultKinds.Workspace:
      case Protocol.ResultKinds.Tab:
        return root.theme.withAlpha(root.theme.accent, 0.10)
      default:
        return root.theme.surfaceStrong
    }
  }

  implicitWidth: root.size
  implicitHeight: root.size
  radius: root.theme.radiusSmall
  color: root.tint(root.kind)

  Image {
    id: image

    anchors.centerIn: parent
    width: root.glyphSize
    height: root.glyphSize
    source: root.iconSource
    sourceSize: Qt.size(root.glyphSize, root.glyphSize)
    fillMode: Image.PreserveAspectFit
    visible: root.iconSource.length > 0 && status === Image.Ready
    asynchronous: true
    smooth: true
    mipmap: true
  }

  Text {
    anchors.centerIn: parent
    visible: !image.visible
    text: root.glyph(root.kind)
    color: root.theme.text
    font.family: root.theme.fontFamily
    font.pixelSize: root.theme.fontSubtitle
  }
}
