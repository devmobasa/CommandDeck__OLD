import QtQuick
import "UiConstants.js" as Ui
import QtQuick.Controls
import QtQuick.Layouts

Rectangle {
  id: root

  required property var theme
  property bool opened: false
  property var result: null
  property string errorText: ""
  property string localError: ""
  property var reservedValues: []
  property alias text: aliasInput.text
  readonly property alias inputControl: aliasInput
  readonly property bool focusTrapConfigured:
    aliasInput.KeyNavigation.tab !== null
      && aliasInput.KeyNavigation.backtab !== null
      && cancelButton.KeyNavigation.tab === saveButton
      && saveButton.KeyNavigation.tab === aliasInput
  signal saveRequested(string value)
  signal removeRequested()
  signal dismissed()

  visible: opened
  color: root.theme.scrim
  focus: visible
  Accessible.role: Accessible.Dialog
  Accessible.name: "Set alias"

  function openFor(target) {
    result = target
    localError = ""
    opened = true
  }

  function prepare() {
    localError = ""
    aliasInput.text = result && result.aliases && result.aliases.length > 0
      ? result.aliases[0]
      : ""
    Qt.callLater(function() {
      aliasInput.forceActiveFocus()
      aliasInput.selectAll()
    })
  }

  onOpenedChanged: {
    if (opened) prepare()
  }

  function locallyValid(value) {
    return validationError(value).length === 0
  }

  function validationError(value) {
    if (!/^[A-Za-z0-9][A-Za-z0-9._-]{0,31}$/.test(value)) {
      return "Use 1–32 letters, digits, dots, underscores, or hyphens."
    }
    if (reservedValues.map(candidate => String(candidate).toLowerCase())
        .indexOf(value.toLowerCase()) >= 0) {
      return "This alias is reserved by launcher syntax."
    }
    return ""
  }

  Keys.onEscapePressed: root.dismissed()

  MouseArea {
    anchors.fill: parent
    acceptedButtons: Qt.AllButtons
    hoverEnabled: true
  }

  Rectangle {
    width: Math.min(430, parent.width - 48)
    implicitHeight: content.implicitHeight + 40
    anchors.centerIn: parent
    radius: root.theme.radius
    color: root.theme.popupBackground
    border.width: 1
    border.color: root.theme.border

    ColumnLayout {
      id: content
      anchors.fill: parent
      anchors.margins: 20
      spacing: 12

      Text {
        Layout.fillWidth: true
        textFormat: Text.PlainText
        text: root.result ? "Alias for “" + root.result.title + "”" : "Set alias"
        color: root.theme.text
        font.family: root.theme.fontFamily
        font.pixelSize: root.theme.fontHeading
        wrapMode: Text.Wrap
      }

      Text {
        Layout.fillWidth: true
        text: "1–32 letters, digits, dots, underscores, or hyphens"
        color: root.theme.textMuted
        font.family: root.theme.fontFamily
        font.pixelSize: root.theme.fontSmall
        wrapMode: Text.Wrap
      }

      SettingsField {
        id: aliasInput
        Layout.fillWidth: true
        theme: root.theme
        placeholderText: "brave"
        Accessible.name: "Alias"
        Accessible.description: "1 to 32 letters, digits, dots, underscores, or hyphens"
        KeyNavigation.tab: removeAliasButton.visible ? removeAliasButton : cancelButton
        KeyNavigation.backtab: saveButton
        onTextChanged: root.localError = ""
        onAccepted: {
          const value = text.trim()
          if (!root.locallyValid(value)) {
            root.localError = root.validationError(value)
          } else {
            root.saveRequested(value)
          }
        }
      }

      Text {
        Layout.fillWidth: true
        visible: root.localError.length > 0 || root.errorText.length > 0
        textFormat: Text.PlainText
        text: root.localError.length > 0 ? root.localError : root.errorText
        color: root.theme.urgent
        font.family: root.theme.fontFamily
        font.pixelSize: root.theme.fontSmall
        wrapMode: Text.Wrap
      }

      RowLayout {
        Layout.fillWidth: true

        DeckButton {
          id: removeAliasButton
          theme: root.theme
          kind: Ui.ButtonVariant.Danger
          visible: root.result && root.result.aliases && root.result.aliases.length > 0
          text: "Remove alias"
          Accessible.name: "Remove alias"
          KeyNavigation.tab: cancelButton
          KeyNavigation.backtab: aliasInput
          onClicked: root.removeRequested()
        }

        Item { Layout.fillWidth: true }

        DeckButton {
          id: cancelButton
          theme: root.theme
          text: "Cancel"
          KeyNavigation.tab: saveButton
          KeyNavigation.backtab: removeAliasButton.visible ? removeAliasButton : aliasInput
          onClicked: root.dismissed()
        }

        DeckButton {
          id: saveButton
          theme: root.theme
          kind: Ui.ButtonVariant.Primary
          text: "Save"
          enabled: root.locallyValid(aliasInput.text.trim())
          KeyNavigation.tab: aliasInput
          KeyNavigation.backtab: cancelButton
          onClicked: root.saveRequested(aliasInput.text.trim())
        }
      }
    }
  }
}
