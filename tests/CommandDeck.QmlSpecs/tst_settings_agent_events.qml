import QtQuick
import QtTest
import "../../qml/common" as CommandDeck
import "../../qml/common/Protocol.js" as Protocol
import "../../qml/common/UiConstants.js" as Ui

EventPathFixture {
  id: scene
  EventPathTestCase {
    fixture: scene
    name: "CommandDeck_settings_agent"
    function test_settings_backend_change_refreshes_models_with_real_keys() {
      fixture.part_settingsPanel.agentBackends = [
        { id: "codex.local", displayName: "Codex" },
        { id: "claude.local", displayName: "Claude" }
      ]
      fixture.part_settingsPanel.agentModels = [{
        id: "gpt-5-codex", displayName: "Codex",
        reasoningEfforts: [{ id: "medium" }]
      }]
      fixture.part_settingsPanel.agentPreferenceValues = {
        enabled: true, backendId: "codex.local", modelId: "gpt-5-codex",
        reasoningEffort: "medium", trustLevel: 2,
        contextPolicy: "approved-folders", showHiddenModels: false,
        conversationTtlMinutes: 30, turnTimeoutMinutes: 20
      }
      fixture.part_settingsPanel.visible = true
      fixture.part_settingsPanel.showPage("assistant")
      wait(0)
      fixture.part_settingsPanel.synchronizeAgentChoices()
      fixture.part_settingsPanel.agentPreferencesEmitted = null
      fixture.part_settingsPanel.agentModelsBackendRequested = ""

      fixture.part_settingsPanel.agentBackendControl.forceActiveFocus()
      keyClick(Qt.Key_Down)

      compare(fixture.part_settingsPanel.agentBackendControl.currentIndex, 1)
      compare(fixture.part_settingsPanel.agentPreferencesEmitted.backendId, "claude.local")
      compare(fixture.part_settingsPanel.agentModelsBackendRequested, "claude.local",
        "changing backend immediately requests that backend's model inventory")
    }

    // A status without a backend (none chosen or installed yet) is ordinary:
    // the answer-only note stays hidden, without a binding warning.
    function test_status_without_a_backend_hides_the_answer_only_note() {
      failOnWarning(/Unable to assign/)
      const panel = fixture.part_settingsPanel
      panel.visible = true
      panel.showPage("assistant")
      wait(0)
      const note = panel.part_assistantCard.part_answerOnlyNote

      panel.agentStatus = { helper: { installed: false }, warnings: [] }
      verify(!note.visible)
      panel.agentStatus = { helper: { installed: false }, warnings: [],
        backend: { id: "ollama", kind: Protocol.AgentBackendKinds.AnswerOnly } }
      verify(note.visible, "an answer-only backend explains that it gives quick answers")
      panel.agentStatus = { helper: { installed: false }, warnings: [],
        backend: { id: "codex.local", kind: Protocol.AgentBackendKinds.CodexAppServer } }
      verify(!note.visible)
    }

    function test_shared_constants_preserve_wire_values_and_are_frozen() {
      // Literal expectations remain independent of the generated definitions.
      compare(Protocol.Version, 3)
      compare(Protocol.Operations.AgentPreferencesUpdate, "agent.preferences.update")
      compare(Protocol.Messages.AgentModelsResult, "agent-models-result")
      compare(Protocol.AgentBackendIds.ClaudeCode, "claude.local")
      compare(Protocol.AgentPreferenceFields.ReasoningEffort, "reasoningEffort")
      compare(Ui.AiState.Preflight, "preflight")
      compare(Ui.ProviderSettingsState.SavedSelectionFailed, "saved-selection-failed")
      compare(Ui.OutputAction.Save, "save")
      for (const group of [Protocol.Operations, Protocol.Messages,
          Protocol.AgentBackendIds, Protocol.AgentPreferenceFields,
          Ui.AiState, Ui.ProviderSettingsState, Ui.OutputAction]) {
        verify(Object.isFrozen(group), "shared constant tables must not be mutable")
      }
    }

    function test_settings_claude_efforts_with_real_keys() {
      const efforts = ["low", "medium", "high", "xhigh", "max"]
      fixture.part_settingsPanel.agentBackends = [{ id: "claude.local", displayName: "Claude Code" }]
      fixture.part_settingsPanel.agentModels = [{
        id: "sonnet", displayName: "Sonnet (latest)",
        reasoningEfforts: efforts.map(id => ({ id: id }))
      }]
      fixture.part_settingsPanel.agentPreferenceValues = {
        enabled: true, backendId: "claude.local", modelId: "sonnet",
        reasoningEffort: "high", trustLevel: 0
      }
      fixture.part_settingsPanel.visible = true
      fixture.part_settingsPanel.showPage("assistant")
      wait(0)
      verify(fixture.part_settingsPanel.searchControl.activeFocus, "Settings opens ready to type")
      const picker = fixture.part_settingsPanel.agentEffortControl
      verify(picker.enabled)
      compare(picker.count, 5)
      compare(picker.currentTitle, "High", "the saved effort is restored")
      for (let tab = 0; tab < 30 && !picker.activeFocus; tab++) keyClick(Qt.Key_Tab)
      verify(picker.activeFocus, "the enabled effort picker is reachable by Tab")

      for (let index = 0; index < efforts.length; index++) {
        fixture.part_settingsPanel.agentPreferencesEmitted = null
        keyClick(Qt.Key_Space)
        tryCompare(picker.popup, "visible", true)
        keyClick(Qt.Key_Home)
        for (let step = 0; step < index; step++) keyClick(Qt.Key_Down)
        keyClick(Qt.Key_Return)
        tryCompare(picker.popup, "visible", false)
        compare(picker.currentOption.id, efforts[index])
        verify(fixture.part_settingsPanel.agentPreferencesEmitted !== null)
        compare(fixture.part_settingsPanel.agentPreferencesEmitted.reasoningEffort, efforts[index])
        compare(fixture.part_settingsPanel.agentPreferencesEmitted.backendId, "claude.local")
        verify(picker.activeFocus, "selection restores focus to the picker")
      }

      fixture.part_settingsPanel.agentPreferencesEmitted = null
      keyClick(Qt.Key_Space)
      tryCompare(picker.popup, "visible", true)
      keyClick(Qt.Key_Up)
      keyClick(Qt.Key_Escape)
      tryCompare(picker.popup, "visible", false)
      compare(picker.currentOption.id, "max")
      compare(fixture.part_settingsPanel.agentPreferencesEmitted, null, "cancelling does not save an effort")
      verify(picker.activeFocus)
      verify(fixture.part_settingsPanel.visible, "Escape dismisses only the picker")
      keyClick(Qt.Key_Tab)
      verify(!picker.activeFocus)
      keyClick(Qt.Key_Backtab)
      verify(picker.activeFocus)
    }

    function test_settings_reasoning_effort_explains_unsupported_models() {
      fixture.part_settingsPanel.agentBackends = [{ id: "cursor.local", displayName: "Cursor" }]
      fixture.part_settingsPanel.agentModels = [{
        id: "auto", displayName: "Auto", reasoningEfforts: []
      }]
      fixture.part_settingsPanel.agentPreferenceValues = {
        enabled: true, backendId: "cursor.local", modelId: "auto",
        reasoningEffort: "", trustLevel: 3,
        contextPolicy: "approved-folders", showHiddenModels: false,
        conversationTtlMinutes: 30, turnTimeoutMinutes: 20
      }

      fixture.part_settingsPanel.synchronizeAgentChoices()

      compare(fixture.part_settingsPanel.agentModelControl.currentTitle, "Auto")
      verify(!fixture.part_settingsPanel.agentEffortControl.enabled)
      compare(fixture.part_settingsPanel.agentEffortControl.placeholder,
        "Reasoning effort is not supported")
    }

    function test_settings_agent_trust_challenge_uses_real_events() {
      fixture.part_settingsPanel.agentBackends = [{
        id: "codex.local", displayName: "Codex", processLocality: "local",
        inferenceLocality: "OpenAI"
      }]
      fixture.part_settingsPanel.agentModels = [{
        id: "gpt-5-codex", displayName: "Codex",
        reasoningEfforts: [{ id: "low" }, { id: "medium" }]
      }]
      fixture.part_settingsPanel.agentGrants = [
        { id: "grant-a", kind: "folder", state: "active", label: "Project folder" }
      ]
      fixture.part_settingsPanel.agentPreferenceValues = {
        enabled: true, backendId: "codex.local", modelId: "gpt-5-codex",
        reasoningEffort: "medium", trustLevel: 2,
        contextPolicy: "approved-folders", webSearchEnabled: false,
        showHiddenModels: false, conversationTtlMinutes: 30, turnTimeoutMinutes: 20
      }
      fixture.part_settingsPanel.visible = true
      fixture.part_settingsPanel.showPage("assistant")
      wait(0)
      fixture.part_settingsPanel.synchronizeAgentChoices()
      compare(fixture.part_settingsPanel.agentTrustControl.currentIndex, 2)
      compare(fixture.part_settingsPanel.agentEffortControl.currentIndex, 1,
        "the effort control reflects the saved reasoning effort")
      compare(fixture.part_settingsPanel.renderedAgentGrantCount, 1)

      fixture.part_settingsPanel.agentTrustControl.forceActiveFocus()
      keyClick(Qt.Key_Down)
      compare(fixture.part_settingsPanel.agentTrustChallengeLevel, 3)
      compare(fixture.part_settingsPanel.agentPreferencesEmitted, null)

      fixture.part_settingsPanel.agentChallenge = {
        id: "challenge-event", targetLevel: 6, targetLevelName: "Unrestricted",
        requiresPhrase: true, requiresDuration: true,
        prompt: "Confirm unrestricted authority",
        warnings: ["Protected paths no longer apply"]
      }
      wait(0)
      verify(fixture.part_settingsPanel.agentChallengeCardControl.visible)
      verify(!fixture.part_settingsPanel.agentChallengeConfirmControl.enabled)

      fixture.part_settingsPanel.agentPhraseControl.forceActiveFocus()
      typeText("ALLOW UNRESTRICTED AGENT")
      compare(fixture.part_settingsPanel.agentPhraseControl.text, "ALLOW UNRESTRICTED AGENT")
      verify(fixture.part_settingsPanel.agentChallengeConfirmControl.enabled)

      fixture.part_settingsPanel.agentDurationControl.currentIndex = 1
      fixture.part_settingsPanel.agentChallengeConfirmControl.forceActiveFocus()
      keyClick(Qt.Key_Space)
      compare(fixture.part_settingsPanel.agentTrustUpdate.challengeId, "challenge-event")
      compare(fixture.part_settingsPanel.agentTrustUpdate.level, 6)
      compare(fixture.part_settingsPanel.agentTrustUpdate.phrase, "ALLOW UNRESTRICTED AGENT")
      compare(fixture.part_settingsPanel.agentTrustUpdate.duration, "until-downgrade")
      compare(fixture.part_settingsPanel.agentPreferencesEmitted, null)

      fixture.part_settingsPanel.agentWebSearchControl.forceActiveFocus()
      keyClick(Qt.Key_Space)
      verify(fixture.part_settingsPanel.agentPreferencesEmitted !== null)
      compare(fixture.part_settingsPanel.agentPreferencesEmitted.webSearchEnabled, true)
      compare(fixture.part_settingsPanel.agentPreferencesEmitted.trustLevel, undefined)
      fixture.part_settingsPanel.visible = false
    }
  }
}
