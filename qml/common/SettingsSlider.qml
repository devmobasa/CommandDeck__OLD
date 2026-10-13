import QtQuick
import QtQuick.Controls
import QtQuick.Layouts
import "TabFence.js" as TabFence

// Themed slider with a live percent (or unit) readout. Keeps the Slider
// contract (`value`, `from`, `to`, `moved`) the panel and its specs rely on.
Item {
  id: root

  required property var theme
  function tint(value, amount) { return Qt.rgba(value.r, value.g, value.b, amount) }
  property alias from: slider.from
  property alias to: slider.to
  property alias value: slider.value
  property alias stepSize: slider.stepSize
  property alias live: slider.live
  property alias pressed: slider.pressed
  property alias snapMode: slider.snapMode
  property string suffix: "%"
  property alias sliderControl: slider

  signal moved()

  implicitHeight: Math.round(38 * root.theme.scale)
  implicitWidth: Math.round(240 * root.theme.scale)

  RowLayout {
    anchors.fill: parent
    spacing: Math.round(10 * root.theme.scale)

    Slider {
      id: slider
      Layout.fillWidth: true
      Layout.alignment: Qt.AlignVCenter
      from: 0
      to: 80
      stepSize: 1
      live: true
      snapMode: Slider.SnapAlways
      onMoved: root.moved()
      hoverEnabled: true
      focusPolicy: Qt.StrongFocus
      Accessible.name: root.Accessible.name
      Accessible.description: root.Accessible.description
      Accessible.role: Accessible.Slider

      // Tab and Shift+Tab stay inside the settings dialog.
      Keys.priority: Keys.BeforeItem
      Keys.onPressed: event => TabFence.route(root.parent, event)

      background: Item {
        x: slider.leftPadding
        y: slider.topPadding + (slider.availableHeight - height) / 2
        implicitWidth: Math.round(140 * root.theme.scale)
        implicitHeight: Math.round(6 * root.theme.scale)
        width: slider.availableWidth
        height: implicitHeight

        Rectangle {
          anchors.fill: parent
          radius: height / 2
          color: root.tint(root.theme.text, slider.enabled ? 0.14 : 0.08)
        }

        Rectangle {
          width: slider.visualPosition * parent.width
          height: parent.height
          radius: height / 2
          color: slider.enabled
            ? root.tint(root.theme.accent, 0.85)
            : root.tint(root.theme.accent, 0.35)
        }
      }

      handle: Rectangle {
        x: slider.leftPadding + slider.visualPosition * (slider.availableWidth - width)
        y: slider.topPadding + (slider.availableHeight - height) / 2
        implicitWidth: Math.round(18 * root.theme.scale)
        implicitHeight: implicitWidth
        radius: width / 2
        color: slider.enabled ? root.theme.selectedText : root.theme.textFaint
        border.width: slider.activeFocus ? 2 : 1
        border.color: slider.activeFocus ? root.theme.focusRing : root.theme.separator
      }
    }

    Text {
      Layout.alignment: Qt.AlignVCenter
      Layout.preferredWidth: Math.round(56 * root.theme.scale)
      horizontalAlignment: Text.AlignRight
      text: String(Math.round(slider.value))
        + (root.suffix.length > 0 ? root.suffix : "")
      color: root.enabled ? root.theme.text : root.theme.textFaint
      font.family: root.theme.fontFamily
      font.pixelSize: Math.round(root.theme.fontBody * 1.15)
      font.weight: Font.Medium
    }
  }
}
