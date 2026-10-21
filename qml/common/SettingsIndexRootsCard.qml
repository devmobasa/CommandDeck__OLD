pragma ComponentBehavior: Bound
import QtQuick
import QtQuick.Layouts
import "UiConstants.js" as Ui
import "Protocol.js" as Protocol

SettingsCard {
  id: card
  required property var host
  readonly property var roots: card.host.indexRoots || []
  readonly property alias pathControl: pathField
  readonly property alias addControl: addButton
  readonly property alias progressControl: reindexStatus
  readonly property alias errorControl: errorText
  readonly property int renderedCount: rows.count
  readonly property bool shouldLoad: card.host.visible && card.host.currentPage === Ui.SettingsPage.Search
  property string pendingFocusId: ""
  function rowAt(index) { return rows.itemAt(index) as RootRow }
  function actionControlAt(index) {
    const row = rowAt(index)
    return row ? row.actionControl : null
  }
  function focusPath() { pathField.forceActiveFocus() }
  function restoreFocus() {
    if (!pendingFocusId || !shouldLoad) return
    const id = pendingFocusId
    pendingFocusId = ""
    for (let index = 0; index < rows.count; index++) {
      const row = rowAt(index)
      if (row.modelData.id === id) {
        row.actionControl.forceActiveFocus()
        return
      }
    }
    focusPath()
  }
  function add() {
    card.host.indexRootChangeRequested(Protocol.Operations.IndexAdd, pathField.text, "")
  }
  onRootsChanged: Qt.callLater(restoreFocus)
  onShouldLoadChanged: {
    pendingFocusId = ""
    if (shouldLoad) card.host.indexRootsRequested()
  }
  Component.onCompleted: if (shouldLoad) card.host.indexRootsRequested()

  Connections {
    target: card.Window.window
    function onActiveFocusItemChanged() {
      const focused = card.Window.window.activeFocusItem
      // Delegate destruction falls back to the window. A new tab stop means
      // the user or another surface has chosen where typing should go.
      if (focused && focused.activeFocusOnTab) card.pendingFocusId = ""
    }
  }

  Timer {
    interval: 1000
    repeat: true
    running: card.shouldLoad && card.visible && !card.host.indexRootsBusy
      && (card.host.indexReindexPending || card.roots.some(root => (root.status || "").indexOf("Indexing") === 0 || root.status === "Retrying indexing"))
    onTriggered: card.host.indexRootsRequested()
  }

  theme: card.host.theme
  searchQuery: card.host.part_settingsSearch.text
  searchTerms: "search indexed folders roots path add remove disable enable configuration missing folder"
  title: "Indexed folders"
  description: "Add folders or disable configuration folders. Changes apply immediately. Privacy exclusions still apply."

  RowLayout {
    Layout.fillWidth: true
    SettingsField {
      id: pathField
      theme: card.theme
      Layout.fillWidth: true
      placeholderText: "Folder path, for example ~/Documents"
      Accessible.name: "Add folder path"
      Accessible.description: "Enter a folder path, then press Enter to add it to the index."
      onAccepted: card.add()
      onTextEdited: { card.host.indexRootPathEdited(); forceActiveFocus() }
    }
    DeckButton {
      id: addButton
      theme: card.theme
      text: "Add folder"
      focusPolicy: Qt.TabFocus
      Accessible.name: "Add indexed folder"
      onClicked: { card.add(); card.focusPath() }
    }
  }
  Text {
    id: errorText
    Layout.fillWidth: true
    visible: text.length > 0
    text: card.host.indexRootsError
    textFormat: Text.PlainText
    wrapMode: Text.Wrap
    color: card.theme.urgent
    font.family: card.theme.fontFamily
    font.pixelSize: card.theme.fontBody
    Accessible.role: Accessible.StaticText
    Accessible.name: text
  }
  Text {
    visible: card.host.indexRootsBusy
    text: "Updating indexed folders…"
    color: card.theme.textMuted
    font.family: card.theme.fontFamily
    font.pixelSize: card.theme.fontBody
  }

  Text {
    id: reindexStatus
    Layout.fillWidth: true
    visible: card.host.indexReindexPending
    text: card.host.indexReindexError ? "Retrying indexing: " + card.host.indexReindexError
      : "Reindexing · " + (card.host.indexReindexProgress ? card.host.indexReindexProgress.indexed || 0 : 0) + " items"
    textFormat: Text.PlainText
    wrapMode: Text.Wrap
    color: card.host.indexReindexError ? card.theme.urgent : card.theme.textMuted
    font.family: card.theme.fontFamily
    font.pixelSize: card.theme.fontBody
    Accessible.role: Accessible.StaticText
    Accessible.name: text
  }

  component RootRow: RowLayout {
    id: row
    required property var modelData
    required property int index
    readonly property alias actionControl: actionButton
    Layout.fillWidth: true
    spacing: Math.round(10 * card.theme.scale)
    Keys.forwardTo: [pathField]
    Keys.onUpPressed: {
      if (index > 0) card.actionControlAt(index - 1).forceActiveFocus()
      else card.focusPath()
    }
    Keys.onDownPressed: {
      if (index + 1 < rows.count) card.actionControlAt(index + 1).forceActiveFocus()
    }

    ColumnLayout {
      Layout.fillWidth: true
      Text {
        Layout.fillWidth: true
        text: row.modelData.path
        textFormat: Text.PlainText
        wrapMode: Text.WrapAnywhere
        color: card.theme.text
        font.family: card.theme.fontFamily
        font.pixelSize: card.theme.fontBody
      }
      Text {
        Layout.fillWidth: true
        text: row.modelData.issue || row.modelData.status
        textFormat: Text.PlainText
        wrapMode: Text.Wrap
        color: row.modelData.issue ? card.theme.urgent : card.theme.textMuted
        font.family: card.theme.fontFamily
        font.pixelSize: card.theme.fontSmall
      }
    }
    Rectangle {
      implicitWidth: sourceText.implicitWidth + Math.round(12 * card.theme.scale)
      implicitHeight: sourceText.implicitHeight + Math.round(8 * card.theme.scale)
      radius: card.theme.radiusSmall
      color: card.theme.surfaceStrong
      Text {
        id: sourceText
        anchors.centerIn: parent
        text: row.modelData.source === "configuration" ? "Configuration" : "Added"
        color: card.theme.textMuted
        font.family: card.theme.fontFamily
        font.pixelSize: card.theme.fontSmall
      }
    }
    DeckButton {
      id: actionButton
      theme: card.theme
      compact: true
      text: row.modelData.source === "added" ? "Remove" : row.modelData.enabled ? "Disable" : "Enable"
      focusPolicy: Qt.TabFocus
      Accessible.name: text + " indexed folder " + row.modelData.path
      onClicked: {
        if (!activeFocus) card.focusPath()
        card.host.indexRootChangeRequested(row.modelData.source === "added"
          ? Protocol.Operations.IndexRemove : row.modelData.enabled
          ? Protocol.Operations.IndexDisable : Protocol.Operations.IndexEnable, "", row.modelData.id)
      }
    }
  }
  Repeater {
    id: rows
    model: card.roots
    delegate: RootRow {}
    onItemRemoved: (index, item) => {
      const row = item as RootRow
      if (row && row.actionControl.activeFocus) card.pendingFocusId = row.modelData.id
    }
    onItemAdded: Qt.callLater(card.restoreFocus)
  }
}
