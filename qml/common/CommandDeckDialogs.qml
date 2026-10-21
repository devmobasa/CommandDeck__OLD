pragma ComponentBehavior: Bound
import QtQuick
import "UiConstants.js" as Ui
import "Protocol.js" as Protocol
import "DeckConfirmationActions.js" as DeckConfirmationActions

Item {
  id: dialogs
  signal backendSettingsRequested()
  required property var surface
  required property Item container
  readonly property alias aliasDialogControl: aliasDialog
  readonly property alias aiPreflightSurfaceControl: aiPreflightSurface
  readonly property alias aiStreamingSurfaceControl: aiStreamingSurface
  // Null until the conversation is first opened in this window.
  readonly property AgentSurface agentSurfaceControl: agentSurface.item as AgentSurface
  readonly property alias detailDialogControl: detailDialog

  AliasDialog {
    parent: dialogs.container
    id: aliasDialog
    anchors.fill: parent
    z: 16
    theme: dialogs.surface.part_appTheme
    opened: dialogs.surface.host.aliasDialogOpen
    result: dialogs.surface.host.aliasTarget
    errorText: dialogs.surface.host.aliasDialogError
    reservedValues: dialogs.surface.host.reservedAliasValues()
    onSaveRequested: value => dialogs.surface.host.setAlias(dialogs.surface.host.aliasTarget, value)
    onRemoveRequested: dialogs.surface.host.setAlias(dialogs.surface.host.aliasTarget, null)
    onDismissed: {
      dialogs.surface.host.finishAliasDialog()
    }
  }

  AiPreflightSurface {
    parent: dialogs.container
    id: aiPreflightSurface
    anchors.fill: parent
    z: 26
    theme: dialogs.surface.part_appTheme
    opened: dialogs.surface.host.part_aiController.state === Ui.AiState.Preflight
    preflight: dialogs.surface.host.part_aiController.preflight
    onAccepted: {
      dialogs.surface.host.activeAiRequestId = ++dialogs.surface.host.requestSequence
      dialogs.surface.host.part_aiController.confirm(dialogs.surface.host.activeAiRequestId)
    }
    onCanceled: {
      dialogs.surface.host.clearAiState()
      dialogs.surface.host.hideAnswer()
      dialogs.surface.host.focusRevision++
    }
  }

  AiStreamingSurface {
    parent: dialogs.container
    id: aiStreamingSurface
    anchors.fill: parent
    z: 26
    theme: dialogs.surface.part_appTheme
    controller: dialogs.surface.host.part_aiController
    saveDirectory: dialogs.surface.host.aiSaveDirectory
    opened: (dialogs.surface.host.answerMode !== "direct" || dialogs.surface.host.aiSurfaceExpanded)
      && (dialogs.surface.host.part_aiController.state === Ui.AiState.Streaming
        || dialogs.surface.host.part_aiController.state === Ui.AiState.Completed
        || dialogs.surface.host.part_aiController.state === Ui.AiState.Failed
        || dialogs.surface.host.part_aiController.state === Ui.AiState.Cancelled
        || dialogs.surface.host.part_aiController.state === Ui.AiState.ConnectionLost)
    onDismissed: {
      if (dialogs.surface.host.part_aiController.state === Ui.AiState.Streaming) {
        dialogs.surface.host.part_aiController.cancel(++dialogs.surface.host.requestSequence)
      }
      dialogs.surface.host.clearAiState()
      dialogs.surface.host.hideAnswer()
      dialogs.surface.host.focusRevision++
    }
    onCancelRequested: dialogs.surface.host.part_aiController.cancel(++dialogs.surface.host.requestSequence)
    onRetryRequested: {
      dialogs.surface.host.activeAiRequestId = ++dialogs.surface.host.requestSequence
      dialogs.surface.host.part_aiController.retry(dialogs.surface.host.activeAiRequestId)
    }
    onOutputRequested: (action, path, confirmationToken) => {
      dialogs.surface.host.part_aiController.outputAction(action, ++dialogs.surface.host.requestSequence, path, confirmationToken)
    }
  }

  OnDemandSurface {
    parent: dialogs.container
    id: agentSurface
    anchors.fill: parent
    z: 27
    wanted: dialogs.surface.presenting && dialogs.surface.host.agentOpen
    sourceComponent: AgentSurface {
      id: agentView
      theme: dialogs.surface.part_appTheme
      controller: dialogs.surface.host.part_agentController
      saveDirectory: dialogs.surface.host.aiSaveDirectory
      opened: agentSurface.presented && dialogs.surface.host.agentOpen
      onDismissed: {
        dialogs.surface.host.part_agentController.clear(++dialogs.surface.host.requestSequence)
        dialogs.surface.host.hideAnswer()
        dialogs.surface.host.closeAgentSurface()
      }
      onBackToSearch: dialogs.surface.host.closeAgentSurface()
      onCancelRequested: dialogs.surface.host.part_agentController.cancelTurn(++dialogs.surface.host.requestSequence)
      onRetryRequested: {
        const entries = dialogs.surface.host.part_agentController.entries || []
        const unsentPrompt = String(dialogs.surface.host.part_agentController.unsentPrompt || "")
        let query = unsentPrompt
        for (let index = entries.length - 1; query.length === 0 && index >= 0; index--) {
          if (entries[index] && entries[index].role === Protocol.AgentTranscriptRoles.User) {
            query = String(entries[index].text || "")
            break
          }
        }
        if (query.length > 0) {
          if (unsentPrompt.length > 0) agentView.consumeDraft(unsentPrompt)
          dialogs.surface.host.part_agentController.startTurn(++dialogs.surface.host.requestSequence, query, "")
        } else {
          dialogs.surface.host.part_agentController.open(++dialogs.surface.host.requestSequence)
        }
      }
      onSubmitRequested: text => dialogs.surface.host.part_agentController.startTurn(
        ++dialogs.surface.host.requestSequence, text, "")
      onOutputRequested: (action, path, token) => dialogs.surface.host.part_agentController.outputAction(
        action, ++dialogs.surface.host.requestSequence, path, token)
      onDecisionRequested: (pendingId, decision) => dialogs.surface.host.part_agentController.resolve(
        ++dialogs.surface.host.requestSequence, pendingId, decision)
      onAnswerRequested: (pendingId, answers) => dialogs.surface.host.part_agentController.answer(
        ++dialogs.surface.host.requestSequence, pendingId, answers)
      onLoginRequested: method => dialogs.surface.host.part_agentController.startLogin(
        ++dialogs.surface.host.requestSequence, method, "")
      onLoginCodeSubmitted: code => dialogs.surface.host.part_agentController.completeLogin(
        ++dialogs.surface.host.requestSequence, code, "")
      onLoginCancelRequested: dialogs.surface.host.part_agentController.cancelLogin(++dialogs.surface.host.requestSequence, "")
      onEnableRequested: dialogs.surface.host.part_agentController.enable(++dialogs.surface.host.requestSequence)
      onOpenLoginPageRequested: dialogs.surface.host.sendAgentLoginAction(Protocol.AgentLoginActionIds.OpenUrl)
      onCopyCodeRequested: dialogs.surface.host.sendAgentLoginAction(Protocol.AgentLoginActionIds.CopyCode)
      onCopyUrlRequested: dialogs.surface.host.sendAgentLoginAction(Protocol.AgentLoginActionIds.CopyUrl)
      onBackendSettingsRequested: dialogs.backendSettingsRequested()
    }
  }

  ConfirmationSurface {
    id: deckConfirmation
    parent: dialogs.container
    anchors.fill: parent
    z: 20
    theme: dialogs.surface.part_appTheme
    readonly property var content: DeckConfirmationActions.confirmationContent(dialogs.surface.host)
    opened: dialogs.surface.host.confirmationOpen
    prompt: deckConfirmation.content.prompt
    confirmLabel: deckConfirmation.content.confirmLabel
    destructive: deckConfirmation.content.destructive
    onAccepted: dialogs.surface.host.acceptConfirmation()
    onCanceled: dialogs.surface.host.cancelConfirmation()
  }

  ActionInputDialog {
    parent: dialogs.container
    anchors.fill: parent
    z: 25
    theme: dialogs.surface.part_appTheme
    opened: dialogs.surface.host.part_invocationController.actionInputOpen
    action: dialogs.surface.host.part_invocationController.actionInputAction
    onSubmitted: values => {
      const selection = dialogs.surface.host.part_invocationController.takeInput()
      dialogs.surface.host.focusRevision++
      dialogs.surface.host.invokeAction(selection.result, selection.action, selection.confirmed, values)
    }
    onDismissed: {
      dialogs.surface.host.part_invocationController.cancelInput()
      dialogs.surface.host.focusRevision++
    }
  }

  DetailDialog {
    parent: dialogs.container
    id: detailDialog
    anchors.fill: parent
    z: 24
    theme: dialogs.surface.part_appTheme
    opened: dialogs.surface.host.detailDialogRequested
    title: dialogs.surface.host.dialogTitle
    text: dialogs.surface.host.detailContent
    plainText: dialogs.surface.host.actionOutputPlainText || dialogs.surface.host.dialogTitle === "Customization export"
      || dialogs.surface.host.dialogTitle === "Why this result?"
    onDismissed: {
      dialogs.surface.host.detailDialogRequested = false
      dialogs.surface.host.actionOutputPlainText = false
      dialogs.surface.host.detailContent = ""
      dialogs.surface.host.dialogTitle = "AI response"
      dialogs.surface.host.focusRevision++
    }
  }
}
