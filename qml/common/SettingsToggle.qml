import QtQuick
import QtQuick.Controls
import "TabFence.js" as TabFence

// A switch that reads as a sentence: the label sits on the left, the pill on
// the right. Keeps the CheckBox contract (`checked`, `toggled`) the panel and
// its specs rely on.
Switch {
  id: root

  required property var theme
  function tint(value, amount) { return Qt.rgba(value.r, value.g, value.b, amount) }
  property string help: ""

  // Tab and Shift+Tab stay inside the settings dialog.
  Keys.priority: Keys.BeforeItem
  Keys.onPressed: event => TabFence.route(root.parent, event)

  readonly property int pillWidth: Math.round(42 * root.theme.scale)
  readonly property int pillHeight: Math.round(24 * root.theme.scale)

  implicitHeight: Math.max(root.pillHeight, labelColumn.implicitHeight) + Math.round(12 * root.theme.scale)
  implicitWidth: labelColumn.implicitWidth + root.pillWidth + spacing
  spacing: Math.round(14 * root.theme.scale)
  hoverEnabled: true
  focusPolicy: Qt.StrongFocus

  indicator: Rectangle {
    x: root.width - width
    y: (root.height - height) / 2
    width: root.pillWidth
    height: root.pillHeight
    radius: height / 2
    color: root.checked
      ? root.tint(root.theme.accent, root.enabled ? 0.85 : 0.4)
      : root.tint(root.theme.text, 0.14)
    border.width: root.activeFocus ? 2 : 1
    border.color: root.activeFocus ? root.theme.focusRing : root.theme.separator

    Rectangle {
      x: root.checked ? parent.width - width - 3 : 3
      y: 3
      width: parent.height - 6
      height: width
      radius: width / 2
      color: root.checked ? root.theme.selectedText : root.theme.text
      Behavior on x { NumberAnimation { duration: root.theme.durationFast !== undefined ? root.theme.durationFast : 90 } }
    }
  }

  // The control stretches its content item to the full available height, so
  // the label column sits inside a plain Item and centres itself in it.
  contentItem: Item {
    anchors.left: parent.left
    anchors.right: root.indicator.left
    anchors.rightMargin: root.spacing

    Column {
      id: labelColumn
      width: parent.width
      anchors.verticalCenter: parent.verticalCenter
      spacing: Math.round(2 * root.theme.scale)

      Text {
        width: parent.width
        textFormat: Text.PlainText
        text: root.text
        color: root.enabled ? root.theme.text : root.theme.textFaint
        wrapMode: Text.Wrap
        font.family: root.theme.fontFamily
        font.pixelSize: Math.round(root.theme.fontBody * 1.15)
        font.weight: Font.Medium
      }

      Text {
        width: parent.width
        visible: root.help.length > 0
        textFormat: Text.PlainText
        text: root.help
        color: root.theme.textMuted
        wrapMode: Text.Wrap
        font.family: root.theme.fontFamily
        font.pixelSize: root.theme.fontBody
      }
    }
  }

  background: Item {}
}
