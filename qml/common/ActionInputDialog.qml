pragma ComponentBehavior: Bound
import QtQuick
import "UiConstants.js" as Ui
import QtQuick.Controls
import QtQuick.Layouts

Rectangle {
  id: root
  required property var theme
  property bool opened: false
  property var action: null
  property string errorText: ""
  readonly property alias submitControl: submitButton
  readonly property alias cancelControl: cancelButton
  signal submitted(var values)
  signal dismissed()
  visible: opened
  color: theme.scrim
  focus: visible
  Accessible.role: Accessible.Dialog
  Accessible.name: action ? action.title : "Action inputs"

  function rowAt(index): var { return fields.itemAt(index) }

  function submit() {
    const values = {}
    for (let i = 0; i < fields.count; i++) {
      const row = root.rowAt(i)
      if (!row.input.text.length) {
        errorText = "Enter a value for " + row.modelData.name
        row.input.forceActiveFocus()
        return
      }
      values[row.modelData.name] = row.input.text
    }
    submitted(values)
  }

  onOpenedChanged: {
    errorText = ""
    if (opened) Qt.callLater(function() {
      // The dialog can be torn down before the deferred call runs.
      if (root && root.opened && fields && fields.count) root.rowAt(0).input.forceActiveFocus()
    })
  }
  Keys.onEscapePressed: dismissed()
  MouseArea { anchors.fill: parent; acceptedButtons: Qt.AllButtons }

  Rectangle {
    anchors.centerIn: parent
    width: Math.min(480, parent.width - 32)
    height: Math.min(content.implicitHeight + 40, parent.height - 40)
    radius: root.theme.radius
    color: root.theme.popupBackground
    border.color: root.theme.border
    ScrollView {
      anchors.fill: parent
      anchors.margins: 20
      contentWidth: availableWidth
      ColumnLayout {
        id: content
        width: parent.width
        spacing: 12
        Text {
          Layout.fillWidth: true
          textFormat: Text.PlainText
          text: root.action ? root.action.title : "Action inputs"
          color: root.theme.text
          font.family: root.theme.fontFamily
          font.pixelSize: root.theme.fontHeading
          wrapMode: Text.Wrap
        }
        Repeater {
          id: fields
          model: root.opened && root.action ? root.action.inputs || [] : []
          delegate: ColumnLayout {
            id: row
            required property var modelData
            required property int index
            property alias input: inputField
            Layout.fillWidth: true
            Text {
              textFormat: Text.PlainText
              text: row.modelData.name
              color: root.theme.text
              font.family: root.theme.fontFamily
              font.pixelSize: root.theme.fontBody
            }
            SettingsField {
              id: inputField
              Layout.fillWidth: true
              theme: root.theme
              text: row.modelData.defaultValue || ""
              maximumLength: 8192
              Accessible.name: row.modelData.name
              KeyNavigation.tab: row.index + 1 < fields.count ? root.rowAt(row.index + 1).input : cancelButton
              KeyNavigation.backtab: row.index > 0 ? root.rowAt(row.index - 1).input : submitButton
              onAccepted: root.submit()
            }
          }
        }
        Text {
          Layout.fillWidth: true
          visible: root.errorText.length > 0
          textFormat: Text.PlainText
          text: root.errorText
          color: root.theme.urgent
          font.family: root.theme.fontFamily
          font.pixelSize: root.theme.fontSmall
          wrapMode: Text.Wrap
        }
        RowLayout {
          Layout.fillWidth: true
          Item { Layout.fillWidth: true }
          DeckButton {
            id: cancelButton
            theme: root.theme
            text: "Cancel"
            KeyNavigation.tab: submitButton
            KeyNavigation.backtab: fields.count ? root.rowAt(fields.count - 1).input : submitButton
            onClicked: root.dismissed()
          }
          DeckButton {
            id: submitButton
            theme: root.theme
            kind: Ui.ButtonVariant.Primary
            text: root.action ? root.action.title : "Continue"
            KeyNavigation.tab: fields.count ? root.rowAt(0).input : cancelButton
            KeyNavigation.backtab: cancelButton
            onClicked: root.submit()
          }
        }
      }
    }
  }
}
