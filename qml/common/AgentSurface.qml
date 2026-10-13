pragma ComponentBehavior: Bound
import QtQuick
import "FocusTrap.js" as FocusTrap
import "UiConstants.js" as Ui
import "Protocol.js" as Protocol
import QtQuick.Layouts

// Inline assistant conversation.
//
// Escape never cancels a daemon-owned turn: it closes the top pending card
// without deciding it, and only then returns to search. Cancellation is a
// visible, named button.
FocusScope {
  id: root
  readonly property alias part_showPendingButton: assistantActions.part_showPendingButton
  readonly property alias part_cancelButton: assistantActions.part_cancelButton
  readonly property alias part_retryButton: assistantActions.part_retryButton
  readonly property alias part_copyButton: assistantActions.part_copyButton
  readonly property alias part_saveButton: assistantActions.part_saveButton
  readonly property alias part_overwriteButton: assistantActions.part_overwriteButton
  readonly property alias part_openSavedButton: assistantActions.part_openSavedButton
  readonly property alias part_clearButton: assistantActions.part_clearButton
  readonly property alias part_backButton: assistantActions.part_backButton
  readonly property alias part_deviceCodeButton: loginCard.part_deviceCodeButton
  readonly property alias part_browserLoginButton: loginCard.part_browserLoginButton
  readonly property alias part_apiKeyButton: loginCard.part_apiKeyButton
  readonly property alias part_loginCodeField: loginCard.part_loginCodeField
  readonly property alias part_loginCodeSubmitButton: loginCard.part_loginCodeSubmitButton
  readonly property alias part_copyCodeButton: loginCard.part_copyCodeButton
  readonly property alias part_copyUrlButton: loginCard.part_copyUrlButton
  readonly property alias part_openLoginButton: loginCard.part_openLoginButton
  readonly property alias part_loginCancelButton: loginCard.part_loginCancelButton
  readonly property alias part_backendSettingsButton: loginCard.part_backendSettingsButton
  readonly property alias part_reasoningToggle: transcriptPane.part_reasoningToggle
  readonly property alias part_outputToast: transcriptPane.part_outputToast
  readonly property alias part_optionRepeater: pendingCard.part_optionRepeater
  readonly property alias part_questionField: pendingCard.part_questionField
  readonly property alias part_questionSubmitButton: pendingCard.part_questionSubmitButton
  readonly property alias part_contextAllowButton: pendingCard.part_contextAllowButton
  readonly property alias part_contextAllowFolderButton: pendingCard.part_contextAllowFolderButton
  readonly property alias part_contextDenyButton: pendingCard.part_contextDenyButton
  readonly property alias part_contextCancelTurnButton: pendingCard.part_contextCancelTurnButton
  readonly property alias part_actionArgvText: pendingCard.part_actionArgvText
  readonly property alias part_actionRunButton: pendingCard.part_actionRunButton
  readonly property alias part_actionAlwaysButton: pendingCard.part_actionAlwaysButton
  readonly property alias part_actionDenyButton: pendingCard.part_actionDenyButton

  required property var theme
  required property var controller
  property bool opened: false
  property bool reasoningExpanded: false
  property bool pendingHidden: false
  property real nowMilliseconds: 0

  signal dismissed()
  signal backToSearch()
  signal cancelRequested()
  signal retryRequested()
  signal submitRequested(string text)
  signal outputRequested(string action, string path, string token)
  signal decisionRequested(string pendingId, string decision)
  signal answerRequested(string pendingId, var answers)
  signal loginRequested(string method)
  signal loginCodeSubmitted(string code)
  signal loginCancelRequested()
  signal enableRequested()
  signal openLoginPageRequested()
  signal copyCodeRequested()
  signal copyUrlRequested()
  signal backendSettingsRequested()

  readonly property var backend: root.controller.backend
  readonly property var preferences: root.controller.preferences
  readonly property var pending: root.controller.pending
  readonly property var login: root.controller.login

  readonly property var trustLevelNames: ["Answers", "Launcher", "Approved context",
    "Read entire home", "Packages", "Confirmed commands", "Unrestricted"]

  readonly property bool enableCardVisible: root.controller.enabled === false
  readonly property string authenticationState: root.backend
    && root.backend.authenticationState
      ? String(root.backend.authenticationState) : ""
  readonly property bool loginCardVisible: root.authenticationState === Protocol.AgentAuthenticationStates.Unauthenticated
    || root.authenticationState === Protocol.AgentAuthenticationStates.Pending
    || root.authenticationState === Protocol.AgentAuthenticationStates.Failed
  readonly property string backendDisplayName: root.backend && root.backend.displayName
    ? String(root.backend.displayName) : "the assistant"
  readonly property string codexPathWarning: {
    if (!root.backend || String(root.backend.id || "") !== Protocol.AgentBackendIds.Codex) return ""
    const warnings = root.controller.status && root.controller.status.warnings
      ? root.controller.status.warnings : []
    const warning = warnings.find(value => String(value).indexOf("Codex launcher shim") >= 0
      || String(value).indexOf("Codex on PATH is a launcher shim") >= 0
      || String(value).indexOf("configured Codex path is a launcher shim") >= 0)
    return warning ? String(warning) : ""
  }
  readonly property bool codexBinaryError: root.backend
    && String(root.backend.id || "") === Protocol.AgentBackendIds.Codex
    && (root.controller.loginError.indexOf("Codex binary") >= 0
      || root.controller.loginError.indexOf("Codex CLI") >= 0
      || root.controller.loginError.indexOf("Codex path") >= 0
      || root.controller.loginError.indexOf("launcher shim") >= 0)
  readonly property var loginMethods: root.login && root.login.supportedMethods
    ? root.login.supportedMethods : [Protocol.AgentLoginMethods.DeviceCode, Protocol.AgentLoginMethods.ApiKey]
  readonly property bool loginPending: root.login && String(root.login.state || "") === Protocol.AgentAuthenticationStates.Pending
  readonly property bool loginCodeNeeded: root.loginPending && root.login.requiresCode === true
  readonly property string loginAccountText: {
    const id = root.backend && root.backend.id ? String(root.backend.id) : ""
    if (id === Protocol.AgentBackendIds.ClaudeCode) return "Inference uses your Anthropic account."
    if (id === Protocol.AgentBackendIds.Cursor) return "Inference uses your Cursor account."
    if (id === Protocol.AgentBackendIds.Grok) return "Inference uses your xAI account."
    return "Inference uses your OpenAI account."
  }
  readonly property bool pendingCardVisible: root.pending !== null
    && root.pending !== undefined && !root.pendingHidden
  readonly property bool pendingParked: (root.pending !== null
    && root.pending !== undefined) && root.pendingHidden
  readonly property string pendingId: root.pending && root.pending.id
    ? String(root.pending.id) : ""
  readonly property string pendingKind: root.pending && root.pending.kind
    ? String(root.pending.kind) : ""
  readonly property var pendingQuestions: root.pending && root.pending.questions
    ? root.pending.questions : []
  readonly property var firstQuestion: root.pendingQuestions.length > 0
    ? root.pendingQuestions[0] : null
  readonly property var pendingContext: root.pending && root.pending.context
    ? root.pending.context : null
  readonly property var pendingAction: root.pending && root.pending.action
    ? root.pending.action : null
  readonly property bool pendingDestructive: root.pendingAction
    ? root.pendingAction.destructive === true : false
  readonly property var pendingCommandText: root.part_actionArgvText.text

  readonly property bool running: root.controller.state === Ui.AgentState.Starting
    || root.controller.state === Protocol.AgentConversationStates.Running
  readonly property bool waiting: root.controller.state === Protocol.AgentConversationStates.Waiting
  readonly property bool completed: root.controller.state === Protocol.AgentConversationStates.Completed
  readonly property bool retryable: root.controller.state === Protocol.AgentConversationStates.Failed
    || root.controller.state === Protocol.AgentConversationStates.Cancelled
  readonly property bool outputAvailable: root.controller.outputGeneration > 0
    || root.completed

  readonly property int trustLevel: root.controller.trust
    && root.controller.trust.level !== undefined
      ? Number(root.controller.trust.level)
      : (root.preferences && root.preferences.trustLevel !== undefined
        ? Number(root.preferences.trustLevel) : 0)
  readonly property string trustName: root.controller.trust
    && root.controller.trust.levelName
      ? String(root.controller.trust.levelName)
      : (root.trustLevel >= 0 && root.trustLevel < root.trustLevelNames.length
        ? root.trustLevelNames[root.trustLevel] : "Unknown")
  readonly property real leaseExpiry: root.controller.trust
    && root.controller.trust.leaseExpiresUnixMilliseconds
      ? Number(root.controller.trust.leaseExpiresUnixMilliseconds) : 0
  // Level 6 and any time-boxed lease both stay visibly red: the header is the
  // only place a user can see that unrestricted authority is live.
  readonly property bool trustAlarming: root.trustLevel >= 6 || root.leaseExpiry > 0
  readonly property string leaseText: {
    if (root.leaseExpiry <= 0) return ""
    const reference = root.nowMilliseconds > 0 ? root.nowMilliseconds : Date.now()
    const minutes = Math.max(0, Math.round((root.leaseExpiry - reference) / 60000))
    return minutes > 0 ? "lease " + minutes + " min left" : "lease expired"
  }

  readonly property string headerText: {
    const parts = []
    const descriptor = root.backend
    parts.push(descriptor && descriptor.displayName
      ? String(descriptor.displayName) : "Assistant")
    if (descriptor) {
      const process = String(descriptor.processLocality || "")
      if (process.length > 0) {
        parts.push(process === Protocol.AiLocality.Local ? "runs locally" : "runs " + process)
      }
      const inference = String(descriptor.inferenceLocality || "")
      if (inference.length > 0) parts.push("inference: " + inference)
    }
    const values = root.preferences
    if (values) {
      if (values.modelId) parts.push("model: " + String(values.modelId))
      // The reasoning effort is intentionally NOT folded into this elided
      // string; it gets its own always-visible chip in the header row.
      if (values.contextPolicy) {
        parts.push("context: " + (String(values.contextPolicy) === Protocol.AgentContextPolicies.ApprovedFolders
          ? "approved folders" : "ask every access"))
      }
      parts.push("web search: " + (values.webSearchEnabled === true ? "on" : "off"))
    }
    return parts.join(" · ")
  }

  // The reasoning effort ("thinking level") is surfaced on its own labeled chip
  // so it stays legible instead of drowning at the tail of the elided header.
  readonly property string reasoningEffort: root.preferences
    && root.preferences.reasoningEffort
      ? String(root.preferences.reasoningEffort) : ""
  readonly property string reasoningEffortLabel: {
    const value = root.reasoningEffort
    if (value.length === 0) return ""
    const named = ({
      low: "Low", medium: "Medium", high: "High",
      xhigh: "X-High", max: "Max", ultra: "Ultra"
    })
    if (named[value]) return named[value]
    return value.charAt(0).toUpperCase() + value.slice(1)
  }

  readonly property string trustBadgeText: root.leaseText.length > 0
    ? "Trust " + root.trustLevel + ": " + root.trustName + " · " + root.leaseText
    : "Trust " + root.trustLevel + ": " + root.trustName

  readonly property string statusText: {
    const state = root.controller.state
    if (state === Ui.AgentState.Starting) return "Starting " + (root.backend && root.backend.displayName ? String(root.backend.displayName) : "Codex")
    if (state === Protocol.AgentConversationStates.Waiting) return "Waiting for your answer"
    if (state === Protocol.AgentConversationStates.Running) {
      return root.controller.activeText.length > 0 ? "Answering" : "Thinking"
    }
    if (state === Protocol.AgentConversationStates.Failed) {
      return root.controller.error.length > 0 ? root.controller.error : "Turn failed"
    }
    if (state === Protocol.AgentConversationStates.Cancelled) return "Turn cancelled"
    if (state === Protocol.AgentConversationStates.Completed) return "Answer complete"
    if (state === Ui.AgentState.ConnectionLost) return root.controller.error
    return "Ask the assistant a question"
  }

  readonly property string runningHintText: root.running || root.waiting
    ? "Closing leaves the turn running" : ""

  function allowsDecision(name) {
    const allowed = root.pending && root.pending.decisions
      ? root.pending.decisions : []
    return allowed.length === 0 || allowed.indexOf(name) >= 0
  }

  function focusTargets() {
    const candidates = [composer, sendButton, root.part_showPendingButton]
    for (let index = 0; index < root.part_optionRepeater.count; index++) {
      const option = root.part_optionRepeater.itemAt(index)
      if (option) candidates.push(option)
    }
    candidates.push(root.part_questionField, root.part_questionSubmitButton,
      root.part_contextAllowButton, root.part_contextAllowFolderButton, root.part_contextDenyButton,
      root.part_contextCancelTurnButton, root.part_actionRunButton, root.part_actionAlwaysButton,
      root.part_actionDenyButton, root.part_reasoningToggle, enableButton, root.part_deviceCodeButton,
      root.part_browserLoginButton, root.part_apiKeyButton, root.part_loginCodeField, root.part_loginCodeSubmitButton,
      root.part_copyCodeButton, root.part_copyUrlButton, root.part_openLoginButton,
      root.part_loginCancelButton, root.part_backendSettingsButton,
      root.part_cancelButton, root.part_retryButton, root.part_copyButton,
      root.part_saveButton, root.part_overwriteButton, root.part_openSavedButton, root.part_clearButton, root.part_backButton)
    return candidates.filter(item => item && item.visible && item.enabled)
  }

  function moveFocus(direction) {
    FocusTrap.cycleTargets(root.focusTargets(), direction > 0)
  }

  function focusLoginAction() {
    if (!root.opened || !root.loginCardVisible) return false
    if (root.loginCodeNeeded) {
      root.part_loginCodeField.forceActiveFocus()
      return true
    }
    if (root.loginPending && root.part_openLoginButton.enabled) {
      root.part_openLoginButton.forceActiveFocus()
      return true
    }
    if (root.controller.loginStarting) return false
    const methods = [root.part_deviceCodeButton, root.part_browserLoginButton, root.part_apiKeyButton]
    const first = methods.find(button => button.visible && button.enabled)
    if (!first) return false
    first.forceActiveFocus()
    return true
  }

  function submitComposer() {
    const value = composer.text
    if (value.trim().length === 0) return
    // The daemon holds one turn at a time and rejects a second with "a turn is
    // already running". Submitting anyway optimistically appended the new line
    // and re-surfaced the previous turn's parked card, so a running or waiting
    // turn must be resolved (answered, decided, or cancelled) before a new one.
    if (root.running || root.waiting) return
    composer.text = ""
    root.submitRequested(value)
  }

  function submitAnswer(text) {
    if (!root.firstQuestion || root.pendingId.length === 0) return
    const answers = ({})
    answers[String(root.firstQuestion.id || "")] = String(text || "")
    root.part_questionField.text = ""
    root.answerRequested(root.pendingId, answers)
  }

  function consumeDraft(text) {
    if (composer.text === text) composer.text = ""
  }

  function decide(decision) {
    if (root.pendingId.length === 0) return
    root.decisionRequested(root.pendingId, decision)
  }

  onPendingChanged: root.pendingHidden = false

  visible: opened
  focus: opened
  Accessible.role: Accessible.Dialog
  Accessible.name: "Assistant conversation"

  // Escape closes the top pending card without approving or denying it. The
  // request stays parked in the daemon and "Show pending" brings it back.
  Keys.onEscapePressed: event => {
    if (root.pendingCardVisible) {
      root.pendingHidden = true
      event.accepted = true
      return
    }
    root.backToSearch()
    event.accepted = true
  }

  Keys.onPressed: event => {
    if (event.key === Qt.Key_Tab) {
      root.moveFocus((event.modifiers & Qt.ShiftModifier) ? -1 : 1)
      event.accepted = true
    } else if (event.key === Qt.Key_Backtab) {
      root.moveFocus(-1)
      event.accepted = true
    }
  }

  // Transcript. Each exchange is a chat bubble: what you asked sits on the
  // right in an accent tint, the answer on the left as plain text with a
  // thin accent rule, and the streaming answer uses the same shape.


  // One-click save into the deck's answer folder (~/Documents by default).
  // A native file chooser cannot run inside the shell process: with the GTK
  // platform theme it took Quickshell down, so the daemon still owns the
  // write, the overwrite confirmation, and reopening the saved file.
  property string saveDirectory: ""
  property string lastSavePath: ""

  function defaultSavePath() {
    if (root.saveDirectory.length === 0) return ""
    const now = new Date()
    const two = value => (value < 10 ? "0" : "") + value
    const stamp = now.getFullYear() + "-" + two(now.getMonth() + 1) + "-" + two(now.getDate())
      + "-" + two(now.getHours()) + two(now.getMinutes()) + two(now.getSeconds())
    return root.saveDirectory + "/commanddeck-answer-" + stamp + ".md"
  }

  function saveTo(path) {
    const target = String(path || "").trim()
    if (target.length === 0) return
    root.lastSavePath = target
    root.outputRequested(Ui.OutputAction.Save, target, "")
  }

  // Any question or answer can be copied. The shell owns keyboard focus while
  // the conversation is open, so a hidden editor's copy() reaches the Wayland
  // clipboard without a daemon round trip.
  property string lastCopiedText: ""
  function copyText(text) {
    const value = String(text || "")
    if (value.length === 0) return
    clipboardHelper.text = value
    clipboardHelper.selectAll()
    clipboardHelper.copy()
    clipboardHelper.text = ""
    root.lastCopiedText = value
    root.part_outputToast.show("Copied.")
  }

  TextEdit {
    id: clipboardHelper
    visible: false
    width: 1
    height: 1
  }

  Timer {
    id: leaseTicker
    interval: 1000
    repeat: true
    running: root.opened && root.leaseExpiry > 0
    onTriggered: root.nowMilliseconds = Date.now()
  }

  Rectangle {
    anchors.fill: parent
    color: root.theme.scrim
    // Clicking away behaves like Escape: return to search, never cancel or
    // discard a turn the daemon is still running.
    MouseArea {
      anchors.fill: parent
      onClicked: root.backToSearch()
    }
  }

  Rectangle {
    // Grows with the theme scale so the conversation is not a tiny box on a 4K
    // panel; the edge insets keep it clear of the screen bounds on small ones.
    width: Math.min(Math.round(860 * root.theme.scale), parent.width - 60)
    height: Math.min(Math.round(700 * root.theme.scale), parent.height - 60)
    anchors.centerIn: parent
    radius: root.theme.radius
    color: root.theme.popupBackground
    border.width: 1
    border.color: root.trustAlarming ? root.theme.urgent : root.theme.border

    MouseArea {
      anchors.fill: parent
      acceptedButtons: Qt.NoButton
    }

    ColumnLayout {
      anchors.fill: parent
      anchors.margins: root.theme.panelPadding
      spacing: root.theme.gap

      AgentHeader {
        id: assistantHeader
        host: root
      }

      // Inline enable card. The disclosure is the first thing a first-time
      // user reads, before any request leaves the machine.
      Rectangle {
        id: enableCard
        Layout.fillWidth: true
        Layout.preferredHeight: enableColumn.implicitHeight + root.theme.gap * 2
        visible: root.enableCardVisible
        radius: root.theme.radiusSmall
        color: root.theme.surface
        border.width: 1
        border.color: root.theme.separator

        ColumnLayout {
          id: enableColumn
          anchors.fill: parent
          anchors.margins: root.theme.gap
          spacing: root.theme.gap

          Text {
            Layout.fillWidth: true
            textFormat: Text.PlainText
            text: (root.backend && root.backend.displayName ? String(root.backend.displayName) : "Codex")
              + " runs locally. " + root.loginAccountText + " "
              + "Answers only until you raise the trust level."
            color: root.theme.text
            wrapMode: Text.Wrap
            font.family: root.theme.fontFamily
            font.pixelSize: root.theme.fontBody
          }

          AgentButton {
            id: enableButton
            theme: root.theme
            kind: Ui.ButtonVariant.Primary
            objectName: "agentEnable"
            text: "Enable"
            Accessible.name: "Enable the assistant at trust level 2"
            Accessible.description: "Stores approved folders context and answers only"
            onClicked: root.enableRequested()
            Keys.onTabPressed: root.moveFocus(1)
            Keys.onBacktabPressed: root.moveFocus(-1)
          }
        }
      }

      // Login card. CommandDeck owns the credential, so the device code and
      // its verification URL are shown here rather than in a browser handoff.
      AgentLoginCard {
        id: loginCard
        host: root
      }

      AgentTranscriptPane {
        id: transcriptPane
        host: root
      }

      // Pending card area. One card at a time; Escape parks it rather than
      // answering for the user.
      AgentPendingCard {
        id: pendingCard
        host: root
      }

      Text {
        id: statusLine
        objectName: "agentStatusLine"
        Layout.fillWidth: true
        textFormat: Text.PlainText
        text: root.runningHintText.length > 0
          ? root.statusText + " · " + root.runningHintText
          : root.statusText
        color: root.controller.state === Protocol.AgentConversationStates.Failed
          ? root.theme.urgent : root.theme.textFaint
        wrapMode: Text.Wrap
        font.family: root.theme.fontFamily
        font.pixelSize: root.theme.fontSmall
        Accessible.role: Accessible.StaticText
        Accessible.name: "Assistant status: " + text
      }

      // Copy and save confirmations show as a toast over the transcript; an
      // overwrite prompt stays in place because it needs a decision.
      Connections {
        target: root.controller
        function onOutputStatusChanged() {
          const status = root.controller.outputStatus
          if (status.length === 0 || root.controller.overwritePending) return
          root.part_outputToast.show(status.indexOf("saved") >= 0 && root.lastSavePath.length > 0
            ? "Saved to " + root.lastSavePath : status)
        }
        // A start the daemon refused returns its prompt to an empty composer.
        function onUnsentPromptChanged() {
          const prompt = root.controller.unsentPrompt
          if (prompt.length > 0 && composer.text.length === 0) composer.text = prompt
        }
      }

      Text {
        Layout.fillWidth: true
        visible: root.controller.overwritePending && root.controller.outputStatus.length > 0
        textFormat: Text.PlainText
        text: root.controller.outputStatus
        color: root.theme.urgent
        wrapMode: Text.Wrap
        Accessible.role: Accessible.StaticText
        Accessible.name: "Assistant output action: " + text
      }

      RowLayout {
        Layout.fillWidth: true
        spacing: root.theme.gap

        AgentField {
          id: composer
          theme: root.theme
          objectName: "agentComposer"
          Layout.fillWidth: true
          placeholderText: root.waiting
            ? "Answer the request above to continue"
            : (root.running ? "The assistant is answering…" : "Ask the assistant")
          Accessible.name: "Assistant question"
          Accessible.description: "Enter sends the question; the turn keeps "
            + "running if you close this view"
          onAccepted: root.submitComposer()
          Keys.onTabPressed: root.moveFocus(1)
          Keys.onBacktabPressed: root.moveFocus(-1)
        }

        AgentButton {
          id: sendButton
          theme: root.theme
          kind: Ui.ButtonVariant.Primary
          objectName: "agentSend"
          text: "Send"
          enabled: composer.text.trim().length > 0 && !root.running && !root.waiting
          Accessible.name: "Send the question"
          onClicked: root.submitComposer()
          Keys.onTabPressed: root.moveFocus(1)
          Keys.onBacktabPressed: root.moveFocus(-1)
        }
      }

      AgentActionBar {
        id: assistantActions
        host: root
      }
    }
  }

  onOpenedChanged: if (opened) Qt.callLater(function() {
    if (!root.focusLoginAction()) composer.forceActiveFocus()
  })
  onLoginPendingChanged: if (opened && loginPending) Qt.callLater(root.focusLoginAction)
}
