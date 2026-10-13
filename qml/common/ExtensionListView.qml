pragma ComponentBehavior: Bound
import QtQuick
import "ListKeys.js" as ListKeys
import "UiConstants.js" as Ui
import QtQuick.Controls
import QtQuick.Layouts

Rectangle {
  id: root
  required property var theme
  property var snapshot: ({ revision: 0, content: { id: "", title: "", items: [], actions: [] } })
  property bool opened: false
  property bool busy: false
  property string externalError: ""
  property string hostTitle: ""
  property bool recoveryVisible: false
  property bool disableAvailable: false
  signal restartRequested()
  signal disableRequested()
  readonly property alias viewActionsControl: viewActions
  readonly property alias restartControl: restartButton
  readonly property alias disableControl: disableButton
  property Item returnFocus: null
  property string selectedId: ""
  property var focusedAction: null
  property string focusedPage: ""
  readonly property alias inputControl: query
  readonly property alias itemsControl: items
  readonly property alias primaryControl: primaryButton
  readonly property alias backControl: back
  readonly property alias previousPageControl: previousPage
  readonly property alias nextPageControl: nextPage
  readonly property Item firstPageControl: previousPage.visible ? previousPage : nextPage.visible ? nextPage : null
  readonly property Item lastPageControl: nextPage.visible ? nextPage : previousPage.visible ? previousPage : null
  readonly property var content: snapshot.content || ({})
  readonly property var filtered: (content.items || []).filter(item =>
    (content.search || {}).mode === "remote" || (item.title + " " + (item.subtitle || "")).toLocaleLowerCase().indexOf(query.text.toLocaleLowerCase()) >= 0)
  readonly property int selectedIndex: filtered.findIndex(item => item.id === selectedId)
  readonly property var selectedItem: selectedIndex >= 0 ? filtered[selectedIndex] : null
  readonly property var selectedActions: selectedItem ? selectedItem.actions || [] : []
  property int presentedRevision: 0
  signal presented(int revision)
  signal actionRequested(int revision, string itemId, string handle)
  signal searchEdited(string query)
  signal backRequested()
  visible: opened
  color: theme.popupBackground
  border.width: 1
  border.color: theme.border
  radius: theme.radius
  Accessible.role: Accessible.Pane
  Accessible.name: content.title || "Extension command"

  function selectAvailable() {
    if (!filtered.some(item => item.id === selectedId)) selectedId = filtered.length ? filtered[0].id : ""
    if (selectedIndex >= 0) items.positionViewAtIndex(selectedIndex, ListView.Contain)
  }
  function acknowledge() {
    if (opened && snapshot.revision !== presentedRevision) {
      presentedRevision = snapshot.revision
      presented(presentedRevision)
    }
  }
  function activate(action, itemId) {
    if (busy || !action || !action.handle) return
    acknowledge()
    actionRequested(presentedRevision, itemId || "", action.handle)
  }
  function primaryAction() { if (selectedActions.length) activate(selectedActions[0], selectedId) }
  function resetState() {
    clearActionFocus()
    query.clear()
    selectedId = ""
    selectAvailable()
    items.contentY = items.originY
    Qt.callLater(function() { if (opened) query.forceActiveFocus() })
  }
  function captureState() {
    return { query: query.text, selectedId: selectedId, scroll: items.contentY, action: focusedAction,
      focus: query.activeFocus ? "query" : primaryButton.activeFocus ? "primary" : back.activeFocus ? "back"
        : previousPage.activeFocus ? "previous" : nextPage.activeFocus ? "next" : "items" }
  }
  function restoreState(state) {
    query.text = state.query
    selectedId = state.selectedId
    selectAvailable()
    items.contentY = Math.max(items.originY, Math.min(state.scroll, Math.max(items.originY, items.contentHeight - items.height)))
    if (state.action) { focusedAction = state.action; restoreActionFocus() }
    else if (state.focus === "primary" && primaryButton.enabled) primaryButton.forceActiveFocus()
    else if (state.focus === "back") back.forceActiveFocus()
    else if (state.focus === "previous" && previousPage.enabled) previousPage.forceActiveFocus()
    else if (state.focus === "next" && nextPage.enabled) nextPage.forceActiveFocus()
    else if (state.focus === "items") items.forceActiveFocus()
    else query.forceActiveFocus()
  }
  function restoreActionFocus() {
    if (!opened || busy || recoveryVisible || !focusedAction) return
    for (let index = 0; index < viewActions.count; index++) {
      const button = viewActions.itemAt(index)
      const candidate = viewActions.model[index]
      if (candidate.action.id === focusedAction.id && candidate.itemId === focusedAction.itemId) {
        button.forceActiveFocus()
        return
      }
    }
    query.forceActiveFocus()
  }
  function restorePageFocus() {
    if (!opened || busy || recoveryVisible || !focusedPage) return
    if (focusedPage === "previous" && previousPage.enabled) previousPage.forceActiveFocus()
    else if (focusedPage === "next" && nextPage.enabled) nextPage.forceActiveFocus()
    else if (firstPageControl && firstPageControl.enabled) firstPageControl.forceActiveFocus()
    else query.forceActiveFocus()
  }
  function clearActionFocus() { focusedAction = null; focusedPage = "" }
  function dismissOrClear() {
    if (query.text.length) { query.clear(); query.forceActiveFocus() }
    else backRequested()
  }
  function select(index) {
    selectedId = filtered[index].id
    items.positionViewAtIndex(index, ListView.Contain)
  }
  // The list's own keys reach here from the search field and, forwarded to
  // it, from the focused list.
  function handleKey(event) {
    if (ListKeys.handleIndex(event, selectedIndex, filtered.length, ListKeys.pageSize(items.height, 56 + items.spacing),
        true, index => root.select(index))) return

    if (event.key === Qt.Key_Return || event.key === Qt.Key_Enter) primaryAction()
    else if (event.key === Qt.Key_Escape) dismissOrClear()
    else return
    event.accepted = true
  }
  onFilteredChanged: selectAvailable()
  onSnapshotChanged: {
    if (opened && snapshot.revision === 0) {
      query.clear()
      Qt.callLater(function() { query.forceActiveFocus() })
    }
    Qt.callLater(acknowledge)
    Qt.callLater(restoreActionFocus)
    Qt.callLater(restorePageFocus)
  }
  onBusyChanged: if (!busy) { Qt.callLater(restoreActionFocus); Qt.callLater(restorePageFocus) }
  onRecoveryVisibleChanged: if (opened && recoveryVisible) Qt.callLater(function() { restartButton.forceActiveFocus() })
  onOpenedChanged: {
    if (opened) {
      query.clear()
      selectAvailable()
      Qt.callLater(function() { query.forceActiveFocus(); root.acknowledge() })
    } else {
      focusedAction = null
      focusedPage = ""
      presentedRevision = 0
      if (returnFocus) returnFocus.forceActiveFocus()
    }
  }
  Component.onCompleted: if (opened) Qt.callLater(function() { query.forceActiveFocus(); root.acknowledge() })

  ColumnLayout {
    anchors.fill: parent
    anchors.margins: 16
    spacing: 10
    RowLayout {
      Layout.fillWidth: true
      DeckButton {
        id: back
        theme: root.theme
        onActiveFocusChanged: if (activeFocus) root.clearActionFocus()
        text: "Back · Esc"
        Accessible.name: "Back to launcher"
        onClicked: root.backRequested()
        KeyNavigation.tab: query
        KeyNavigation.backtab: root.recoveryVisible ? disableButton : root.lastPageControl || (viewActions.count ? viewActions.itemAt(viewActions.count - 1) : primaryButton.enabled ? primaryButton : items)
        Keys.onEscapePressed: root.dismissOrClear()
      }
      Text {
        Layout.fillWidth: true
        text: root.hostTitle ? root.hostTitle + " · " + (root.content.title || "") : root.content.title || ""
        textFormat: Text.PlainText
        color: root.theme.text
        font.pixelSize: root.theme.fontHeading
        elide: Text.ElideRight
      }
      BusyIndicator { running: !root.recoveryVisible && (root.busy || !!root.content.loading); visible: running; implicitWidth: 28; implicitHeight: 28 }
    }
    TextField {
      id: query
      onActiveFocusChanged: if (activeFocus) root.clearActionFocus()
      Layout.fillWidth: true
      placeholderText: (root.content.search || {}).placeholder || "Search…"
      Accessible.name: "Search " + (root.content.title || "extension")
      maximumLength: 4096
      color: root.theme.text
      background: Rectangle {
        radius: root.theme.radiusSmall
        color: root.theme.surface
        border.width: query.activeFocus ? 2 : 1
        border.color: query.activeFocus ? root.theme.focusRing : root.theme.border
      }
      onTextEdited: { forceActiveFocus(); root.searchEdited(text) }
      Keys.priority: Keys.BeforeItem
      Keys.onPressed: event => root.handleKey(event)
      KeyNavigation.tab: items
      KeyNavigation.backtab: back
    }
    Text {
      Layout.fillWidth: true
      visible: !!root.externalError || !!root.content.error
      text: root.externalError || root.content.error || ""
      textFormat: Text.PlainText
      color: root.theme.urgent
      wrapMode: Text.Wrap
    }
    ListView {
      id: items
      onActiveFocusChanged: if (activeFocus) root.clearActionFocus()
      Layout.fillWidth: true
      Layout.fillHeight: true
      clip: true
      activeFocusOnTab: true
      model: root.filtered
      spacing: 3
      Accessible.role: Accessible.List
      Accessible.name: "Command results"
      Keys.priority: Keys.AfterItem
      Keys.forwardTo: [query]
      KeyNavigation.priority: KeyNavigation.BeforeItem
      Keys.onPressed: event => root.handleKey(event)
      KeyNavigation.tab: primaryButton.enabled ? primaryButton : root.recoveryVisible ? restartButton : viewActions.count ? viewActions.itemAt(0) : root.firstPageControl || back
      KeyNavigation.backtab: query
      ScrollBar.vertical: ScrollBar { }
      delegate: Rectangle {
        id: row
        required property var modelData
        required property int index
        readonly property var itemData: row.modelData || ({})
        width: items.width
        height: 56
        radius: root.theme.radiusSmall
        color: row.itemData.id === root.selectedId ? root.theme.selectedBackground : "transparent"
        border.width: row.itemData.id === root.selectedId && (items.activeFocus || query.activeFocus) ? 2 : 0
        border.color: root.theme.focusRing
        Accessible.role: Accessible.ListItem
        Accessible.name: row.itemData.title || ""
        Accessible.selected: row.itemData.id === root.selectedId
        Image {
          objectName: "packageImage"
          anchors.left: parent.left
          anchors.leftMargin: 8
          anchors.verticalCenter: parent.verticalCenter
          width: 40; height: 40
          sourceSize: Qt.size(40, 40)
          fillMode: Image.PreserveAspectFit
          source: (row.itemData.imageSource || "").startsWith("data:image/png;base64,") ? row.itemData.imageSource : ""
          visible: source.toString().length > 0
          Accessible.ignored: true
        }
        Column {
          anchors.fill: parent
          anchors.margins: 8
          anchors.leftMargin: row.itemData.imageSource ? 56 : 8
          Text {
            width: parent.width
            text: row.itemData.title || ""
            textFormat: Text.PlainText
            color: root.theme.text
            elide: Text.ElideRight
          }
          Text {
            width: parent.width
            text: row.itemData.subtitle || ""
            textFormat: Text.PlainText
            color: root.theme.textMuted
            elide: Text.ElideRight
          }
        }
        MouseArea {
          anchors.fill: parent
          onClicked: { root.selectedId = row.itemData.id; query.forceActiveFocus() }
          onDoubleClicked: { root.selectedId = row.itemData.id; root.primaryAction() }
        }
      }
      Text {
        anchors.centerIn: parent
        visible: !root.filtered.length
        text: root.content.emptyMessage || "No results"
        textFormat: Text.PlainText
        color: root.theme.textMuted
      }
    }
    RowLayout {
      Layout.fillWidth: true
      visible: root.recoveryVisible
      DeckButton {
        id: restartButton
        theme: root.theme
        onActiveFocusChanged: if (activeFocus) root.clearActionFocus()
        text: "Restart command"
        onClicked: root.restartRequested()
        KeyNavigation.tab: disableButton.enabled ? disableButton : back
        KeyNavigation.backtab: items
        Keys.onRightPressed: if (disableButton.enabled) disableButton.forceActiveFocus()
        Keys.onEscapePressed: root.dismissOrClear()
      }
      DeckButton {
        id: disableButton
        theme: root.theme
        onActiveFocusChanged: if (activeFocus) root.clearActionFocus()
        text: "Disable extension"
        enabled: root.disableAvailable
        onClicked: root.disableRequested()
        KeyNavigation.tab: back
        KeyNavigation.backtab: restartButton
        Keys.onLeftPressed: restartButton.forceActiveFocus()
        Keys.onEscapePressed: root.dismissOrClear()
      }
    }
    Flow {
      Layout.fillWidth: true
      spacing: 6
      Repeater {
        id: viewActions
        onItemAdded: Qt.callLater(root.restoreActionFocus)
        model: root.recoveryVisible ? [] : root.selectedActions.slice(1).map(action => ({ action: action, itemId: root.selectedId }))
          .concat((root.content.actions || []).map(action => ({ action: action, itemId: "" })))
        delegate: LiteralButton {
          focusPolicy: Qt.TabFocus
          required property var modelData
          required property int index
          text: modelData.action.title
          enabled: !root.busy
          onActiveFocusChanged: if (activeFocus) { root.focusedPage = ""; root.focusedAction = { id: modelData.action.id, itemId: modelData.itemId } }
          onClicked: root.activate(modelData.action, modelData.itemId)
          KeyNavigation.tab: index + 1 < viewActions.count ? viewActions.itemAt(index + 1) : root.firstPageControl || back
          KeyNavigation.backtab: index > 0 ? viewActions.itemAt(index - 1) : primaryButton.enabled ? primaryButton : items
          Keys.onLeftPressed: { if (index > 0) viewActions.itemAt(index - 1).forceActiveFocus() }
          Keys.onRightPressed: { if (index + 1 < viewActions.count) viewActions.itemAt(index + 1).forceActiveFocus() }
          Keys.onEscapePressed: root.dismissOrClear()
        }
      }
    }
    RowLayout {
      visible: !root.recoveryVisible && (!!root.content.previousPage || !!root.content.nextPage)
      Layout.fillWidth: true
      DeckButton {
        id: previousPage
        theme: root.theme
        focusPolicy: Qt.TabFocus
        text: "Previous page"
        visible: !!root.content.previousPage
        enabled: visible && !root.busy
        onClicked: root.activate({ handle: root.content.previousPage }, "")
        onActiveFocusChanged: if (activeFocus) { root.focusedAction = null; root.focusedPage = "previous" }
        KeyNavigation.tab: nextPage.visible ? nextPage : back
        KeyNavigation.backtab: viewActions.count ? viewActions.itemAt(viewActions.count - 1) : primaryButton.enabled ? primaryButton : items
        Keys.onRightPressed: if (nextPage.enabled) nextPage.forceActiveFocus()
        Keys.onEscapePressed: root.dismissOrClear()
      }
      DeckButton {
        id: nextPage
        theme: root.theme
        focusPolicy: Qt.TabFocus
        text: "Next page"
        visible: !!root.content.nextPage
        enabled: visible && !root.busy
        onClicked: root.activate({ handle: root.content.nextPage }, "")
        onActiveFocusChanged: if (activeFocus) { root.focusedAction = null; root.focusedPage = "next" }
        KeyNavigation.tab: back
        KeyNavigation.backtab: previousPage.visible ? previousPage : viewActions.count ? viewActions.itemAt(viewActions.count - 1) : primaryButton.enabled ? primaryButton : items
        Keys.onLeftPressed: if (previousPage.enabled) previousPage.forceActiveFocus()
        Keys.onEscapePressed: root.dismissOrClear()
      }
      Text { Layout.fillWidth: true; text: "Search filters this page"; color: root.theme.textMuted; font.pixelSize: root.theme.fontSmall }
    }
    RowLayout {
      Layout.fillWidth: true
      Text { text: "↑↓ Select · Enter Activate · Esc Back"; color: root.theme.textMuted; font.pixelSize: root.theme.fontSmall }
      Item { Layout.fillWidth: true }
      DeckButton {
        id: primaryButton
        theme: root.theme
        kind: Ui.ButtonVariant.Primary
        focusPolicy: Qt.TabFocus
        onActiveFocusChanged: if (activeFocus) root.clearActionFocus()
        enabled: !root.busy && root.selectedActions.length > 0
        text: root.selectedActions.length ? root.selectedActions[0].title : "Select an item"
        onClicked: root.primaryAction()
        KeyNavigation.tab: viewActions.count ? viewActions.itemAt(0) : root.firstPageControl || back
        KeyNavigation.backtab: items
        Keys.onEscapePressed: root.dismissOrClear()
      }
    }
  }
}
