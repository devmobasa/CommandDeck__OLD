import QtQuick
import QtQuick.Controls

// Single-line text input in the launcher palette.
//
// This mirrors SettingsField's visuals, but deliberately omits the
// settingsTabFence Keys handler: the assistant conversation is not inside the
// settings dialog and owns its own focus cycling.
TextField {
  id: root

  required property var theme

  implicitHeight: Math.round(38 * root.theme.scale)
  leftPadding: Math.round(12 * root.theme.scale)
  rightPadding: leftPadding
  color: root.enabled ? root.theme.text : root.theme.textFaint
  placeholderTextColor: root.theme.textFaint
  selectByMouse: true
  hoverEnabled: true
  font.family: root.theme.fontFamily
  font.pixelSize: Math.round(root.theme.fontBody * 1.1)

  background: Rectangle {
    radius: Math.max(4, root.theme.radiusSmall)
    color: root.theme.surfaceStrong
    border.width: root.activeFocus ? 2 : 1
    border.color: root.activeFocus ? root.theme.focusRing : root.theme.separator
  }
}
