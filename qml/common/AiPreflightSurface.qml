import QtQuick
import "UiConstants.js" as Ui
import "Protocol.js" as Protocol
import QtQuick.Controls
import QtQuick.Layouts

FocusScope {
  id: root
  required property var theme
  property var preflight: null
  property bool opened: false
  signal accepted()
  signal canceled()
  readonly property string disclosureText: {
    if (!root.preflight) return ""
    let attachments = ""
    const values = root.preflight.attachments || []
    for (let index = 0; index < values.length; index++) {
      const value = values[index]
      attachments += "\nAttachment " + (index + 1) + ": "
        + (value.displayName || value.kind || "attachment") + " — "
        + Number(value.utf8Bytes || 0) + " UTF-8 bytes; included as untrusted data"
    }
    return "Provider: " + root.preflight.providerId
      + "\nModel: " + root.preflight.modelId
      + "\nDestination: " + root.preflight.destination
      + " (" + root.preflight.locality + ")"
      + "\nCredential source: " + root.preflight.credentialSource
      + "\nPayload digest (SHA-256): " + root.preflight.payloadDigest
      + "\nPayload: " + root.preflight.payloadBytes + " UTF-8 bytes"
      + attachments
      + "\n\nSystem message:\n" + root.preflight.systemMessage
      + "\n\nUser message and exact attachments:\n" + root.preflight.userMessage
      + "\n\nExact provider request JSON:\n" + root.preflight.providerRequestPreview
  }

  function focusTargets() { return [payloadText, cancelButton, sendButton] }
  function moveFocus(direction) {
    const targets = focusTargets()
    let current = targets.findIndex(item => item.activeFocus)
    if (current < 0) current = direction > 0 ? -1 : 0
    targets[(current + direction + targets.length) % targets.length].forceActiveFocus()
  }

  visible: opened
  focus: opened
  Accessible.role: Accessible.Dialog
  Accessible.name: "Confirm AI request"
  Accessible.description: root.disclosureText
  Keys.onEscapePressed: root.canceled()
  Keys.onPressed: event => {
    if (event.key === Qt.Key_Tab) {
      root.moveFocus((event.modifiers & Qt.ShiftModifier) ? -1 : 1)
      event.accepted = true
    } else if (event.key === Qt.Key_Backtab) {
      root.moveFocus(-1)
      event.accepted = true
    }
  }

  Rectangle {
    anchors.fill: parent
    color: root.theme.scrim
    MouseArea { anchors.fill: parent; onClicked: root.canceled() }
  }
  Rectangle {
    width: Math.min(760, parent.width - 80)
    height: Math.min(620, parent.height - 80)
    anchors.centerIn: parent
    radius: root.theme.radius
    color: root.theme.popupBackground
    border.width: 1
    border.color: root.theme.border
    MouseArea { anchors.fill: parent; acceptedButtons: Qt.NoButton }
    ColumnLayout {
      anchors.fill: parent
      anchors.margins: root.theme.panelPadding
      spacing: root.theme.gap
      Label { text: "Confirm AI request"; font.bold: true; color: root.theme.text }
      Label {
        textFormat: Text.PlainText
        text: root.preflight
          ? (root.preflight.locality === Protocol.AiLocality.Remote ? "Remote destination: " : "Local destination: ")
            + root.preflight.destination
          : ""
        color: root.preflight && root.preflight.locality === Protocol.AiLocality.Remote
          ? root.theme.urgent : root.theme.text
        wrapMode: Text.Wrap
      }
      ScrollView {
        Layout.fillWidth: true
        Layout.fillHeight: true
        TextArea {
          id: payloadText
          objectName: "aiPreflightPayload"
          readOnly: true
          textFormat: TextEdit.PlainText
          wrapMode: TextEdit.Wrap
          text: root.disclosureText
          Accessible.name: "Exact AI payload"
        }
      }
      RowLayout {
        Layout.alignment: Qt.AlignRight
        DeckButton {
          id: cancelButton
          theme: root.theme
          objectName: "aiPreflightCancel"
          text: "Cancel"
          onClicked: root.canceled()
        }
        DeckButton {
          id: sendButton
          theme: root.theme
          kind: Ui.ButtonVariant.Primary
          objectName: "aiPreflightSend"
          text: "Send"
          onClicked: root.accepted()
        }
      }
    }
  }
  onOpenedChanged: if (opened) Qt.callLater(function() { cancelButton.forceActiveFocus() })
}
