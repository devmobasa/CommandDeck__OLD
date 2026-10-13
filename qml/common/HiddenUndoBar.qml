import QtQuick
import QtQuick.Layouts
import "UiConstants.js" as Ui

// "Hidden <title> · Undo": a transient strip under the results after "Don't
// show this", or after a reversible change such as a HEY Reply Later, which
// sets the message. Undo reverses it and Ctrl+Z in the search field does the
// same while the strip is visible. It hides itself after a few seconds;
// Settings → Hidden items stays available.
Rectangle {
  id: root

  required property var theme
  property string title: ""
  // Replaces "Hidden <title>" for any other reversible change.
  property string message: ""
  readonly property string displayText: root.message.length > 0 ? root.message : "Hidden " + root.title
  property bool active: false
  property int holdMs: 8000
  signal undoRequested()
  signal dismissed()
  readonly property alias undoControl: undoButton
  readonly property alias dismissControl: dismissButton

  visible: root.active
  implicitHeight: row.implicitHeight + Math.round(12 * root.theme.scale)
  radius: root.theme.radiusSmall
  color: root.theme.withAlpha(root.theme.accent, 0.10)
  border.width: 1
  border.color: root.theme.withAlpha(root.theme.accent, 0.4)
  Accessible.role: Accessible.StaticText
  Accessible.name: root.displayText + ". Undo is available."

  onActiveChanged: {
    if (root.active) hold.restart()
    else hold.stop()
  }
  // A newer change replaces the message and gets the full hold again.
  onDisplayTextChanged: if (root.active) hold.restart()

  Timer {
    id: hold
    interval: root.holdMs
    onTriggered: root.dismissed()
  }

  RowLayout {
    id: row
    anchors.fill: parent
    anchors.leftMargin: Math.round(12 * root.theme.scale)
    anchors.rightMargin: Math.round(8 * root.theme.scale)
    spacing: Math.round(10 * root.theme.scale)

    Text {
      Layout.fillWidth: true
      textFormat: Text.PlainText
      text: root.displayText
      color: root.theme.text
      elide: Text.ElideRight
      font.family: root.theme.fontFamily
      font.pixelSize: root.theme.fontBody
    }

    Text {
      text: "Ctrl+Z"
      color: root.theme.textFaint
      font.family: root.theme.fontFamily
      font.pixelSize: root.theme.fontSmall
    }

    // Tab reaches both buttons; a click leaves typing in search.
    DeckButton {
      id: undoButton
      theme: root.theme
      kind: Ui.ButtonVariant.Primary
      compact: true
      focusPolicy: Qt.TabFocus
      text: "Undo"
      Accessible.name: root.message.length > 0 ? "Undo: " + root.message : "Undo hiding " + root.title
      onClicked: root.undoRequested()
    }

    DeckButton {
      id: dismissButton
      theme: root.theme
      compact: true
      flat: true
      focusPolicy: Qt.TabFocus
      text: "✕"
      Accessible.name: "Dismiss the undo strip"
      onClicked: root.dismissed()
    }
  }
}
