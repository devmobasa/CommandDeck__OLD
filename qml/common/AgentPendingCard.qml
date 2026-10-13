pragma ComponentBehavior: Bound
import QtQuick
import QtQuick.Layouts
import "UiConstants.js" as Ui
import "Protocol.js" as Protocol

Rectangle {
  required property var host
  readonly property alias part_optionRepeater: optionRepeater
  readonly property alias part_questionField: questionField
  readonly property alias part_questionSubmitButton: questionSubmitButton
  readonly property alias part_contextAllowButton: contextAllowButton
  readonly property alias part_contextAllowFolderButton: contextAllowFolderButton
  readonly property alias part_contextDenyButton: contextDenyButton
  readonly property alias part_contextCancelTurnButton: contextCancelTurnButton
  readonly property alias part_actionArgvText: actionArgvText
  readonly property alias part_actionRunButton: actionRunButton
  readonly property alias part_actionAlwaysButton: actionAlwaysButton
  readonly property alias part_actionDenyButton: actionDenyButton

  id: pendingCard
  objectName: "agentPendingCard"
  Layout.fillWidth: true
  Layout.preferredHeight: pendingColumn.implicitHeight + pendingCard.host.theme.gap * 2
  visible: pendingCard.host.pendingCardVisible
  radius: pendingCard.host.theme.radiusSmall
  color: pendingCard.host.theme.surfaceStrong
  border.width: 1
  border.color: pendingCard.host.pendingAction && pendingCard.host.pendingAction.destructive === true
    ? pendingCard.host.theme.urgent : pendingCard.host.theme.separator

  ColumnLayout {
    id: pendingColumn
    anchors.fill: parent
    anchors.margins: pendingCard.host.theme.gap
    spacing: pendingCard.host.theme.gap

    Text {
      Layout.fillWidth: true
      textFormat: Text.PlainText
      text: pendingCard.host.pending && pendingCard.host.pending.prompt
        ? String(pendingCard.host.pending.prompt) : "The assistant needs a decision."
      color: pendingCard.host.theme.text
      wrapMode: Text.Wrap
      font.family: pendingCard.host.theme.fontFamily
      font.pixelSize: pendingCard.host.theme.fontBody
      font.weight: Font.DemiBold
    }

    Text {
      Layout.fillWidth: true
      visible: pendingCard.host.pending !== null && pendingCard.host.pending !== undefined
        && String(pendingCard.host.pending.detail || "").length > 0
      textFormat: Text.PlainText
      text: pendingCard.host.pending ? String(pendingCard.host.pending.detail || "") : ""
      color: pendingCard.host.theme.textMuted
      wrapMode: Text.Wrap
      font.family: pendingCard.host.theme.fontFamily
      font.pixelSize: pendingCard.host.theme.fontSmall
    }

    // Question card.
    Text {
      Layout.fillWidth: true
      visible: pendingCard.host.pendingKind === Protocol.AgentPendingRequestKinds.Question && pendingCard.host.firstQuestion !== null
      textFormat: Text.PlainText
      text: pendingCard.host.firstQuestion
        ? String(pendingCard.host.firstQuestion.header || "")
          + (pendingCard.host.firstQuestion.header && pendingCard.host.firstQuestion.prompt ? " — " : "")
          + String(pendingCard.host.firstQuestion.prompt || "")
        : ""
      color: pendingCard.host.theme.text
      wrapMode: Text.Wrap
      font.family: pendingCard.host.theme.fontFamily
      font.pixelSize: pendingCard.host.theme.fontSmall
    }

    Flow {
      Layout.fillWidth: true
      visible: pendingCard.host.pendingKind === Protocol.AgentPendingRequestKinds.Question
      spacing: pendingCard.host.theme.gap

      Repeater {
        id: optionRepeater
        model: pendingCard.host.firstQuestion && pendingCard.host.firstQuestion.options
          ? pendingCard.host.firstQuestion.options : []

        delegate: AgentButton {
          id: optionButton
          theme: pendingCard.host.theme
          compact: true
          required property var modelData
          required property int index
          objectName: "agentQuestionOption" + index
          text: String(modelData)
          Accessible.name: "Answer with " + text
          onClicked: pendingCard.host.submitAnswer(optionButton.modelData)
          Keys.onTabPressed: pendingCard.host.moveFocus(1)
          Keys.onBacktabPressed: pendingCard.host.moveFocus(-1)
        }
      }
    }

    RowLayout {
      Layout.fillWidth: true
      visible: pendingCard.host.pendingKind === Protocol.AgentPendingRequestKinds.Question
        && (pendingCard.host.firstQuestion === null
          || pendingCard.host.firstQuestion.allowFreeText !== false)
      spacing: pendingCard.host.theme.gap

      AgentField {
        id: questionField
        theme: pendingCard.host.theme
        objectName: "agentQuestionText"
        Layout.fillWidth: true
        placeholderText: "Your answer"
        Accessible.name: "Assistant question answer"
        onAccepted: pendingCard.host.submitAnswer(questionField.text)
        Keys.onTabPressed: pendingCard.host.moveFocus(1)
        Keys.onBacktabPressed: pendingCard.host.moveFocus(-1)
      }

      AgentButton {
        id: questionSubmitButton
        theme: pendingCard.host.theme
        kind: Ui.ButtonVariant.Primary
        objectName: "agentQuestionSubmit"
        text: "Send Answer"
        enabled: questionField.text.trim().length > 0
        Accessible.name: "Send the typed answer"
        onClicked: pendingCard.host.submitAnswer(questionField.text)
        Keys.onTabPressed: pendingCard.host.moveFocus(1)
        Keys.onBacktabPressed: pendingCard.host.moveFocus(-1)
      }
    }

    // Context card.
    Text {
      Layout.fillWidth: true
      objectName: "agentContextDetail"
      visible: pendingCard.host.pendingKind === Protocol.AgentPendingRequestKinds.Context && pendingCard.host.pendingContext !== null
      textFormat: Text.PlainText
      text: pendingCard.host.pendingContext
        ? String(pendingCard.host.pendingContext.operation || "read")
          + " · " + String(pendingCard.host.pendingContext.path || "")
          + (pendingCard.host.pendingContext.proposedFolder
            ? "\nFolder: " + String(pendingCard.host.pendingContext.proposedFolder) : "")
          + (pendingCard.host.pendingContext.maximumBytes
            ? "\nBounded to " + String(pendingCard.host.pendingContext.maximumBytes)
              + " bytes" : "")
          + (pendingCard.host.pendingContext.protectedTarget === true
            ? "\nProtected path" : "")
        : ""
      color: pendingCard.host.theme.text
      wrapMode: Text.WrapAnywhere
      font.family: "monospace"
      font.pixelSize: pendingCard.host.theme.fontSmall
    }

    Flow {
      Layout.fillWidth: true
      visible: pendingCard.host.pendingKind === Protocol.AgentPendingRequestKinds.Context
      spacing: pendingCard.host.theme.gap

      AgentButton {
        id: contextAllowButton
        theme: pendingCard.host.theme
        kind: Ui.ButtonVariant.Primary
        objectName: "agentContextAllow"
        text: "Allow"
        visible: pendingCard.host.allowsDecision(Protocol.AgentRequestDecisions.Allow)
        Accessible.name: "Allow this one access"
        onClicked: pendingCard.host.decide(Protocol.AgentRequestDecisions.Allow)
        Keys.onTabPressed: pendingCard.host.moveFocus(1)
        Keys.onBacktabPressed: pendingCard.host.moveFocus(-1)
      }
      AgentButton {
        id: contextAllowFolderButton
        theme: pendingCard.host.theme
        objectName: "agentContextAllowFolder"
        text: "Allow Folder"
        visible: pendingCard.host.allowsDecision(Protocol.AgentRequestDecisions.AllowFolder)
        Accessible.name: "Approve this folder for later reads"
        onClicked: pendingCard.host.decide(Protocol.AgentRequestDecisions.AllowFolder)
        Keys.onTabPressed: pendingCard.host.moveFocus(1)
        Keys.onBacktabPressed: pendingCard.host.moveFocus(-1)
      }
      AgentButton {
        id: contextDenyButton
        theme: pendingCard.host.theme
        kind: Ui.ButtonVariant.Danger
        objectName: "agentContextDeny"
        text: "Deny"
        visible: pendingCard.host.allowsDecision(Protocol.AgentRequestDecisions.Deny)
        Accessible.name: "Deny this access"
        onClicked: pendingCard.host.decide(Protocol.AgentRequestDecisions.Deny)
        Keys.onTabPressed: pendingCard.host.moveFocus(1)
        Keys.onBacktabPressed: pendingCard.host.moveFocus(-1)
      }
      AgentButton {
        id: contextCancelTurnButton
        theme: pendingCard.host.theme
        kind: Ui.ButtonVariant.Danger
        objectName: "agentContextCancelTurn"
        text: "Cancel Turn"
        visible: pendingCard.host.allowsDecision(Protocol.AgentRequestDecisions.CancelTurn)
        Accessible.name: "Deny and cancel the whole turn"
        onClicked: pendingCard.host.decide(Protocol.AgentRequestDecisions.CancelTurn)
        Keys.onTabPressed: pendingCard.host.moveFocus(1)
        Keys.onBacktabPressed: pendingCard.host.moveFocus(-1)
      }
    }

    // Action and command card. The argv is displayed literally, one
    // argument per line, because that is what will actually run.
    Text {
      Layout.fillWidth: true
      visible: pendingCard.host.pendingAction !== null
        && (pendingCard.host.pendingKind === Protocol.AgentPendingRequestKinds.Action || pendingCard.host.pendingKind === Protocol.AgentPendingRequestKinds.Command)
      textFormat: Text.PlainText
      text: pendingCard.host.pendingAction ? String(pendingCard.host.pendingAction.title || "") : ""
      color: pendingCard.host.theme.text
      wrapMode: Text.Wrap
      font.family: pendingCard.host.theme.fontFamily
      font.pixelSize: pendingCard.host.theme.fontBody
    }

    Text {
      id: actionArgvText
      objectName: "agentActionArgv"
      Layout.fillWidth: true
      visible: pendingCard.host.pendingAction !== null
        && (pendingCard.host.pendingKind === Protocol.AgentPendingRequestKinds.Action || pendingCard.host.pendingKind === Protocol.AgentPendingRequestKinds.Command)
      textFormat: Text.PlainText
      text: {
        const action = pendingCard.host.pendingAction
        if (!action) return ""
        const lines = []
        if (action.executable) lines.push(String(action.executable))
        const argv = action.argv || []
        for (let index = 0; index < argv.length; index++) {
          lines.push(String(argv[index]))
        }
        if (action.workingDirectory) {
          lines.push("cwd: " + String(action.workingDirectory))
        }
        if (action.environmentProfile) {
          lines.push("environment: " + String(action.environmentProfile))
        }
        if (action.risk) lines.push("risk: " + String(action.risk))
        return lines.join("\n")
      }
      color: pendingCard.host.theme.text
      wrapMode: Text.WrapAnywhere
      font.family: "monospace"
      font.pixelSize: pendingCard.host.theme.fontSmall
      Accessible.role: Accessible.StaticText
      Accessible.name: "Exact command to run"
    }

    Repeater {
      id: actionDetailRepeater
      model: pendingCard.host.pendingAction && pendingCard.host.pendingAction.details
        ? pendingCard.host.pendingAction.details : []

      delegate: RowLayout {
        id: detailDelegate
        required property var modelData
        Layout.fillWidth: true
        spacing: pendingCard.host.theme.gap

        Text {
          textFormat: Text.PlainText
          text: String(detailDelegate.modelData
            ? detailDelegate.modelData.label || "" : "")
          color: pendingCard.host.theme.textFaint
          font.family: pendingCard.host.theme.fontFamily
          font.pixelSize: pendingCard.host.theme.fontCaption
        }
        Text {
          Layout.fillWidth: true
          textFormat: Text.PlainText
          text: String(detailDelegate.modelData
            ? detailDelegate.modelData.value || "" : "")
          color: pendingCard.host.theme.textMuted
          wrapMode: Text.WrapAnywhere
          font.family: detailDelegate.modelData
            && detailDelegate.modelData.monospace === true
              ? "monospace" : pendingCard.host.theme.fontFamily
          font.pixelSize: pendingCard.host.theme.fontCaption
        }
      }
    }

    Flow {
      Layout.fillWidth: true
      visible: pendingCard.host.pendingKind === Protocol.AgentPendingRequestKinds.Action || pendingCard.host.pendingKind === Protocol.AgentPendingRequestKinds.Command
      spacing: pendingCard.host.theme.gap

      AgentButton {
        id: actionRunButton
        theme: pendingCard.host.theme
        // A destructive command's Run button reads red, matching the card
        // border, so the danger is obvious at the point of action.
        kind: pendingCard.host.pendingDestructive ? Ui.ButtonVariant.Danger : Ui.ButtonVariant.Primary
        objectName: "agentActionRun"
        text: "Run"
        visible: pendingCard.host.allowsDecision(Protocol.AgentRequestDecisions.Allow)
        Accessible.name: pendingCard.host.pendingAction
          && pendingCard.host.pendingAction.destructive === true
            ? "Run this destructive action"
            : "Run this action"
        Accessible.description: pendingCard.host.pendingAction
          && pendingCard.host.pendingAction.destructive === true
            ? "Destructive action. Runs the exact command shown"
            : "Runs the exact command shown"
        onClicked: pendingCard.host.decide(Protocol.AgentRequestDecisions.Allow)
        Keys.onTabPressed: pendingCard.host.moveFocus(1)
        Keys.onBacktabPressed: pendingCard.host.moveFocus(-1)
      }
      AgentButton {
        id: actionAlwaysButton
        theme: pendingCard.host.theme
        objectName: "agentActionAlways"
        text: "Always Allow"
        visible: pendingCard.host.pendingAction
          && pendingCard.host.pendingAction.canRemember === true
          && pendingCard.host.allowsDecision(Protocol.AgentRequestDecisions.AllowAlways)
        Accessible.name: "Always allow this exact command"
        Accessible.description: "Binds executable, arguments, directory, "
          + "and environment profile"
        onClicked: pendingCard.host.decide(Protocol.AgentRequestDecisions.AllowAlways)
        Keys.onTabPressed: pendingCard.host.moveFocus(1)
        Keys.onBacktabPressed: pendingCard.host.moveFocus(-1)
      }
      AgentButton {
        id: actionDenyButton
        theme: pendingCard.host.theme
        kind: Ui.ButtonVariant.Danger
        objectName: "agentActionDeny"
        text: "Deny"
        visible: pendingCard.host.allowsDecision(Protocol.AgentRequestDecisions.Deny)
        Accessible.name: "Deny this action"
        onClicked: pendingCard.host.decide(Protocol.AgentRequestDecisions.Deny)
        Keys.onTabPressed: pendingCard.host.moveFocus(1)
        Keys.onBacktabPressed: pendingCard.host.moveFocus(-1)
      }
    }
  }
}
