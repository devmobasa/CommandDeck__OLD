pragma ComponentBehavior: Bound
import QtQuick
import QtQuick.Layouts
import "QuicklinkValidation.js" as Validation
import "UiConstants.js" as Ui

ColumnLayout {
  id: root
  required property var theme
  required property var controller
  property string editingId: ""
  property int waitingTicket: 0
  property bool waiting: false
  readonly property var draft: ({ id: editingId, title: titleField.text,
    urlTemplate: urlField.text, alias: aliasField.text || null, icon: iconField.text || null })
  readonly property string validationError: Validation.validate(draft, controller ? controller.quicklinks : [])
  signal finished()
  spacing: Math.round(8 * theme.scale)
  onVisibleChanged: { if (!visible) detachSave() }

  function detachSave() {
    waiting = false
    waitingTicket = 0
  }

  function dismiss() {
    detachSave()
    finished()
  }

  function open(link) {
    editingId = link.id
    titleField.text = link.title || ""
    urlField.text = link.urlTemplate || ""
    aliasField.text = link.alias || ""
    iconField.text = link.icon || ""
    detachSave()
    controller.errorText = ""
    titleField.forceActiveFocus()
  }

  function save() {
    if (validationError || waiting) return
    waiting = true
    waitingTicket = controller.submit("save", { quicklink: draft })
  }

  Keys.onEscapePressed: event => { root.dismiss(); event.accepted = true }

  Text {
    objectName: "titleFieldLabel"
    text: "Title"
    color: root.theme.textMuted
    font.family: root.theme.fontFamily
    font.pixelSize: root.theme.fontBody
  }
  SettingsField {
    id: titleField
    objectName: "quicklinkTitle"
    theme: root.theme
    Layout.fillWidth: true
    placeholderText: "Title"
    Accessible.name: "Quicklink title"
    onAccepted: root.save()
  }
  Text {
    objectName: "urlFieldLabel"
    text: "URL template"
    color: root.theme.textMuted
    font.family: root.theme.fontFamily
    font.pixelSize: root.theme.fontBody
  }
  SettingsField {
    id: urlField
    objectName: "quicklinkUrl"
    theme: root.theme
    Layout.fillWidth: true
    placeholderText: "https://example.org/search?q={query}"
    Accessible.name: "Quicklink URL template"
    onAccepted: root.save()
  }
  Text {
    objectName: "aliasFieldLabel"
    text: "Alias (optional)"
    color: root.theme.textMuted
    font.family: root.theme.fontFamily
    font.pixelSize: root.theme.fontBody
  }
  SettingsField {
    id: aliasField
    objectName: "quicklinkAlias"
    theme: root.theme
    Layout.fillWidth: true
    placeholderText: "Alias (optional)"
    Accessible.name: "Quicklink alias"
    onAccepted: root.save()
  }
  Text {
    objectName: "iconFieldLabel"
    text: "Icon (optional)"
    color: root.theme.textMuted
    font.family: root.theme.fontFamily
    font.pixelSize: root.theme.fontBody
  }
  SettingsField {
    id: iconField
    objectName: "quicklinkIcon"
    theme: root.theme
    Layout.fillWidth: true
    placeholderText: "Icon (optional)"
    Accessible.name: "Quicklink icon"
    onAccepted: root.save()
  }
  Text {
    text: "Sample query"
    color: root.theme.textMuted
    font.family: root.theme.fontFamily
    font.pixelSize: root.theme.fontBody
  }
  SettingsField {
    id: sampleField
    objectName: "quicklinkSample"
    theme: root.theme
    Layout.fillWidth: true
    text: "hello world"
    Accessible.name: "Preview sample query"
  }
  Text {
    objectName: "quicklinkPreview"
    Layout.fillWidth: true
    textFormat: Text.PlainText
    text: "Preview: " + Validation.preview(urlField.text, sampleField.text)
    wrapMode: Text.WrapAnywhere
    color: root.theme.textMuted
    font.family: root.theme.fontFamily
    font.pixelSize: root.theme.fontBody
    Accessible.role: Accessible.StaticText
    Accessible.name: text
  }
  Text {
    objectName: "quicklinkValidation"
    Layout.fillWidth: true
    visible: text.length > 0
    textFormat: Text.PlainText
    text: root.validationError
    wrapMode: Text.Wrap
    color: root.theme.urgent
    font.family: root.theme.fontFamily
    font.pixelSize: root.theme.fontBody
    Accessible.role: Accessible.StaticText
    Accessible.name: text
  }
  RowLayout {
    DeckButton {
      objectName: "quicklinkSave"
      theme: root.theme
      text: "Save"
      kind: Ui.ButtonVariant.Primary
      enabled: root.validationError.length === 0 && !root.waiting
      Accessible.name: "Save quicklink"
      onClicked: root.save()
    }
    DeckButton {
      objectName: "quicklinkCancel"
      theme: root.theme
      text: "Cancel"
      Accessible.name: "Cancel quicklink edit"
      onClicked: root.dismiss()
    }
  }
  Connections {
    target: root.controller
    function onSaved(id, ticket) {
      if (root.visible && root.waiting && ticket === root.waitingTicket && id === root.editingId) root.dismiss()
    }
    function onFailed(ticket) { if (ticket === root.waitingTicket) root.waiting = false }
  }
}
