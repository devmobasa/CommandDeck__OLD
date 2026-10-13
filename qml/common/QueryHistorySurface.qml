pragma ComponentBehavior: Bound
import QtQuick
import "UiConstants.js" as Ui
import QtQuick.Controls
import QtQuick.Layouts

Rectangle {
  id: root

  required property var theme
  property bool opened: false
  property bool loading: false
  property var snapshot: null
  property string errorText: ""
  property int pendingDeleteId: 0
  property bool pendingReset: false
  readonly property alias closeControl: closeButton
  readonly property alias resetControl: resetButton
  readonly property alias confirmationControl: confirmation
  readonly property int entryCount: entryRepeater.count

  signal dismissed()
  signal reloadRequested()
  signal deleteRequested(int historyId)
  signal resetRequested()

  visible: opened
  // Covers the rounded palette and the results beneath, so it repeats the
  // palette frame and stays opaque.
  color: root.theme.popupBackground
  radius: root.theme.radius
  border.width: 1
  border.color: root.theme.border
  focus: visible
  Accessible.role: Accessible.Dialog
  Accessible.name: "Query history"

  function entryItemAt(index) {
    return index >= 0 && index < entryRepeater.count
      ? entryRepeater.itemAt(index)
      : null
  }

  function containsFocusItem(item) {
    let current = item
    while (current) {
      if (current === root) return true
      current = current.parent
    }
    return false
  }

  function cycleFocus(forward) {
    const window = root.Window.window
    const current = window ? window.activeFocusItem : null
    const next = current ? current.nextItemInFocusChain(forward) : null
    if (!next || !containsFocusItem(next)) closeButton.forceActiveFocus()
    else next.forceActiveFocus()
  }

  onOpenedChanged: {
    if (opened) {
      errorText = ""
      Qt.callLater(function() { closeButton.forceActiveFocus() })
    } else {
      pendingDeleteId = 0
      pendingReset = false
    }
  }

  Keys.priority: Keys.BeforeItem
  Keys.onPressed: event => {
    if (event.key === Qt.Key_Escape) {
      root.dismissed()
      event.accepted = true
    } else if (event.key === Qt.Key_Tab && !(event.modifiers & Qt.ShiftModifier)) {
      root.cycleFocus(true)
      event.accepted = true
    } else if (event.key === Qt.Key_Backtab
        || (event.key === Qt.Key_Tab && (event.modifiers & Qt.ShiftModifier))) {
      root.cycleFocus(false)
      event.accepted = true
    }
  }

  MouseArea {
    anchors.fill: parent
    acceptedButtons: Qt.AllButtons
    hoverEnabled: true
  }

  ColumnLayout {
    anchors.fill: parent
    anchors.margins: root.theme.panelPadding
    spacing: root.theme.gap

    RowLayout {
      Layout.fillWidth: true

      Text {
        Layout.fillWidth: true
        text: "Query history"
        color: root.theme.text
        font.family: root.theme.fontFamily
        font.pixelSize: root.theme.fontHeading
        font.weight: Font.DemiBold
      }

      DeckButton {
        theme: root.theme
        text: "Reload"
        enabled: !root.loading
        Accessible.name: "Reload query history"
        onClicked: root.reloadRequested()
      }

      DeckButton {
        id: closeButton
        theme: root.theme
        text: "Close"
        Accessible.name: "Close query history"
        onClicked: root.dismissed()
      }
    }

    Text {
      Layout.fillWidth: true
      text: root.snapshot && root.snapshot.enabled
        ? "Queries are retained for " + root.snapshot.retentionDays
          + " days, up to " + root.snapshot.maximumEntries + " entries."
        : "Query history is disabled. Enable privacy.storeQueryHistory to retain it."
      color: root.theme.textMuted
      font.family: root.theme.fontFamily
      font.pixelSize: root.theme.fontSmall
      wrapMode: Text.Wrap
      Accessible.role: Accessible.StaticText
      Accessible.name: text
    }

    Text {
      visible: root.errorText.length > 0
      Layout.fillWidth: true
      textFormat: Text.PlainText
      text: root.errorText
      color: root.theme.urgent
      font.family: root.theme.fontFamily
      font.pixelSize: root.theme.fontSmall
      wrapMode: Text.Wrap
      Accessible.role: Accessible.AlertMessage
      Accessible.name: text
    }

    BusyIndicator {
      visible: root.loading
      running: visible
      Layout.alignment: Qt.AlignHCenter
      Accessible.name: "Loading query history"
    }

    Text {
      visible: !root.loading && (!root.snapshot || root.snapshot.entries.length === 0)
      Layout.fillWidth: true
      Layout.fillHeight: true
      text: "No retained queries"
      color: root.theme.textMuted
      horizontalAlignment: Text.AlignHCenter
      verticalAlignment: Text.AlignVCenter
      font.family: root.theme.fontFamily
      font.pixelSize: root.theme.fontBody
      Accessible.role: Accessible.StaticText
      Accessible.name: text
    }

    ScrollView {
      visible: !root.loading && root.snapshot && root.snapshot.entries.length > 0
      Layout.fillWidth: true
      Layout.fillHeight: true
      clip: true
      contentWidth: availableWidth

      ColumnLayout {
        width: parent.width
        spacing: 6

        Repeater {
          id: entryRepeater
          model: root.snapshot ? root.snapshot.entries || [] : []

          RowLayout {
            id: historyEntry

            required property var modelData
            readonly property alias deleteControl: deleteButton
            Layout.fillWidth: true
            Accessible.role: Accessible.ListItem
            Accessible.name: historyEntry.modelData.query

            Text {
              Layout.fillWidth: true
              textFormat: Text.PlainText
              text: historyEntry.modelData.query
              color: root.theme.text
              font.family: root.theme.fontFamily
              font.pixelSize: root.theme.fontBody
              elide: Text.ElideRight
            }

            DeckButton {
              id: deleteButton
              theme: root.theme
              kind: Ui.ButtonVariant.Danger
              text: "Delete"
              enabled: !root.loading
              Accessible.name: "Delete query " + historyEntry.modelData.query
              Accessible.description: "Destructive action. Requires confirmation"
              onClicked: root.pendingDeleteId = Number(historyEntry.modelData.id)
            }
          }
        }
      }
    }

    RowLayout {
      Layout.fillWidth: true
      Item { Layout.fillWidth: true }
      DeckButton {
        id: resetButton
        theme: root.theme
        kind: Ui.ButtonVariant.Danger
        text: "Clear All"
        enabled: !root.loading && root.snapshot && root.snapshot.entries.length > 0
        Accessible.name: "Clear all query history"
        Accessible.description: "Destructive action. Requires confirmation"
        onClicked: root.pendingReset = true
      }
    }
  }

  ConfirmationSurface {
    id: confirmation
    anchors.fill: parent
    z: 4
    theme: root.theme
    opened: root.pendingReset || root.pendingDeleteId > 0
    prompt: root.pendingReset
      ? "Clear all retained query history?"
      : "Delete this retained query?"
    confirmLabel: "Delete"
    destructive: true
    onAccepted: {
      if (root.pendingReset) root.resetRequested()
      else if (root.pendingDeleteId > 0) root.deleteRequested(root.pendingDeleteId)
      root.pendingReset = false
      root.pendingDeleteId = 0
    }
    onCanceled: {
      root.pendingReset = false
      root.pendingDeleteId = 0
      root.focus = true
    }
  }
}
