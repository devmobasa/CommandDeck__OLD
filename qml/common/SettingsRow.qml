import QtQuick
import QtQuick.Layouts

// Label and help on the left, the control on the right, one hairline below.
// The label column never collapses, so long control text cannot push the
// caption out of view the way the old free-form rows did.
//
// The row is responsive: when the width cannot hold the label column (at its
// minimum) and the control column (at its implicit/minimum width) side by
// side, the control drops beneath the label and takes the full row width. This
// keeps wide controls and their buttons from clipping at high display scale.
Item {
  id: root

  required property var theme
  property string label: ""
  property string help: ""
  property bool divider: true
  property int controlMinimumWidth: Math.round(220 * root.theme.scale)
  default property alias control: slot.data

  readonly property int rowSpacing: Math.round(16 * root.theme.scale)
  readonly property int stackSpacing: Math.round(6 * root.theme.scale)
  readonly property int labelMinimum: Math.round(160 * root.theme.scale)
  // The control column's natural width: its own content, but never below the
  // caller's floor.
  readonly property int controlMinimum: Math.max(root.controlMinimumWidth, Math.round(slot.implicitWidth))
  // Stack when the label column and the control column cannot fit beside each
  // other in the available width. `stacked` drives both the arrangement and the
  // height, so a spec can read it to confirm the reflow.
  readonly property bool stacked: root.width > 0
    && root.width < root.labelMinimum + root.controlMinimum + root.rowSpacing

  Layout.fillWidth: true
  implicitHeight: (root.stacked
    ? labels.implicitHeight + root.stackSpacing + slot.implicitHeight
    : Math.max(labels.implicitHeight, slot.implicitHeight, Math.round(40 * root.theme.scale)))
    + Math.round(16 * root.theme.scale)
  Accessible.role: Accessible.Grouping
  Accessible.name: root.label

  GridLayout {
    anchors.fill: parent
    anchors.topMargin: Math.round(8 * root.theme.scale)
    anchors.bottomMargin: Math.round(8 * root.theme.scale)
    // Two columns side by side, one column stacked.
    columns: root.stacked ? 1 : 2
    columnSpacing: root.rowSpacing
    rowSpacing: root.stackSpacing

    ColumnLayout {
      id: labels
      Layout.fillWidth: true
      Layout.minimumWidth: root.labelMinimum
      Layout.preferredWidth: root.stacked ? -1 : Math.round(root.width * 0.46)
      Layout.alignment: Qt.AlignVCenter
      spacing: Math.round(2 * root.theme.scale)

      Text {
        Layout.fillWidth: true
        textFormat: Text.PlainText
        text: root.label
        color: root.theme.text
        wrapMode: Text.Wrap
        font.family: root.theme.fontFamily
        font.pixelSize: Math.round(root.theme.fontBody * 1.15)
        font.weight: Font.Medium
      }

      Text {
        Layout.fillWidth: true
        visible: root.help.length > 0
        textFormat: Text.PlainText
        text: root.help
        color: root.theme.textMuted
        wrapMode: Text.Wrap
        font.family: root.theme.fontFamily
        font.pixelSize: root.theme.fontBody
      }
    }

    RowLayout {
      id: slot
      // The control column. It fills about half the row so fill-width controls
      // (pickers, fields) read as a proper column with a consistent left edge,
      // and grows to its content — never below controlMinimum — so multi-control
      // rows never clip. When stacked it fills the full row width beneath the
      // label. There is no upper cap: the stack decision guarantees the label
      // keeps its minimum, so a wide control takes the width it needs.
      Layout.fillWidth: true
      Layout.minimumWidth: root.controlMinimum
      Layout.preferredWidth: root.stacked ? -1 : Math.round(root.width * 0.5)
      Layout.alignment: Qt.AlignLeft | Qt.AlignVCenter
      spacing: Math.round(8 * root.theme.scale)
    }
  }

  Rectangle {
    anchors.left: parent.left
    anchors.right: parent.right
    anchors.bottom: parent.bottom
    height: 1
    visible: root.divider
    color: root.theme.separator
  }
}
