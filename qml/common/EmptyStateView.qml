pragma ComponentBehavior: Bound
import QtQuick
import "UiConstants.js" as Ui
import QtQuick.Controls
import QtQuick.Layouts

// A2 - teaching empty-state, with the D12 assistant entry pinned in.
//
// Shown in the content area when the query is empty and there is no home/recent
// content to display. It renders a compact "What you can do" grid built from the
// daemon filter descriptors plus a curated example each, a prominent "Ask the
// assistant" chip, a "Browse filters" affordance, and a footer pointing at F1
// and "?". Chips are keyboard-navigable; activating one inserts its token into
// the search field so the user is dropped into that filter.
Item {
  id: root

  required property var theme
  // The AI master switch hides the assistant route entirely.
  property bool assistantAvailable: true
  property var filterDescriptors: []

  signal tokenActivated(string token)
  signal assistantRequested()
  signal filtersRequested()

  readonly property var chipEntries: catalog.chipEntries(root.filterDescriptors, true)
  readonly property string footerText: "Press F1 for everything · type ? for filters"

  // Test and wiring hooks.
  readonly property int renderedChipCount: chipRepeater.count
  readonly property alias assistantControl: assistantChip
  readonly property alias filtersControl: filtersChip
  function chipAt(index) { return chipRepeater.itemAt(index) }
  function chipTokenAt(index) {
    return index >= 0 && index < chipEntries.length
      ? String(chipEntries[index].token) : ""
  }
  function activateChip(index) {
    if (index < 0 || index >= chipEntries.length) return
    root.tokenActivated(String(chipEntries[index].token))
  }

  FilterCatalog { id: catalog }

  Accessible.role: Accessible.Grouping
  Accessible.name: "What you can do"

  ScrollView {
    id: scroller
    anchors.fill: parent
    clip: true
    contentWidth: availableWidth

    ColumnLayout {
      width: scroller.availableWidth
      spacing: root.theme.gap

      Text {
        text: "What you can do"
        color: root.theme.text
        font.family: root.theme.fontFamily
        font.pixelSize: root.theme.fontSubtitle
        font.weight: Font.DemiBold
      }

      Text {
        Layout.fillWidth: true
        text: "Start typing, or jump into a filter."
        color: root.theme.textMuted
        font.family: root.theme.fontFamily
        font.pixelSize: root.theme.fontSmall
        wrapMode: Text.Wrap
      }

      // Pinned assistant entry (D12). Kept visually distinct from the filter
      // chips so people find the assistant without knowing the `ai:` prefix.
      Rectangle {
        id: assistantChip
        visible: root.assistantAvailable
        Layout.fillWidth: true
        Layout.topMargin: Math.round(4 * root.theme.scale)
        implicitHeight: Math.round(root.theme.inputHeight * 0.92)
        radius: root.theme.radiusSmall
        color: assistantChip.activeFocus
          ? root.theme.selectedBackground
          : root.theme.withAlpha(root.theme.accent, 0.12)
        border.width: 1
        border.color: assistantChip.activeFocus
          ? root.theme.selectedBorder
          : root.theme.withAlpha(root.theme.accent, 0.5)
        activeFocusOnTab: true

        Accessible.role: Accessible.Button
        Accessible.name: "Ask the assistant"
        Accessible.description: "Route your text to the Codex assistant"
        Accessible.onPressAction: assistantChip.activate()

        function activate() { root.assistantRequested() }

        Keys.onPressed: event => {
          if (event.key === Qt.Key_Return || event.key === Qt.Key_Enter
              || event.key === Qt.Key_Space) {
            assistantChip.activate()
            event.accepted = true
          }
        }

        RowLayout {
          anchors.fill: parent
          anchors.leftMargin: Math.round(12 * root.theme.scale)
          anchors.rightMargin: Math.round(12 * root.theme.scale)
          spacing: Math.round(10 * root.theme.scale)

          Text {
            text: "✦"
            color: root.theme.accent
            font.family: root.theme.fontFamily
            font.pixelSize: root.theme.fontBody
          }

          Text {
            Layout.fillWidth: true
            text: "Ask the assistant"
            color: root.theme.text
            font.family: root.theme.fontFamily
            font.pixelSize: root.theme.fontSmall
            font.weight: Font.DemiBold
          }

          Text {
            text: Ui.QueryPrefix.Ai
            color: root.theme.accent
            font.family: "monospace"
            font.pixelSize: root.theme.fontSmall
          }
        }

        MouseArea {
          anchors.fill: parent
          onClicked: assistantChip.activate()
        }
      }

      // Filter chips derived from the descriptors.
      Flow {
        Layout.fillWidth: true
        Layout.topMargin: Math.round(4 * root.theme.scale)
        spacing: Math.max(6, Math.round(8 * root.theme.scale))

        Repeater {
          id: chipRepeater
          model: root.chipEntries

          delegate: Rectangle {
            id: chip

            required property int index
            required property var modelData

            implicitWidth: Math.min(
              chipContent.implicitWidth + Math.round(24 * root.theme.scale),
              scroller.availableWidth)
            implicitHeight: Math.round(root.theme.inputHeight * 0.88)
            radius: root.theme.radiusSmall
            color: chip.activeFocus ? root.theme.selectedBackground : root.theme.surface
            border.width: chip.activeFocus ? 1 : 0
            border.color: root.theme.selectedBorder
            activeFocusOnTab: true

            Accessible.role: Accessible.Button
            Accessible.name: String(chip.modelData.label) + " filter, "
              + String(chip.modelData.token)
            Accessible.description: chip.modelData.hasExample
              ? "Example: " + String(chip.modelData.example) : ""
            Accessible.onPressAction: chip.activate()

            function activate() { root.tokenActivated(String(chip.modelData.token)) }

            Keys.onPressed: event => {
              if (event.key === Qt.Key_Return || event.key === Qt.Key_Enter
                  || event.key === Qt.Key_Space) {
                chip.activate()
                event.accepted = true
              }
            }

            ColumnLayout {
              id: chipContent
              anchors.left: parent.left
              anchors.right: parent.right
              anchors.verticalCenter: parent.verticalCenter
              anchors.leftMargin: Math.round(12 * root.theme.scale)
              anchors.rightMargin: Math.round(12 * root.theme.scale)
              spacing: 1

              Text {
                textFormat: Text.PlainText
                text: String(chip.modelData.token) + "  "
                  + String(chip.modelData.label)
                color: root.theme.text
                font.family: root.theme.fontFamily
                font.pixelSize: root.theme.fontSmall
                font.weight: Font.DemiBold
                elide: Text.ElideRight
                Layout.fillWidth: true
              }

              Text {
                visible: chip.modelData.hasExample === true
                textFormat: Text.PlainText
                text: String(chip.modelData.example)
                color: root.theme.textMuted
                font.family: "monospace"
                font.pixelSize: root.theme.fontCaption
                elide: Text.ElideRight
                Layout.fillWidth: true
              }
            }

            MouseArea {
              anchors.fill: parent
              onClicked: chip.activate()
            }
          }
        }
      }

      // "Browse filters" affordance -> opens the "?" palette.
      Rectangle {
        id: filtersChip
        Layout.topMargin: Math.round(2 * root.theme.scale)
        implicitWidth: filtersLabel.implicitWidth + Math.round(24 * root.theme.scale)
        implicitHeight: Math.round(root.theme.inputHeight * 0.7)
        radius: root.theme.radiusSmall
        color: filtersChip.activeFocus ? root.theme.selectedBackground : "transparent"
        border.width: 1
        border.color: filtersChip.activeFocus
          ? root.theme.selectedBorder : root.theme.separator
        activeFocusOnTab: true

        Accessible.role: Accessible.Button
        Accessible.name: "Browse all filters"
        Accessible.onPressAction: filtersChip.activate()

        function activate() { root.filtersRequested() }

        Keys.onPressed: event => {
          if (event.key === Qt.Key_Return || event.key === Qt.Key_Enter
              || event.key === Qt.Key_Space) {
            filtersChip.activate()
            event.accepted = true
          }
        }

        Text {
          id: filtersLabel
          anchors.centerIn: parent
          text: "Browse all filters  ?"
          color: root.theme.textMuted
          font.family: root.theme.fontFamily
          font.pixelSize: root.theme.fontSmall
        }

        MouseArea {
          anchors.fill: parent
          onClicked: filtersChip.activate()
        }
      }

      Rectangle {
        Layout.fillWidth: true
        Layout.topMargin: Math.round(4 * root.theme.scale)
        Layout.preferredHeight: 1
        color: root.theme.separator
      }

      Text {
        id: footerLabel
        Layout.fillWidth: true
        text: root.footerText
        color: root.theme.textFaint
        font.family: root.theme.fontFamily
        font.pixelSize: root.theme.fontSmall
        wrapMode: Text.Wrap
      }
    }
  }
}
