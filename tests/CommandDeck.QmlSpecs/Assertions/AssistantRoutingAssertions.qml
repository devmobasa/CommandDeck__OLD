import QtQuick
import "Protocol.js" as Protocol
import Quickshell
import "." as CommandDeck

QtObject {
  required property var host
  function run(context) {
    host.verifyAgentController()
    host.verifyAgentSurface()
    host.verifyAgentSettings()
    host.verifySearchFieldHints()
    host.verifyDiscoverabilitySlice2()
    host.verifyHeyMail()

    // Inline answer pane: a switched-on, signed-in assistant answers beside
    // the row instead of opening the conversation.
    if (!host.part_integratedDeck.opened) host.part_integratedDeck.open("{}")
    const paneController = host.part_integratedDeck.agentTestController
    paneController.resetConversation()
    paneController.enabled = true
    paneController.backend = { id: "codex.local", displayName: "Codex", authenticationState: "authenticated" }
    const paneStart = host.part_integratedDeck.sentRequests.length
    host.part_integratedDeck.invokeAction({}, {
      kind: "agent-prompt",
      arguments: { prompt: "who founded Google", backendId: "codex.local" }
    }, false)
    const paneTurn = host.part_integratedDeck.sentRequests.slice(paneStart).find(request =>
      request.operation === Protocol.Operations.AgentTurnStart)
    host.expect(!host.part_integratedDeck.agentOpen
        && host.part_integratedDeck.answerMode === "agent"
        && host.part_integratedDeck.answerOpen
        && host.part_answerPaneSpec.query === "who founded Google"
        && host.part_answerPaneSpec.backendLabel === "Codex"
        && host.part_answerPaneSpec.running
        && paneTurn !== undefined && paneTurn.query === "who founded Google",
      "a ready assistant answers in the pane instead of opening the conversation")
    paneController.handleMessage({
      type: Protocol.Messages.AgentEvent, requestId: paneTurn.requestId,
      agentEvent: { kind: "text", generation: 9, sequence: 1, text: "Larry Page and Sergey Brin." }
    })
    paneController.flushDeltas()
    host.expect(host.part_answerPaneSpec.answerText === "Larry Page and Sergey Brin."
        && host.part_answerPaneSpec.running,
      "the pane streams the answer text as it arrives")
    paneController.handleMessage({
      type: Protocol.Messages.AgentEvent, requestId: paneTurn.requestId,
      agentEvent: { kind: "done", generation: 9, sequence: 2, done: { outcome: "completed" } }
    })
    host.expect(host.part_answerPaneSpec.completed && !host.part_answerPaneSpec.running
        && host.part_answerPaneSpec.answerText === "Larry Page and Sergey Brin."
        && host.part_answerPaneSpec.statusLine === "Done"
        && !host.part_integratedDeck.agentOpen,
      "the finished answer stays in the pane")
    paneController.state = "idle"
    host.expect(host.part_answerPaneSpec.completed && host.part_answerPaneSpec.statusLine === "Done"
        && host.part_answerPaneSpec.answerText === "Larry Page and Sergey Brin.",
      "a conversation snapshot that resets the state to idle keeps the answer as done")
    const paneCloseStart = host.part_integratedDeck.sentRequests.length
    host.part_integratedDeck.escapeFromSearch()
    host.expect(host.part_integratedDeck.answerMode === "" && host.part_integratedDeck.opened
        && host.part_integratedDeck.sentRequests.slice(paneCloseStart).every(request =>
          request.operation !== Protocol.Operations.AgentTurnCancel),
      "Esc closes a finished answer without cancelling or closing the palette")
    // A running answer is cancelled by Esc, and a question from the assistant
    // expands to the conversation on its own.
    const runningStart = host.part_integratedDeck.sentRequests.length
    host.part_integratedDeck.invokeAction({}, {
      kind: "agent-prompt", arguments: { prompt: "and Apple?", backendId: "codex.local" }
    }, false)
    const runningTurn = host.part_integratedDeck.sentRequests.slice(runningStart).find(request =>
      request.operation === Protocol.Operations.AgentTurnStart)
    paneController.handleMessage({
      type: Protocol.Messages.AgentEvent, requestId: runningTurn.requestId,
      agentEvent: { kind: "request", generation: 9, sequence: 3, request: { id: "q1", kind: "question", text: "Which Apple?" } }
    })
    host.expect(host.part_integratedDeck.agentOpen && host.part_integratedDeck.answerMode === "agent",
      "a pending assistant question expands the inline answer to the conversation")
    // Leaving assistant mode while the answer is still running cancels the
    // turn: here closing the conversation is what leaves assistant mode,
    // because no ai: filter is active in this harness.
    const cancelStart = host.part_integratedDeck.sentRequests.length
    host.part_integratedDeck.closeAgentSurface()
    host.expect(host.part_integratedDeck.answerMode === ""
        && host.part_integratedDeck.sentRequests.slice(cancelStart).some(request =>
          request.operation === Protocol.Operations.AgentTurnCancel),
      "leaving assistant mode with a running inline answer cancels the turn")
    // With the enable card pending, Enter opens the conversation as before.
    paneController.resetConversation()
    paneController.enabled = false
    host.part_integratedDeck.invokeAction({}, {
      kind: "agent-prompt", arguments: { prompt: "needs the card", backendId: "codex.local" }
    }, false)
    host.expect(host.part_integratedDeck.agentOpen && host.part_integratedDeck.answerMode === "",
      "a switched-off assistant still opens the conversation with its card")
    host.part_integratedDeck.closeAgentSurface()
    paneController.resetConversation()
    paneController.enabled = null
    paneController.backend = null

    const agentPromptStart = host.part_integratedDeck.sentRequests.length
    host.part_integratedDeck.invokeAction({}, {
      kind: "agent-prompt",
      arguments: { prompt: "Explain this repository", backendId: "codex.local" }
    }, false)
    const agentPromptRequests = host.part_integratedDeck.sentRequests.slice(agentPromptStart)
    host.expect(!host.part_integratedDeck.agentOpen
        && host.part_integratedDeck.answerMode === "agent"
        && host.part_integratedDeck.agentState === "starting"
        && agentPromptRequests.some(request =>
          request.operation === Protocol.Operations.AgentStatusGet)
        && agentPromptRequests.some(request =>
          request.operation === Protocol.Operations.AgentTurnStart
            && request.query === "Explain this repository"
            && request.version === 3),
      "an agent-prompt action with unknown status answers in the pane, refreshes status, and starts one turn")
    host.part_integratedDeck.agentTestController.enabled = false
    host.expect(host.part_integratedDeck.agentOpen && host.part_integratedDeck.answerMode === "agent",
      "a status that needs the enable card expands the inline answer to the conversation")
    host.part_integratedDeck.closeAgentSurface()
    host.part_integratedDeck.agentTestController.enabled = null

    const agentOpenStart = host.part_integratedDeck.sentRequests.length
    host.part_integratedDeck.invokeAction({}, { kind: "agent-open", arguments: {} }, false)
    host.expect(host.part_integratedDeck.agentOpen
        && host.part_integratedDeck.sentRequests.slice(agentOpenStart).some(request =>
          request.operation === Protocol.Operations.AgentConversationGet),
      "an agent-open action resumes the daemon-held conversation")

    const agentCloseStart = host.part_integratedDeck.sentRequests.length
    host.part_integratedDeck.toggle("{}")
    host.expect(!host.part_integratedDeck.agentOpen
        && host.part_integratedDeck.agentTestController.entries.length === 1
        && host.part_integratedDeck.sentRequests.slice(agentCloseStart).every(request =>
          request.operation !== Protocol.Operations.AgentClear),
      "closing the palette parks the assistant conversation instead of clearing it")

    const agentLoginStart = host.part_integratedDeck.sentRequests.length
    host.part_integratedDeck.sendAgentLoginAction(Protocol.AgentLoginActionIds.CopyCode)
    const agentLoginRequest = host.part_integratedDeck.sentRequests[agentLoginStart]
    host.expect(agentLoginRequest.operation === Protocol.Operations.Invoke
        && agentLoginRequest.resultId === "agent:login"
        && agentLoginRequest.actionId === Protocol.AgentLoginActionIds.CopyCode
        && agentLoginRequest.version === 3,
      "login helper actions reuse the daemon-owned cached result")
    host.part_integratedDeck.handleMessage({
      type: Protocol.Messages.ActionResult, requestId: agentLoginRequest.requestId,
      actionResult: { succeeded: false, message: "The login action failed." }
    })
    host.expect(host.part_integratedDeck.agentTestController.loginError === "The login action failed.",
      "the integrated login action shows a daemon failure")
    host.part_integratedDeck.toggle("{}")


    Object.assign(context, { paneController, paneStart, paneTurn, paneCloseStart, runningStart, runningTurn, cancelStart, agentPromptStart, agentPromptRequests, agentOpenStart, agentCloseStart, agentLoginStart, agentLoginRequest })
  }
}
