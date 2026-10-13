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
  property string purpose: ""
  property var capturedFields: ({})
  property string explanation: ""
  readonly property alias editorControl: editor
  readonly property alias cancelControl: cancel
  readonly property alias acceptControl: accept
  readonly property alias handlersControl: handlers
  width: Math.min(parent.width - 32, 560)
  anchors.centerIn: parent
  modal: true
  focus: true
  closePolicy: Popup.CloseOnEscape
  padding: Math.round(20 * dialog.theme.scale)
  topPadding: Math.round(14 * dialog.theme.scale)

  function show(kind, invoker) {
    purpose = kind
    returnFocus = invoker
    const settings = controller.settings
    if (kind === Ui.ClipboardDialogPurpose.Keywords) {
      capturedFields = controller.entryFields()
      editor.text = controller.selected ? (controller.selected.keywords || []).join("\n") : ""
      title = "Edit keywords"
      explanation = "One keyword per line. Up to 16 keywords, 64 UTF-8 bytes each."
    } else if (kind === Ui.ClipboardDialogPurpose.Clear) {
      capturedFields = {action: Protocol.ClipboardActionIds.Clear, storeRevision: settings.migration.storeRevision}
      title = "Clear clipboard history?"
      explanation = "Delete all " + settings.migration.entryCount + " entries, including pins and keywords? This cannot be undone."
    } else if (kind === Ui.ClipboardDialogPurpose.Open) {
      const preparation = controller.openPreparation || {handlers: [], status: "Unavailable", unavailableReason: ""}
      capturedFields = {releaseId: preparation.releaseId}
      title = "Inspect link handlers"
      const destination = controller.preview && controller.preview.openDestination ? controller.preview.openDestination : ""
      explanation = (destination ? destination + "\n\n" : "")
        + (preparation.status === Protocol.ClipboardHandlerStatuses.Resolved ? "Registered handlers are listed below. " : describeStatus(preparation.status))
        + "Opening from history is not available in this build: it requires the desktop release acknowledgement and managed launch proofs. Copy remains available."
    } else if (kind === Ui.ClipboardDialogPurpose.Enable) {
      capturedFields = {policyRevision: settings.policy.revision, consent: "Text", paused: false}
      title = "Enable clipboard history?"
      explanation = "Copies will be stored locally as plaintext. Secret detection is imperfect. The current limits are "
        + settings.policy.maximumEntries + " entries and " + settings.policy.retentionHours + " hours."
    } else {
      capturedFields = {policyRevision: settings.policy.revision, storeRevision: settings.migration.storeRevision}
      title = "Review migrated history"
      explanation = "Your " + settings.migration.entryCount + " entries and " + settings.migration.pinnedCount
        + " pins were retained. Capacity is " + settings.migration.effectiveMaximumEntries
        + (settings.migration.pendingStartupClear ? ". Clear-on-startup is pending: delete ALL entries, including pins?"
          : ". Apply retention and delete " + settings.migration.expiredCount + " expired unpinned entries?")
    }
    open()
  }

  function describeStatus(status) {
    if (status === Protocol.ClipboardHandlerStatuses.NoAssociations) return "No application is registered for this link type. "
    if (status === Protocol.ClipboardHandlerStatuses.ActionsBusy) return "Handler discovery is busy; try again shortly. "
    if (status === Protocol.ClipboardHandlerStatuses.CheckTimedOut) return "Handler discovery timed out. "
    if (status === Protocol.ClipboardHandlerStatuses.InvalidAssociations) return "The desktop association files conflict for this link type. "
    return "Handler discovery is unavailable. "
  }
  function handlerLine(handler) {
    return handler.label + (handler.rank === Protocol.ClipboardHandlerRanks.Default ? " (default)" : "") + (handler.available ? "" : " · " + describeReason(handler.reason))
  }
  function describeReason(reason) {
    if (reason === Protocol.ClipboardHandlerReasons.UnsupportedTerminalLaunch) return "terminal launch unsupported"
    if (reason === Protocol.ClipboardHandlerReasons.UnsupportedActivation) return "D-Bus activation unsupported"
    if (reason === Protocol.ClipboardHandlerReasons.ExecutableUnavailable) return "executable missing"
    return reason || "unavailable"
  }

  onOpened: {
    if (purpose === Ui.ClipboardDialogPurpose.Keywords) editor.forceActiveFocus()
    else cancel.forceActiveFocus()
  }
  onClosed: {
    if (purpose === Ui.ClipboardDialogPurpose.Open && capturedFields.releaseId) controller.abortRelease(capturedFields.releaseId)
    if (returnFocus && returnFocus.visible) returnFocus.forceActiveFocus()
    capturedFields = ({})
    editor.text = ""
  }

  function submit() {
    const fields = Object.assign({}, capturedFields)
    if (purpose === Ui.ClipboardDialogPurpose.Keywords) {
      fields.action = Protocol.ClipboardActionIds.Keywords
      fields.keywords = editor.text.split("\n").filter(value => value.trim().length > 0)
      controller.send(Ui.ClipboardPendingKey.Action, Protocol.Operations.ClipboardAction, fields)
    } else if (purpose === Ui.ClipboardDialogPurpose.Clear) controller.send(Ui.ClipboardPendingKey.Clear, Protocol.Operations.ClipboardAction, fields, true)
    else if (purpose === Ui.ClipboardDialogPurpose.Enable) controller.send(Ui.ClipboardPendingKey.SettingsSet, Protocol.Operations.ClipboardSettingsSet, fields, true)
    else controller.send(Ui.ClipboardPendingKey.Migration, Protocol.Operations.ClipboardMigrationAcknowledge, fields, true)
    close()
  }

  // An opaque panel over a dimmed page, so the browser never reads through the dialog.
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
    Label {
      Layout.fillWidth: true
      text: dialog.explanation
      textFormat: Text.PlainText
      wrapMode: Text.Wrap
      lineHeight: 1.2
      color: dialog.theme.textMuted
      font.family: dialog.theme.fontFamily
      font.pixelSize: dialog.theme.fontBody
    }
    Repeater {
      id: handlers
      model: dialog.purpose === Ui.ClipboardDialogPurpose.Open && dialog.controller.openPreparation ? dialog.controller.openPreparation.handlers : []
      delegate: Label {
        required property var modelData
        Layout.fillWidth: true
        text: "• " + dialog.handlerLine(modelData)
        textFormat: Text.PlainText
        elide: Text.ElideRight
        color: modelData.available ? dialog.theme.text : dialog.theme.textMuted
        font.family: dialog.theme.fontFamily
        font.pixelSize: dialog.theme.fontBody
      }
    }
    TextArea {
      id: editor
      Layout.fillWidth: true
      Layout.preferredHeight: 140
      visible: dialog.purpose === Ui.ClipboardDialogPurpose.Keywords
      textFormat: TextEdit.PlainText
      selectByMouse: true
      padding: Math.round(10 * dialog.theme.scale)
      color: dialog.theme.text
      selectionColor: dialog.theme.withAlpha(dialog.theme.accent, 0.4)
      selectedTextColor: dialog.theme.text
      font.family: dialog.theme.fontFamily
      font.pixelSize: dialog.theme.fontBody
      Accessible.name: "Keywords, one per line"
      KeyNavigation.priority: KeyNavigation.BeforeItem
      KeyNavigation.tab: cancel
      KeyNavigation.backtab: accept
      background: Rectangle {
        color: dialog.theme.surface
        radius: dialog.theme.radiusSmall
        border.color: editor.activeFocus ? dialog.theme.focusRing : dialog.theme.separator
        border.width: editor.activeFocus ? 2 : 1
      }
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
        KeyNavigation.tab: dialog.purpose === Ui.ClipboardDialogPurpose.Open ? cancel : accept
        KeyNavigation.backtab: dialog.purpose === Ui.ClipboardDialogPurpose.Keywords ? editor : dialog.purpose === Ui.ClipboardDialogPurpose.Open ? cancel : accept
        onClicked: dialog.close()
      }
      DeckButton {
        id: accept
        visible: dialog.purpose !== Ui.ClipboardDialogPurpose.Open
        theme: dialog.theme
        kind: dialog.purpose === Ui.ClipboardDialogPurpose.Clear ? Ui.ButtonVariant.Danger : Ui.ButtonVariant.Primary
        text: dialog.purpose === Ui.ClipboardDialogPurpose.Keywords ? "Save" : dialog.purpose === Ui.ClipboardDialogPurpose.Clear ? "Clear history" : dialog.purpose === Ui.ClipboardDialogPurpose.Enable ? "Enable" : "Apply retention"
        tabFence: false
        KeyNavigation.tab: dialog.purpose === Ui.ClipboardDialogPurpose.Keywords ? editor : cancel
        KeyNavigation.backtab: cancel
        onClicked: dialog.submit()
      }
    }
  }
}
