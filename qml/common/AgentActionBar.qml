pragma ComponentBehavior: Bound
import QtQuick
import QtQuick.Layouts
import "UiConstants.js" as Ui

Flow {
  required property var host
  readonly property alias part_showPendingButton: showPendingButton
  readonly property alias part_cancelButton: cancelButton
  readonly property alias part_retryButton: retryButton
  readonly property alias part_copyButton: copyButton
  readonly property alias part_saveButton: saveButton
  readonly property alias part_overwriteButton: overwriteButton
  readonly property alias part_openSavedButton: openSavedButton
  readonly property alias part_clearButton: clearButton
  readonly property alias part_backButton: backButton

  id: assistantActions
  Layout.fillWidth: true
  spacing: assistantActions.host.theme.gap

  AgentButton {
    id: showPendingButton
    theme: assistantActions.host.theme
    objectName: "agentShowPending"
    text: "Show Pending"
    visible: assistantActions.host.pendingParked
    Accessible.name: "Show the parked assistant request"
    Accessible.description: "The request is still waiting; nothing was decided"
    onClicked: assistantActions.host.pendingHidden = false
    Keys.onTabPressed: assistantActions.host.moveFocus(1)
    Keys.onBacktabPressed: assistantActions.host.moveFocus(-1)
  }
  AgentButton {
    id: cancelButton
    theme: assistantActions.host.theme
    objectName: "agentCancel"
    text: "Cancel"
    visible: assistantActions.host.running || assistantActions.host.waiting
    Accessible.name: "Cancel the assistant turn"
    onClicked: assistantActions.host.cancelRequested()
    Keys.onTabPressed: assistantActions.host.moveFocus(1)
    Keys.onBacktabPressed: assistantActions.host.moveFocus(-1)
  }
  AgentButton {
    id: retryButton
    theme: assistantActions.host.theme
    objectName: "agentRetry"
    text: "Retry"
    visible: assistantActions.host.retryable
    Accessible.name: "Retry the last assistant question"
    onClicked: assistantActions.host.retryRequested()
    Keys.onTabPressed: assistantActions.host.moveFocus(1)
    Keys.onBacktabPressed: assistantActions.host.moveFocus(-1)
  }
  AgentButton {
    id: copyButton
    theme: assistantActions.host.theme
    objectName: "agentCopy"
    text: "Copy"
    visible: assistantActions.host.outputAvailable
    Accessible.name: "Copy the assistant answer"
    onClicked: assistantActions.host.outputRequested(Ui.OutputAction.Copy, "", "")
    Keys.onTabPressed: assistantActions.host.moveFocus(1)
    Keys.onBacktabPressed: assistantActions.host.moveFocus(-1)
  }
  AgentButton {
    id: saveButton
    theme: assistantActions.host.theme
    objectName: "agentSave"
    text: "Save"
    visible: assistantActions.host.outputAvailable
    enabled: assistantActions.host.saveDirectory.length > 0
    Accessible.name: "Save the assistant answer"
    Accessible.description: "Saves a Markdown file into " + assistantActions.host.saveDirectory
    onClicked: assistantActions.host.saveTo(assistantActions.host.defaultSavePath())
    Keys.onTabPressed: assistantActions.host.moveFocus(1)
    Keys.onBacktabPressed: assistantActions.host.moveFocus(-1)
  }
  AgentButton {
    id: overwriteButton
    theme: assistantActions.host.theme
    kind: Ui.ButtonVariant.Danger
    objectName: "agentOverwrite"
    text: "Confirm Overwrite"
    visible: assistantActions.host.outputAvailable && assistantActions.host.controller.overwritePending
    Accessible.name: "Confirm overwrite of the assistant output file"
    Accessible.description: "Destructive action. Replaces the selected file"
    onClicked: assistantActions.host.outputRequested(
      Ui.OutputAction.Save,
      assistantActions.host.controller.outputPathPending,
      assistantActions.host.controller.overwriteConfirmationToken)
    Keys.onTabPressed: assistantActions.host.moveFocus(1)
    Keys.onBacktabPressed: assistantActions.host.moveFocus(-1)
  }
  AgentButton {
    id: openSavedButton
    theme: assistantActions.host.theme
    objectName: "agentOpenSavedOutput"
    text: "Open Saved Output"
    visible: assistantActions.host.outputAvailable
    enabled: assistantActions.host.controller.savedOutputAvailable
    Accessible.name: "Open the saved assistant output"
    Accessible.description: enabled
      ? "Opens the exact file saved from this answer"
      : "Save this answer before opening it"
    onClicked: assistantActions.host.outputRequested(Ui.OutputAction.Open, "", "")
    Keys.onTabPressed: assistantActions.host.moveFocus(1)
    Keys.onBacktabPressed: assistantActions.host.moveFocus(-1)
  }
  AgentButton {
    id: clearButton
    theme: assistantActions.host.theme
    objectName: "agentClear"
    text: "Clear"
    Accessible.name: "Clear the assistant conversation"
    Accessible.description: "Cancels pending work and discards the transcript"
    onClicked: assistantActions.host.dismissed()
    Keys.onTabPressed: assistantActions.host.moveFocus(1)
    Keys.onBacktabPressed: assistantActions.host.moveFocus(-1)
  }
  AgentButton {
    id: backButton
    theme: assistantActions.host.theme
    objectName: "agentBackToSearch"
    text: "Back to Search"
    Accessible.name: "Return to search"
    Accessible.description: "The turn keeps running in the daemon"
    onClicked: assistantActions.host.backToSearch()
    Keys.onTabPressed: assistantActions.host.moveFocus(1)
    Keys.onBacktabPressed: assistantActions.host.moveFocus(-1)
  }
}
