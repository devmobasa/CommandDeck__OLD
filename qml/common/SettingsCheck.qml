import QtQuick
import QtQuick.Controls
import "TabFence.js" as TabFence

// A compact checkbox for columns of on/off choices, such as Settings → Search
// sources. An outline means off and an accent fill with a check mark means on,
// so colour alone never carries the state. Keeps the stock CheckBox contract
// (`checked`, `toggled`, Space to toggle); Enter toggles as well.
CheckBox {
  id: root

  required property var theme
  function tint(value, amount) { return Qt.rgba(value.r, value.g, value.b, amount) }
  readonly property int boxSize: Math.round(18 * root.theme.scale)

  // Tab and Shift+Tab stay inside the settings dialog.
  Keys.priority: Keys.BeforeItem
  Keys.onPressed: event => TabFence.route(root.parent, event)
  Keys.onReturnPressed: root.toggleByKeyboard()
  Keys.onEnterPressed: root.toggleByKeyboard()

  // Enter behaves like Space: flip the state, then report it as a click.
  function toggleByKeyboard() {
    root.toggle()
    root.clicked()
  }

  implicitWidth: root.boxSize
  implicitHeight: root.boxSize
  padding: 0
  spacing: 0
  hoverEnabled: true
  focusPolicy: Qt.StrongFocus

  indicator: Rectangle {
    x: (root.width - width) / 2
    y: (root.height - height) / 2
    width: root.boxSize
    height: root.boxSize
    radius: Math.max(3, Math.round(4 * root.theme.scale))
    color: root.checked
      ? root.tint(root.theme.accent, root.enabled ? 0.9 : 0.4)
      : (root.hovered && root.enabled ? root.theme.surfaceStrong : "transparent")
    border.width: root.checked ? 0 : Math.max(1, Math.round(1.5 * root.theme.scale))
    border.color: root.hovered && root.enabled ? root.theme.text : root.theme.textFaint

    Text {
      anchors.centerIn: parent
      visible: root.checked
      text: "✓"
      color: root.theme.popupBackground
      font.family: root.theme.fontFamily
      font.pixelSize: Math.round(root.boxSize * 0.72)
      font.weight: Font.Bold
    }

    Rectangle {
      anchors.fill: parent
      anchors.margins: -Math.round(4 * root.theme.scale)
      visible: root.activeFocus
      radius: parent.radius + Math.round(3 * root.theme.scale)
      color: "transparent"
      border.width: Math.max(2, Math.round(2 * root.theme.scale))
      border.color: root.theme.focusRing
    }
  }

  contentItem: Item { }
}
