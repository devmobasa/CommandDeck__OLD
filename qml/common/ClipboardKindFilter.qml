pragma ComponentBehavior: Bound
import QtQuick
import QtQuick.Controls

// Segmented kind filter and a single Tab stop. The selected segment takes Tab
// focus; Left/Right wrap to the neighbouring kind and Home/End jump to the
// ends, applying the filter while focus stays in the group. A click applies
// the filter without taking focus from search.
FocusScope {
  id: root

  required property var theme
  required property var kinds
  required property string selectedKind
  property var labelFor: function(kind) { return kind }
  readonly property int selectedIndex: Math.max(0, root.kinds.indexOf(root.selectedKind))
  signal kindRequested(string kind)

  function segmentAt(index) { return segments.itemAt(index) }

  function focusSelected() {
    const segment = segments.itemAt(root.selectedIndex)
    if (segment) segment.forceActiveFocus()
  }

  // Focus moves before the kind changes: the segment losing selection leaves the
  // Tab chain, which Qt refuses while that segment still holds active focus.
  function choose(index) {
    const count = root.kinds.length
    const target = ((index % count) + count) % count
    const segment = segments.itemAt(target)
    if (segment) segment.forceActiveFocus()

    if (root.kinds[target] !== root.selectedKind) root.kindRequested(root.kinds[target])
  }

  implicitWidth: track.implicitWidth
  implicitHeight: Math.round(32 * root.theme.scale)
  Accessible.role: Accessible.Grouping
  Accessible.name: "Filter by kind"

  Rectangle {
    id: track
    readonly property int inset: Math.max(2, Math.round(3 * root.theme.scale))
    anchors.fill: parent
    implicitWidth: segmentRow.implicitWidth + inset * 2
    radius: Math.max(4, root.theme.radiusSmall)
    color: root.theme.surface
    border.width: 1
    border.color: root.theme.separator

    Row {
      id: segmentRow
      anchors.left: parent.left
      anchors.top: parent.top
      anchors.bottom: parent.bottom
      anchors.margins: track.inset
      spacing: Math.max(1, Math.round(2 * root.theme.scale))

      Repeater {
        id: segments
        model: root.kinds

        delegate: KeyboardButton {
          id: segment
          required property string modelData
          required property int index
          readonly property bool current: segment.modelData === root.selectedKind

          height: segmentRow.height
          text: root.labelFor(segment.modelData)
          // A focused segment stays in the Tab chain until focus leaves, even when
          // Escape or another shortcut clears its selection underneath it.
          focusPolicy: segment.current || segment.activeFocus ? Qt.TabFocus : Qt.NoFocus
          hoverEnabled: true
          leftPadding: Math.round(11 * root.theme.scale)
          rightPadding: leftPadding
          topPadding: 0
          bottomPadding: 0
          Accessible.role: Accessible.RadioButton
          Accessible.name: segment.text
          Accessible.checkable: true
          Accessible.checked: segment.current

          Keys.onPressed: event => {
            if (event.modifiers !== Qt.NoModifier) return
            if (event.key === Qt.Key_Left) root.choose(segment.index - 1)
            else if (event.key === Qt.Key_Right) root.choose(segment.index + 1)
            else if (event.key === Qt.Key_Home) root.choose(0)
            else if (event.key === Qt.Key_End) root.choose(root.kinds.length - 1)
            else return
            event.accepted = true
          }
          onClicked: {
            if (segment.activeFocus) root.choose(segment.index)
            else if (!segment.current) root.kindRequested(segment.modelData)
          }

          contentItem: Text {
            text: segment.text
            horizontalAlignment: Text.AlignHCenter
            verticalAlignment: Text.AlignVCenter
            color: segment.current ? root.theme.text : root.theme.textMuted
            font.family: root.theme.fontFamily
            font.pixelSize: root.theme.fontSmall
            font.weight: segment.current ? Font.DemiBold : Font.Normal
          }
          background: Rectangle {
            radius: Math.max(3, track.radius - track.inset)
            color: segment.current
              ? root.theme.withAlpha(root.theme.accent, segment.activeFocus ? 0.36 : 0.24)
              : segment.hovered ? root.theme.withAlpha(root.theme.text, 0.07) : "transparent"
            border.width: segment.activeFocus ? 2 : 0
            border.color: root.theme.accent
          }
        }
      }
    }
  }
}
