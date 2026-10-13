import QtQuick
import QtQuick.Controls
import "TabFence.js" as TabFence

// Multi-line input in the launcher palette.
TextArea {
  id: root

  required property var theme

  // Tab and Shift+Tab stay inside the settings dialog.
  Keys.priority: Keys.BeforeItem
  Keys.onPressed: event => TabFence.route(root.parent, event)

  implicitHeight: Math.round(84 * root.theme.scale)
  padding: Math.round(12 * root.theme.scale)
  color: root.enabled ? root.theme.text : root.theme.textFaint
  placeholderTextColor: root.theme.textFaint
  selectByMouse: true
  wrapMode: TextEdit.Wrap
  font.family: root.theme.fontFamily
  font.pixelSize: Math.round(root.theme.fontBody * 1.1)

  background: Rectangle {
    radius: Math.max(4, root.theme.radiusSmall)
    color: root.theme.surfaceStrong
    border.width: root.activeFocus ? 2 : 1
    border.color: root.activeFocus ? root.theme.focusRing : root.theme.separator
  }
}
