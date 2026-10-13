.pragma library
.import "AgentRequests.js" as AgentRequests
.import "UiConstants.js" as Ui
.import "ProtocolErrors.js" as ProtocolErrors
.import "Protocol.js" as Protocol

function disconnect(root) {
  const turnInFlight = root.state === Ui.AgentState.Starting
    || root.state === Protocol.AgentConversationStates.Running
    || root.state === Protocol.AgentConversationStates.Waiting
  if (root.loginStarting) {
    root.loginStarting = false
    root.loginError = "Connection lost during sign-in. Reconnect and try again."
  }
  if (root.loginActionRequestId !== 0) {
    root.loginActionStatus = ""
    root.loginError = "Connection lost. Try the login action again."
    root.loginActionRequestId = 0
  }
  root.deltaFlushTimer.stop()
  root.flushDeltas()
  root.trackedRequests = ({})
  root.activeRequestId = 0
  root.conversationRequestId = 0
  root.outputRequestId = 0
  root.challenge = null
  if (!turnInFlight) {
    if (root.state === Protocol.AgentConversationStates.Idle) root.error = ""
    return
  }
  root.state = Ui.AgentState.ConnectionLost
  root.error = "Connection lost; the assistant turn keeps running in the daemon."
}

function resetConversation(root) {
  root.deltaFlushTimer.stop()
  root.entries = []
  root.activeText = ""
  root.activeReasoning = ""
  root.pendingText = ""
  root.pendingReasoning = ""
  root.activeStatus = ""
  root.tools = []
  root.pending = null
  root.usage = null
  root.error = ""
  root.generation = 0
  root.lastSequence = 0
  root.activeTurnId = ""
  root.activeRequestId = 0
  root.conversationRequestId = 0
  root.outputRequestId = 0
  root.outputGeneration = 0
  root.outputActions = []
  root.savedOutputAvailable = false
  root.overwritePending = false
  root.overwriteConfirmationToken = ""
  root.outputStatus = ""
  root.outputActionPending = ""
  root.outputPathPending = ""
  root.pendingRequestCount = 0
  root.state = Protocol.AgentConversationStates.Idle
}

function finishTurn(root, done) {
  root.deltaFlushTimer.stop()
  root.flushDeltas()
  if (root.activeText.length > 0) {
    root.entries = root.entries.concat([{
      role: Protocol.AgentTranscriptRoles.Assistant,
      text: root.activeText,
      turnId: String(done.turnId || root.activeTurnId),
      truncated: false
    }])
  }
  root.activeText = ""
  root.activeReasoning = ""
  root.activeStatus = ""
  root.pending = null
  root.activeTurnId = ""
  if (done.outputGeneration !== undefined) {
    root.outputGeneration = Number(done.outputGeneration || 0)
  }
  if (done.outputActions !== undefined) {
    root.outputActions = done.outputActions || []
  }
  root.error = root.describeError(done.error)
  root.state = String(done.outcome || Protocol.AgentConversationStates.Completed)
}

function applyTool(root, tool) {
  if (!tool || tool.id === undefined) return
  const next = []
  let replaced = false
  for (let index = 0; index < root.tools.length; index++) {
    const existing = root.tools[index]
    if (existing && existing.id === tool.id) {
      next.push(tool)
      replaced = true
    } else {
      next.push(existing)
    }
  }
  if (!replaced) next.push(tool)
  root.tools = next
}

function applyEvent(root, event) {
  if (!event) return
  const eventGeneration = Number(event.generation || 0)
  // A brand-new turn has no snapshot yet, so its first event establishes
  // the epoch. Afterwards a differing generation is stale queued work from
  // before an `agent.clear` barrier and must never repopulate the view.
  if (root.generation === 0) root.generation = eventGeneration
  else if (eventGeneration !== root.generation) return

  const sequence = Number(event.sequence || 0)
  if (sequence <= root.lastSequence) return
  root.lastSequence = sequence

  if (event.turnId) root.activeTurnId = String(event.turnId)
  const kind = String(event.kind || "")
  if (kind !== Protocol.AgentEventKinds.Request && root.pending) root.pending = null

  if (kind === Protocol.AgentEventKinds.Status) {
    root.activeStatus = String(event.status || "")
    root.state = Protocol.AgentConversationStates.Running
    return
  }
  if (kind === Protocol.AgentEventKinds.Text) {
    root.pendingText += String(event.text || "")
    root.state = Protocol.AgentConversationStates.Running
    if (!root.deltaFlushTimer.running) root.deltaFlushTimer.start()
    return
  }
  if (kind === Protocol.AgentEventKinds.Reasoning) {
    root.pendingReasoning += String(event.text || "")
    root.state = Protocol.AgentConversationStates.Running
    if (!root.deltaFlushTimer.running) root.deltaFlushTimer.start()
    return
  }
  if (kind === Protocol.AgentEventKinds.Tool) {
    root.applyTool(event.tool || null)
    root.state = Protocol.AgentConversationStates.Running
    return
  }
  if (kind === Protocol.AgentEventKinds.Request) {
    root.pending = event.request || null
    root.state = Protocol.AgentConversationStates.Waiting
    return
  }
  if (kind === Protocol.AgentEventKinds.Usage) {
    root.usage = event.usage || null
    return
  }
  if (kind === Protocol.AgentEventKinds.Done) root.finishTurn(event.done || ({}))
}

