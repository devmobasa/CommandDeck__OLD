pragma ComponentBehavior: Bound
import QtQuick
import QtQuick.Controls
import QtQuick.Layouts

Rectangle {
  id: root
  required property var theme
  property var snapshot: ({ revision: 0, content: {} })
  property bool opened: false
  property bool busy: false
  property string externalError: ""
  readonly property var content: snapshot.content || ({})
  readonly property alias bodyControl: body
  readonly property alias actionsControl: actions
  readonly property alias backControl: back
  property string focusedActionId: ""
  property int presentedRevision: 0
  signal presented(int revision)
  signal actionRequested(int revision, string itemId, string handle)
  signal backRequested()
  visible: opened
  color: theme.popupBackground
  border.width: 1
  border.color: theme.border
  radius: theme.radius
  Accessible.role: Accessible.Pane
  Accessible.name: content.title || "Extension details"

  function activate(action) {
    if (!busy && action && action.handle) { acknowledge(); actionRequested(snapshot.revision, "", action.handle) }
  }
  function acknowledge() {
    if (opened && snapshot.revision > 0 && snapshot.revision !== presentedRevision) {
      presentedRevision = snapshot.revision
      presented(presentedRevision)
    }
  }
  function focusOnOpen() { if (opened) { body.forceActiveFocus(); acknowledge() } }
  function resetState() {
    focusedActionId = ""
    scroll.contentY = 0
    Qt.callLater(focusOnOpen)
  }
  function restoreActionFocus() {
    if (!opened || busy || !focusedActionId) return
    for (let index = 0; index < actions.count; index++) {
      if (actions.model[index].id === focusedActionId) { actions.itemAt(index).forceActiveFocus(); return }
    }
    body.forceActiveFocus()
  }
  function captureState() { return { scroll: scroll.contentY, action: focusedActionId, back: back.activeFocus } }
  function restoreState(state) {
    scroll.contentY = Math.max(0, Math.min(state.scroll, scroll.contentHeight - scroll.height))
    if (state.back) back.forceActiveFocus()
    else if (state.action) { focusedActionId = state.action; restoreActionFocus() }
    else body.forceActiveFocus()
  }
  onOpenedChanged: {
    if (opened) Qt.callLater(focusOnOpen)
    else { presentedRevision = 0; focusedActionId = "" }
  }
  onSnapshotChanged: { Qt.callLater(acknowledge); Qt.callLater(restoreActionFocus) }
  onBusyChanged: if (!busy) Qt.callLater(restoreActionFocus)
  Component.onCompleted: if (opened) Qt.callLater(focusOnOpen)

  ColumnLayout {
    anchors.fill: parent
    anchors.margins: 16
    spacing: 10
    RowLayout {
      Layout.fillWidth: true
      DeckButton {
        id: back
        theme: root.theme
        text: "Back · Esc"
        onClicked: root.backRequested()
        onActiveFocusChanged: if (activeFocus) root.focusedActionId = ""
        KeyNavigation.tab: body
        KeyNavigation.backtab: actions.count ? actions.itemAt(actions.count - 1) : body
        Keys.onEscapePressed: root.backRequested()
      }
      Text {
        Layout.fillWidth: true
        text: root.content.title || "Details"
        textFormat: Text.PlainText
        color: root.theme.text
        font.pixelSize: root.theme.fontHeading
        elide: Text.ElideRight
      }
      BusyIndicator { running: root.busy || !!root.content.loading; visible: running; implicitWidth: 28; implicitHeight: 28 }
    }
    Text {
      Layout.fillWidth: true
      visible: text.length > 0
      text: root.externalError || root.content.error || ""
      textFormat: Text.PlainText
      color: root.theme.urgent
      wrapMode: Text.Wrap
    }
    Flickable {
      id: scroll
      Layout.fillWidth: true
      Layout.fillHeight: true
      contentWidth: width
      contentHeight: fields.implicitHeight
      clip: true
      ScrollBar.vertical: ScrollBar { }
      ColumnLayout {
        id: fields
        width: scroll.width
        Image {
          objectName: "packageImage"
          Layout.alignment: Qt.AlignHCenter
          Layout.preferredWidth: 192
          Layout.preferredHeight: 192
          sourceSize: Qt.size(192, 192)
          fillMode: Image.PreserveAspectFit
          source: (root.content.imageSource || "").startsWith("data:image/png;base64,") ? root.content.imageSource : ""
          visible: source.toString().length > 0
          Accessible.ignored: true
        }
        AiMarkdownView {
          id: body
          Layout.fillWidth: true
          theme: root.theme
          sourceText: root.content.body || ""
          Accessible.name: "Extension details"
          Accessible.description: sourceText
          activeFocusOnTab: true
          background: Rectangle { color: "transparent"; border.width: body.activeFocus ? 2 : 0; border.color: root.theme.focusRing }
          onActiveFocusChanged: if (activeFocus) root.focusedActionId = ""
          KeyNavigation.tab: actions.count ? actions.itemAt(0) : back
          KeyNavigation.backtab: back
          Keys.priority: Keys.BeforeItem
          Keys.onEscapePressed: root.backRequested()
          Keys.onReturnPressed: root.activate((root.content.actions || [])[0])
          Keys.onDownPressed: scroll.contentY = Math.min(Math.max(0, scroll.contentHeight - scroll.height), scroll.contentY + 32)
          Keys.onUpPressed: scroll.contentY = Math.max(0, scroll.contentY - 32)
          Keys.onPressed: event => {
            if (event.key === Qt.Key_PageDown) scroll.contentY = Math.min(Math.max(0, scroll.contentHeight - scroll.height), scroll.contentY + scroll.height)
            else if (event.key === Qt.Key_PageUp) scroll.contentY = Math.max(0, scroll.contentY - scroll.height)
            else return
            event.accepted = true
          }
        }
        Repeater {
          model: root.content.metadata || []
          delegate: ColumnLayout {
            required property var modelData
            Layout.fillWidth: true
            Text { text: parent.modelData.label; textFormat: Text.PlainText; color: root.theme.textMuted; Layout.fillWidth: true; wrapMode: Text.Wrap }
            Text { text: parent.modelData.value; textFormat: Text.PlainText; color: root.theme.text; Layout.fillWidth: true; wrapMode: Text.WrapAnywhere }
          }
        }
      }
    }
    Flow {
      Layout.fillWidth: true
      spacing: 6
      Repeater {
        id: actions
        model: root.content.actions || []
        delegate: LiteralButton {
          required property var modelData
          required property int index
          text: modelData.title
          enabled: !root.busy
          onClicked: root.activate(modelData)
          onActiveFocusChanged: if (activeFocus) root.focusedActionId = modelData.id
          KeyNavigation.tab: index + 1 < actions.count ? actions.itemAt(index + 1) : back
          KeyNavigation.backtab: index > 0 ? actions.itemAt(index - 1) : body
          Keys.onLeftPressed: if (index > 0) actions.itemAt(index - 1).forceActiveFocus()
          Keys.onRightPressed: if (index + 1 < actions.count) actions.itemAt(index + 1).forceActiveFocus()
          Keys.onEscapePressed: root.backRequested()
        }
      }
    }
  }
}
