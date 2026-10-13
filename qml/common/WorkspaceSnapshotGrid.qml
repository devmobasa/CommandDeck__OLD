pragma ComponentBehavior: Bound
import QtQuick

Item {
  id: root
  required property var theme
  property var entries: []
  // Supplied by the host adapter, never inferred from the daemon connection.
  property bool permitted: false
  property string epoch: ""
  property string selectionKey: ""
  property Component captureComponent: null
  property var settledEntries: []
  readonly property int count: settledEntries.length
  readonly property int columns: count === 1 ? 1 : 2
  readonly property int rows: Math.max(1, Math.ceil(count / columns))

  function reset() {
    settle.stop()
    settledEntries = []
    if (permitted && epoch.length > 0 && entries.length > 0) settle.restart()
  }

  onEntriesChanged: reset()
  onPermittedChanged: reset()
  onEpochChanged: reset()
  onSelectionKeyChanged: reset()

  Timer {
    id: settle
    interval: 150
    onTriggered: {
      if (!root.permitted || root.epoch.length === 0) return
      // Bound source pixels as well as cell count. Two native buffers per
      // source are budgeted; this is preflight, not an allocator hard limit.
      let pixels = 0
      const accepted = []
      for (const entry of root.entries.slice(0, 64)) {
        const area = entry.width * entry.height
        if (!entry.source || !Number.isFinite(area) || entry.width <= 0 || entry.height <= 0
            || entry.width > 4096 || entry.height > 4096 || area > 8388608) continue
        if (pixels + area > 16777216) continue
        accepted.push(entry)
        pixels += area
        if (accepted.length === 4) break
      }
      root.settledEntries = accepted
    }
  }

  Repeater {
    model: root.settledEntries
    delegate: Rectangle {
      id: cell
      required property var modelData
      required property int index
      readonly property var capture: captureLoader.item
      objectName: "workspaceSnapshotCell" + index
      x: (index % root.columns) * root.width / root.columns
      y: Math.floor(index / root.columns) * root.height / root.rows
      width: root.width / root.columns
      height: root.height / root.rows
      color: root.theme.surface
      clip: true

      Loader {
        id: captureLoader
        anchors.fill: parent
        anchors.margins: 4
        anchors.bottomMargin: 26
        active: root.permitted && root.epoch.length > 0
        Component.onCompleted: {
          if (root.captureComponent) sourceComponent = root.captureComponent
          else setSource(Qt.resolvedUrl("WorkspaceSnapshotCell.qml"), { source: cell.modelData.source })
        }
        onLoaded: {
          cell.capture.source = cell.modelData.source
          cell.capture.permitted = Qt.binding(() => root.permitted && root.epoch.length > 0)
        }
      }
      Text {
        anchors.centerIn: parent
        width: parent.width - 8
        horizontalAlignment: Text.AlignHCenter
        visible: cell.capture === null || !cell.capture.hasContent
        text: cell.capture && cell.capture.expired ? "Preview unavailable" : "Loading snapshot…"
        textFormat: Text.PlainText
        color: root.theme.textMuted
        font.pixelSize: root.theme.fontSmall
      }
      Text {
        anchors.left: parent.left
        anchors.right: parent.right
        anchors.bottom: parent.bottom
        anchors.margins: 4
        text: cell.modelData.label
        textFormat: Text.PlainText
        elide: Text.ElideRight
        color: root.theme.textMuted
        font.family: root.theme.fontFamily
        font.pixelSize: root.theme.fontSmall
      }
    }
  }
}
