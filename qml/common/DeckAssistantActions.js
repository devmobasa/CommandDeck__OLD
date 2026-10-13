.pragma library
.import "UiConstants.js" as Ui
.import "Protocol.js" as Protocol

function sendAgentLoginAction(root, actionId) {
  root.part_agentController.invokeLoginAction(
    ++root.requestSequence, actionId, root.integrationIds.AgentLoginResult)
}

function requestAgentSettings(root) {
  if (!root.daemonAvailable) return
  root.part_agentController.refreshStatus(++root.requestSequence)
  root.part_agentController.requestBackends(++root.requestSequence)
  root.part_agentController.requestGrants(++root.requestSequence)
}

function startLoginFromSettings(root) {
  const status = root.agentStatus
  const backendId = status && status.backend ? String(status.backend.id || "") : ""
  const login = status ? status.login : null
  const pending = login && login.state === Protocol.AgentAuthenticationStates.Pending
  const methods = login && login.supportedMethods ? login.supportedMethods : []
  const method = methods.length > 0
    ? String(methods[0]) : root.part_agentController.defaultLoginMethod(backendId)

  root.closeSettings()
  root.openAgentSurface()
  if (!pending) root.part_agentController.startLogin(++root.requestSequence, method, backendId)
}

function syncAgentSettingsState(root) {
  root.agentBackends = root.part_agentController.backends
  root.agentModels = root.part_agentController.models
  root.agentGrants = root.part_agentController.grants
  root.agentChallenge = root.part_agentController.challenge
  root.agentStatus = root.part_agentController.status
  root.agentPreferences = root.part_agentController.preferences
  root.agentSettingsError = root.part_agentController.settingsError
}

function updateAssistantPrewarm(root) {
  if (!root.assistantModeActive) {
    root.assistantPrewarmArmed = false
    return
  }
  if (root.assistantPrewarmArmed) return
  if (root.part_agentController.enabled === false) return
  root.assistantPrewarmArmed = true
  root.part_agentController.prewarm(++root.requestSequence)
}

function escapeFromSearch(root) {
  if (root.answerOpen) { root.closeAnswer(); return }
  root.dismiss()
}

function hideAnswer(root) {
  root.answerMode = ""
  root.answerQuery = ""
  root.answerBackendId = ""
  root.answerBackendLabel = ""
  root.aiSurfaceExpanded = false
}

function closeAnswer(root) {
  if (!root.answerOpen) return
  if (root.answerMode === "agent" && root.agentTurnRunning) root.part_agentController.cancelTurn(++root.requestSequence)
  if (root.answerMode === "direct") root.clearAiState()
  root.hideAnswer()
  root.focusRevision++
}

function openAnswer(root, mode, query, backendId, label) {
  root.answerMode = mode
  root.answerQuery = query
  root.answerBackendId = backendId
  root.answerBackendLabel = label
  root.aiSurfaceExpanded = false
}

function agentBackendLabel(root, backendId) {
  const backends = root.part_agentController.backends || []
  for (let index = 0; index < backends.length; index++) {
    const entry = backends[index]
    if (entry && backendId.length > 0 && entry.id === backendId && entry.displayName) return String(entry.displayName)
  }
  return root.part_agentController.backend && root.part_agentController.backend.displayName
    ? String(root.part_agentController.backend.displayName) : "Assistant"
}

function expandAnswerIfCardNeeded(root) {
  if (root.answerMode !== "agent" || root.agentOpen) return
  if (root.agentNeedsCard()) root.openAgentSurface()
}

function agentAnswerReady(root) { return !root.agentNeedsCard() }

function agentNeedsCard(root) {
  if (root.part_agentController.enabled === false) return true
  const backend = root.part_agentController.backend
  const auth = backend && backend.authenticationState ? String(backend.authenticationState) : ""
  return auth === Protocol.AgentAuthenticationStates.Unauthenticated
    || auth === Protocol.AgentAuthenticationStates.Pending
    || auth === Protocol.AgentAuthenticationStates.Failed
}

function setAiFeaturesEnabled(root, enabled) {
  root.part_aiFeaturesStore.set(enabled === true)
  if (enabled === true) return
  if (root.agentOpen) root.closeAgentSurface()
  const settings = root.part_aiSettingsController.settings
  if (settings && settings.enabled === true) root.part_aiSettingsController.setEnabled(++root.requestSequence, false)
}

function refuseWhenAiOff(root) {
  if (root.aiFeaturesEnabled) return true
  root.statusText = root.aiOffStatusText
  return false
}

function closeAgentSurface(root) {
  root.agentOpen = false
  root.focusRevision++
}

function openAgentSurface(root) {
  if (!root.refuseWhenAiOff()) return
  root.agentOpen = true
  root.settingsOpen = false
  root.actionPanelOpen = false
  root.helpOpen = false
  if (root.part_agentController.enabled === null || root.part_agentController.enabled === undefined) {
    root.part_agentController.refreshStatus(++root.requestSequence)
  }
}
