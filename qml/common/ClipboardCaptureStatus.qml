pragma ComponentBehavior: Bound
import QtQuick
import QtQuick.Layouts
import "ClipboardFormat.js" as Format

// Capture status and its pause toggle in the clipboard browser header, beside
// the other history-wide controls. The label elides rather than widening the
// header. A request names the toggle as the invoker only when it holds keyboard
// focus, so the consent dialog can hand focus back to it.
RowLayout {
  id: root

  required property var theme
  required property var controller
  readonly property alias captureControl: captureToggle
  readonly property alias labelControl: label
  readonly property var policy: root.controller.settings && root.controller.settings.policy ? root.controller.settings.policy : null
  readonly property string captureStatus: root.controller.settings ? String(root.controller.settings.status || "") : ""
  readonly property string captureTone: Format.captureTone(root.captureStatus)
  signal captureRequested(Item invoker)

  spacing: Math.round(4 * root.theme.scale)

  Rectangle {
    Layout.alignment: Qt.AlignVCenter
    implicitWidth: Math.round(8 * root.theme.scale)
    implicitHeight: implicitWidth
    radius: implicitWidth / 2
    color: root.captureTone === "warning" ? root.theme.urgent
      : root.captureTone === "idle" ? root.theme.textFaint : root.theme.accent
  }
  Text {
    id: label
    Layout.fillWidth: true
    Layout.minimumWidth: 0
    // Whole pixels: layouts round sizes, and a fractional shortfall would elide the label.
    Layout.preferredWidth: Math.min(Math.ceil(label.implicitWidth), Math.round(240 * root.theme.scale))
    Layout.maximumWidth: Layout.preferredWidth
    text: Format.captureLabel(root.captureStatus)
    textFormat: Text.PlainText
    elide: Text.ElideRight
    color: root.captureTone === "warning" ? root.theme.urgent : root.theme.textMuted
    font.family: root.theme.fontFamily
    font.pixelSize: root.theme.fontSmall
    Accessible.role: Accessible.StaticText
    Accessible.name: "Capture status: " + text
  }
  ClipboardActionButton {
    id: captureToggle
    theme: root.theme
    text: !root.policy || root.policy.consent === "None" ? "Enable capture…" : root.policy.paused ? "Resume" : "Pause"
    enabled: root.controller.settings !== null
    onClicked: root.captureRequested(captureToggle.activeFocus ? captureToggle : null)
  }
}
