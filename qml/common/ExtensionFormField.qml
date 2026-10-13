pragma ComponentBehavior: Bound
import QtQuick
import "Protocol.js" as Protocol
import QtQuick.Layouts

ColumnLayout {
  id: root
  required property var theme
  required property var field
  property var value
  property string error: ""
  readonly property Item editor: field.kind === Protocol.ExtensionFormFieldKinds.Checkbox ? checkbox : field.kind === Protocol.ExtensionFormFieldKinds.Choice ? choice : field.kind === Protocol.ExtensionFormFieldKinds.Multiline ? multiline : input
  signal edited(var value)
  signal focused()
  signal traverse(bool backwards)
  signal cancel()
  spacing: 4
  Text {
    visible: root.field.kind !== Protocol.ExtensionFormFieldKinds.Checkbox
    text: root.field.label + (root.field.required ? " *" : "")
    color: root.theme.text
    textFormat: Text.PlainText
    font.family: root.theme.fontFamily
    font.pixelSize: Math.round(root.theme.fontBody * 1.1)
  }
  SettingsField {
    id: input
    theme: root.theme
    Layout.fillWidth: true
    visible: root.field.kind === Protocol.ExtensionFormFieldKinds.Text
    text: String(root.value ?? "")
    maximumLength: root.field.maxLength || 8192
    Accessible.name: root.field.label
    onTextEdited: root.edited(text)
    onActiveFocusChanged: if (activeFocus) root.focused()
    Keys.onTabPressed: root.traverse(false)
    Keys.onBacktabPressed: root.traverse(true)
    Keys.onEscapePressed: root.cancel()
  }
  SettingsTextArea {
    id: multiline
    theme: root.theme
    Layout.fillWidth: true
    Layout.preferredHeight: 130
    visible: root.field.kind === Protocol.ExtensionFormFieldKinds.Multiline
    text: String(root.value ?? "")
    wrapMode: TextEdit.Wrap
    selectByMouse: true
    Accessible.name: root.field.label
    onTextChanged: if (activeFocus) root.edited(text)
    onActiveFocusChanged: if (activeFocus) root.focused()
    Keys.priority: Keys.BeforeItem
    Keys.onTabPressed: root.traverse(false)
    Keys.onBacktabPressed: root.traverse(true)
    Keys.onEscapePressed: root.cancel()
  }
  SettingsChoice {
    id: choice
    theme: root.theme
    Layout.fillWidth: true
    visible: root.field.kind === Protocol.ExtensionFormFieldKinds.Choice
    options: (root.field.choices || []).map(item => ({ id: item.value, title: item.label }))
    showCurrentDescription: false
    currentIndex: optionIndex(root.value)
    Accessible.name: root.field.label
    onActivated: if (currentOption) root.edited(currentOption.id)
    onActiveFocusChanged: if (activeFocus) root.focused()
    Keys.onTabPressed: root.traverse(false)
    Keys.onBacktabPressed: root.traverse(true)
    Keys.onEscapePressed: { if (popup.visible) popup.close(); else root.cancel() }
  }
  SettingsToggle {
    id: checkbox
    theme: root.theme
    Layout.fillWidth: true
    visible: root.field.kind === Protocol.ExtensionFormFieldKinds.Checkbox
    checked: root.value === true
    text: root.field.label
    Accessible.name: root.field.label
    onToggled: root.edited(checked)
    onActiveFocusChanged: if (activeFocus) root.focused()
    Keys.onTabPressed: root.traverse(false)
    Keys.onBacktabPressed: root.traverse(true)
    Keys.onEscapePressed: root.cancel()
    background: Rectangle {
      radius: Math.max(4, root.theme.radiusSmall)
      color: checkbox.activeFocus ? checkbox.tint(root.theme.accent, 0.1) : "transparent"
      border.width: checkbox.activeFocus ? 2 : 0
      border.color: root.theme.focusRing
    }
  }
  Text {
    Layout.fillWidth: true
    visible: root.error.length > 0
    text: root.error
    textFormat: Text.PlainText
    wrapMode: Text.Wrap
    color: root.theme.urgent
    font.family: root.theme.fontFamily
    font.pixelSize: root.theme.fontBody
    Accessible.role: Accessible.AlertMessage
  }
}
