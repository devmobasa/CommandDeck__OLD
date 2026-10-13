import QtQuick

// Small key legend used in the footer action bar.
Rectangle {
  id: root

  required property var theme
  property string label: ""

  implicitHeight: Math.round(root.theme.fontCaption * 1.9)
  implicitWidth: Math.max(capText.implicitWidth + 10, root.implicitHeight)
  radius: Math.max(2, root.theme.radiusSmall - 2)
  color: root.theme.surfaceStrong
  border.width: 1
  border.color: root.theme.separator

  Text {
    id: capText

    anchors.centerIn: parent
    textFormat: Text.PlainText
    text: root.label
    color: root.theme.textMuted
    font.family: root.theme.fontFamily
    font.pixelSize: root.theme.fontCaption
  }
}
