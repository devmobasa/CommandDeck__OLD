import QtQuick
import QtQuick.Layouts

Item {
  id: bubble
  required property var theme
  property bool fromUser: false
  property string label: ""
  property string body: ""
  property bool truncated: false
  signal copyRequested()
  readonly property int pad: Math.round(12 * bubble.theme.scale)
  readonly property int maxWidth: Math.round(bubble.width * (bubble.fromUser ? 0.78 : 1))
  implicitHeight: card.implicitHeight

  // A wrapping TextArea reports no useful implicit width, so a hidden plain
  // Text measures what you typed; answers always take the full width.
  Text {
    id: measure
    visible: false
    text: bubble.body
    textFormat: Text.PlainText
    font.family: bubble.theme.fontFamily
    font.pixelSize: Math.round(bubble.theme.fontBody * 1.05)
  }

  Rectangle {
    id: card
    x: bubble.fromUser ? bubble.width - width : 0
    width: bubble.fromUser
      ? Math.max(Math.round(120 * bubble.theme.scale), Math.min(Math.round(measure.implicitWidth * 1.18) + bubble.pad * 2 + Math.round(8 * bubble.theme.scale), bubble.maxWidth))
      : bubble.maxWidth
    implicitHeight: inner.implicitHeight + bubble.pad * 2
    radius: Math.round(14 * bubble.theme.scale)
    color: bubble.fromUser
      ? bubble.theme.withAlpha(bubble.theme.accent, 0.16)
      : bubble.theme.withAlpha(bubble.theme.text, 0.04)
    border.width: 1
    border.color: bubble.fromUser
      ? bubble.theme.withAlpha(bubble.theme.accent, 0.4)
      : bubble.theme.separator

    Rectangle {
      visible: !bubble.fromUser
      width: Math.round(3 * bubble.theme.scale)
      radius: width / 2
      color: bubble.theme.accent
      anchors.left: parent.left
      anchors.leftMargin: Math.round(6 * bubble.theme.scale)
      anchors.top: parent.top
      anchors.bottom: parent.bottom
      anchors.topMargin: bubble.pad
      anchors.bottomMargin: bubble.pad
    }

    ColumnLayout {
      id: inner
      x: bubble.pad + (bubble.fromUser ? 0 : Math.round(10 * bubble.theme.scale))
      y: bubble.pad
      width: card.width - bubble.pad * 2 - (bubble.fromUser ? 0 : Math.round(10 * bubble.theme.scale))
      spacing: Math.round(3 * bubble.theme.scale)

      RowLayout {
        Layout.fillWidth: true
        spacing: Math.round(8 * bubble.theme.scale)

        Text {
          Layout.fillWidth: true
          textFormat: Text.PlainText
          text: bubble.label
          color: bubble.fromUser ? bubble.theme.withAlpha(bubble.theme.text, 0.7) : bubble.theme.accent
          elide: Text.ElideRight
          font.family: bubble.theme.fontFamily
          font.pixelSize: bubble.theme.fontCaption
          font.weight: Font.DemiBold
          font.letterSpacing: 0.5
        }

        DeckButton {
          theme: bubble.theme
          compact: true
          flat: true
          text: "Copy"
          focusPolicy: Qt.NoFocus
          Accessible.name: "Copy this " + (bubble.fromUser ? "question" : "answer")
          onClicked: bubble.copyRequested()
        }
      }

      AiMarkdownView {
        Layout.fillWidth: true
        theme: bubble.theme
        sourceText: bubble.body
        background: null
        padding: 0
        Accessible.name: bubble.label + " message"
      }

      Text {
        visible: bubble.truncated
        text: "Message truncated for display."
        color: bubble.theme.textFaint
        font.family: bubble.theme.fontFamily
        font.pixelSize: bubble.theme.fontCaption
      }
    }
  }
}
