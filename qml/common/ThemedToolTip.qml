import QtQuick
import QtQuick.Controls

ToolTip {
  id: root

  required property var theme
  // Render inside the launcher instead of a platform-native tooltip window.
  popupType: Popup.Item
  focus: false
  margins: Math.round(8 * root.theme.scale)
  padding: Math.round(10 * root.theme.scale)
  delay: 600
  x: 0
  y: parent ? parent.height + Math.round(4 * root.theme.scale) : 0
  width: Math.min(implicitWidth, Math.round(420 * root.theme.scale),
    parent && parent.Window.window ? Math.max(1, parent.Window.window.width - margins * 2) : Math.round(420 * root.theme.scale))

  contentItem: Text {
    text: root.text
    textFormat: Text.PlainText
    wrapMode: Text.WrapAnywhere
    maximumLineCount: 8
    elide: Text.ElideRight
    color: root.theme.text
    font.family: root.theme.fontFamily
    font.pixelSize: root.theme.fontSmall
  }

  background: Rectangle {
    color: root.theme.popupBackground
    border.color: root.theme.border
    border.width: 1
    radius: root.theme.radiusSmall
  }
}
