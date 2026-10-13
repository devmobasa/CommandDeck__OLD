import QtQuick

// A stock button whose label is shown as written. The stock label renders
// markup-like text as rich text, and these titles come from extensions and
// file names. Enter and Return press it under every platform theme.
KeyboardButton {
  id: root

  contentItem: Text {
    text: root.text
    textFormat: Text.PlainText
    font: root.font
    color: root.palette.buttonText
    horizontalAlignment: Text.AlignHCenter
    verticalAlignment: Text.AlignVCenter
    elide: Text.ElideRight
  }
}
