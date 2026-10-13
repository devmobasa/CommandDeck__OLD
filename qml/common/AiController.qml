import QtQuick
import "UiConstants.js" as Ui
import "Protocol.js" as Protocol

QtObject {
  id: root

  property int activeRequestId: 0
  property var preflight: null
  property string output: ""
  property string pendingOutput: ""
  property string outputStatus: ""
  property int outputRequestId: 0
  property string outputActionPending: ""
  property string outputPathPending: ""
  property bool overwritePending: false
  property string overwriteConfirmationToken: ""
  property bool savedOutputAvailable: false
  property string state: Ui.AiState.Idle
  property string error: ""
  property var usage: null
  property int generation: 0
  property string sessionId: ""
  property var lastDraft: null
  signal request(var message)

  property Timer outputFlushTimer: Timer {
    interval: 50
    repeat: false
    onTriggered: root.flushOutput()
  }

  function flushOutput() {
    if (pendingOutput.length === 0) return
    output += pendingOutput
    pendingOutput = ""
  }

  function begin(draft, requestId) {
    clear()
    lastDraft = draft
    activeRequestId = requestId
    state = Ui.AiState.Preflighting
    request({
      version: Protocol.Version,
      requestId: requestId,
      operation: Protocol.Operations.AiPreflight,
      aiDraft: draft
    })
  }

  function confirm(requestId) {
    if (!preflight || state !== Ui.AiState.Preflight) return
    activeRequestId = requestId
    generation = Number(preflight.generation || 0)
    sessionId = makeSessionId()
    output = ""
    error = ""
    state = Ui.AiState.Streaming
    request({
      version: Protocol.Version,
      requestId: requestId,
      operation: Protocol.Operations.AiStart,
      preflightId: preflight.id,
      payloadDigest: preflight.payloadDigest,
      generation: generation,
      sessionId: sessionId
    })
  }

  function cancel(requestId) {
    if (state !== Ui.AiState.Streaming || activeRequestId <= 0) return
    request({
      version: Protocol.Version,
      requestId: requestId,
      operation: Protocol.Operations.Cancel,
      targetRequestId: activeRequestId
    })
  }

  function outputAction(action, requestId, path, confirmationToken) {
    outputRequestId = requestId
    outputActionPending = action
    outputPathPending = path || ""
    outputStatus = ""
    overwritePending = false
    request({
      version: Protocol.Version,
      requestId: requestId,
      operation: Protocol.Operations.AiOutput,
      generation: generation,
      outputAction: action,
      path: path && path.length > 0 ? path : undefined,
      confirmationToken: confirmationToken && confirmationToken.length > 0
        ? confirmationToken : undefined
    })
  }

  function retry(requestId) {
    if (!lastDraft) return
    begin(lastDraft, requestId)
  }

  function handleMessage(message) {
    if (!message) return false
    if (message.requestId === outputRequestId && message.type === Protocol.Messages.ActionResult) {
      const result = message.actionResult || ({})
      outputStatus = result.message || (result.succeeded ? "Done" : "Action failed")
      if (result.succeeded && outputActionPending === Ui.OutputAction.Save) savedOutputAvailable = true
      overwritePending = !result.succeeded && outputActionPending === Ui.OutputAction.Save
        && outputStatus.toLowerCase().indexOf("confirm overwrite") >= 0
      overwriteConfirmationToken = overwritePending
        ? (result.confirmationToken || "") : ""
      outputRequestId = 0
      return true
    }
    if (message.requestId !== activeRequestId) return false
    if (message.type === Protocol.Messages.AiPreflightResult) {
      preflight = message.aiPreflight || null
      state = preflight ? Ui.AiState.Preflight : Ui.AiState.Failed
      return true
    }
    if (message.type === Protocol.Messages.AiChunk) {
      if (message.sessionId !== sessionId) return true
      const chunk = message.aiChunk || ({})
      if (Number(chunk.generation || 0) === generation) {
        pendingOutput += chunk.text || ""
        if (!outputFlushTimer.running) outputFlushTimer.start()
      }
      return true
    }
    if (message.type === Protocol.Messages.AiDone) {
      if (message.sessionId !== sessionId) return true
      const done = message.aiDone || ({})
      if (Number(done.generation || 0) !== generation) return true
      outputFlushTimer.stop()
      flushOutput()
      usage = done.usage || null
      error = done.error && done.error.summary ? done.error.summary : ""
      state = done.outcome || Ui.AiState.Failed
      activeRequestId = 0
      return true
    }
    if (message.type === Protocol.Messages.Error) {
      error = message.error || "AI request failed"
      state = Ui.AiState.Failed
      activeRequestId = 0
      return true
    }
    return message.type === Protocol.Messages.Done
  }

  function clear() {
    activeRequestId = 0
    preflight = null
    output = ""
    pendingOutput = ""
    outputStatus = ""
    outputRequestId = 0
    outputActionPending = ""
    outputPathPending = ""
    overwritePending = false
    overwriteConfirmationToken = ""
    savedOutputAvailable = false
    outputFlushTimer.stop()
    state = Ui.AiState.Idle
    error = ""
    usage = null
    generation = 0
    sessionId = ""
    lastDraft = null
  }

  // Only a request that was on screen turns into "connection lost"; an idle
  // controller stays idle so a daemon restart never opens the response panel.
  function disconnect() {
    const wasActive = state !== Ui.AiState.Idle
    clear()
    lastDraft = null
    if (!wasActive) return
    state = Ui.AiState.ConnectionLost
    error = "Connection lost; the AI request was cancelled."
  }

  function makeSessionId() {
    const seed = Date.now().toString(16) + Math.floor(Math.random() * 0x7fffffff).toString(16)
    return ("qml-" + seed + "-00000000000000000000000000000000").slice(0, 40)
  }
}
