import QtQuick
import Quickshell
import "." as CommandDeck

QtObject {
  required property var host
  function run(){
    host.verifyAiProviderSettings()
    host.part_agentSettingsPanel.agentBackends = [{
      id: "codex.local", displayName: "Codex", processLocality: "local",
      inferenceLocality: "OpenAI"
    }, {
      id: "ollama", displayName: "Ollama", kind: "answer-only",
      processLocality: "local", inferenceLocality: "local"
    }]
    host.part_agentSettingsPanel.agentModels = [{
      id: "gpt-5-codex", displayName: "Codex", reasoningEfforts: [
        { id: "low", description: "Fast" },
        { id: "medium", description: "Balanced" }
      ]
    }]
    host.part_agentSettingsPanel.agentGrants = [
      { id: "grant-1", kind: "folder", state: "active", label: "Project folder" },
      { id: "grant-2", kind: "command", state: "active", label: "make build" }
    ]
    host.part_agentSettingsPanel.agentPreferenceValues = {
      enabled: true, backendId: "codex.local", modelId: "gpt-5-codex",
      reasoningEffort: "medium", trustLevel: 2,
      contextPolicy: "approved-folders", webSearchEnabled: false,
      showHiddenModels: false, conversationTtlMinutes: 30, turnTimeoutMinutes: 20
    }
    host.part_agentSettingsPanel.agentStatus = {
      helper: { installed: false },
      trust: { level: 2, levelName: "Approved context" },
      warnings: []
    }
    host.expect(host.part_agentSettingsPanel.renderedAgentGrantCount === 2
        && host.part_agentSettingsPanel.agentTrustControl.count === 7
        && host.part_agentSettingsPanel.agentTrustLevelNames[6] === "Unrestricted",
      "the assistant settings section renders every grant and the whole trust ladder")
    host.expect(host.part_agentSettingsPanel.assistantBackends.length === 1
        && host.part_agentSettingsPanel.agentBackendControl.count === 1
        && host.part_agentSettingsPanel.agentBackendOptions()[0].id === "codex.local",
      "quick answer providers do not appear in the assistant backend picker")
    host.expect(host.part_agentSettingsPanel.agentStatusControl.text.indexOf(
          "Package helper: not installed") >= 0
        && host.part_agentSettingsPanel.agentStatusControl.text.indexOf(
          "Approved context") >= 0,
      "assistant settings report helper availability and the active trust level")

    host.part_agentSettingsPanel.mutateAgentPreference("webSearchEnabled", true)
    host.expect(host.part_agentSettingsPanel.agentPreferencesEmitted !== null
        && host.part_agentSettingsPanel.agentPreferencesEmitted.webSearchEnabled === true
        && host.part_agentSettingsPanel.agentPreferencesEmitted.turnTimeoutMinutes === 20
        && host.part_agentSettingsPanel.agentPreferencesEmitted.trustLevel === undefined,
      "settings edits carry the whole preference object but never a trust level")

    host.part_agentSettingsPanel.agentChallenge = {
      id: "challenge-7", targetLevel: 6, targetLevelName: "Unrestricted",
      requiresPhrase: true, requiresDuration: true,
      prompt: "Confirm unrestricted authority",
      warnings: ["Protected paths no longer apply", "Credentials become reachable"]
    }
    host.expect(host.part_agentSettingsPanel.renderedAgentWarningCount === 2
        && host.part_agentSettingsPanel.agentChallengePromptControl.text
          === "Confirm unrestricted authority"
        && !host.part_agentSettingsPanel.agentChallengeSatisfied
        && !host.part_agentSettingsPanel.agentChallengeConfirmControl.enabled,
      "a level 6 challenge shows its warnings and stays unconfirmable")

    host.part_agentSettingsPanel.agentPhraseControl.text = "allow unrestricted agent"
    host.expect(!host.part_agentSettingsPanel.confirmAgentChallenge()
        && host.part_agentSettingsPanel.agentTrustUpdate === null,
      "a near-miss phrase cannot raise authority")

    host.part_agentSettingsPanel.agentPhraseControl.text = "ALLOW UNRESTRICTED AGENT"
    host.part_agentSettingsPanel.agentDurationControl.currentIndex = 0
    host.expect(host.part_agentSettingsPanel.confirmAgentChallenge()
        && host.part_agentSettingsPanel.agentTrustUpdate.challengeId === "challenge-7"
        && host.part_agentSettingsPanel.agentTrustUpdate.level === 6
        && host.part_agentSettingsPanel.agentTrustUpdate.duration === "one-hour",
      "the exact phrase plus a duration confirms the single-use challenge")

    host.part_agentSettingsPanel.synchronizeAgentChoices()
    host.expect(host.part_agentSettingsPanel.agentEffortControl.currentIndex === 1
        && host.part_agentSettingsPanel.agentEffortControl.currentTitle === "Medium"
        && host.part_agentSettingsPanel.agentModelControl.currentTitle === "Codex",
      "model and effort controls show the saved choice with a readable title")
    host.expect(host.part_agentSettingsPanel.agentTrustControl.count === 7
        && host.part_agentSettingsPanel.agentTrustControl.options[6].badge === "Phrase required"
        && host.part_agentSettingsPanel.agentTrustControl.options[2].description.indexOf("folders you approve") >= 0,
      "every trust level carries a description and the top level is marked")

    host.part_agentSettingsPanel.showPage("assistant")
    host.expect(host.part_agentSettingsPanel.currentPage === "ai"
        && host.part_agentSettingsPanel.part_aiHub.section === "assistant"
        && host.part_agentSettingsPanel.showPage("nowhere") && host.part_agentSettingsPanel.currentPage === "search",
      "AI sections share one page and unknown pages fall back to Search")

    host.part_agentSettingsPanel.agentModels = []
    host.part_agentSettingsPanel.agentModelsBackendRequested = ""
    host.part_agentSettingsPanel.agentStatus = {
      helper: { installed: false }, trust: { level: 2, levelName: "Approved context" },
      login: { state: "authenticated", method: "chatgpt" }, warnings: []
    }
    host.part_agentSettingsPanel.visible = true
    host.part_agentSettingsPanel.showPage("assistant")
    host.expect(host.part_agentSettingsPanel.agentModelsBackendRequested === "codex.local"
        && !host.part_agentSettingsPanel.agentLoginControl.visible
        && host.part_agentSettingsPanel.agentLogoutControl.visible,
      "an authenticated assistant page loads its models once without a manual refresh")

    host.part_agentSettingsPanel.agentStatus = {
      helper: { installed: false }, trust: { level: 2, levelName: "Approved context" },
      login: { state: "unauthenticated" }, warnings: []
    }
    host.part_agentSettingsPanel.agentLoginControl.clicked()
    host.expect(host.part_agentSettingsPanel.agentLoginControl.visible
        && !host.part_agentSettingsPanel.agentLogoutControl.visible
        && host.part_agentSettingsPanel.agentLogins === 1,
      "a signed-out assistant page offers sign-in from settings")
    host.part_agentSettingsPanel.visible = false

    // A row too narrow for label + control side by side stacks the control
    // (full width beneath the label); a wide one keeps them side by side. The
    // stacked row is taller because the control now sits under, not beside.
    host.expect(host.part_narrowSettingsRow.stacked
        && !host.part_wideSettingsRow.stacked
        && host.part_narrowSettingsRow.implicitHeight > host.part_wideSettingsRow.implicitHeight,
      "SettingsRow stacks its control when narrow and stays side by side when wide")
  }
}
