pragma ComponentBehavior: Bound
import QtQuick
import QtQuick.Layouts
import "TabFence.js" as TabFence

// Left rail of the settings dialog. One entry per page; Up/Down move the
// selection when the rail has focus.
Item {
  id: root

  required property var theme
  function tint(value, amount) { return Qt.rgba(value.r, value.g, value.b, amount) }
  property var pages: []
  property string currentId: ""
  signal selected(string pageId)

  readonly property int rowHeight: Math.round(54 * root.theme.scale)
  implicitWidth: Math.round(224 * root.theme.scale)
  implicitHeight: column.implicitHeight
  activeFocusOnTab: true
  Accessible.role: Accessible.List
  Accessible.name: "Settings pages"

  function indexOf(pageId) {
    for (let index = 0; index < (root.pages || []).length; index++) {
      if (root.pages[index].id === pageId) return index
    }
    return -1
  }

  function step(delta) {
    const count = (root.pages || []).length
    if (count === 0) return
    const next = Math.max(0, Math.min(count - 1, root.indexOf(root.currentId) + delta))
    root.selected(root.pages[next].id)
  }

  Keys.onUpPressed: root.step(-1)
  Keys.onDownPressed: root.step(1)
  // Tab and Shift+Tab stay inside the settings dialog.
  Keys.priority: Keys.BeforeItem
  Keys.onPressed: event => TabFence.route(root.parent, event)

  ColumnLayout {
    id: column
    anchors.left: parent.left
    anchors.right: parent.right
    spacing: Math.round(2 * root.theme.scale)

    Repeater {
      model: root.pages || []

      delegate: Rectangle {
        id: entry
        required property var modelData
        readonly property bool current: entry.modelData.id === root.currentId
        Layout.fillWidth: true
        Layout.preferredHeight: root.rowHeight
        radius: Math.max(4, root.theme.radiusSmall)
        color: entry.current
          ? root.theme.selectedBackground
          : hover.hovered ? root.tint(root.theme.text, 0.06) : "transparent"
        border.width: entry.current && root.activeFocus ? 2 : entry.current ? 1 : 0
        border.color: entry.current && root.activeFocus ? root.theme.focusRing : root.theme.selectedBorder
        Accessible.role: Accessible.ListItem
        Accessible.name: String(entry.modelData.title || "")
        Accessible.focusable: true

        HoverHandler { id: hover }
        TapHandler { onTapped: root.selected(entry.modelData.id) }

        RowLayout {
          anchors.fill: parent
          anchors.leftMargin: Math.round(12 * root.theme.scale)
          anchors.rightMargin: Math.round(10 * root.theme.scale)
          spacing: Math.round(12 * root.theme.scale)

          Text {
            Layout.preferredWidth: Math.round(22 * root.theme.scale)
            text: String(entry.modelData.icon || "")
            color: entry.current ? root.theme.selectedText : root.theme.textMuted
            horizontalAlignment: Text.AlignHCenter
            font.family: root.theme.fontFamily
            font.pixelSize: Math.round(root.theme.fontBody * 1.35)
          }

          ColumnLayout {
            Layout.fillWidth: true
            spacing: 0

            Text {
              Layout.fillWidth: true
              text: String(entry.modelData.title || "")
              color: entry.current ? root.theme.selectedText : root.theme.text
              elide: Text.ElideRight
              font.family: root.theme.fontFamily
              font.pixelSize: Math.round(root.theme.fontBody * 1.15)
              font.weight: entry.current ? Font.DemiBold : Font.Medium
            }

            Text {
              Layout.fillWidth: true
              visible: text.length > 0
              text: String(entry.modelData.subtitle || "")
              color: root.theme.textFaint
              elide: Text.ElideRight
              font.family: root.theme.fontFamily
              font.pixelSize: root.theme.fontSmall
            }
          }
        }
      }
    }
  }
}
