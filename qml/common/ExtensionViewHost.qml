import QtQuick
import "Protocol.js" as Protocol
import "UiConstants.js" as Ui
import QtQuick.Controls
import QtQuick.Window

Item {
  id: root
  required property var theme
  property var snapshot: ({ revision: 0, content: {} })
  property var displayedSnapshot: ({ revision: 0, content: {} })
  property bool opened: false
  property bool busy: false
  property bool completed: false
  property bool noView: false
  readonly property bool showStatus: (noView || completed) && !recoveryVisible
  readonly property alias completionControl: completionButton
  property string externalError: ""
  property string hostTitle: ""
  property var debugLease: null
  property var confirmation: null
  signal remoteDecided(bool confirmed)
  readonly property alias remoteCancelControl: remoteCancel
  readonly property alias remoteApproveControl: remoteApprove
  readonly property alias remoteDialogControl: remoteDialog
  readonly property alias remoteBodyControl: remoteBody
  onConfirmationChanged: if (confirmation && opened) remoteDialog.open(); else remoteDialog.close()
  property bool recoveryVisible: false
  property bool disableAvailable: false
  property Item returnFocus: null
  property var savedStates: ({})
  property var pendingRestore: null
  readonly property bool isDetail: displayedSnapshot.content.kind === Protocol.ExtensionViewKinds.Detail && !recoveryVisible
  readonly property bool isForm: displayedSnapshot.content.kind === Protocol.ExtensionViewKinds.Form && !recoveryVisible
  readonly property var activeView: isForm ? form : isDetail ? detail : list
  readonly property alias formControl: form
  readonly property alias listControl: list
  readonly property alias detailControl: detail
  // Existing launcher journey references remain attached to the native List.
  readonly property alias inputControl: list.inputControl
  readonly property alias primaryControl: list.primaryControl
  readonly property alias backControl: list.backControl
  readonly property alias viewActionsControl: list.viewActionsControl
  readonly property alias restartControl: list.restartControl
  readonly property alias disableControl: list.disableControl
  readonly property alias filtered: list.filtered
  readonly property alias selectedId: list.selectedId
  signal presented(int revision)
  signal actionRequested(int revision, string itemId, string handle)
  signal formSubmitted(int revision, string handle, int formRevision, var values)
  signal backRequested()
  signal restartRequested()
  signal disableRequested()

  property Item windowProbe: Item { visible: false }

  function back() {
    const content = displayedSnapshot.content
    if (content.backHandle && !recoveryVisible) {
      if (!busy) actionRequested(displayedSnapshot.revision, "", content.backHandle)
    } else backRequested()
  }
  function restore() {
    if (!opened || busy || !pendingRestore) return
    const state = pendingRestore
    pendingRestore = null
    activeView.restoreState(state)
  }
  function applySnapshot() {
    const previousDepth = displayedSnapshot.content.depth || 1
    const nextDepth = snapshot.content.depth || 1
    const freshView = snapshot.revision > 0 && nextDepth >= previousDepth && (displayedSnapshot.revision === 0 || nextDepth > previousDepth
      || snapshot.content.kind !== displayedSnapshot.content.kind || snapshot.content.id !== displayedSnapshot.content.id)
    if (snapshot.revision === 0) { savedStates = ({}); pendingRestore = null }
    else if (nextDepth > previousDepth) savedStates[previousDepth] = activeView.captureState()
    else if (nextDepth < previousDepth) {
      pendingRestore = savedStates[nextDepth] || null
      for (const depth in savedStates) if (Number(depth) >= nextDepth) delete savedStates[depth]
    }
    displayedSnapshot = snapshot
    if (freshView) {
      pendingRestore = null
      activeView.resetState()
    }
    Qt.callLater(restore)
  }
  onSnapshotChanged: applySnapshot()
  onBusyChanged: if (!busy) Qt.callLater(restore)
  onOpenedChanged: if (!opened) {
    savedStates = ({})
    pendingRestore = null
    if (returnFocus) returnFocus.forceActiveFocus()
  }
  Component.onCompleted: applySnapshot()
  Dialog {
    id: remoteDialog
    anchors.centerIn: parent
    width: Math.min(parent.width - 32, 720)
    height: Math.min(parent.height - 32, 500)
    modal: true
    title: "Confirm remote change"
    closePolicy: Popup.CloseOnEscape
    property Item previousFocus: null
    onAboutToShow: { previousFocus = root.windowProbe.Window.window ? root.windowProbe.Window.window.activeFocusItem : null; Qt.callLater(() => remoteCancel.forceActiveFocus()) }
    onClosed: { if (root.confirmation) root.remoteDecided(false); if (previousFocus && previousFocus.visible) previousFocus.forceActiveFocus() }
    contentItem: Column {
      spacing: 12
      Text { width: parent.width; textFormat: Text.PlainText; wrapMode: Text.Wrap; text: root.confirmation ? root.confirmation.method + " " + root.confirmation.destination + "\nConnection: " + root.confirmation.connection + "\nRequest key: " + root.confirmation.idempotencyKey : ""; color: root.theme.text }
      ScrollView { width: parent.width; height: Math.max(40, remoteDialog.availableHeight - 150); TextArea { id: remoteBody; text: root.confirmation ? root.confirmation.body : ""; readOnly: true; textFormat: TextEdit.PlainText; wrapMode: TextEdit.Wrap; Accessible.name: "Exact request body"; KeyNavigation.tab: remoteCancel; KeyNavigation.backtab: remoteApprove } }
      Row {
        spacing: 12
        DeckButton { id: remoteCancel; theme: root.theme; text: "Cancel · Esc"; onClicked: root.remoteDecided(false); KeyNavigation.tab: remoteApprove; KeyNavigation.backtab: remoteBody }
        DeckButton { id: remoteApprove; theme: root.theme; kind: Ui.ButtonVariant.Primary; text: "Send once"; onClicked: root.remoteDecided(true); KeyNavigation.tab: remoteBody; KeyNavigation.backtab: remoteCancel }
      }
    }
  }
  onShowStatusChanged: Qt.callLater(function() { if (root.opened && root.showStatus) completionButton.forceActiveFocus() })

  ExtensionListView {
    id: list
    anchors.fill: parent
    anchors.topMargin: debugBanner.height
    theme: root.theme
    snapshot: root.isDetail || root.isForm ? { revision: 0, content: {} } : root.displayedSnapshot
    opened: root.opened && !root.isDetail && !root.isForm && !root.showStatus
    busy: root.busy
    externalError: root.externalError
    hostTitle: root.hostTitle
    recoveryVisible: root.recoveryVisible
    disableAvailable: root.disableAvailable
    onPresented: revision => { if (revision > 0) root.presented(revision) }
    onActionRequested: (revision, itemId, handle) => root.actionRequested(revision, itemId, handle)
    onBackRequested: root.back()
    onRestartRequested: root.restartRequested()
    onDisableRequested: root.disableRequested()
  }
  ExtensionDetailView {
    id: detail
    anchors.fill: parent
    anchors.topMargin: debugBanner.height
    theme: root.theme
    snapshot: root.displayedSnapshot
    opened: root.opened && root.isDetail && !root.showStatus
    busy: root.busy
    externalError: root.externalError
    onPresented: revision => { if (revision > 0) root.presented(revision) }
    onActionRequested: (revision, itemId, handle) => root.actionRequested(revision, itemId, handle)
    onBackRequested: root.back()
  }
  ExtensionFormView {
    id: form
    anchors.fill: parent
    anchors.topMargin: debugBanner.height
    theme: root.theme
    snapshot: root.displayedSnapshot
    opened: root.opened && root.isForm && !root.showStatus
    busy: root.busy
    externalError: root.externalError
    onPresented: revision => root.presented(revision)
    onSubmitted: (revision, handle, formRevision, values) => root.formSubmitted(revision, handle, formRevision, values)
    onBackRequested: root.back()
  }
  Rectangle {
    anchors.fill: parent
    visible: root.opened && root.showStatus
    color: root.theme.popupBackground
    radius: root.theme.radius
    Accessible.role: Accessible.Pane
    Accessible.name: root.completed ? "Command completed" : "Running command"
    MouseArea { anchors.fill: parent; onClicked: completionButton.forceActiveFocus() }
  }
  Rectangle {
    id: debugBanner
    z: 10
    width: parent.width
    height: root.debugLease ? 64 : 0
    visible: height > 0
    color: root.theme.popupBackground
    border.color: root.theme.accent
    Text {
      anchors.fill: parent; anchors.margins: 8
      text: root.debugLease ? "Development debug lease · Ends " + root.debugLease.expiresAt + " · Esc closes and revokes\n" + root.debugLease.socketPath : ""
      textFormat: Text.PlainText; wrapMode: Text.Wrap; color: root.theme.accent
    }
  }
  Column {
    anchors.centerIn: parent
    visible: root.opened && root.showStatus
    spacing: 16
    Text { text: root.completed ? "Command completed" : "Running command…"; color: root.theme.text; font.pixelSize: root.theme.fontHeading }
    DeckButton {
      id: completionButton
      theme: root.theme
      text: root.completed ? "Done · Esc" : "Cancel · Esc"
      onClicked: root.backRequested()
      Keys.onEscapePressed: root.backRequested()
      KeyNavigation.tab: completionButton
      KeyNavigation.backtab: completionButton
    }
  }
}
