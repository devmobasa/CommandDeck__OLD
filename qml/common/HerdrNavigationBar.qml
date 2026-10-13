pragma ComponentBehavior: Bound
import QtQuick
import QtQuick.Controls
import QtQuick.Layouts

RowLayout {
  id: root
  required property var theme
  required property var controller
  property Item previousTabStop: null
  property Item nextTabStop: null
  property Item searchInput: null
  readonly property Item firstControl: backButton.visible ? backButton : attentionButton
  readonly property alias lastControl: refreshButton
  readonly property alias attentionControl: attentionButton
  readonly property alias backControl: backButton
  Keys.forwardTo: searchInput ? [searchInput] : []

  component NavigationButton: Button {
    id: button
    focusPolicy: Qt.TabFocus
    Keys.onReturnPressed: clicked()
    Keys.onEnterPressed: clicked()
    background: Rectangle {
      color: button.hovered || button.activeFocus ? root.theme.surfaceStrong : "transparent"
      radius: root.theme.radiusSmall
      border.width: button.activeFocus ? 1 : 0
      border.color: root.theme.focusRing
    }
    contentItem: Text {
      textFormat: Text.PlainText
      text: button.text
      color: root.theme.text
      font.pixelSize: root.theme.fontSmall
      horizontalAlignment: Text.AlignHCenter
      verticalAlignment: Text.AlignVCenter
    }
  }

  NavigationButton {
    id: backButton
    visible: root.controller.herdrWorkspaceId.length > 0
    text: "Back · Esc"
    focusPolicy: Qt.TabFocus
    KeyNavigation.backtab: root.previousTabStop
    KeyNavigation.tab: attentionButton
    KeyNavigation.right: attentionButton
    onClicked: root.controller.backFromHerdrSpace()
  }
  Text {
    Layout.fillWidth: true
    text: root.controller.herdrWorkspaceTitle || "Herdr"
    textFormat: Text.PlainText
    elide: Text.ElideRight
    color: root.theme.textMuted
    font.pixelSize: root.theme.fontSmall
  }
  NavigationButton {
    id: attentionButton
    text: "Needs attention"
    focusPolicy: Qt.TabFocus
    KeyNavigation.backtab: backButton.visible ? backButton : root.previousTabStop
    KeyNavigation.tab: refreshButton
    KeyNavigation.left: backButton.visible ? backButton : null
    KeyNavigation.right: refreshButton
    onClicked: {
      const keyboard = activeFocus
      root.controller.showHerdrAttention()
      if (keyboard) Qt.callLater(() => attentionButton.forceActiveFocus())
    }
  }
  NavigationButton {
    id: refreshButton
    text: "Refresh"
    focusPolicy: Qt.TabFocus
    KeyNavigation.backtab: attentionButton
    KeyNavigation.tab: root.nextTabStop
    KeyNavigation.left: attentionButton
    onClicked: {
      root.controller.searchRequested()
      if (!activeFocus) root.controller.focusRevision++
    }
  }
}
