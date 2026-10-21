pragma ComponentBehavior: Bound
import QtQuick
import QtQuick.Layouts
import "UiConstants.js" as Ui

SettingsCard {
  id: card
  required property var host
  readonly property var controller: host.quicklinksController
  property bool editing: false
  property string focusId: ""
  property bool restoreRemove: false
  property string removeId: ""
  property var replacementFocus: null
  property int focusSession: 0
  readonly property bool shouldLoad: host.visible && host.currentPage === Ui.SettingsPage.Search
  theme: host.theme
  title: "Quicklinks"
  description: "Add web searches or links. Use {query} for the search text."
  searchQuery: host.part_settingsSearch.text
  searchTerms: "quicklinks web searches urls templates aliases icons add edit remove disable enable"
  onEditingChanged: focusSession++
  onRemoveIdChanged: focusSession++
  onVisibleChanged: {
    focusSession++
    if (!visible) { editing = false; removeId = "" }
  }
  onShouldLoadChanged: {
    focusSession++
    if (shouldLoad && controller) controller.load()
    if (!shouldLoad) { editing = false; removeId = "" }
  }
  Component.onCompleted: { if (shouldLoad && controller) controller.load() }

  function canRestoreFocus(session) {
    return session === focusSession && visible && shouldLoad && !editing && !removeId
  }

  function restoreFocus() {
    editing = false
    removeId = ""
    const session = focusSession
    const identity = focusId
    const removing = restoreRemove
    Qt.callLater(function() {
      if (!card.canRestoreFocus(session)) return
      for (let index = 0; index < rows.count; index++) {
        const row = rows.itemAt(index)
        if (row.modelData.quicklink.id === identity) {
          const control = removing ? row.removeControl : row.editControl
          if (!control.visible || !control.enabled) break
          control.forceActiveFocus()
          return
        }
      }
      addButton.forceActiveFocus()
    })
  }

  Connections {
    target: card.controller
    function onReplacing() {
      card.replacementFocus = null
      for (let index = 0; index < rows.count; index++) {
        const row = rows.itemAt(index)
        for (const control of [row.editControl, row.removeControl]) {
          if (control.activeFocus) card.replacementFocus = { identity: control.objectName, session: card.focusSession }
        }
      }
    }
    function onQuicklinksChanged() {
      const replacement = card.replacementFocus
      card.replacementFocus = null
      if (!replacement) return
      Qt.callLater(function() {
        if (!card.canRestoreFocus(replacement.session)) return
        for (let index = 0; index < rows.count; index++) {
          const row = rows.itemAt(index)
          for (const control of [row.editControl, row.removeControl]) {
            if (control.objectName === replacement.identity && control.visible && control.enabled) {
              control.forceActiveFocus()
              return
            }
          }
        }
        addButton.forceActiveFocus()
      })
    }
  }

  DeckButton {
    id: addButton
    objectName: "quicklinkAdd"
    theme: card.theme
    text: "Add quicklink"
    enabled: !!card.controller
    visible: !card.editing && !card.removeId
    Accessible.name: text
    onClicked: {
      card.restoreRemove = false
      card.focusId = ""
      card.editing = true
      editor.open({ id: "user-" + Date.now().toString(36) + "-" + Math.random().toString(36).slice(2), title: "", urlTemplate: "" })
    }
  }

  QuicklinkEditor {
    id: editor
    Layout.fillWidth: true
    visible: card.editing
    theme: card.theme
    controller: card.controller
    onFinished: card.restoreFocus()
  }

  ConfirmationSurface {
    id: removeConfirmation
    objectName: "quicklinkRemoveConfirmation"
    parent: card.host
    anchors.fill: parent
    z: 100
    theme: card.theme
    opened: card.removeId.length > 0
    prompt: "Remove this user quicklink?"
    confirmLabel: "Remove"
    destructive: true
    property bool settingsTabFence: true
    function cycleFocus(forward) {
      if (cancelControl.activeFocus) confirmControl.forceActiveFocus()
      else cancelControl.forceActiveFocus()
    }
    onCanceled: card.restoreFocus()
    onAccepted: {
      card.controller.submit("remove", { quicklinkId: card.removeId, confirmed: true })
      card.restoreFocus()
    }
  }

  Text {
    objectName: "quicklinksFeedback"
    Layout.fillWidth: true
    visible: text.length > 0
    textFormat: Text.PlainText
    text: card.controller ? card.controller.errorText || card.controller.statusText : ""
    wrapMode: Text.Wrap
    color: card.controller && card.controller.errorText ? card.theme.urgent : card.theme.textMuted
    font.family: card.theme.fontFamily
    font.pixelSize: card.theme.fontBody
    Accessible.role: Accessible.StaticText
    Accessible.name: text
  }

  Repeater {
    id: rows
    model: card.controller ? card.controller.quicklinks : []
    RowLayout {
      id: row
      required property int index
      required property var modelData
      readonly property alias removeControl: removeButton
      readonly property alias editControl: editButton
      visible: !card.editing && !card.removeId
      Layout.fillWidth: true
      Accessible.role: Accessible.ListItem
      Accessible.name: modelData.quicklink.title + ", " + modelData.source + (modelData.enabled ? "" : ", disabled")
      ColumnLayout {
        Layout.fillWidth: true
        Text {
          Layout.fillWidth: true
          textFormat: Text.PlainText
          text: row.modelData.quicklink.title
          elide: Text.ElideRight
          color: card.theme.text
          font.family: card.theme.fontFamily
          font.pixelSize: card.theme.fontBody
        }
        Text {
          Layout.fillWidth: true
          textFormat: Text.PlainText
          text: (row.modelData.quicklink.alias ? row.modelData.quicklink.alias + " · " : "") + row.modelData.quicklink.urlTemplate
          elide: Text.ElideRight
          color: card.theme.textMuted
          font.family: card.theme.fontFamily
          font.pixelSize: card.theme.fontSmall
        }
      }
      Rectangle {
        implicitWidth: chip.implicitWidth + 12
        implicitHeight: chip.implicitHeight + 6
        radius: 4
        color: card.theme.background
        Text {
          id: chip
          anchors.centerIn: parent
          text: row.modelData.source === "user" ? "User" : "Configuration"
          color: card.theme.textMuted
          font.family: card.theme.fontFamily
          font.pixelSize: card.theme.fontSmall
        }
      }
      DeckButton {
        id: editButton
        objectName: "quicklinkEdit-" + row.modelData.quicklink.id
        theme: card.theme
        compact: true
        text: row.modelData.source === "user" ? "Edit" : row.modelData.enabled ? "Disable" : "Enable"
        Accessible.name: text + " " + row.modelData.quicklink.title
        Accessible.description: "Up and Down move between quicklinks. Enter or Space activates."
        Keys.onUpPressed: { if (row.index > 0) rows.itemAt(row.index - 1).editControl.forceActiveFocus() }
        Keys.onDownPressed: { if (row.index + 1 < rows.count) rows.itemAt(row.index + 1).editControl.forceActiveFocus() }
        focusPolicy: Qt.TabFocus
        down: pressed || editPointer.pressed
        function activate(fromPointer) {
          card.restoreRemove = false
          card.focusId = row.modelData.quicklink.id
          if (row.modelData.source === "user") {
            card.editing = true
            editor.open(row.modelData.quicklink)
          } else {
            if (fromPointer) card.host.part_settingsSearch.forceActiveFocus()
            card.controller.submit(row.modelData.enabled ? "disable" : "enable", { quicklinkId: row.modelData.quicklink.id })
          }
        }
        onClicked: activate(false)
        MouseArea {
          id: editPointer
          anchors.fill: parent
          onClicked: editButton.activate(true)
        }
      }
      DeckButton {
        id: removeButton
        objectName: "quicklinkRemove-" + row.modelData.quicklink.id
        theme: card.theme
        compact: true
        visible: row.modelData.source === "user"
        text: "Remove"
        Accessible.name: "Remove " + row.modelData.quicklink.title
        onClicked: {
          card.focusId = row.modelData.quicklink.id
          card.restoreRemove = true
          card.removeId = card.focusId
          removeConfirmation.cancelControl.forceActiveFocus()
        }
      }
    }
  }
  DeckButton {
    id: refreshButton
    objectName: "quicklinksRefresh"
    theme: card.theme
    compact: true
    text: "Refresh quicklinks"
    visible: !card.editing && !card.removeId
    enabled: !!card.controller
    Accessible.name: text
    focusPolicy: Qt.TabFocus
    down: pressed || refreshPointer.pressed
    onClicked: card.controller.load()
    MouseArea {
      id: refreshPointer
      anchors.fill: parent
      onClicked: {
        card.host.part_settingsSearch.forceActiveFocus()
        card.controller.load()
      }
    }
  }

}