function applySnapshot(root, snapshot) {
  if (!snapshot) return
  root.deltaFlushTimer.stop()
  root.pendingText = ""
  root.pendingReasoning = ""
  root.entries = snapshot.entries || []
  root.activeText = String(snapshot.activeText || "")
  root.activeReasoning = String(snapshot.activeReasoning || "")
  root.activeStatus = String(snapshot.activeStatus || "")
  root.tools = snapshot.tools || []
  root.pending = snapshot.pending || null
  root.generation = Number(snapshot.generation || 0)
  root.lastSequence = Number(snapshot.lastSequence || 0)
  root.activeTurnId = String(snapshot.activeTurnId || "")
  root.usage = snapshot.usage || null
  root.outputGeneration = Number(snapshot.outputGeneration || 0)
  root.outputActions = snapshot.outputActions || []
  root.error = snapshot.lastError && snapshot.lastError.summary
    ? String(snapshot.lastError.summary) : ""
  const summary = snapshot.summary || null
  root.state = summary && summary.state
    ? String(summary.state)
    : (root.pending ? Protocol.AgentConversationStates.Waiting : Protocol.AgentConversationStates.Idle)
}

function applyStatus(root, value) {
  root.status = value
  if (!value) return
  root.enabled = value.enabled === true
  root.backend = value.backend || null
  root.trust = value.trust || root.trust
  root.login = value.login || root.login
  root.preferences = value.preferences || root.preferences
  root.pendingRequestCount = Number(value.pendingRequestCount || 0)
}

function describeError(root, value) {
  if (!value) return ""
  if (typeof value === "string") return value
  return String(value.summary || value.category || "Assistant request failed")
}

// A start the daemon refused never became a turn: its optimistic line is not
// shown as sent, and its prompt is kept so it can be sent again.
function keepUnsentPrompt(root) {
  const prompt = root.pendingStartPrompt
  root.pendingStartPrompt = ""
  if (prompt.length === 0) return
  const entries = root.entries || []
  const last = entries.length > 0 ? entries[entries.length - 1] : null
  if (last && last.role === Protocol.AgentTranscriptRoles.User &&
      String(last.turnId || "") === "" && last.text === prompt) {
    root.entries = entries.slice(0, entries.length - 1)
  }
  root.unsentPrompt = prompt
}

