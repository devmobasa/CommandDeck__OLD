pragma ComponentBehavior: Bound
import QtQuick
import "Protocol.js" as Protocol
import QtQuick.Layouts
import "ClipboardFormat.js" as Format

// One clipboard history entry. Selection must read at a glance whether focus is
// in search or in the list: the selected row carries an accent rail, an accent
// wash and outline, a bold title, and an inverted kind tile. List focus
// strengthens the wash and turns the outline into a solid accent ring.
Item {
  id: root

  required property var modelData
  required property int index
  required property var theme
  property bool selected: false
  property bool listFocused: false
  property bool sectionVisible: false
  property double nowSeconds: 0
  property string description: ""
  signal clicked()
  signal activated()

  readonly property string kind: Format.kindOf(root.modelData)
  readonly property var keywords: root.modelData.keywords || []
  readonly property string titleText: String(root.modelData.summary || "").replace(/\s*[\r\n]+\s*/g, " ")
  readonly property string descriptionText: root.description.length > 0 ? root.description : Format.kindName(root.kind)
  readonly property string ageText: Format.compactAge(root.modelData.copiedUnixSeconds, root.nowSeconds)
  readonly property int rowHeight: Math.round(52 * root.theme.scale)
  readonly property alias titleControl: title
  readonly property alias descriptionControl: descriptionLabel
  readonly property alias ageControl: age
  readonly property alias railControl: rail
  readonly property alias cardControl: card
  readonly property alias sectionControl: section

  implicitHeight: section.height + root.rowHeight

  Item {
    id: section
    visible: root.sectionVisible
    width: parent.width
    height: root.sectionVisible ? Math.round(root.theme.fontCaption * 2.6) : 0
    Accessible.role: Accessible.Heading
    Accessible.name: sectionTitle.text

    Text {
      id: sectionTitle
      anchors.left: parent.left
      anchors.leftMargin: Math.round(6 * root.theme.scale)
      anchors.verticalCenter: parent.verticalCenter
      text: root.modelData.pinned ? "PINNED" : "RECENT"
      color: root.theme.textFaint
      font.family: root.theme.fontFamily
      font.pixelSize: root.theme.fontCaption
      font.weight: Font.DemiBold
      font.letterSpacing: 1
    }

    Rectangle {
      anchors.left: sectionTitle.right
      anchors.leftMargin: Math.round(8 * root.theme.scale)
      anchors.right: parent.right
      anchors.rightMargin: Math.round(6 * root.theme.scale)
      anchors.verticalCenter: parent.verticalCenter
      height: 1
      color: root.theme.separator
    }
  }

  Rectangle {
    id: card
    y: section.height
    width: parent.width
    height: root.rowHeight
    radius: root.theme.radiusSmall
    color: root.selected ? root.theme.withAlpha(root.theme.accent, root.listFocused ? 0.26 : 0.18)
      : mouse.containsMouse ? root.theme.withAlpha(root.theme.text, 0.06) : "transparent"
    border.width: root.selected ? (root.listFocused ? 2 : 1) : 0
    border.color: root.theme.withAlpha(root.theme.accent, root.listFocused ? 1 : 0.6)
    Accessible.role: Accessible.ListItem
    Accessible.name: root.titleText + ", " + root.descriptionText
      + (root.keywords.length > 0 ? ", keywords " + root.keywords.join(", ") : "")
      + (root.modelData.pinned ? ", pinned" : "")
      + ", copied " + Format.fullTimestamp(root.modelData.copiedUnixSeconds)
    Accessible.selected: root.selected
    Accessible.onPressAction: root.clicked()

    Rectangle {
      id: rail
      visible: root.selected
      anchors.left: parent.left
      anchors.leftMargin: Math.round(4 * root.theme.scale)
      anchors.verticalCenter: parent.verticalCenter
      width: Math.max(3, Math.round(3 * root.theme.scale))
      height: parent.height - Math.round(18 * root.theme.scale)
      radius: width / 2
      color: root.theme.accent
    }

    RowLayout {
      anchors.fill: parent
      anchors.leftMargin: Math.round(14 * root.theme.scale)
      anchors.rightMargin: Math.round(10 * root.theme.scale)
      spacing: Math.round(10 * root.theme.scale)

      Rectangle {
        Layout.alignment: Qt.AlignVCenter
        implicitWidth: Math.round(28 * root.theme.scale)
        implicitHeight: implicitWidth
        radius: Math.max(4, root.theme.radiusSmall - 1)
        color: root.selected ? root.theme.accent : root.theme.withAlpha(root.theme.text, 0.07)

        Text {
          anchors.centerIn: parent
          text: Format.kindGlyph(root.kind)
          color: root.selected ? root.theme.background : root.theme.textMuted
          font.family: root.theme.fontFamily
          font.pixelSize: root.kind === Protocol.ClipboardPresentationKinds.Text ? root.theme.fontTitle : Math.round(root.theme.fontHeading * 1.15)
          font.weight: Font.DemiBold
        }
      }

      ColumnLayout {
        Layout.fillWidth: true
        Layout.minimumWidth: 0
        Layout.alignment: Qt.AlignVCenter
        spacing: Math.round(3 * root.theme.scale)

        RowLayout {
          Layout.fillWidth: true
          spacing: Math.round(8 * root.theme.scale)

          Text {
            id: title
            Layout.fillWidth: true
            Layout.minimumWidth: 0
            text: root.titleText
            textFormat: Text.PlainText
            maximumLineCount: 1
            elide: root.kind === Protocol.ClipboardPresentationKinds.Links || root.kind === Protocol.ClipboardPresentationKinds.Files ? Text.ElideMiddle : Text.ElideRight
            color: root.theme.text
            font.family: root.theme.fontFamily
            font.pixelSize: root.theme.fontSubtitle
            font.weight: root.selected ? Font.DemiBold : Font.Normal
          }

          Text {
            id: age
            text: root.ageText
            color: root.selected ? root.theme.textMuted : root.theme.textFaint
            font.family: root.theme.fontFamily
            font.pixelSize: root.theme.fontCaption
          }
        }

        RowLayout {
          Layout.fillWidth: true
          spacing: Math.round(6 * root.theme.scale)

          Text {
            id: descriptionLabel
            text: root.descriptionText
            textFormat: Text.PlainText
            color: root.theme.textMuted
            font.family: root.theme.fontFamily
            font.pixelSize: root.theme.fontSmall
          }

          Text {
            Layout.fillWidth: true
            Layout.minimumWidth: 0
            text: root.keywords.length > 0 ? "· " + root.keywords.join(", ") : ""
            textFormat: Text.PlainText
            maximumLineCount: 1
            elide: Text.ElideRight
            color: root.theme.withAlpha(root.theme.accent, 0.9)
            font.family: root.theme.fontFamily
            font.pixelSize: root.theme.fontSmall
          }
        }
      }
    }
  }

  MouseArea {
    id: mouse
    anchors.fill: parent
    hoverEnabled: true
    onClicked: root.clicked()
    onDoubleClicked: root.activated()
  }
}
