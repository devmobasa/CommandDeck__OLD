pragma ComponentBehavior: Bound
import QtQuick
import QtQuick.Controls
import QtQuick.Layouts
import "Protocol.js" as Protocol
import "UiConstants.js" as Ui

// The inline answer pane. An assistant turn or a direct AI answer streams in
// beside the selected row, so asking never leaves the results screen. The
// full surfaces stay one click away: the conversation for follow-ups and
// tool questions, the AI response dialog for saving output.
Rectangle {
  id: root

  required property var theme
  // Named so the deck can bind `assistantController: agentController` without
  // the property name shadowing the controller id in the binding scope.
  required property var assistantController
  required property var directController
  // "agent" for the embedded assistant, "direct" for a direct provider.
  property string mode: ""
  property string query: ""
  property string backendLabel: ""
  signal cancelRequested()
  signal retryRequested()
  signal copyRequested()
  signal expandRequested()
  signal closeRequested()

  readonly property bool agentMode: root.mode === "agent"
  // Controllers can vanish before the pane during teardown; every read below
  // tolerates a missing one.
  readonly property var agent: root.assistantController ? root.assistantController : null
  readonly property var ai: root.directController ? root.directController : null
  readonly property string agentState: root.agent ? String(root.agent.state || "") : ""
  readonly property string aiState: root.ai ? String(root.ai.state || "") : ""
  readonly property string agentActiveText: root.agent ? String(root.agent.activeText || "") : ""
  readonly property string agentActiveStatus: root.agent ? String(root.agent.activeStatus || "") : ""

  // After a turn finishes the controller moves the streamed text into the
  // transcript, so the pane keeps showing the last assistant entry of the
  // current exchange.
  readonly property string lastAssistantText: {
    const entries = root.agent && root.agent.entries ? root.agent.entries : []
    for (let index = entries.length - 1; index >= 0; index--) {
      const entry = entries[index]
      if (!entry) continue
      if (entry.role === Protocol.AgentTranscriptRoles.User) return ""
      if (entry.role === Protocol.AgentTranscriptRoles.Assistant) return String(entry.text || "")
    }
    return ""
  }
  readonly property string answerText: root.agentMode
    ? (root.agentActiveText.length > 0 ? root.agentActiveText : root.lastAssistantText)
    : (root.ai ? String(root.ai.output || "") : "")
  readonly property bool waitingForInput: root.agentMode
    && root.agentState === Protocol.AgentConversationStates.Waiting
  readonly property bool running: root.agentMode
    ? (root.agentState === Ui.AgentState.Starting
      || root.agentState === Protocol.AgentConversationStates.Running
      || root.waitingForInput)
    : (root.aiState === Ui.AiState.Preflighting
      || root.aiState === Ui.AiState.Preflight
      || root.aiState === Ui.AiState.Streaming)
  readonly property bool hasAnswer: root.answerText.length > 0
  // A conversation snapshot can reset the controller to idle after the turn,
  // so text that is no longer streaming counts as a finished answer.
  readonly property bool completed: root.agentMode
    ? (root.agentState === Protocol.AgentConversationStates.Completed
      || (!root.running && !root.failed && !root.cancelled && root.hasAnswer))
    : root.aiState === Ui.AiState.Completed
  readonly property bool cancelled: root.agentMode
    ? root.agentState === Protocol.AgentConversationStates.Cancelled
    : root.aiState === Ui.AiState.Cancelled
  readonly property bool failed: root.agentMode
    ? (root.agentState === Protocol.AgentConversationStates.Failed
      || root.agentState === Ui.AgentState.ConnectionLost)
    : (root.aiState === Ui.AiState.Failed || root.aiState === Ui.AiState.ConnectionLost)
  readonly property string errorText: root.agentMode
    ? (root.agent ? String(root.agent.error || "") : "")
    : (root.ai ? String(root.ai.error || "") : "")
  readonly property string statusLine: root.waitingForInput
    ? "Needs your input · open the conversation"
    : root.running
      ? (root.agentMode
        ? (root.agentActiveStatus.length > 0 ? root.agentActiveStatus : "Thinking…")
        : (root.aiState === Ui.AiState.Streaming ? "Generating…" : "Review the request to send it"))
    : root.failed ? (root.errorText.length > 0 ? root.errorText : "Failed")
    : root.cancelled ? "Cancelled"
    : root.completed ? "Done"
    : ""
  readonly property string expandLabel: root.agentMode ? "Open conversation" : "Expand"

  function focusTargets() {
    const candidates = [cancelButton, retryButton, copyButton, expandButton, closeButton]
    return candidates.filter(item => item.visible && item.enabled)
  }

  color: root.theme.surface
  radius: root.theme.radiusSmall
  Accessible.role: Accessible.Grouping
  Accessible.name: "AI answer"
  Keys.onEscapePressed: root.closeRequested()

  // Copy confirmation: the daemon reports the clipboard result through the
  // controller's outputStatus, which the toast echoes.
  Connections {
    target: root.agent
    function onOutputStatusChanged() {
      if (root.agentMode && root.agent && root.agent.outputStatus.length > 0) toast.show(root.agent.outputStatus)
    }
  }
  Connections {
    target: root.ai
    function onOutputStatusChanged() {
      if (!root.agentMode && root.ai && root.ai.outputStatus.length > 0) toast.show(root.ai.outputStatus)
    }
  }

  DeckToast {
    id: toast
    objectName: "aiAnswerToast"
    theme: root.theme
    z: 2
    anchors.horizontalCenter: parent.horizontalCenter
    anchors.bottom: parent.bottom
    anchors.bottomMargin: footer.height + Math.round(28 * root.theme.scale)
  }

  ColumnLayout {
    anchors.fill: parent
    anchors.margins: Math.round(16 * root.theme.scale)
    spacing: Math.round(10 * root.theme.scale)

    RowLayout {
      Layout.fillWidth: true
      spacing: Math.round(8 * root.theme.scale)

      Text {
        text: "✦"
        color: root.theme.accent
        font.family: root.theme.fontFamily
        font.pixelSize: root.theme.fontBody
      }

      Text {
        objectName: "aiAnswerBackend"
        text: root.backendLabel.length > 0 ? root.backendLabel : (root.agentMode ? "Assistant" : "Direct AI")
        textFormat: Text.PlainText
        color: root.theme.text
        elide: Text.ElideRight
        font.family: root.theme.fontFamily
        font.pixelSize: root.theme.fontBody
        font.weight: Font.DemiBold
      }

      // Spinner while the answer is being produced.
      Item {
        id: spinner
        objectName: "aiAnswerSpinner"
        visible: root.running && !root.waitingForInput
        readonly property int size: Math.round(14 * root.theme.scale)
        implicitWidth: size
        implicitHeight: size
        Layout.alignment: Qt.AlignVCenter

        Canvas {
          id: spinnerCanvas
          anchors.fill: parent
          onPaint: {
            const context = getContext("2d")
            context.reset()
            const radius = width / 2 - 1.5
            context.lineWidth = 2
            context.strokeStyle = root.theme.accent
            context.beginPath()
            context.arc(width / 2, height / 2, radius, 0, Math.PI * 1.5)
            context.stroke()
          }
          Component.onCompleted: requestPaint()
          onWidthChanged: requestPaint()
        }

        RotationAnimation on rotation {
          running: spinner.visible && !root.theme.reduceMotion
          from: 0
          to: 360
          duration: 900
          loops: Animation.Infinite
        }
      }

      Text {
        id: statusText
        objectName: "aiAnswerStatus"
        Layout.fillWidth: true
        text: root.statusLine
        textFormat: Text.PlainText
        color: root.failed ? root.theme.urgent : root.theme.textMuted
        elide: Text.ElideRight
        font.family: root.theme.fontFamily
        font.pixelSize: root.theme.fontSmall
        Accessible.role: Accessible.StaticText
        Accessible.name: "Answer status: " + text
      }
    }

    Text {
      objectName: "aiAnswerQuery"
      Layout.fillWidth: true
      visible: root.query.length > 0
      text: root.query
      textFormat: Text.PlainText
      color: root.theme.textMuted
      elide: Text.ElideRight
      maximumLineCount: 2
      wrapMode: Text.Wrap
      font.family: root.theme.fontFamily
      font.pixelSize: root.theme.fontSmall
    }

    Rectangle {
      Layout.fillWidth: true
      implicitHeight: 1
      color: root.theme.separator
    }

    // Same scroll pattern as DetailPane: the flickable owns the width, the
    // markdown view fills it and grows downwards.
    Flickable {
      Layout.fillWidth: true
      Layout.fillHeight: true
      contentWidth: width
      contentHeight: answerColumn.implicitHeight
      clip: true
      boundsBehavior: Flickable.StopAtBounds

      ScrollBar.vertical: ScrollBar { }

      ColumnLayout {
        id: answerColumn
        width: parent.width
        spacing: Math.round(8 * root.theme.scale)

        AiMarkdownView {
          id: answerView
          objectName: "aiAnswerText"
          Layout.fillWidth: true
          visible: root.hasAnswer
          theme: root.theme
          sourceText: root.answerText
          background: null
          leftPadding: 0
          rightPadding: 0
          topPadding: 0
          Accessible.name: "AI answer text"
        }

        Text {
          Layout.fillWidth: true
          visible: !root.hasAnswer
          text: root.waitingForInput ? "The assistant asked a question before answering."
            : root.running ? "Thinking…"
            : root.failed ? "No answer was produced." : ""
          color: root.theme.textFaint
          wrapMode: Text.Wrap
          font.family: root.theme.fontFamily
          font.pixelSize: root.theme.fontSmall
        }
      }
    }

    RowLayout {
      id: footer
      Layout.fillWidth: true
      spacing: Math.round(8 * root.theme.scale)

      DeckButton {
        id: cancelButton
        theme: root.theme
        compact: true
        visible: root.running && !root.waitingForInput
        text: "Cancel"
        Accessible.name: "Cancel the answer"
        onClicked: root.cancelRequested()
      }
      DeckButton {
        id: retryButton
        theme: root.theme
        compact: true
        visible: root.failed || root.cancelled
        text: "Retry"
        onClicked: root.retryRequested()
      }
      DeckButton {
        id: copyButton
        theme: root.theme
        compact: true
        visible: root.completed && root.hasAnswer
        text: "Copy"
        Accessible.name: "Copy the answer"
        onClicked: { toast.show("Copying…"); root.copyRequested() }
      }
      DeckButton {
        id: expandButton
        theme: root.theme
        compact: true
        kind: root.waitingForInput ? Ui.ButtonVariant.Primary : Ui.ButtonVariant.Secondary
        text: root.expandLabel
        onClicked: root.expandRequested()
      }
      Item { Layout.fillWidth: true }
      DeckButton {
        id: closeButton
        theme: root.theme
        compact: true
        text: "Close · Esc"
        onClicked: root.closeRequested()
      }
    }
  }
}
