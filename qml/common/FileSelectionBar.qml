pragma ComponentBehavior: Bound
import QtQuick
import QtQuick.Layouts

ColumnLayout {
  id: root
  required property var theme
  property var entries: []
  property Item previousTabStop: null
  property Item nextTabStop: null
  readonly property alias firstControl: copyButton
  readonly property var lastControl: entries.length ? chips.itemAt(entries.length - 1) : clearButton
  signal copyRequested()
  signal openRequested()
  signal clearRequested()
  signal removeRequested(var entry)
  spacing: 4

  RowLayout {
    Layout.fillWidth: true
    Text {
      text: root.entries.length + " selected"
      color: root.theme.textMuted
      font.pixelSize: root.theme.fontSmall
    }
    Item { Layout.fillWidth: true }
    DeckButton { id: copyButton; theme: root.theme; text: "Copy paths"; KeyNavigation.tab: openButton; KeyNavigation.backtab: root.previousTabStop; onClicked: root.copyRequested() }
    DeckButton { id: openButton; theme: root.theme; text: "Open all"; KeyNavigation.tab: clearButton; KeyNavigation.backtab: copyButton; onClicked: root.openRequested() }
    DeckButton { id: clearButton; theme: root.theme; text: "Clear"; KeyNavigation.tab: root.entries.length ? chips.itemAt(0) : root.nextTabStop; KeyNavigation.backtab: openButton; onClicked: root.clearRequested() }
  }
  Flickable {
    Layout.fillWidth: true
    Layout.preferredHeight: selectedPaths.implicitHeight
    contentWidth: selectedPaths.implicitWidth
    contentHeight: height
    clip: true
    Row {
      id: selectedPaths
      spacing: 6
      Repeater {
        id: chips
        model: root.entries
        delegate: LiteralButton {
          id: pathChip
          required property var modelData
          required property int index
          KeyNavigation.tab: index + 1 < root.entries.length ? chips.itemAt(index + 1) : root.nextTabStop
          KeyNavigation.backtab: index > 0 ? chips.itemAt(index - 1) : clearButton
          text: modelData.title + " ×"
          Accessible.name: "Remove " + modelData.path
          ThemedToolTip {
            theme: root.theme
            visible: pathChip.hovered
            text: pathChip.modelData.path
          }
          onClicked: root.removeRequested(modelData)
        }
      }
    }
  }
}
