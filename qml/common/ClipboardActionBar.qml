pragma ComponentBehavior: Bound
import QtQuick
import QtQuick.Layouts
import "ClipboardFormat.js" as Format

// Bottom bar of the clipboard browser: actions for the selected entry with
// their shortcuts, then the capture status and its pause toggle. Clicks never
// take focus from search; a request names its button as the invoker only when
// that button holds keyboard focus, so dialogs can hand focus back to it.
RowLayout {
  id: root

  required property var theme
  required property var controller
  readonly property alias copyControl: copy
  readonly property alias pinControl: pin
  readonly property alias openControl: openAction
  readonly property alias keywordsControl: keywords
  readonly property alias deleteControl: deleteAction
  readonly property alias captureControl: captureToggle
  readonly property var policy: root.controller.settings && root.controller.settings.policy ? root.controller.settings.policy : null
  readonly property string captureStatus: root.controller.settings ? String(root.controller.settings.status || "") : ""
  readonly property string captureTone: Format.captureTone(root.captureStatus)
  readonly property bool entrySelected: root.controller.selected !== null
  signal copyRequested(Item invoker)
  signal pinRequested()
  signal openRequested(Item invoker)
  signal keywordsRequested(Item invoker)
  signal deleteRequested()
  signal captureRequested(Item invoker)

  spacing: Math.round(4 * root.theme.scale)

  ClipboardActionButton {
    id: copy
    theme: root.theme
    tone: "primary"
    text: root.controller.pending.copy ? "Copying…" : "Copy"
    keyHint: "Enter"
    enabled: root.controller.canCopy && root.entrySelected
    onClicked: root.copyRequested(copy.activeFocus ? copy : null)
  }
  ClipboardActionButton {
    id: pin
    theme: root.theme
    text: root.controller.selected && root.controller.selected.pinned ? "Unpin" : "Pin"
    keyHint: "Ctrl+P"
    enabled: root.controller.mutationReady
    onClicked: root.pinRequested()
  }
  ClipboardActionButton {
    id: openAction
    theme: root.theme
    text: root.controller.pending.prepare ? "Resolving…" : "Inspect Open…"
    keyHint: "Ctrl+K"
    enabled: root.controller.canInspectOpenHandlers && root.entrySelected && root.controller.selected.presentation !== undefined
      && root.controller.selected.presentation.hasOpenDestination === true && !root.controller.pending.prepare
    onClicked: root.openRequested(openAction.activeFocus ? openAction : null)
  }
  ClipboardActionButton {
    id: keywords
    theme: root.theme
    text: "Keywords…"
    keyHint: "Ctrl+E"
    enabled: root.controller.mutationReady
    onClicked: root.keywordsRequested(keywords.activeFocus ? keywords : null)
  }
  ClipboardActionButton {
    id: deleteAction
    theme: root.theme
    tone: "danger"
    text: "Delete"
    keyHint: "Shift+Del"
    enabled: root.controller.mutationReady
    onClicked: root.deleteRequested()
  }
  Item { Layout.fillWidth: true; Layout.minimumWidth: 0 }
  Rectangle {
    Layout.alignment: Qt.AlignVCenter
    implicitWidth: Math.round(8 * root.theme.scale)
    implicitHeight: implicitWidth
    radius: implicitWidth / 2
    color: root.captureTone === "warning" ? root.theme.urgent
      : root.captureTone === "idle" ? root.theme.textFaint : root.theme.accent
  }
  Text {
    Layout.preferredWidth: Math.min(implicitWidth, Math.round(240 * root.theme.scale))
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
