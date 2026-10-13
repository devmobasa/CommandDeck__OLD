import QtQuick
import "UiConstants.js" as Ui
import QtQuick.Layouts

Rectangle {
  id: root

  required property var theme
  property bool opened: false
  property string prompt: "Run this action?"
  property string confirmLabel: "Run"
  // Confirming removes data or state, so the confirm button reads as danger.
  property bool destructive: false
  readonly property alias cancelControl: cancelButton
  readonly property alias confirmControl: confirmButton
  readonly property bool focusTrapConfigured:
    cancelButton.KeyNavigation.tab === confirmButton
    && cancelButton.KeyNavigation.backtab === confirmButton
    && confirmButton.KeyNavigation.tab === cancelButton
    && confirmButton.KeyNavigation.backtab === cancelButton

  signal accepted()
  signal canceled()

  visible: opened
  color: root.theme.scrim
  focus: visible
  Accessible.role: Accessible.Dialog
  Accessible.name: prompt

  onVisibleChanged: {
    if (visible) Qt.callLater(function() { cancelButton.forceActiveFocus() })
  }

  Keys.onPressed: event => {
    if (event.key === Qt.Key_Escape) {
      root.canceled()
      event.accepted = true
    }
  }

  MouseArea {
    anchors.fill: parent
    acceptedButtons: Qt.AllButtons
    hoverEnabled: true
  }

  Rectangle {
    width: Math.min(420, parent.width - 48)
    implicitHeight: confirmationContent.implicitHeight + 40
    anchors.centerIn: parent
    radius: root.theme.radius
    color: root.theme.popupBackground
    border.width: 1
    border.color: root.theme.border

    ColumnLayout {
      id: confirmationContent
      anchors.fill: parent
      anchors.margins: 20
      spacing: 16

      Text {
        Layout.fillWidth: true
        textFormat: Text.PlainText
        text: root.prompt
        color: root.theme.text
        font.family: root.theme.fontFamily
        font.pixelSize: root.theme.fontHeading
        wrapMode: Text.Wrap
      }

      RowLayout {
        Layout.alignment: Qt.AlignRight

        DeckButton {
          id: cancelButton
          theme: root.theme
          kind: activeFocus ? Ui.ButtonVariant.Primary : Ui.ButtonVariant.Secondary
          text: "Cancel"
          activeFocusOnTab: true
          KeyNavigation.tab: confirmButton
          KeyNavigation.backtab: confirmButton
          KeyNavigation.left: confirmButton
          KeyNavigation.right: confirmButton
          onClicked: root.canceled()
        }

        DeckButton {
          id: confirmButton
          theme: root.theme
          kind: root.destructive ? Ui.ButtonVariant.Danger
            : activeFocus ? Ui.ButtonVariant.Primary : Ui.ButtonVariant.Secondary
          text: root.confirmLabel
          activeFocusOnTab: true
          KeyNavigation.tab: cancelButton
          KeyNavigation.backtab: cancelButton
          KeyNavigation.left: cancelButton
          KeyNavigation.right: cancelButton
          Accessible.description: root.destructive
            ? "Destructive action. Requires confirmation"
            : "Requires confirmation"
          onClicked: root.accepted()
        }
      }

      Text {
        Layout.fillWidth: true
        text: "←/→ Choose · Enter Select · Esc Cancel"
        color: root.theme.textMuted
        font.family: root.theme.fontFamily
        font.pixelSize: root.theme.fontCaption
        wrapMode: Text.Wrap
      }
    }
  }
}
