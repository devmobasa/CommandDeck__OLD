import QtQuick
import "../../qml/common" as CommandDeck
import "../../qml/common/Protocol.js" as Protocol

// The agent surface for the event paths: it drives the fixture's agent
// controller and records what it was asked to do.
CommandDeck.AgentSurface {
  id: agentEventSurface
  required property var host

  anchors.fill: parent
  z: 140
  theme: host.part_theme
  controller: host.part_agentEventController
  opened: false
  property int requestSequence: 900
  property int backToSearchCount: 0
  property int dismissCount: 0
  property int cancelCount: 0
  property string submitted: ""
  property var decisions: []
  property string answeredPendingId: ""
  property string answeredText: ""
  property string loginMethod: ""
  property string submittedLoginCode: ""
  property int enableCount: 0
  property int copyCodeCount: 0
  property int openLoginCount: 0
  property int backendSettingsCount: 0
  property string requestedOutputAction: ""
  onBackToSearch: backToSearchCount++
  onDismissed: {
    dismissCount++
    opened = false
  }
  onCancelRequested: {
    cancelCount++
    agentEventSurface.controller.cancelTurn(++requestSequence)
  }
  onLoginCodeSubmitted: code => submittedLoginCode = code
  onSubmitRequested: text => {
    submitted = text
    agentEventSurface.controller.startTurn(++requestSequence, text, "")
  }
  onDecisionRequested: (pendingId, decision) => {
    decisions = decisions.concat([pendingId + ":" + decision])
    agentEventSurface.controller.resolve(++requestSequence, pendingId, decision)
  }
  onAnswerRequested: (pendingId, answers) => {
    answeredPendingId = pendingId
    answeredText = String(answers["question-1"] || "")
    agentEventSurface.controller.answer(++requestSequence, pendingId, answers)
  }
  onLoginRequested: method => {
    loginMethod = method
    agentEventSurface.controller.startLogin(++requestSequence, method, "")
  }
  onEnableRequested: enableCount++
  onCopyCodeRequested: {
    copyCodeCount++
    agentEventSurface.controller.invokeLoginAction(++requestSequence, Protocol.AgentLoginActionIds.CopyCode, "agent:login")
  }
  onOpenLoginPageRequested: {
    openLoginCount++
    agentEventSurface.controller.invokeLoginAction(++requestSequence, Protocol.AgentLoginActionIds.OpenUrl, "agent:login")
  }
  onBackendSettingsRequested: backendSettingsCount++
  onOutputRequested: (action, path, token) => requestedOutputAction = action
}
