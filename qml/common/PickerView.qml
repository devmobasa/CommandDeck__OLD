pragma ComponentBehavior: Bound
import QtQuick
import "ListKeys.js" as ListKeys
import QtQuick.Controls
import QtQuick.Layouts

Rectangle {
  id: root
  required property var theme
  property var session: ({ prompt: "Choose an item", choices: [] })
  readonly property alias inputControl: query
  property int selectedIndex: 0
  readonly property var filtered: (session.choices || []).map((choice, index) => ({ label: choice.label, index: index }))
    .filter(choice => choice.label.toLocaleLowerCase().indexOf(query.text.toLocaleLowerCase()) >= 0)
  signal chosen(int index)
  signal canceled()
  color: theme.popupBackground
  radius: theme.radius
  border.color: theme.border
  onFilteredChanged: selectedIndex = 0
  onSelectedIndexChanged: choices.positionViewAtIndex(selectedIndex, ListView.Contain)

  function accept() {
    if (selectedIndex >= 0 && selectedIndex < filtered.length) chosen(filtered[selectedIndex].index)
  }
  function handleKey(event) {
    if (ListKeys.handleIndex(event, selectedIndex, filtered.length, ListKeys.pageSize(choices.height, 40 + choices.spacing),
        true, index => { selectedIndex = index })) return

    if (event.key === Qt.Key_Escape) canceled()
    else if (event.key === Qt.Key_Return || event.key === Qt.Key_Enter) accept()
    else return
    event.accepted = true
  }
  Component.onCompleted: Qt.callLater(function() { query.forceActiveFocus() })

  ColumnLayout {
    anchors.fill: parent
    anchors.margins: 18
    spacing: 12
    Text {
      Layout.fillWidth: true
      text: root.session.prompt || "Choose an item"
      textFormat: Text.PlainText
      color: root.theme.text
      font.pixelSize: root.theme.fontHeading
      elide: Text.ElideRight
    }
    TextField {
      id: query
      Layout.fillWidth: true
      placeholderText: "Filter choices…"
      Accessible.name: "Filter choices"
      Keys.priority: Keys.BeforeItem
      Keys.onPressed: event => root.handleKey(event)
      KeyNavigation.tab: cancelButton
      KeyNavigation.backtab: cancelButton
    }
    ListView {
      id: choices
      Layout.fillWidth: true
      Layout.fillHeight: true
      clip: true
      model: root.filtered
      spacing: 3
      ScrollBar.vertical: ScrollBar { }
      delegate: Rectangle {
        required property var modelData
        required property int index
        width: choices.width
        height: 40
        radius: 6
        color: index === root.selectedIndex ? root.theme.selectedBackground : "transparent"
        Accessible.role: Accessible.ListItem
        Accessible.name: modelData.label
        Accessible.selected: index === root.selectedIndex
        Text {
          anchors.fill: parent
          anchors.margins: 8
          text: parent.modelData.label
          textFormat: Text.PlainText
          color: root.theme.text
          elide: Text.ElideRight
          verticalAlignment: Text.AlignVCenter
        }
        MouseArea {
          anchors.fill: parent
          onClicked: root.chosen(parent.modelData.index)
        }
      }
    }
    RowLayout {
      Layout.fillWidth: true
      Text {
        text: root.filtered.length ? root.filtered.length + (root.filtered.length === 1 ? " choice · Enter to select" : " choices · Enter to select") : "No matching choices"
        color: root.theme.textMuted
      }
      Item { Layout.fillWidth: true }
      DeckButton {
        id: cancelButton
        theme: root.theme
        text: "Cancel"
        onClicked: root.canceled()
        Keys.onEscapePressed: root.canceled()
        KeyNavigation.tab: query
        KeyNavigation.backtab: query
      }
    }
  }
}
