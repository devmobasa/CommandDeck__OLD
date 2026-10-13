pragma ComponentBehavior: Bound
import QtQuick
import QtQuick.Controls
import "TabFence.js" as TabFence

// Stepper for small integers with a unit suffix. Same SpinBox contract
// (`value`, `from`, `to`, `valueModified`).
SpinBox {
  id: root

  required property var theme
  function tint(value, amount) { return Qt.rgba(value.r, value.g, value.b, amount) }
  property string suffix: ""

  // Tab and Shift+Tab stay inside the settings dialog.
  Keys.priority: Keys.BeforeItem
  Keys.onPressed: event => TabFence.route(root.parent, event)

  implicitHeight: Math.round(38 * root.theme.scale)
  implicitWidth: Math.round(180 * root.theme.scale)
  editable: true
  hoverEnabled: true
  focusPolicy: Qt.StrongFocus
  leftPadding: root.down.indicator.width + Math.round(6 * root.theme.scale)
  rightPadding: root.up.indicator.width + Math.round(6 * root.theme.scale)

  textFromValue: function(value, locale) {
    return String(value) + (root.suffix.length > 0 ? " " + root.suffix : "")
  }
  valueFromText: function(text, locale) {
    const digits = String(text).replace(/[^0-9]/g, "")
    return digits.length > 0 ? Number(digits) : root.value
  }

  contentItem: TextInput {
    // While editing, this field has focus and would move Tab along Qt's focus
    // chain itself before the spin box saw the key.
    Keys.priority: Keys.BeforeItem
    Keys.onPressed: event => TabFence.route(root.parent, event)

    text: root.displayText
    color: root.enabled ? root.theme.text : root.theme.textFaint
    horizontalAlignment: Qt.AlignHCenter
    verticalAlignment: Qt.AlignVCenter
    readOnly: !root.editable
    validator: root.validator
    inputMethodHints: Qt.ImhDigitsOnly
    selectByMouse: true
    font.family: root.theme.fontFamily
    font.pixelSize: Math.round(root.theme.fontBody * 1.15)
    font.weight: Font.Medium
  }

  component StepButton: Rectangle {
    property bool pressedState: false
    property bool hoveredState: false
    property string glyph: "+"
    property bool enabledState: true
    width: Math.round(34 * root.theme.scale)
    height: root.height
    color: pressedState ? root.tint(root.theme.text, 0.16)
      : hoveredState ? root.tint(root.theme.text, 0.10) : "transparent"
    radius: Math.max(4, root.theme.radiusSmall)
    Text {
      anchors.centerIn: parent
      text: parent.glyph
      color: parent.enabledState ? root.theme.text : root.theme.textFaint
      font.family: root.theme.fontFamily
      font.pixelSize: Math.round(root.theme.fontBody * 1.3)
      font.weight: Font.DemiBold
    }
  }

  up.indicator: StepButton {
    x: root.width - width
    glyph: "+"
    pressedState: root.up.pressed
    hoveredState: root.up.hovered
    enabledState: root.enabled && root.value < root.to
  }

  down.indicator: StepButton {
    x: 0
    glyph: "−"
    pressedState: root.down.pressed
    hoveredState: root.down.hovered
    enabledState: root.enabled && root.value > root.from
  }

  background: Rectangle {
    radius: Math.max(4, root.theme.radiusSmall)
    color: root.theme.surfaceStrong
    border.width: root.activeFocus ? 2 : 1
    border.color: root.activeFocus ? root.theme.focusRing : root.theme.separator
  }
}
