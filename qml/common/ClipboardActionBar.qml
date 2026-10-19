pragma ComponentBehavior: Bound
import QtQuick
import QtQuick.Layouts

// Bottom bar of the clipboard browser: actions for the selected entry with
// their shortcuts. History-wide controls, including capture, live in the
// header. Clicks never take focus from search; a request names its button as
// the invoker only when that button holds keyboard focus, so dialogs can hand
// focus back to it.
RowLayout {
  id: root

  required property var theme
  required property var controller
  readonly property alias copyControl: copy
  readonly property alias pinControl: pin
  readonly property alias sequenceControl: sequenceAction
  readonly property alias openControl: openAction
  readonly property alias keywordsControl: keywords
  readonly property alias deleteControl: deleteAction
  readonly property var policy: root.controller.settings && root.controller.settings.policy ? root.controller.settings.policy : null
  readonly property bool entrySelected: root.controller.selected !== null
  signal copyRequested(Item invoker)
  signal pinRequested()
  signal sequenceRequested()
  signal openRequested(Item invoker)
  signal keywordsRequested(Item invoker)
  signal deleteRequested()

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
  // Copy next clipboard entry continues from the selected entry; it needs capture consent.
  ClipboardActionButton {
    id: sequenceAction
    theme: root.theme
    text: "Start sequence"
    keyHint: "Ctrl+S"
    enabled: root.controller.mutationReady && root.policy !== null && root.policy.consent !== "None"
    onClicked: root.sequenceRequested()
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
}