function handleMessage(root, message) {
  if (!message) return false
  const type = String(message.type || "")
  const id = Number(message.requestId || 0)

  if (type === Protocol.Messages.AgentEvent) {
    if (id !== root.activeRequestId && id !== root.conversationRequestId) {
      return false
    }
    if (id === root.activeRequestId) root.pendingStartPrompt = ""
    root.applyEvent(message.agentEvent || ({}))
    return true
  }

  const kind = root.trackedRequests[id]
  if (kind === undefined) return false

  if (kind === root.requestKinds.Prewarm) {
    // Fire-and-forget: swallow both the done ack and any error without
    // touching state, the transcript, or root.error.
    if (type === Protocol.Messages.Done || type === Protocol.Messages.Error) root.release(id)
    return true
  }

  if (type === Protocol.Messages.ActionResult && kind === root.requestKinds.Output) {
    const outcome = message.actionResult || ({})
    root.outputStatus = outcome.message
      || (outcome.succeeded ? "Done" : "Action failed")
    if (outcome.succeeded && root.outputActionPending === Ui.OutputAction.Save) {
      root.savedOutputAvailable = true
    }
    root.overwritePending = !outcome.succeeded
      && root.outputActionPending === Ui.OutputAction.Save
      && root.outputStatus.toLowerCase().indexOf("confirm overwrite") >= 0
    root.overwriteConfirmationToken = root.overwritePending
      ? (outcome.confirmationToken || "") : ""
    root.outputRequestId = 0
    root.release(id)
    return true
  }

  if (type === Protocol.Messages.ActionResult && kind === root.requestKinds.LoginAction) {
    if (id === root.loginActionRequestId) {
      const outcome = message.actionResult || ({})
      root.loginActionStatus = outcome.succeeded
        ? (root.loginActionId === Protocol.AgentLoginActionIds.OpenUrl ? "The desktop accepted the request. Check your browser."
          : root.loginActionId === Protocol.AgentLoginActionIds.CopyCode ? "The code is on the clipboard."
          : "The URL is on the clipboard.")
        : ""
      root.loginError = outcome.succeeded ? "" : String(outcome.message || "The login action failed.")
      root.loginActionRequestId = 0
    }
    root.release(id)
    return true
  }

  if (type === Protocol.Messages.AgentStatusResult) {
    root.applyStatus(message.agentStatus || null)
    return true
  }

  if (type === Protocol.Messages.AgentBackendsResult) {
    const payload = message.agentBackends || ({})
    root.backends = payload.backends || []
    root.selectedBackendId = String(payload.selectedBackendId || "")
    if (payload.enabled !== undefined) root.enabled = payload.enabled === true
    return true
  }

  if (type === Protocol.Messages.AgentModelsResult) {
    if (id !== root.latestModelsRequestId) return true
    const payload = message.agentModels || ({})
    root.models = payload.models || []
    root.settingsError = root.describeError(payload.error)
    return true
  }

  if (type === Protocol.Messages.AgentPreferencesResult) {
    root.preferences = message.agentPreferences || root.preferences
    if (root.preferences) root.enabled = root.preferences.enabled === true
    return true
  }

  if (type === Protocol.Messages.AgentLoginResult) {
    root.loginStarting = false
    root.loginError = ""
    root.login = message.agentLogin || null
    const state = root.login ? String(root.login.state || "") : ""
    if (state === Protocol.AgentAuthenticationStates.Authenticated || state === Protocol.AgentAuthenticationStates.Failed || state === Protocol.AgentAuthenticationStates.Unauthenticated) {
      root.statusRefreshRequested()
    }
    return true
  }

  if (type === Protocol.Messages.AgentConversationResult) {
    if (kind === root.requestKinds.Turn && id === root.activeRequestId) root.pendingStartPrompt = ""
    root.applySnapshot(message.agentConversation || null)
    return true
  }

  if (type === Protocol.Messages.AgentTrustResult) {
    root.trust = message.agentTrust || null
    root.challenge = null
    if (root.trust && root.preferences) {
      const values = JSON.parse(JSON.stringify(root.preferences))
      values.trustLevel = Number(root.trust.level || 0)
      root.preferences = values
    }
    return true
  }

  if (type === Protocol.Messages.AgentTrustChallengeResult) {
    root.challenge = message.agentTrustChallenge || null
    return true
  }

  if (type === Protocol.Messages.AgentGrantsResult) {
    const payload = message.agentGrants || ({})
    root.grants = payload.grants || []
    return true
  }

  if (type === Protocol.Messages.Error) {
    if (kind === root.requestKinds.LoginAction) {
      if (id === root.loginActionRequestId) {
        root.loginActionStatus = ""
        root.loginError = String(message.error || "The login action failed.")
        root.loginActionRequestId = 0
      }
      root.release(id)
      return true
    }
    if (kind === root.requestKinds.Models && id !== root.latestModelsRequestId) {
      root.release(id)
      return true
    }
    const text = message.error || "Assistant request failed"
    if (kind === root.requestKinds.Turn || kind === root.requestKinds.Conversation) {
      root.deltaFlushTimer.stop()
      root.flushDeltas()
      root.error = text
      root.state = Protocol.AgentConversationStates.Failed
      if (kind === root.requestKinds.Turn && id === root.activeRequestId) keepUnsentPrompt(root)
      if (id === root.activeRequestId) root.activeRequestId = 0
      if (id === root.conversationRequestId) root.conversationRequestId = 0
    } else if (kind === root.requestKinds.Login) {
      root.loginStarting = false
      root.loginError = text
    } else if (kind === root.requestKinds.Preferences) {
      // A stale save comes back as "conflict: ...": show the sentence, keep the
      // category, and reload the settings the save was refused against.
      const parsed = ProtocolErrors.split(text)
      root.settingsError = parsed.summary
      root.settingsErrorCategory = parsed.category
      if (parsed.category === Protocol.AiErrorCategories.Conflict) root.statusRefreshRequested()
    } else {
      root.settingsError = text
    }
    root.release(id)
    return true
  }

  if (type === Protocol.Messages.Done) {
    if (kind === root.requestKinds.Login) root.loginStarting = false
    if (kind === root.requestKinds.Turn || kind === root.requestKinds.Conversation) {
      root.deltaFlushTimer.stop()
      root.flushDeltas()
      if (id === root.activeRequestId) root.activeRequestId = 0
      if (id === root.conversationRequestId) root.conversationRequestId = 0
    }
    root.release(id)
    return true
  }

  return false
}
