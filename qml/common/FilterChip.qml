import QtQuick
import QtQuick.Layouts

Rectangle {
  id: root

  required property var theme
  property string label: ""
  readonly property string accessibilityName: root.label + " filter, remove"
  signal removeRequested()

  implicitWidth: chipRow.implicitWidth + 16
  implicitHeight: Math.round(root.theme.inputHeight * 0.62)
  radius: root.theme.radiusSmall
  color: root.theme.surfaceStrong
  border.width: 1
  border.color: root.theme.selectedBorder

  Accessible.role: Accessible.Button
  Accessible.name: root.accessibilityName
  Accessible.onPressAction: root.activate()
  activeFocusOnTab: true

  function activate() {
    root.removeRequested()
  }

  Keys.onPressed: event => {
    if (event.key === Qt.Key_Return || event.key === Qt.Key_Enter
        || event.key === Qt.Key_Space) {
      root.activate()
      event.accepted = true
    }
  }

  RowLayout {
    id: chipRow
    anchors.centerIn: parent
    spacing: 6

    Text {
      textFormat: Text.PlainText
      text: root.label
      color: root.theme.text
      font.family: root.theme.fontFamily
      font.pixelSize: root.theme.fontSmall
    }

    Text {
      text: "×"
      color: root.theme.textMuted
      font.family: root.theme.fontFamily
      font.pixelSize: root.theme.fontBody
    }
  }

  MouseArea {
    anchors.fill: parent
    onClicked: root.activate()
  }
}
