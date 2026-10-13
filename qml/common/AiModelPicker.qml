pragma ComponentBehavior: Bound
import QtQuick
import "ListKeys.js" as ListKeys
import QtQuick.Layouts

// Searchable chooser for models returned by an explicit provider test. Opening
// it focuses the search field at once; arrows move the highlight, Enter picks,
// Escape closes without choosing. A manual model ID stays available in the
// editor, so this list is a convenience, never the only path.
FocusScope {
  id: root

  required property var theme
  property bool opened: false
  property var models: []
  property string currentModelId: ""
  readonly property alias searchControl: searchField
  readonly property alias listControl: modelList
  readonly property var filteredModels: {
    const query = searchField.text.trim().toLowerCase()
    const values = root.models || []
    if (query.length === 0) return values
    return values.filter(value => String(value.id || "").toLowerCase().indexOf(query) >= 0
      || String(value.displayName || "").toLowerCase().indexOf(query) >= 0)
  }

  signal selected(string modelId)
  signal dismissed()

  visible: opened
  focus: opened
  Accessible.role: Accessible.Dialog
  Accessible.name: "Choose a model"

  onOpenedChanged: {
    if (!opened) return
    searchField.text = ""
    modelList.currentIndex = Math.max(0, root.indexOfModel(root.currentModelId))
    Qt.callLater(function() { searchField.forceActiveFocus() })
  }

  function indexOfModel(id) {
    const values = root.filteredModels
    for (let index = 0; index < values.length; index++) {
      if (String(values[index].id) === String(id)) return index
    }
    return -1
  }

  function chooseCurrent() {
    const values = root.filteredModels
    if (modelList.currentIndex < 0 || modelList.currentIndex >= values.length) return false
    root.selected(String(values[modelList.currentIndex].id))
    return true
  }

  function handleListKey(event, textInput) {
    return ListKeys.handleIndex(event, modelList.currentIndex, root.filteredModels.length,
      ListKeys.pageSize(modelList.height, Math.round(38 * root.theme.scale)), textInput,
      index => { modelList.currentIndex = index })
  }

  Keys.priority: Keys.BeforeItem
  Keys.onPressed: event => {
    if (root.handleListKey(event, false)) return

    if (event.key === Qt.Key_Escape) {
      root.dismissed()
      event.accepted = true
    } else if (event.key === Qt.Key_Return || event.key === Qt.Key_Enter) {
      root.chooseCurrent()
      event.accepted = true
    } else if (event.key === Qt.Key_Tab || event.key === Qt.Key_Backtab) {
      // The chooser is a two-part modal: search and list share the keyboard.
      searchField.forceActiveFocus()
      event.accepted = true
    }
  }

  Rectangle {
    anchors.fill: parent
    color: root.theme.scrim

    MouseArea {
      anchors.fill: parent
      onClicked: root.dismissed()
    }
  }

  Rectangle {
    width: Math.min(Math.round(520 * root.theme.scale), parent.width - Math.round(40 * root.theme.scale))
    height: Math.min(Math.round(420 * root.theme.scale), parent.height - Math.round(40 * root.theme.scale))
    anchors.centerIn: parent
    radius: root.theme.radius
    color: root.theme.popupBackground
    border.width: 1
    border.color: root.theme.border

    MouseArea {
      anchors.fill: parent
      acceptedButtons: Qt.NoButton
    }

    ColumnLayout {
      anchors.fill: parent
      anchors.margins: root.theme.panelPadding
      spacing: root.theme.gap

      Text {
        Layout.fillWidth: true
        text: "Choose a model"
        color: root.theme.text
        font.family: root.theme.fontFamily
        font.pixelSize: root.theme.fontHeading
        font.weight: Font.DemiBold
        Accessible.role: Accessible.Heading
        Accessible.name: text
      }

      SettingsField {
        id: searchField
        theme: root.theme
        Layout.fillWidth: true
        placeholderText: "Search models…"
        Accessible.name: "Search models"
        Accessible.description: "Filters the models the explicit test returned. Arrows move, Enter chooses, Escape closes."
        onTextChanged: modelList.currentIndex = root.filteredModels.length > 0 ? 0 : -1
        onAccepted: root.chooseCurrent()
        // Runs with the field's own Tab handling; the keys do not overlap.
        Keys.onPressed: event => root.handleListKey(event, true)
        Keys.onEscapePressed: root.dismissed()
      }

      ListView {
        id: modelList
        Layout.fillWidth: true
        Layout.fillHeight: true
        clip: true
        model: root.filteredModels
        boundsBehavior: Flickable.StopAtBounds
        highlightMoveDuration: 0
        keyNavigationEnabled: false
        Accessible.role: Accessible.List
        Accessible.name: "Models"
        delegate: Rectangle {
          id: row
          required property var modelData
          required property int index
          width: ListView.view ? ListView.view.width : 0
          height: Math.round(38 * root.theme.scale)
          radius: Math.max(3, root.theme.radiusSmall - 2)
          color: row.index === modelList.currentIndex ? root.theme.selectedBackground : "transparent"
          Accessible.role: Accessible.ListItem
          Accessible.name: String(row.modelData.displayName || row.modelData.id || "")

          Text {
            anchors.fill: parent
            anchors.leftMargin: Math.round(10 * root.theme.scale)
            anchors.rightMargin: anchors.leftMargin
            verticalAlignment: Text.AlignVCenter
            textFormat: Text.PlainText
            text: String(row.modelData.displayName || row.modelData.id || "")
              + (String(row.modelData.id) !== String(root.currentModelId) ? "" : "  ·  current")
            color: row.index === modelList.currentIndex ? root.theme.selectedText : root.theme.text
            elide: Text.ElideRight
            font.family: root.theme.fontFamily
            font.pixelSize: Math.round(root.theme.fontBody * 1.1)
          }

          MouseArea {
            anchors.fill: parent
            onClicked: {
              modelList.currentIndex = row.index
              root.chooseCurrent()
            }
          }
        }
      }

      Text {
        Layout.fillWidth: true
        visible: root.filteredModels.length === 0
        text: (root.models || []).length === 0
          ? "No models were returned. Enter a model ID manually in the editor."
          : "No models match the search."
        color: root.theme.textMuted
        wrapMode: Text.Wrap
        font.family: root.theme.fontFamily
        font.pixelSize: root.theme.fontBody
        Accessible.role: Accessible.StaticText
        Accessible.name: text
      }

      Text {
        Layout.fillWidth: true
        text: "↑/↓ Move · Enter Choose · Esc Close"
        color: root.theme.textMuted
        font.family: root.theme.fontFamily
        font.pixelSize: root.theme.fontCaption
        Accessible.role: Accessible.StaticText
        Accessible.name: text
      }
    }
  }
}
