import QtQuick
import QtQuick.Controls
import QtQuick.Layouts

// Clipboard browser button with an optional key hint. Idle neutral buttons stay
// quiet so the selected entry carries the page; hover fills faintly, keyboard
// focus fills toward the tone colour with a solid outline, `outlined` gives a
// toolbar control a resting hairline, and `latched` marks an active toggle.
// `tone` is "primary", "neutral", or "danger".
KeyboardButton {
  id: root

  required property var theme
  property string tone: "neutral"
  property string keyHint: ""
  property bool latched: false
  property bool outlined: false
  property string toolTipText: ""
  readonly property bool primary: root.tone === "primary"
  readonly property bool danger: root.tone === "danger"
  readonly property color toneColor: root.danger ? root.theme.urgent : root.theme.accent

  function tint(value, amount) { return Qt.rgba(value.r, value.g, value.b, amount) }

  focusPolicy: Qt.TabFocus
  hoverEnabled: true
  implicitHeight: Math.round(32 * root.theme.scale)
  implicitWidth: Math.max(implicitHeight, labelRow.implicitWidth + leftPadding + rightPadding)
  leftPadding: Math.round(10 * root.theme.scale)
  rightPadding: root.keyHint.length > 0 ? Math.round(5 * root.theme.scale) : leftPadding
  topPadding: 0
  bottomPadding: 0
  Accessible.name: root.text
  Accessible.description: root.keyHint.length > 0 ? "Shortcut " + root.keyHint : root.toolTipText

  // Centred so a button wider than its label (Load more, match steps) stays balanced.
  contentItem: Item {
    implicitWidth: labelRow.implicitWidth
    implicitHeight: labelRow.implicitHeight

    RowLayout {
      id: labelRow
      anchors.centerIn: parent
      spacing: Math.round(8 * root.theme.scale)

      Text {
        Layout.alignment: Qt.AlignVCenter
        text: root.text
        textFormat: Text.PlainText
        color: !root.enabled ? root.theme.textFaint
          : root.primary ? root.theme.background
          : root.danger ? root.theme.urgent
          : root.theme.text
        font.family: root.theme.fontFamily
        font.pixelSize: root.theme.fontBody
        font.weight: root.primary ? Font.DemiBold : Font.Medium
      }

      Rectangle {
        Layout.alignment: Qt.AlignVCenter
        visible: root.keyHint.length > 0
        implicitHeight: Math.round(root.theme.fontCaption * 1.9)
        implicitWidth: Math.max(implicitHeight, hintLabel.implicitWidth + Math.round(10 * root.theme.scale))
        radius: Math.max(2, root.theme.radiusSmall - 2)
        color: root.primary ? root.tint(root.theme.background, 0.18) : root.theme.surfaceStrong
        border.width: root.primary ? 0 : 1
        border.color: root.theme.separator

        Text {
          id: hintLabel
          anchors.centerIn: parent
          text: root.keyHint
          color: root.primary && root.enabled ? root.theme.background
            : root.enabled ? root.theme.textMuted : root.theme.textFaint
          font.family: root.theme.fontFamily
          font.pixelSize: root.theme.fontCaption
        }
      }
    }
  }

  background: Rectangle {
    radius: Math.max(4, root.theme.radiusSmall)
    color: {
      if (!root.enabled) return root.primary ? root.theme.surface : "transparent"
      if (root.primary)
        return root.down ? root.tint(root.theme.accent, 0.72)
          : root.hovered || root.activeFocus ? root.theme.accent : root.tint(root.theme.accent, 0.88)
      if (root.down) return root.tint(root.toneColor, 0.32)
      if (root.activeFocus) return root.tint(root.toneColor, 0.24)
      if (root.latched) return root.tint(root.theme.accent, root.hovered ? 0.26 : 0.18)
      if (root.hovered) return root.tint(root.theme.text, 0.08)
      return root.outlined ? root.theme.surface : "transparent"
    }
    border.width: root.activeFocus ? 2 : root.latched || root.outlined ? 1 : 0
    border.color: root.activeFocus ? (root.primary ? root.theme.background : root.toneColor)
      : root.latched ? root.tint(root.theme.accent, 0.55) : root.theme.separator

    // A primary button is already accent-filled, so keyboard focus adds a ring.
    Rectangle {
      visible: root.activeFocus && root.primary
      anchors.fill: parent
      anchors.margins: -3
      radius: parent.radius + 3
      color: "transparent"
      border.width: 2
      border.color: root.theme.accent
    }
  }

  ThemedToolTip {
    theme: root.theme
    visible: root.hovered && root.toolTipText.length > 0
    text: root.toolTipText
  }
}
