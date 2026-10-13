pragma ComponentBehavior: Bound
import QtQuick
import "ListKeys.js" as ListKeys
import QtQuick.Layouts

// A3 - the "?" filter palette.
//
// Typing "?" as the whole query (or the empty-state "Browse filters"
// affordance) opens this. It lists every filter prefix with a runnable example,
// derived from the daemon descriptors so it cannot drift from the registry.
// ↑↓ move, Enter inserts the highlighted token and closes, Esc closes. It traps
// arrow/enter/esc while open; when closed it takes no focus, so the launcher's
// own Esc-to-close is untouched.
Rectangle {
  id: root

  required property var theme
  property bool opened: false
  property var filterDescriptors: []
  property Item returnFocusTarget: null
  property bool focusReturnAllowed: true
  property bool includeCalculator: true

  signal tokenInserted(string token)
  signal dismissed()

  readonly property var entries: catalog.paletteEntries(
    root.filterDescriptors, root.includeCalculator)
  property int selectedIndex: 0

  // Test and wiring hooks.
  readonly property int renderedCount: root.entries.length
  function entryTokenAt(index) {
    return index >= 0 && index < entries.length ? String(entries[index].token) : ""
  }
  function selectedToken() {
    return root.entryTokenAt(root.selectedIndex)
  }
  function moveSelection(delta) {
    if (entries.length === 0) return
    root.select(Math.max(0, Math.min(entries.length - 1, root.selectedIndex + delta)))
  }
  function select(index) {
    root.selectedIndex = index
    paletteList.positionViewAtIndex(index, ListView.Contain)
  }
  function activateSelected() {
    const token = root.selectedToken()
    if (token.length === 0) return
    root.tokenInserted(token)
  }

  FilterCatalog { id: catalog }

  onOpenedChanged: {
    if (opened) {
      root.selectedIndex = 0
      Qt.callLater(function() { root.forceActiveFocus() })
    } else if (root.returnFocusTarget) {
      Qt.callLater(function() {
        if (!root.focusReturnAllowed) return
        const target = root.returnFocusTarget
        if (target && target.visible && target.enabled) target.forceActiveFocus()
      })
    }
  }
  onEntriesChanged: {
    if (root.selectedIndex > entries.length - 1) {
      root.selectedIndex = Math.max(0, entries.length - 1)
    }
  }

  visible: root.opened
  color: root.theme.scrim
  focus: visible
  activeFocusOnTab: true
  KeyNavigation.tab: root
  KeyNavigation.backtab: root
  Accessible.role: Accessible.Dialog
  Accessible.name: "Filter palette"
  Accessible.description: "Browse every search filter. Arrow keys move, Enter inserts, Escape closes."

  Keys.onPressed: event => {
    if (ListKeys.handleIndex(event, root.selectedIndex, root.entries.length, 6, false,
        index => root.select(index))) return

    if (event.key === Qt.Key_Escape) {
      root.dismissed()
      event.accepted = true
    } else if (event.key === Qt.Key_Tab) {
      // The palette is modal and holds no other control, so Tab steps too.
      root.moveSelection(1)
      event.accepted = true
    } else if (event.key === Qt.Key_Backtab) {
      root.moveSelection(-1)
      event.accepted = true
    } else if (event.key === Qt.Key_Return || event.key === Qt.Key_Enter) {
      root.activateSelected()
      event.accepted = true
    }
  }

  // Swallow scrim clicks so nothing behind the palette reacts.
  MouseArea {
    anchors.fill: parent
    acceptedButtons: Qt.AllButtons
    onClicked: root.dismissed()
  }

  Rectangle {
    width: Math.min(Math.round(560 * root.theme.scale), parent.width - 48)
    height: Math.min(Math.round(520 * root.theme.scale), parent.height - 48)
    anchors.centerIn: parent
    radius: root.theme.radius
    color: root.theme.popupBackground
    border.width: 1
    border.color: root.theme.border

    MouseArea {
      anchors.fill: parent
      acceptedButtons: Qt.AllButtons
    }

    ColumnLayout {
      anchors.fill: parent
      anchors.margins: root.theme.panelPadding
      spacing: root.theme.gap

      RowLayout {
        Layout.fillWidth: true

        Text {
          Layout.fillWidth: true
          text: "Filters"
          color: root.theme.text
          font.family: root.theme.fontFamily
          font.pixelSize: root.theme.fontHeading
          font.weight: Font.DemiBold
        }

        // Label-then-key legend, matching the footer hint bar.
        Repeater {
          model: [{ text: "Use", key: "↵" }, { text: "Close", key: "Esc" }]

          delegate: Row {
            id: legendItem
            required property var modelData
            Layout.leftMargin: Math.round(6 * root.theme.scale)
            spacing: Math.round(6 * root.theme.scale)

            Text {
              anchors.verticalCenter: parent.verticalCenter
              text: legendItem.modelData.text
              color: root.theme.textMuted
              font.family: root.theme.fontFamily
              font.pixelSize: root.theme.fontSmall
            }

            KeyCap {
              anchors.verticalCenter: parent.verticalCenter
              theme: root.theme
              label: legendItem.modelData.key
            }
          }
        }
      }

      Rectangle {
        Layout.fillWidth: true
        Layout.preferredHeight: 1
        color: root.theme.separator
      }

      ListView {
        id: paletteList
        Layout.fillWidth: true
        Layout.fillHeight: true
        clip: true
        spacing: Math.max(2, Math.round(3 * root.theme.scale))
        model: root.entries
        interactive: true
        boundsBehavior: Flickable.StopAtBounds

        delegate: Rectangle {
          id: entryRow

          required property int index
          required property var modelData

          width: paletteList.width
          height: Math.round(root.theme.inputHeight * 0.92)
          radius: root.theme.radiusSmall
          readonly property bool active: entryRow.index === root.selectedIndex
          // The first column already shows the token, so only a real example
          // earns the third column.
          readonly property string exampleText: entryRow.modelData.hasExample
            ? String(entryRow.modelData.example) : ""
          color: entryRow.active ? root.theme.selectedBackground : "transparent"
          border.width: entryRow.active ? 1 : 0
          border.color: root.theme.selectedBorder

          Accessible.role: Accessible.ListItem
          Accessible.name: String(entryRow.modelData.label)
            + " filter, " + String(entryRow.modelData.token)
            + (entryRow.exampleText.length > 0 ? ", example " + entryRow.exampleText : "")
          Accessible.selected: entryRow.active

          RowLayout {
            anchors.fill: parent
            anchors.leftMargin: Math.round(12 * root.theme.scale)
            anchors.rightMargin: Math.round(12 * root.theme.scale)
            spacing: Math.round(10 * root.theme.scale)

            Text {
              Layout.preferredWidth: Math.round(96 * root.theme.scale)
              textFormat: Text.PlainText
              text: String(entryRow.modelData.token)
              color: root.theme.accent
              font.family: "monospace"
              font.pixelSize: root.theme.fontBody
              elide: Text.ElideRight
            }

            Text {
              textFormat: Text.PlainText
              text: String(entryRow.modelData.label)
              color: entryRow.active ? root.theme.selectedText : root.theme.text
              font.family: root.theme.fontFamily
              font.pixelSize: root.theme.fontSmall
            }

            Text {
              Layout.fillWidth: true
              horizontalAlignment: Text.AlignRight
              textFormat: Text.PlainText
              text: entryRow.exampleText
              color: root.theme.textMuted
              font.family: "monospace"
              font.pixelSize: root.theme.fontSmall
              elide: Text.ElideRight
            }
          }

          MouseArea {
            anchors.fill: parent
            hoverEnabled: true
            onEntered: root.selectedIndex = entryRow.index
            onClicked: {
              root.selectedIndex = entryRow.index
              root.activateSelected()
            }
          }
        }
      }

      Text {
        Layout.fillWidth: true
        visible: root.entries.length === 0
        text: "Filters load when the daemon connects."
        color: root.theme.textFaint
        font.family: root.theme.fontFamily
        font.pixelSize: root.theme.fontSmall
        wrapMode: Text.Wrap
      }
    }
  }
}
