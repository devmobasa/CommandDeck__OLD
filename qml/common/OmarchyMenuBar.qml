pragma ComponentBehavior: Bound
import QtQuick
import QtQuick.Controls
import QtQuick.Layouts

// The open Omarchy submenu: its path from Omarchy's menu, and Back to the view
// it was opened from. Typing while Back has focus still reaches the search.
RowLayout {
  id: root
  required property var theme
  required property var controller
  property Item previousTabStop: null
  property Item nextTabStop: null
  property Item searchInput: null
  readonly property alias firstControl: backButton
  readonly property alias lastControl: backButton
  readonly property alias backControl: backButton
  readonly property alias pathControl: pathLabel
  readonly property string pathText: "Omarchy › " + (root.controller.omarchyMenuPath || "Menu")
  Keys.forwardTo: searchInput ? [searchInput] : []

  Button {
    id: backButton
    text: "Back · Esc"
    focusPolicy: Qt.TabFocus
    Accessible.name: "Back from " + root.pathText
    KeyNavigation.backtab: root.previousTabStop
    KeyNavigation.tab: root.nextTabStop
    Keys.onReturnPressed: clicked()
    Keys.onEnterPressed: clicked()
    onClicked: root.controller.backFromOmarchyMenu()
    background: Rectangle {
      color: backButton.hovered || backButton.activeFocus ? root.theme.surfaceStrong : "transparent"
      radius: root.theme.radiusSmall
      border.width: backButton.activeFocus ? 1 : 0
      border.color: root.theme.focusRing
    }
    contentItem: Text {
      textFormat: Text.PlainText
      text: backButton.text
      color: root.theme.text
      font.pixelSize: root.theme.fontSmall
      horizontalAlignment: Text.AlignHCenter
      verticalAlignment: Text.AlignVCenter
    }
  }
  Text {
    id: pathLabel
    Layout.fillWidth: true
    text: root.pathText
    textFormat: Text.PlainText
    elide: Text.ElideLeft
    color: root.theme.textMuted
    font.pixelSize: root.theme.fontSmall
    Accessible.role: Accessible.StaticText
    Accessible.name: root.pathText
  }
}
