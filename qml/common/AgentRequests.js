.pragma library
.import "UiConstants.js" as Ui
.import "Protocol.js" as Protocol

function outputAction(root, action, requestId, path, token) {
  root.outputRequestId = requestId
  root.outputActionPending = action
  root.outputPathPending = path || ""
  root.outputStatus = ""
  root.overwritePending = false
  root.dispatch(requestId, root.requestKinds.Output, {
    version: Protocol.Version,
    requestId: requestId,
    operation: Protocol.Operations.AgentOutput,
    generation: root.outputGeneration,
    outputAction: action,
    path: path && path.length > 0 ? path : undefined,
    confirmationToken: token && token.length > 0 ? token : undefined
  })
}

function clearGrants(root, requestId) {
  root.dispatch(requestId, root.requestKinds.Grants, {
    version: Protocol.Version,
    requestId: requestId,
    operation: Protocol.Operations.AgentGrantsClear,
    confirmed: true
  })
}

function removeGrant(root, requestId, id) {
  root.dispatch(requestId, root.requestKinds.Grants, {
    version: Protocol.Version,
    requestId: requestId,
    operation: Protocol.Operations.AgentGrantsRemove,
    grantId: id
  })
}

function requestGrants(root, requestId) {
  root.dispatch(requestId, root.requestKinds.Grants, {
    version: Protocol.Version,
    requestId: requestId,
    operation: Protocol.Operations.AgentGrantsGet
  })
}

function updateTrust(root, requestId, challengeId, level, phrase, duration) {
  root.settingsError = ""
  root.dispatch(requestId, root.requestKinds.Trust, {
    version: Protocol.Version,
    requestId: requestId,
    operation: Protocol.Operations.AgentTrustUpdate,
    challengeId: challengeId,
    trustLevel: Number(level),
    phrase: phrase && phrase.length > 0 ? phrase : undefined,
    trustDuration: duration && duration.length > 0 ? duration : undefined
  })
}

function requestChallenge(root, requestId, level) {
  root.settingsError = ""
  root.dispatch(requestId, root.requestKinds.Challenge, {
    version: Protocol.Version,
    requestId: requestId,
    operation: Protocol.Operations.AgentTrustChallenge,
    trustLevel: Number(level)
  })
}

function updatePreferences(root, requestId, values) {
  const preferences = JSON.parse(JSON.stringify(values || ({})))
  if (root.preferences) {
    preferences.trustLevel = Number(root.preferences.trustLevel || 0)
    preferences.trustDuration = root.preferences.trustDuration || null
    preferences.trustLeaseExpiresUnixMilliseconds =
      root.preferences.trustLeaseExpiresUnixMilliseconds || null
  }
  root.settingsError = ""
  root.settingsErrorCategory = ""
  root.dispatch(requestId, root.requestKinds.Preferences, {
    version: Protocol.Version,
    requestId: requestId,
    operation: Protocol.Operations.AgentPreferencesUpdate,
    agentPreferences: preferences
  })
}

function requestModels(root, requestId, includeHidden, backendId) {
  const selected = backendId && backendId.length > 0
    ? backendId
    : (root.preferences && root.preferences.backendId
      ? String(root.preferences.backendId) : "")
  root.latestModelsRequestId = requestId
  root.models = []
  root.settingsError = ""
  root.dispatch(requestId, root.requestKinds.Models, {
    version: Protocol.Version,
    requestId: requestId,
    operation: Protocol.Operations.AgentModelsGet,
    backendId: selected.length > 0 ? selected : undefined,
    includeHidden: includeHidden === true
  })
}

function prewarm(root, requestId) {
  root.dispatch(requestId, root.requestKinds.Prewarm, {
    version: Protocol.Version,
    requestId: requestId,
    operation: Protocol.Operations.AgentPrewarm
  })
}

function clear(root, requestId) {
  root.dispatch(requestId, root.requestKinds.Clear, {
    version: Protocol.Version,
    requestId: requestId,
    operation: Protocol.Operations.AgentClear
  })
  root.resetConversation()
}

function cancelTurn(root, requestId) {
  root.dispatch(requestId, root.requestKinds.Cancel, {
    version: Protocol.Version,
    requestId: requestId,
    operation: Protocol.Operations.AgentTurnCancel
  })
}

function resolve(root, requestId, pendingId, decision) {
  root.pending = null
  root.dispatch(requestId, root.requestKinds.Resolve, {
    version: Protocol.Version,
    requestId: requestId,
    operation: Protocol.Operations.AgentContextResolve,
    pendingRequestId: pendingId,
    decision: decision
  })
}

function answer(root, requestId, pendingId, answers) {
  // Hide the card at once; the turn resumes and the daemon confirms with its
  // next event. A rejection re-raises the request through a fresh snapshot.
  root.pending = null
  root.dispatch(requestId, root.requestKinds.Input, {
    version: Protocol.Version,
    requestId: requestId,
    operation: Protocol.Operations.AgentTurnInput,
    pendingRequestId: pendingId,
    answers: answers || ({})
  })
}

