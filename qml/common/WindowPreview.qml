pragma ComponentBehavior: Bound
import QtQuick
import "Protocol.js" as Protocol
import Quickshell.Wayland

Item {
  id: root

  required property var theme
  property var result: null
  property bool opened: false
  property bool selected: false
  property DesktopIntegration integration: null
  property var toplevels: []
  property string settledAddress: ""
  property bool captureTimedOut: false
  // Temporary diagnostics for alternating frames after switching windows.
  property bool debugCapture: true
  property int debugEvents: 0
  property int captureSerial: 0
  readonly property var captureItem: captureLoader.item

  function traceCapture(event) {
    if (!debugCapture || debugEvents >= 400) return
    debugEvents++
    const view = root.captureItem
    console.log("[CD-PREVIEW] " + JSON.stringify({
      time: Date.now(), event: event, instance: String(root), capture: captureSerial,
      target: targetAddress, settled: settledAddress,
      matched: toplevel ? root.normalizeAddress(toplevel.address) : "",
      source: toplevel ? String(toplevel.wayland) : "",
      opened: opened, selected: selected, active: active, visible: visible,
      loaded: captureLoaded, timedOut: captureTimedOut,
      content: view ? view.hasContent : false,
      captureSource: view ? String(view.sourceIdentity) : "",
      sourceSize: view ? String(view.frameSize) : ""
    }))
  }

  Timer {
    interval: 500
    repeat: true
    running: root.debugCapture && root.opened && root.selected && root.debugEvents < 400
    onTriggered: root.traceCapture("heartbeat")
  }

  readonly property string targetAddress: root.result
    && root.result.kind === Protocol.ResultKinds.Window
    && root.result.metadata
    && root.result.metadata.address
      ? root.normalizeAddress(root.result.metadata.address)
      : ""
  readonly property var toplevel: root.findToplevel(
    root.settledAddress,
    root.toplevels)
  readonly property bool supported: !!root.integration
    && !!root.integration.capabilities
    && root.integration.capabilities.windowPreviews
  readonly property bool active: root.opened && root.selected
    && root.supported
    && root.toplevel !== null
  readonly property bool hasPreview: root.active
    && root.captureItem !== null
    && root.captureItem.hasContent
  readonly property bool captureLoaded: captureLoader.item !== null
  readonly property bool showCaptureArea: root.active
    && (!root.captureTimedOut || root.hasPreview)
  readonly property int previewHeight: Math.round(180 * root.theme.scale)

  implicitHeight: root.showCaptureArea ? root.previewHeight : 0
  clip: true

  function refreshToplevels() {
    if (!root.integration) return
    const values = root.integration.windowToplevels || []
    const next = []
    for (let index = 0; index < values.length; index++) {
      next.push(values[index])
    }
    root.toplevels = next
  }

  function armCaptureDeadline() {
    captureDeadline.stop()
    root.captureTimedOut = false
    if (root.active && !root.hasPreview) {
      captureDeadline.restart()
    }
  }

  function normalizeAddress(address) {
    let normalized = String(address || "").trim().toLowerCase()
    if (normalized.indexOf("0x") === 0) {
      normalized = normalized.slice(2)
    }
    return normalized
  }

  function findToplevel(address, candidates) {
    const normalized = root.normalizeAddress(address)
    if (normalized.length === 0 || !candidates) {
      return null
    }

    for (let index = 0; index < candidates.length; index++) {
      const candidate = candidates[index]
      if (candidate
          && root.normalizeAddress(candidate.address) === normalized) {
        return candidate
      }
    }

    return null
  }

  onTargetAddressChanged: {
    root.traceCapture("target-changed")
    if (root.targetAddress.length === 0) {
      settleTimer.stop()
      root.settledAddress = ""
    } else {
      settleTimer.restart()
    }
  }

  onOpenedChanged: {
    if (root.opened) root.debugEvents = 0
    root.traceCapture("opened-changed")
    if (root.opened) {
      if (root.integration) root.integration.refreshWindows()
      root.refreshToplevels()
    }
    root.armCaptureDeadline()
  }

  onSettledAddressChanged: root.traceCapture("settled-changed")
  onSelectedChanged: { root.traceCapture("selected-changed"); root.armCaptureDeadline() }
  onToplevelChanged: { root.traceCapture("toplevel-changed"); root.armCaptureDeadline() }
  onActiveChanged: { root.traceCapture("active-changed"); root.armCaptureDeadline() }
  onHasPreviewChanged: {
    root.traceCapture("preview-content-changed")
    if (root.hasPreview) {
      captureDeadline.stop()
      root.captureTimedOut = false
    }
  }

  Component.onCompleted: {
    root.traceCapture("diagnostics-loaded-v1")
    root.refreshToplevels()
    if (root.targetAddress.length > 0) {
      settleTimer.restart()
    }
  }

  Connections {
    target: root.integration
    enabled: root.opened

    function onWindowsChanged() {
      root.refreshToplevels()
    }
  }

  Timer {
    id: settleTimer

    interval: 80
    repeat: false
    onTriggered: root.settledAddress = root.targetAddress
  }

  Timer {
    id: captureDeadline

    interval: 1200
    repeat: false
    onTriggered: root.captureTimedOut = !root.hasPreview
  }

  Rectangle {
    width: root.width
    height: root.previewHeight
    visible: root.showCaptureArea
    radius: root.theme.radiusSmall
    color: root.theme.surfaceStrong
    clip: true

    Loader {
      id: captureLoader

      anchors.fill: parent
      active: root.active && !root.captureTimedOut
      asynchronous: false
      onLoaded: { root.captureSerial++; root.traceCapture("capture-created") }

      sourceComponent: Item {
        readonly property bool hasContent: capture.hasContent
        readonly property var sourceIdentity: capture.captureSource
        readonly property size frameSize: capture.sourceSize
        Component.onDestruction: root.traceCapture("capture-destroyed")

        ScreencopyView {
          id: capture

          anchors.fill: parent
          captureSource: root.toplevel ? root.toplevel.wayland : null
          live: root.active
          paintCursor: false
          onCaptureSourceChanged: root.traceCapture("capture-source-changed")
          onSourceSizeChanged: root.traceCapture("capture-size-changed")
          onStopped: root.traceCapture("capture-stopped")
          constraintSize: Qt.size(
            Math.max(1, width),
            Math.max(1, height))
        }
      }
    }

    Text {
      anchors.centerIn: parent
      visible: !root.hasPreview
      text: "Loading preview…"
      color: root.theme.textFaint
      font.family: root.theme.fontFamily
      font.pixelSize: root.theme.fontSmall
    }
  }
}
