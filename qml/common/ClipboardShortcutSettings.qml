import QtQuick
import QtQuick.Controls
import QtQuick.Layouts

ColumnLayout {
  id: root
  required property var theme
  property var preferences: null
  property bool busy: false
  property var savedSearches: []
  readonly property bool conflict: preferences && savedSearches.some(search =>
    String(search.shortcut || "").toUpperCase() === String(preferences.clipboardHistoryShortcut === undefined
      ? "CTRL+SHIFT+V" : preferences.clipboardHistoryShortcut).toUpperCase() && Boolean(search.shortcut))
  readonly property alias field: chordField
  readonly property alias applyControl: applyButton
  signal saved(var preferences)

  function save() {
    if (!preferences || busy) return
    const next = JSON.parse(JSON.stringify(preferences))
    next.clipboardHistoryShortcut = chordField.text.trim()
    saved(next)
  }

  Label {
    text: "Open Clipboard History"
    color: root.theme.text
    font.family: root.theme.fontFamily
    font.bold: true
  }
  Label {
    Layout.fillWidth: true
    text: "While CommandDeck is open. Use Ctrl+Shift+letter or Ctrl+Alt+letter; leave blank to disable. Default: Ctrl+Shift+V. Ctrl+Shift+C remains Copy file path."
    textFormat: Text.PlainText
    wrapMode: Text.Wrap
    color: root.theme.textMuted
  }
  RowLayout {
    Layout.fillWidth: true
    SettingsField {
      id: chordField
      theme: root.theme
      Layout.fillWidth: true
      enabled: !root.busy && root.preferences !== null
      text: root.preferences && root.preferences.clipboardHistoryShortcut !== undefined
        ? root.preferences.clipboardHistoryShortcut : "CTRL+SHIFT+V"
      placeholderText: "Disabled"
      Accessible.name: "Clipboard History shortcut"
      onAccepted: root.save()
    }
    DeckButton {
      id: applyButton
      theme: root.theme
      text: "Apply"
      enabled: !root.busy && root.preferences !== null
      onClicked: root.save()
    }
  }
  Label {
    Layout.fillWidth: true
    visible: root.conflict
    text: "A saved search already uses this chord. Clipboard's shortcut is inactive until you change or disable it."
    textFormat: Text.PlainText
    wrapMode: Text.Wrap
    color: root.theme.urgent
  }
}
