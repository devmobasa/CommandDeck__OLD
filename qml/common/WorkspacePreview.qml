pragma ComponentBehavior: Bound
import QtQuick
import QtQuick.Layouts

ColumnLayout {
  id: root
  required property var theme
  property var backend: null
  property var result: null
  property bool eligible: false
  property int issuedRequestId: 0
  property int resultRequestId: 0
  property string mode: "layout"
  property var snapshotLifecycle: null
  readonly property bool snapshotsPermitted: mode === "snapshots" && eligible
    && !!snapshotLifecycle && snapshotLifecycle.permitted === true
  readonly property alias model: previewModel
  readonly property bool hasContent: eligible
  visible: hasContent
  spacing: Math.round(8 * theme.scale)

  WorkspacePreviewModel {
    id: previewModel
    backend: root.backend
    result: root.result
    eligible: root.eligible
    issuedRequestId: root.issuedRequestId
    resultRequestId: root.resultRequestId
  }

  Text {
    Layout.fillWidth: true
    text: root.snapshotsPermitted && snapshotGrid.count > 0 ? "Window snapshots · last app frames" : previewModel.status
    textFormat: Text.PlainText
    color: root.theme.textMuted
    font.family: root.theme.fontFamily
    font.pixelSize: root.theme.fontSmall
  }

  Item {
    Layout.fillWidth: true
    Layout.preferredHeight: Math.round(180 * root.theme.scale)
    visible: previewModel.descriptors.length > 0 && (!root.snapshotsPermitted || snapshotGrid.count === 0)

    Rectangle {
      id: canvas
      objectName: "workspacePreviewCanvas"
      anchors.centerIn: parent
      readonly property real aspect: previewModel.monitorGeometry
        ? previewModel.monitorGeometry.width / previewModel.monitorGeometry.height : 1.7
      width: Math.min(parent.width, parent.height * aspect)
      height: Math.min(parent.height, parent.width / aspect)
      color: root.theme.background
      border.color: root.theme.separator
      clip: true

      Repeater {
        model: root.eligible ? previewModel.descriptors.slice(0, 24) : []
        delegate: Rectangle {
          id: outline
          objectName: "workspacePreviewOutline" + index
          required property var modelData
          required property int index
          readonly property bool positioned: previewModel.layoutAvailable && modelData.rect !== null
          readonly property int columns: Math.max(1, Math.min(3, previewModel.descriptors.length))
          x: positioned ? modelData.rect.x * canvas.width : (index % columns) * canvas.width / columns
          y: positioned ? modelData.rect.y * canvas.height
            : Math.floor(index / columns) * canvas.height / Math.max(1, Math.ceil(Math.min(24, previewModel.descriptors.length) / columns))
          width: positioned ? modelData.rect.width * canvas.width : canvas.width / columns
          height: positioned ? modelData.rect.height * canvas.height
            : canvas.height / Math.max(1, Math.ceil(Math.min(24, previewModel.descriptors.length) / columns))
          color: root.theme.surface
          border.color: root.theme.textFaint
          border.width: 1
          clip: true

          Text {
            anchors.fill: parent
            anchors.margins: 4
            text: outline.modelData.label + (outline.modelData.title ? "\n" + outline.modelData.title : "")
            textFormat: Text.PlainText
            color: root.theme.textMuted
            font.family: root.theme.fontFamily
            font.pixelSize: root.theme.fontSmall
            wrapMode: Text.Wrap
            elide: Text.ElideRight
          }
        }
      }
    }
  }

  WorkspaceSnapshotGrid {
    id: snapshotGrid
    Layout.fillWidth: true
    Layout.preferredHeight: Math.round(180 * root.theme.scale)
    visible: root.snapshotsPermitted && count > 0
    theme: root.theme
    permitted: root.snapshotsPermitted
    epoch: root.snapshotLifecycle ? root.snapshotLifecycle.epoch : ""
    selectionKey: JSON.stringify(previewModel.identity) + ":" + root.resultRequestId
    entries: previewModel.snapshotEntries
  }

  Text {
    Layout.fillWidth: true
    readonly property int omitted: root.snapshotsPermitted && snapshotGrid.count > 0
      ? Math.max(0, previewModel.totalCount - snapshotGrid.count) : previewModel.overflow
    visible: omitted > 0
    text: omitted + " more windows not shown"
    textFormat: Text.PlainText
    color: root.theme.textFaint
    font.pixelSize: root.theme.fontSmall
  }

  Text {
    Layout.fillWidth: true
    visible: root.mode === "snapshots" && !root.snapshotsPermitted
    text: "Snapshots unavailable: this desktop does not expose the required lock and compositor lifecycle signals."
    textFormat: Text.PlainText
    wrapMode: Text.Wrap
    color: root.theme.textFaint
    font.pixelSize: root.theme.fontSmall
  }
}
