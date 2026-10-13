import QtQuick
import "TabFence.js" as TabFence
import "UiConstants.js" as Ui
import QtQuick.Controls

// The CommandDeck button. `kind` is primary, secondary, or danger; `compact`
// shrinks it for dense rows; `flat` hides the idle chrome (icon buttons) while
// keeping the same hover and focus emphasis as every other button.
KeyboardButton {
  id: root

  required property var theme
  function tint(value, amount) { return Qt.rgba(value.r, value.g, value.b, amount) }
  property string kind: Ui.ButtonVariant.Secondary
  property bool compact: false
  // Surfaces that own their own focus cycling (the assistant conversation)
  // turn the settings tab fence off.
  property bool tabFence: true

  // Tab and Shift+Tab stay inside the settings dialog.
  Keys.priority: Keys.BeforeItem
  Keys.onPressed: event => {
    if (root.tabFence) TabFence.route(root.parent, event)
  }

  readonly property bool primary: root.kind === Ui.ButtonVariant.Primary
  readonly property bool dangerous: root.kind === Ui.ButtonVariant.Danger

  implicitHeight: Math.round((root.compact ? 32 : 38) * root.theme.scale)
  implicitWidth: Math.max(
    contentText.implicitWidth + Math.round((root.compact ? 20 : 28) * root.theme.scale),
    implicitHeight)
  leftPadding: Math.round((root.compact ? 10 : 14) * root.theme.scale)
  rightPadding: leftPadding
  hoverEnabled: true
  focusPolicy: Qt.StrongFocus

  contentItem: Text {
    id: contentText
    text: root.text
    // Titles can come from extensions and the assistant; show them as written.
    textFormat: Text.PlainText
    color: !root.enabled
      ? root.theme.textFaint
      : root.primary ? root.theme.background
      : root.dangerous ? root.theme.urgent
      : root.theme.text
    horizontalAlignment: Text.AlignHCenter
    verticalAlignment: Text.AlignVCenter
    // No elide: settings buttons carry short labels and their rows can give them
    // the width. Eliding collapses the button's implicitWidth in a tight layout
    // and truncates the label ("Sign out" -> "Sign o…"), same as AgentButton.
    font.family: root.theme.fontFamily
    font.pixelSize: root.compact ? root.theme.fontBody : Math.round(root.theme.fontBody * 1.1)
    font.weight: root.primary ? Font.DemiBold : Font.Medium
  }

  // Hover and keyboard focus must read at a glance on the dark launcher
  // palette: the fill shifts toward the accent, the border thickens to the
  // text colour, and keyboard focus adds a ring outside the button.
  readonly property bool emphasized: root.enabled && (root.hovered || root.activeFocus)
  readonly property bool focusRingVisible: root.activeFocus

  background: Rectangle {
    radius: Math.max(6, root.theme.radiusSmall)
    color: !root.enabled
      ? (root.flat ? "transparent" : root.theme.surface)
      : root.flat && !root.emphasized && !root.down ? "transparent"
      : root.primary
        ? (root.down ? root.tint(root.theme.accent, 0.72)
          : root.emphasized ? root.theme.accent : root.tint(root.theme.accent, 0.9))
      : root.dangerous
        ? (root.down ? root.tint(root.theme.urgent, 0.42)
          : root.activeFocus ? root.tint(root.theme.urgent, 0.34)
          : root.hovered ? root.tint(root.theme.urgent, 0.26)
          : root.tint(root.theme.urgent, 0.08))
        : (root.down ? root.theme.surfaceStrong
          : root.activeFocus ? root.tint(root.theme.accent, 0.58)
          : root.hovered ? root.tint(root.theme.accent, 0.38)
          : root.theme.surfaceStrong)
    border.width: root.emphasized ? 3 : root.flat ? 0 : 1
    border.color: root.emphasized
      ? root.theme.text
      : root.primary ? root.theme.accent
      : root.dangerous ? root.tint(root.theme.urgent, 0.4)
      : root.theme.separator

    Rectangle {
      anchors.fill: parent
      anchors.margins: -Math.round(5 * root.theme.scale)
      visible: root.focusRingVisible
      color: "transparent"
      radius: parent.radius + Math.round(4 * root.theme.scale)
      border.width: Math.max(4, Math.round(4 * root.theme.scale))
      border.color: root.dangerous ? root.theme.urgent : root.theme.focusRing
    }
  }
}
