import QtQuick
import Quickshell
import "." as CommandDeck

QtObject {
  required property var host

  id: theme

  readonly property color surface: "#111111"
  readonly property color separator: "#333333"
  readonly property color text: "#ffffff"
  readonly property color textMuted: "#bbbbbb"
  readonly property color textFaint: "#888888"
  readonly property color accent: "#6699ff"
  readonly property color focusRing: theme.accent
  readonly property color surfaceStrong: "#222222"
  readonly property color background: "#090909"
  readonly property color popupBackground: "#090909"
  readonly property color scrim: "#99000000"
  readonly property color border: "#444444"
  readonly property color selectedBackground: "#222244"
  readonly property color selectedText: "#ffffff"
  readonly property color selectedBorder: "#6666aa"
  readonly property color urgent: "#ff7777"
  readonly property bool reduceMotion: false
  readonly property real scale: 1
  readonly property int radiusSmall: 8
  readonly property int radius: 14
  readonly property int inputHeight: 46
  readonly property int actionRowHeight: 36
  readonly property int panelPadding: 14
  readonly property int gap: 8
  readonly property int fontCaption: 10
  readonly property int fontSmall: 11
  readonly property int fontBody: 12
  readonly property int fontSubtitle: 13
  readonly property int fontHeading: 16
  readonly property int fontTitle: 14
  readonly property string fontFamily: "sans-serif"
  readonly property int rowHeight: 48
  readonly property int compactRowHeight: 38
  readonly property int compactIconSize: 26
  readonly property int rowPaddingX: 10
  readonly property int iconGap: 8
  readonly property int iconSize: 24
  readonly property int durationFast: 0

  function withAlpha(value, amount) {
    return Qt.rgba(value.r, value.g, value.b, amount)
  }
}
