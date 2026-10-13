pragma ComponentBehavior: Bound
import QtQuick
import QtQuick.Controls
import QtQuick.Layouts
import "Protocol.js" as Protocol
import "UiConstants.js" as Ui

Dialog {
  id: dialog
  required property var theme
  required property var controller
  property Item returnFocus: null
  property double policyRevision: 0
  property var review: null
  property bool submitted: false
  readonly property alias countControl: count
  readonly property alias ageControl: age
  readonly property alias quotaControl: quota
  readonly property alias clearControl: startup
  readonly property alias cancelControl: cancel
  readonly property alias saveControl: save
  width: Math.min(parent.width - 32, 560)
  anchors.centerIn: parent
  modal: true
  focus: true
  closePolicy: Popup.CloseOnEscape
  padding: Math.round(20 * dialog.theme.scale)
  topPadding: Math.round(14 * dialog.theme.scale)
  title: review ? "Confirm clipboard settings" : "Clipboard limits"

  function show(invoker) {
    const policy = controller.settings.policy
    returnFocus = invoker
    policyRevision = policy.revision
    count.value = policy.maximumEntries
    age.value = policy.retentionHours
    quota.value = policy.quotaMiB
    startup.checked = policy.clearOnStartup === true
    review = null
    submitted = false
    controller.error = ""
    open()
  }
  onOpened: count.contentItem.forceActiveFocus()
  onClosed: { submitted = false; review = null; if (returnFocus && returnFocus.visible) returnFocus.forceActiveFocus() }

  function submit() {
    submitted = true
    if (review) {
      controller.send("settingsSet", Protocol.Operations.ClipboardSettingsSet,
        {policyRevision: review.policyRevision, storeRevision: review.storeRevision}, true, undefined, review.token)
    } else {
      controller.send("settingsSet", Protocol.Operations.ClipboardSettingsSet,
        {policyRevision: policyRevision, patch: {maximumEntries: count.value, retentionHours: age.value,
          quotaMiB: quota.value, clearOnStartup: startup.checked}})
    }
  }

  Connections {
    target: dialog.controller
    function onSettingsChanged() {
      if (!dialog.opened || !dialog.submitted) return
      dialog.submitted = false
      const value = dialog.controller.settings
      if (value && value.policyReview) {
        dialog.review = value.policyReview
        cancel.forceActiveFocus()
      } else dialog.close()
    }
    function onErrorChanged() { if (dialog.controller.error.length > 0) dialog.submitted = false }
  }

  // An opaque panel over a dimmed page, matching the other clipboard dialogs.
  Overlay.modal: Rectangle { color: dialog.theme.scrimColor }
  background: Rectangle {
    color: dialog.theme.popupBackground
    radius: dialog.theme.radius
    border.width: 1
    border.color: dialog.theme.withAlpha(dialog.theme.text, 0.16)
  }
  // Constant paddings and no elide: a padded, elided title feeds back into the
  // Dialog's implicit width and loops. Titles are short enough to fit.
  header: Label {
    text: dialog.title
    textFormat: Text.PlainText
    leftPadding: Math.round(20 * dialog.theme.scale)
    rightPadding: Math.round(20 * dialog.theme.scale)
    topPadding: Math.round(20 * dialog.theme.scale)
    color: dialog.theme.text
    font.family: dialog.theme.fontFamily
    font.pixelSize: dialog.theme.fontHeading
    font.weight: Font.DemiBold
  }
  contentItem: ColumnLayout {
    spacing: Math.round(12 * dialog.theme.scale)
    GridLayout {
      Layout.fillWidth: true
      visible: !dialog.review
      columns: 2
      columnSpacing: Math.round(16 * dialog.theme.scale)
      rowSpacing: Math.round(8 * dialog.theme.scale)
      Label { Layout.fillWidth: true; text: "Maximum entries"; color: dialog.theme.text; font.family: dialog.theme.fontFamily; font.pixelSize: dialog.theme.fontBody }
      SpinBox { id: count; from: 10; to: 1000; editable: true; font.family: dialog.theme.fontFamily; Accessible.name: "Maximum clipboard entries"; KeyNavigation.priority: KeyNavigation.BeforeItem; KeyNavigation.tab: age; KeyNavigation.backtab: save }
      Label { Layout.fillWidth: true; text: "Retention in hours"; color: dialog.theme.text; font.family: dialog.theme.fontFamily; font.pixelSize: dialog.theme.fontBody }
      SpinBox { id: age; from: 1; to: 8760; editable: true; font.family: dialog.theme.fontFamily; Accessible.name: "Clipboard retention hours"; KeyNavigation.priority: KeyNavigation.BeforeItem; KeyNavigation.tab: quota; KeyNavigation.backtab: count }
      Label { Layout.fillWidth: true; text: "Payload quota in MiB"; color: dialog.theme.text; font.family: dialog.theme.fontFamily; font.pixelSize: dialog.theme.fontBody }
      SpinBox { id: quota; from: 64; to: 1024; editable: true; font.family: dialog.theme.fontFamily; Accessible.name: "Clipboard payload quota"; KeyNavigation.priority: KeyNavigation.BeforeItem; KeyNavigation.tab: startup; KeyNavigation.backtab: age }
      CheckBox {
        id: startup
        Layout.columnSpan: 2
        Layout.topMargin: Math.round(4 * dialog.theme.scale)
        text: "Clear history on daemon startup, including pins"
        font.family: dialog.theme.fontFamily
        font.pixelSize: dialog.theme.fontBody
        KeyNavigation.tab: cancel
        KeyNavigation.backtab: quota
      }
    }
    Label {
      Layout.fillWidth: true
      visible: dialog.review !== null
      text: dialog.review ? "Apply " + dialog.review.proposed.maximumEntries + " entries, " + dialog.review.proposed.retentionHours
        + " hours and " + dialog.review.proposed.quotaMiB + " MiB? This deletes " + dialog.review.deleteCount
        + " unpinned entries now. " + dialog.review.pinnedCount + " pins remain."
        + (dialog.review.proposed.clearOnStartup ? " Clear-on-startup will also delete all entries, including pins, when startup completes." : "") : ""
      textFormat: Text.PlainText
      wrapMode: Text.Wrap
      lineHeight: 1.2
      color: dialog.theme.text
      font.family: dialog.theme.fontFamily
      font.pixelSize: dialog.theme.fontBody
    }
    Label {
      Layout.fillWidth: true
      text: dialog.controller.error
      textFormat: Text.PlainText
      wrapMode: Text.Wrap
      visible: text.length > 0
      color: dialog.theme.urgent
      font.family: dialog.theme.fontFamily
      font.pixelSize: dialog.theme.fontSmall
    }
    RowLayout {
      Layout.alignment: Qt.AlignRight
      Layout.topMargin: Math.round(4 * dialog.theme.scale)
      spacing: Math.round(8 * dialog.theme.scale)
      DeckButton {
        id: cancel
        theme: dialog.theme
        text: "Cancel"
        tabFence: false
        KeyNavigation.tab: save
        KeyNavigation.backtab: dialog.review ? save : startup
        onClicked: dialog.close()
      }
      DeckButton {
        id: save
        theme: dialog.theme
        kind: Ui.ButtonVariant.Primary
        text: dialog.review ? "Apply changes" : "Review changes"
        enabled: !dialog.submitted
        tabFence: false
        KeyNavigation.tab: dialog.review ? cancel : count
        KeyNavigation.backtab: cancel
        onClicked: dialog.submit()
      }
    }
  }
}
