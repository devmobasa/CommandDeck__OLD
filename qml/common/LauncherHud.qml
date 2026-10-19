import QtQuick
import Quickshell
import Quickshell.Wayland

// A short message near the bottom of the screen after an action closes the
// launcher, such as "Copied." It never takes keyboard focus or pointer input
// and disappears on its own when the host clears the text.
// Quickshell registers the platform-specific PanelWindow at runtime; its static
// interface is marked uncreatable. The component suite instantiates it.
// qmllint disable uncreatable-type
PanelWindow {
  id: hud

  required property var host
  // Whether this screen is the one the launcher was opened on.
  property bool onLauncherScreen: true
  readonly property string text: hud.host.hudText
  readonly property bool shown: hud.onLauncherScreen && hud.text.length > 0
  readonly property var theme: hud.host.part_appTheme

  visible: hud.shown
  color: "transparent"
  anchors { top: true; bottom: true; left: true; right: true }
  exclusionMode: ExclusionMode.Ignore
  WlrLayershell.layer: WlrLayer.Overlay
  WlrLayershell.keyboardFocus: WlrKeyboardFocus.None
  WlrLayershell.namespace: "commanddeck-hud"
  // Visual only: an empty input region never blocks clicks to the desktop.
  mask: Region {}

  Rectangle {
    id: card
    readonly property int padding: Math.round(18 * hud.theme.scale)
    anchors.horizontalCenter: parent.horizontalCenter
    anchors.bottom: parent.bottom
    anchors.bottomMargin: Math.round(parent.height * 0.12)
    width: Math.min(label.implicitWidth + 2 * card.padding, Math.round(parent.width * 0.6))
    height: label.implicitHeight + Math.round(20 * hud.theme.scale)
    radius: hud.theme.radius
    color: hud.theme.popupBackground
    border.width: 1
    border.color: hud.theme.border
    opacity: hud.shown ? 1 : 0
    Behavior on opacity {
      enabled: !hud.theme.reduceMotion
      NumberAnimation { duration: hud.theme.durationFast }
    }

    Text {
      id: label
      anchors.centerIn: parent
      width: Math.min(implicitWidth, card.width - 2 * card.padding)
      text: hud.text
      textFormat: Text.PlainText
      elide: Text.ElideRight
      color: hud.theme.text
      font.family: hud.theme.fontFamily
      font.pixelSize: hud.theme.fontBody
      Accessible.role: Accessible.AlertMessage
      Accessible.name: hud.text
    }
  }
}
