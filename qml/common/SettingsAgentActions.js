.pragma library
.import "UiConstants.js" as Ui
.import "Protocol.js" as Protocol

function maybeRefreshAgentModels(root) {
  if (!root.visible || root.currentPage !== Ui.SettingsPage.Ai
      || root.part_aiHub.section !== "assistant") return false
  if ((root.agentModels || []).length > 0 || root.agentModelsAutoRequested) return false
  if (!root.agentEnabled || !root.agentAuthenticated) return false
  const backend = root.assistantBackends[Math.max(0, root.part_agentBackendChoice.currentIndex)]
  root.agentModelsAutoRequested = true
  root.agentModelsRefreshRequested(backend ? String(backend.id) : "")
  return true
}

function synchronizeAgentEffort(root) {
  const efforts = root.agentReasoningEfforts()
  if (efforts.length === 0) return
  const values = root.agentPreferenceValues || ({})
  const model = root.agentSelectedModel()
  let index = root.indexOfId(efforts, values.reasoningEffort)
  if (index < 0 && model) index = root.indexOfId(efforts, model.defaultReasoningEffort)
  root.part_agentEffortChoice.currentIndex = Math.max(0, index)
}

function synchronizeAgentChoices(root) {
  const values = root.agentPreferenceValues
  if (!values) return
  const backendIndex = root.indexOfId(root.assistantBackends, values.backendId)
  root.part_agentBackendChoice.currentIndex = backendIndex
  const modelIndex = root.indexOfId(root.agentModels, values.modelId)
  if (modelIndex >= 0) {
    root.part_agentModelChoice.currentIndex = modelIndex
  } else if ((root.agentModels || []).length > 0) {
    const defaultIndex = (root.agentModels || []).findIndex(value => value.isDefault === true)
    root.part_agentModelChoice.currentIndex = Math.max(0, defaultIndex)
  }
  root.synchronizeAgentEffort()
  const level = Number(values.trustLevel || 0)
  if (level >= 0 && level < root.agentTrustLevelNames.length) {
    root.part_agentTrustChoice.currentIndex = level
  }
  root.part_agentContextPolicyChoice.currentIndex =
    String(values.contextPolicy || "") === Protocol.AgentContextPolicies.AskEveryAccess ? 1 : 0
  root.part_agentRetentionSpin.value = Math.max(0, Math.min(1440,
    Number(values.conversationTtlMinutes || 0)))
  root.part_agentTurnTimeoutSpin.value = Math.max(5, Math.min(120,
    Number(values.turnTimeoutMinutes || 5)))
  root.part_agentDurationChoice.currentIndex =
    String(values.trustDuration || "") === Protocol.AgentTrustDurations.UntilDowngrade ? 1 : 0
}

function confirmAgentChallenge(root) {
  if (!root.agentChallengeSatisfied) return false
  const challenge = root.agentChallenge
  const duration = root.agentChallengeRequiresDuration
    ? (root.part_agentDurationChoice.currentIndex === 1 ? Protocol.AgentTrustDurations.UntilDowngrade : Protocol.AgentTrustDurations.OneHour)
    : ""
  root.agentTrustUpdateRequested(
    String(challenge.id || ""),
    Number(challenge.targetLevel || 0),
    root.agentChallengeRequiresPhrase ? root.part_agentPhraseField.text : "",
    duration)
  root.part_agentPhraseField.text = ""
  return true
}

function mutateAgentPreference(root, field, value) {
  const values = root.cloneAgentPreferences()
  values[field] = value
  delete values.trustLevel
  root.agentPreferencesChanged(values)
  return true
}

function cloneAgentPreferences(root) {
  return root.agentPreferenceValues
    ? JSON.parse(JSON.stringify(root.agentPreferenceValues))
    : ({})
}

function agentEffortOptions(root) {
  const model = root.agentSelectedModel()
  const fallbackDefault = model ? String(model.defaultReasoningEffort || "") : ""
  return root.agentReasoningEfforts().map(value => {
    const id = String(value.id || "")
    const heavy = id === Ui.ReasoningEffort.Max || id === Ui.ReasoningEffort.Ultra
    return {
      id: id,
      title: root.agentEffortTitle(id),
      description: String(value.description || ""),
      badge: id === fallbackDefault ? "Default" : (heavy ? "Higher usage" : ""),
      warning: heavy && id !== fallbackDefault
    }
  })
}

function agentReasoningEfforts(root) {
  const model = root.agentSelectedModel()
  return model && model.reasoningEfforts ? model.reasoningEfforts : []
}

function agentSelectedModel(root) {
  const values = root.agentModels || []
  if (root.part_agentModelChoice.currentIndex < 0
      || root.part_agentModelChoice.currentIndex >= values.length) {
    return null
  }
  return values[root.part_agentModelChoice.currentIndex]
}

function agentEffortTitle(root, id) {
  switch (String(id || "")) {
    case Ui.ReasoningEffort.Low: return "Low"
    case Ui.ReasoningEffort.Medium: return "Medium"
    case Ui.ReasoningEffort.High: return "High"
    case Ui.ReasoningEffort.ExtraHigh: return "Extra high"
    case Ui.ReasoningEffort.Max: return "Max"
    case Ui.ReasoningEffort.Ultra: return "Ultra"
    default: return String(id || "")
  }
}

function agentModelOptions(root) {
  return (root.agentModels || []).map(value => ({
    id: String(value.id || ""),
    title: String(value.displayName || value.id || ""),
    description: String(value.description || ""),
    badge: value.isDefault === true ? "Default" : (value.hidden === true ? "Hidden" : "")
  }))
}

function agentBackendOptions(root) {
  return root.assistantBackends.map(value => ({
    id: String(value.id || ""),
    title: String(value.displayName || value.id || ""),
    description: root.agentLocalityLabel(value)
  }))
}

function agentLocalityLabel(root, backend) {
  if (!backend) return ""
  const process = String(backend.processLocality || "")
  const inference = String(backend.inferenceLocality || "")
  const parts = []
  if (process.length > 0) {
    parts.push(process === Protocol.AiLocality.Local ? "runs locally" : "runs " + process)
  }
  if (inference.length > 0) parts.push("inference: " + inference)
  return parts.join(", ")
}
