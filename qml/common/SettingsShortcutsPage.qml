pragma ComponentBehavior: Bound
import QtQuick
import "Protocol.js" as Protocol
import QtQuick.Controls
import QtQuick.Layouts

ScrollView {
  required property var host
  readonly property alias part_shortcutsCard: shortcutsCard
  readonly property alias part_shortcutRepeater: shortcutRepeater

  id: shortcutsScroll
  clip: true
  contentWidth: availableWidth

  ColumnLayout {
    width: shortcutsScroll.availableWidth
    spacing: Math.round(14 * shortcutsScroll.host.theme.scale)

    SettingsCard {
      id: shortcutsCard
      searchQuery: shortcutsScroll.host.part_settingsSearch.text
      searchTerms: "shortcuts keyboard hotkeys bindings conflicts clipboard history clip paste"
      theme: shortcutsScroll.host.theme
      title: "Shortcuts"
      description: "Configure in-app shortcuts here. Desktop-wide Hyprland bindings are shown below; change those in your Hyprland configuration."

      ClipboardShortcutSettings {
        theme: shortcutsScroll.host.theme
        savedSearches: shortcutsScroll.host.savedSearches || []
        Layout.fillWidth: true
        preferences: shortcutsScroll.host.snapshot ? shortcutsScroll.host.snapshot.preferences : null
        busy: shortcutsScroll.host.loading || shortcutsScroll.host.snapshotMutationBusy
        onSaved: preferences => shortcutsScroll.host.preferencesChanged(preferences)
      }

      Repeater {
        id: shortcutRepeater
        model: shortcutsScroll.host.snapshot ? shortcutsScroll.host.snapshot.shortcuts || [] : []

        Item {
          id: shortcutDelegate
          required property var modelData
          required property int index
          readonly property string statusText: shortcutsScroll.host.shortcutStatus(modelData)
          Layout.fillWidth: true
          implicitHeight: Math.max(shortcutLabel.implicitHeight, shortcutValue.implicitHeight) + Math.round(20 * shortcutsScroll.host.theme.scale)
          Accessible.role: Accessible.ListItem
          Accessible.name: modelData.label + ": " + statusText
          Accessible.description: modelData.detail || ""

          Rectangle {
            anchors.left: parent.left
            anchors.right: parent.right
            anchors.bottom: parent.bottom
            height: 1
            visible: shortcutDelegate.index < (shortcutsScroll.host.snapshot ? (shortcutsScroll.host.snapshot.shortcuts || []).length : 0) - 1
            color: shortcutsScroll.host.theme.separator
          }

          RowLayout {
            anchors.fill: parent
            spacing: Math.round(16 * shortcutsScroll.host.theme.scale)

            Text {
              id: shortcutLabel
              Layout.fillWidth: true
              textFormat: Text.PlainText
              text: shortcutDelegate.modelData.label
              color: shortcutsScroll.host.theme.text
              wrapMode: Text.Wrap
              font.family: shortcutsScroll.host.theme.fontFamily
              font.pixelSize: Math.round(shortcutsScroll.host.theme.fontBody * 1.1)
              font.weight: Font.Medium
            }

            Text {
              id: shortcutValue
              textFormat: Text.PlainText
              text: shortcutDelegate.statusText
              Layout.maximumWidth: Math.round(380 * shortcutsScroll.host.theme.scale)
              horizontalAlignment: Text.AlignRight
              wrapMode: Text.Wrap
              color: shortcutDelegate.modelData.status === Protocol.ShortcutStatuses.Configured ? shortcutsScroll.host.theme.text : shortcutsScroll.host.theme.textMuted
              font.family: shortcutDelegate.modelData.status === Protocol.ShortcutStatuses.Configured ? "monospace" : shortcutsScroll.host.theme.fontFamily
              font.pixelSize: shortcutsScroll.host.theme.fontBody
              Accessible.role: Accessible.StaticText
              Accessible.name: shortcutDelegate.modelData.label + ": " + shortcutDelegate.statusText
              Accessible.description: shortcutDelegate.modelData.detail || ""
            }
          }
        }
      }
    }
  }
}
