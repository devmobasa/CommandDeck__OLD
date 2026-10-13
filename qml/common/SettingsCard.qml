import QtQuick
import QtQuick.Layouts

// One titled group of related settings. Cards give the page rhythm: a title,
// one sentence of intent, then rows. Content goes into `body`.
Rectangle {
  id: root

  required property var theme
  function tint(value, amount) { return Qt.rgba(value.r, value.g, value.b, amount) }
  property string title: ""
  property string description: ""
  property string searchQuery: ""
  property string searchTerms: ""
  property var wheelForwarder: null
  readonly property bool matchesSearch: {
    const text = (root.title + " " + root.description + " " + root.searchTerms).toLowerCase()
    return root.searchQuery.toLowerCase().trim().split(/\s+/).every(term => text.indexOf(term) >= 0)
  }
  visible: matchesSearch
  property bool danger: false
  default property alias body: content.data

  readonly property int pad: Math.round(18 * root.theme.scale)

  Layout.fillWidth: true
  implicitHeight: column.implicitHeight + root.pad * 2
  radius: Math.max(6, root.theme.radius)
  color: root.theme.surface
  border.width: 1
  border.color: root.danger ? root.tint(root.theme.urgent, 0.35) : root.theme.separator
  Accessible.role: Accessible.Grouping
  Accessible.name: root.title

  WheelHandler {
    target: null
    enabled: typeof root.wheelForwarder === "function"
    onWheel: event => root.wheelForwarder(event)
  }

  ColumnLayout {
    id: column
    anchors.fill: parent
    anchors.margins: root.pad
    spacing: Math.round(10 * root.theme.scale)

    ColumnLayout {
      Layout.fillWidth: true
      spacing: Math.round(3 * root.theme.scale)
      visible: root.title.length > 0 || root.description.length > 0

      Text {
        Layout.fillWidth: true
        visible: root.title.length > 0
        text: root.title
        color: root.danger ? root.theme.urgent : root.theme.text
        font.family: root.theme.fontFamily
        font.pixelSize: root.theme.fontHeading
        font.weight: Font.DemiBold
        elide: Text.ElideRight
      }

      Text {
        Layout.fillWidth: true
        visible: root.description.length > 0
        text: root.description
        color: root.theme.textMuted
        wrapMode: Text.Wrap
        font.family: root.theme.fontFamily
        font.pixelSize: root.theme.fontBody
      }
    }

    ColumnLayout {
      id: content
      Layout.fillWidth: true
      spacing: Math.round(4 * root.theme.scale)
    }
  }
}
