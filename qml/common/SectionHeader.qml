import QtQuick
import QtQuick.Layouts

// Group label above a run of results of the same kind.
//
// Results are already sorted by score, so the per-row kind label they used to
// carry was noise. Grouping states the same fact once per run.
Item {
  id: root

  required property var theme
  property string title: ""
  property int count: 0

  implicitHeight: Math.round(root.theme.fontCaption * 2.6)
  Accessible.role: Accessible.Heading
  Accessible.name: root.title + ", " + root.count + " results"

  RowLayout {
    anchors.fill: parent
    anchors.leftMargin: root.theme.rowPaddingX
    anchors.rightMargin: root.theme.rowPaddingX
    anchors.bottomMargin: 2
    spacing: 8

    Text {
      Layout.alignment: Qt.AlignVCenter
      textFormat: Text.PlainText
      text: root.title.toUpperCase()
      color: root.theme.textFaint
      font.family: root.theme.fontFamily
      font.pixelSize: root.theme.fontCaption
      font.letterSpacing: 0.8
      font.weight: Font.DemiBold
    }

    Rectangle {
      Layout.fillWidth: true
      Layout.preferredHeight: 1
      Layout.alignment: Qt.AlignVCenter
      color: root.theme.separator
    }

    Text {
      Layout.alignment: Qt.AlignVCenter
      text: root.count
      color: root.theme.textFaint
      font.family: root.theme.fontFamily
      font.pixelSize: root.theme.fontCaption
    }
  }
}