function startTurn(root, requestId, text, backendId) {
  const query = String(text === undefined || text === null ? "" : text)
  if (query.trim().length === 0) return
  root.deltaFlushTimer.stop()
  root.activeRequestId = requestId
  root.error = ""
  root.pending = null
  root.activeText = ""
  root.activeReasoning = ""
  root.pendingText = ""
  root.pendingReasoning = ""
  root.activeStatus = ""
  root.tools = []
  root.usage = null
  root.state = Ui.AgentState.Starting
  root.pendingStartPrompt = query
  root.unsentPrompt = ""
  root.entries = root.entries.concat([{
    role: Protocol.AgentTranscriptRoles.User,
    text: query,
    turnId: "",
    truncated: false
  }])
  root.dispatch(requestId, root.requestKinds.Turn, {
    version: Protocol.Version,
    requestId: requestId,
    operation: Protocol.Operations.AgentTurnStart,
    query: query,
    backendId: backendId && backendId.length > 0 ? backendId : undefined
  })
}

function open(root, requestId) {
  root.conversationRequestId = requestId
  root.dispatch(requestId, root.requestKinds.Conversation, {
    version: Protocol.Version,
    requestId: requestId,
    operation: Protocol.Operations.AgentConversationGet
  })
}

function logout(root, requestId, backendId) {
  const target = root.loginBackendId(backendId)
  root.dispatch(requestId, root.requestKinds.Login, {
    version: Protocol.Version,
    requestId: requestId,
    operation: Protocol.Operations.AgentLogout,
    backendId: target.length > 0 ? target : undefined
  })
}

function cancelLogin(root, requestId, backendId) {
  const target = root.loginBackendId(backendId)
  root.dispatch(requestId, root.requestKinds.Login, {
    version: Protocol.Version,
    requestId: requestId,
    operation: Protocol.Operations.AgentLoginCancel,
    backendId: target.length > 0 ? target : undefined
  })
}

function completeLogin(root, requestId, code, backendId) {
  const value = String(code === undefined || code === null ? "" : code).trim()
  if (value.length === 0) return
  const target = root.loginBackendId(backendId)
  root.dispatch(requestId, root.requestKinds.Login, {
    version: Protocol.Version,
    requestId: requestId,
    operation: Protocol.Operations.AgentLoginComplete,
    loginCode: value,
    backendId: target.length > 0 ? target : undefined
  })
}

function startLogin(root, requestId, method, backendId) {
  if (root.loginStarting) return

  const target = root.loginBackendId(backendId)
  const selectedMethod = method && method.length > 0 ? method : root.defaultLoginMethod(target)
  root.loginError = ""
  root.loginActionStatus = ""
  root.loginStarting = true

  const request = {
    version: Protocol.Version,
    requestId: requestId,
    operation: Protocol.Operations.AgentLoginStart,
    loginMethod: selectedMethod,
    backendId: target.length > 0 ? target : undefined
  }

  root.dispatch(requestId, root.requestKinds.Login, request)
}

function invokeLoginAction(root, requestId, actionId, resultId) {
  root.loginActionRequestId = requestId
  root.loginActionId = actionId
  root.loginError = ""
  root.loginActionStatus = actionId === Protocol.AgentLoginActionIds.OpenUrl
    ? "The browser request is in progress." : "The copy request is in progress."

  root.dispatch(requestId, root.requestKinds.LoginAction, {
    version: Protocol.Version,
    requestId: requestId,
    operation: Protocol.Operations.Invoke,
    resultId: resultId,
    invocationReference: root.login ? root.login.invocationReference : undefined,
    actionId: actionId
  })
}

function defaultLoginMethod(root, backendId) {
  const methods = root.login && root.login.supportedMethods ? root.login.supportedMethods : []
  if (methods.length > 0) return String(methods[0])
  return Protocol.AgentLoginMethods.DeviceCode
}

function loginBackendId(root, backendId) {
  if (backendId && String(backendId).length > 0) return String(backendId)
  if (root.backend && root.backend.id) return String(root.backend.id)
  if (root.preferences && root.preferences.backendId) return String(root.preferences.backendId)
  return ""
}

function enable(root, requestId) {
  const values = root.preferences
    ? JSON.parse(JSON.stringify(root.preferences))
    : ({})
  values.enabled = true
  values.backendId = Protocol.AgentBackendIds.Codex
  values.trustLevel = 2
  values.contextPolicy = Protocol.AgentContextPolicies.ApprovedFolders
  root.dispatch(requestId, root.requestKinds.Preferences, {
    version: Protocol.Version,
    requestId: requestId,
    operation: Protocol.Operations.AgentPreferencesUpdate,
    agentPreferences: values
  })
}

function requestBackends(root, requestId) {
  root.dispatch(requestId, root.requestKinds.Backends, {
    version: Protocol.Version,
    requestId: requestId,
    operation: Protocol.Operations.AgentBackendsGet
  })
}

function refreshStatus(root, requestId) {
  root.dispatch(requestId, root.requestKinds.Status, {
    version: Protocol.Version,
    requestId: requestId,
    operation: Protocol.Operations.AgentStatusGet
  })
}

function cancelChallenge(root) {
  root.challenge = null
}

function release(root, requestId) {
  const tracked = root.trackedRequests
  delete tracked[requestId]
  root.trackedRequests = tracked
}

function dispatch(root, requestId, kind, message) {
  const tracked = root.trackedRequests
  tracked[requestId] = kind
  root.trackedRequests = tracked
  root.request(message)
}
