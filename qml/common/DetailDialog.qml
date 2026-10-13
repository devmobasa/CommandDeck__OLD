import QtQuick
import QtQuick.Controls
import QtQuick.Layouts

FocusScope {
  id: root

  required property var theme
  property bool opened: false
  property string title: "Details"
  property string text: ""
  property bool plainText: false
  readonly property alias closeControl: closeButton
  signal dismissed()

  visible: opened
  focus: opened
  Accessible.role: Accessible.Dialog
  Accessible.name: title
  Accessible.description: text

  onOpenedChanged: {
    if (opened) Qt.callLater(function() {
      if (root && root.opened && closeButton) closeButton.forceActiveFocus()
    })
  }

  Keys.priority: Keys.BeforeItem
  Keys.onPressed: event => {
    if (event.key === Qt.Key_Escape) {
      root.dismissed()
      event.accepted = true
    } else if (event.key === Qt.Key_Tab || event.key === Qt.Key_Backtab) {
      closeButton.forceActiveFocus()
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
    width: Math.min(760, parent.width - 80)
    height: Math.min(520, parent.height - 80)
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

      RowLayout {
        Layout.fillWidth: true

        Text {
          Layout.fillWidth: true
          textFormat: Text.PlainText
          text: root.title
          color: root.theme.text
          font.family: root.theme.fontFamily
          font.pixelSize: root.theme.fontHeading
          font.weight: Font.DemiBold
          Accessible.role: Accessible.Heading
          Accessible.name: root.title
        }

        DeckButton {
          id: closeButton
          theme: root.theme
          text: "Close"
          Accessible.name: "Close " + root.title
          KeyNavigation.tab: closeButton
          KeyNavigation.backtab: closeButton
          onClicked: root.dismissed()
        }
      }

      ScrollView {
        Layout.fillWidth: true
        Layout.fillHeight: true
        clip: true
        contentWidth: availableWidth

        Text {
          width: parent.width
          text: root.text
          wrapMode: Text.Wrap
          textFormat: root.plainText ? Text.PlainText : Text.MarkdownText
          color: root.theme.text
          font.family: root.theme.fontFamily
          font.pixelSize: root.theme.fontBody
          Accessible.role: Accessible.StaticText
          Accessible.name: root.text
        }
      }
    }
  }
}
