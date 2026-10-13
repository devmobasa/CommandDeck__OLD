import QtQuick
import QtTest
import "../../qml/common" as CommandDeck
import "../../qml/common/Protocol.js" as Protocol
import "../../qml/common/UiConstants.js" as Ui

EventPathFixture {
  id: scene
  EventPathTestCase {
    fixture: scene
    name: "CommandDeck_settings_providers"
    function test_settings_ai_master_switch_hides_both_sections() {
      fixture.part_settingsPanel.aiSettings = aiSpecSettings(true, 3, [])
      fixture.part_settingsPanel.visible = true
      tryCompare(fixture.part_settingsPanel.searchControl, "activeFocus", true)
      compare(fixture.part_settingsPanel.filteredPages.filter(page => page.id === "ai").length, 1)
      fixture.part_settingsPanel.showPage("ai")
      mouseClick(findChild(fixture.part_settingsPanel, "aiQuickSection"))
      compare(fixture.part_settingsPanel.aiFeaturesControl.checked, true)
      compare(fixture.part_settingsPanel.aiManagementCardControl.visible, true)

      const toggled = []
      const capture = enabled => toggled.push(enabled)
      fixture.part_settingsPanel.aiFeaturesToggled.connect(capture)
      mouseClick(fixture.part_settingsPanel.aiFeaturesControl)
      compare(toggled, [false])
      fixture.part_settingsPanel.aiFeaturesEnabled = false
      compare(fixture.part_settingsPanel.aiFeaturesControl.checked, false)
      compare(fixture.part_settingsPanel.aiFeaturesCardControl.visible, true)
      compare(fixture.part_settingsPanel.aiManagementCardControl.visible, false)
      compare(fixture.part_settingsPanel.aiProvidersCardControl.visible, false)
      compare(fixture.part_settingsPanel.templatesCardControl.visible, false)
      verify(!findChild(fixture.part_settingsPanel, "aiAssistantSection").visible)
      compare(fixture.part_settingsPanel.filteredPages.filter(page => page.id === "ai").length, 1)

      keyClick(Qt.Key_F, Qt.ControlModifier)
      typeText("retention")
      compare(fixture.part_settingsPanel.filteredPages.length, 0)
      keyClick(Qt.Key_F, Qt.ControlModifier)
      typeText("assistant")
      verify(fixture.part_settingsPanel.filteredPages.some(page => page.id === "ai"))
      fixture.part_settingsPanel.showPage("ai")
      verify(fixture.part_settingsPanel.aiFeaturesCardControl.visible)
      keyClick(Qt.Key_Escape)

      fixture.part_settingsPanel.aiFeaturesEnabled = true
      compare(fixture.part_settingsPanel.aiFeaturesControl.checked, true)
      compare(fixture.part_settingsPanel.aiManagementCardControl.visible, true)
      compare(fixture.part_settingsPanel.aiProvidersCardControl.visible, true)
      compare(fixture.part_settingsPanel.templatesCardControl.visible, true)
      verify(findChild(fixture.part_settingsPanel, "aiAssistantSection").visible)
      fixture.part_settingsPanel.aiFeaturesToggled.disconnect(capture)
    }

    function test_ai_hub_wheel_scrolls_from_header_and_tabs() {
      const panel = fixture.part_settingsPanel
      fixture.height = 600
      panel.aiSettings = aiSpecSettings(true, 3, [])
      panel.visible = true
      panel.showPage("ai")
      verify(waitForRendering(panel))

      const hub = panel.part_aiHub
      const assistant = hub.assistantPage.contentItem
      const quickAnswers = hub.aiPage.contentItem
      const header = panel.aiFeaturesCardControl
      const assistantTab = findChild(panel, "aiAssistantSection")
      const quickTab = findChild(panel, "aiQuickSection")
      tryVerify(function() { return assistant.contentHeight > assistant.height })

      assistant.contentY = 0
      mouseWheel(hub.assistantPage, hub.assistantPage.width / 2, hub.assistantPage.height / 2, 0, -120)
      tryVerify(function() { return assistant.contentY > 0 })
      assistant.contentY = 0
      mouseWheel(header, 24, 24, 0, -120)
      tryVerify(function() { return assistant.contentY > 0 })
      const afterHeader = assistant.contentY
      mouseWheel(assistantTab, assistantTab.width / 2, assistantTab.height / 2, 0, -120)
      tryVerify(function() { return assistant.contentY > afterHeader })
      const assistantPosition = assistant.contentY

      mouseClick(quickTab)
      compare(hub.section, "quick")
      tryVerify(function() { return quickAnswers.contentHeight > quickAnswers.height })
      quickAnswers.contentY = 0
      mouseWheel(header, 24, 24, 0, -120)
      tryVerify(function() { return quickAnswers.contentY > 0 })
      const quickAfterHeader = quickAnswers.contentY
      mouseWheel(quickTab, quickTab.width / 2, quickTab.height / 2, 0, -120)
      tryVerify(function() { return quickAnswers.contentY > quickAfterHeader })
      compare(assistant.contentY, assistantPosition)

      panel.visible = false
    }

    // Tab and Shift+Tab step one field at a time from the provider type too.
    function test_tab_from_the_provider_type_reaches_the_display_name() {
      fixture.part_settingsPanel.aiSettings = aiSpecSettings(true, 3, [])
      fixture.part_settingsPanel.visible = true
      fixture.part_settingsPanel.showPage("ai")
      fixture.part_settingsPanel.openAiProviderEditor(null, fixture.part_settingsPanel.aiAddProviderControl)
      const editor = fixture.part_settingsPanel.aiProviderEditorControl
      tryCompare(editor.typeControl, "activeFocus", true)

      keyClick(Qt.Key_Tab)
      verify(editor.nameControl.activeFocus, "Tab from the provider type reaches the display name")
      keyClick(Qt.Key_Tab, Qt.ShiftModifier)
      verify(editor.typeControl.activeFocus, "Shift+Tab returns to the provider type")
      keyClick(Qt.Key_Tab, Qt.ShiftModifier)
      verify(editor.focusTargets().slice(-1)[0].activeFocus,
        "Shift+Tab from the provider type wraps to the editor's last control")

      editor.close()
      fixture.part_settingsPanel.visible = false
    }

    function test_settings_ai_provider_add_journey_uses_only_the_keyboard() {
      fixture.part_settingsPanel.aiSettings = aiSpecSettings(true, 3, [])
      fixture.part_settingsPanel.visible = true
      tryCompare(fixture.part_settingsPanel.searchControl, "activeFocus", true)
      typeText("direct ai")
      compare(fixture.part_settingsPanel.currentPage, "ai")
      keyClick(Qt.Key_Return)
      compare(fixture.part_settingsPanel.navigationControl.activeFocus, true)
      keyClick(Qt.Key_Tab)
      tryCompare(fixture.part_settingsPanel.aiFeaturesControl, "activeFocus", true)
      keyClick(Qt.Key_Tab)
      tryCompare(findChild(fixture.part_settingsPanel, "aiAssistantSection"), "activeFocus", true)
      keyClick(Qt.Key_Tab)
      tryCompare(findChild(fixture.part_settingsPanel, "aiQuickSection"), "activeFocus", true)
      keyClick(Qt.Key_Tab)
      tryCompare(fixture.part_settingsPanel.aiEnableControl, "activeFocus", true)
      keyClick(Qt.Key_Tab)
      tryCompare(fixture.part_settingsPanel.aiAddProviderControl, "activeFocus", true)
      keyClick(Qt.Key_Space)
      const editor = fixture.part_settingsPanel.aiProviderEditorControl
      tryCompare(editor, "opened", true)
      tryCompare(editor.typeControl, "activeFocus", true)
      compare(editor.mode, "add")

      // Arrows choose the type; Enter continues to the first editable field.
      keyClick(Qt.Key_Down)
      compare(editor.typeControl.currentIndex, 1)
      compare(editor.baseUrlControl.text, "https://api.openai.com/v1")
      compare(editor.credentialNameControl.text, "OPENAI_API_KEY")
      compare(editor.idControl.text, "openai")
      keyClick(Qt.Key_Return)
      tryCompare(editor.nameControl, "activeFocus", true)
      compare(editor.nameControl.text, "OpenAI")
      keyClick(Qt.Key_Tab)
      tryCompare(editor.baseUrlControl, "activeFocus", true)
      keyClick(Qt.Key_Tab)
      tryCompare(editor.credentialKindControl, "activeFocus", true)
      keyClick(Qt.Key_Tab)
      tryCompare(editor.credentialNameControl, "activeFocus", true)
      keyClick(Qt.Key_Tab)
      tryCompare(editor.remoteConsentControl, "activeFocus", true)
      verify(!editor.valid)
      keyClick(Qt.Key_Space)
      compare(editor.remoteConsentControl.checked, true)

      // Test is explicit, sends no prompt, and needs no model yet.
      keyClick(Qt.Key_Tab)
      tryCompare(editor.testControl, "activeFocus", true)
      verify(fixture.part_settingsPanel.aiDraftInvalidations > 0)
      keyClick(Qt.Key_Space)
      verify(fixture.part_settingsPanel.aiTestRequested !== null)
      compare(fixture.part_settingsPanel.aiTestRequested.baseUrl, "https://api.openai.com/v1")
      compare(fixture.part_settingsPanel.aiTestRequested.credential.name, "OPENAI_API_KEY")
      compare(fixture.part_settingsPanel.aiTestRequested.allowRemoteEndpoint, true)
      fixture.part_settingsPanel.aiTestResult = {
        draftRevision: 0, outcome: "models",
        models: [{ id: "gpt-5-mini", displayName: "gpt-5-mini" }, { id: "gpt-5", displayName: "gpt-5" }]
      }
      wait(0)
      verify(editor.chooseModelControl.visible)
      compare(fixture.part_settingsPanel.aiRowTestStates["openai"], "models")

      // The chooser focuses its search at once; Enter picks the filtered model.
      keyClick(Qt.Key_Tab)
      tryCompare(editor.modelControl, "activeFocus", true)
      keyClick(Qt.Key_Tab)
      tryCompare(editor.chooseModelControl, "activeFocus", true)
      keyClick(Qt.Key_Space)
      tryCompare(editor.modelPicker, "opened", true)
      tryCompare(editor.modelPicker.searchControl, "activeFocus", true)
      typeText("mini")
      compare(editor.modelPicker.filteredModels.length, 1)
      keyClick(Qt.Key_Return)
      tryCompare(editor.modelPicker, "opened", false)
      compare(editor.modelControl.text, "gpt-5-mini")
      tryCompare(editor.modelControl, "activeFocus", true)
      verify(editor.valid)

      // Escape on a dirty draft asks first, with Cancel focused.
      keyClick(Qt.Key_Escape)
      tryCompare(editor.discardConfirmation, "opened", true)
      tryCompare(editor.discardConfirmation.cancelControl, "activeFocus", true)
      keyClick(Qt.Key_Return)
      tryCompare(editor.discardConfirmation, "opened", false)
      compare(editor.opened, true)
      tryCompare(editor.cancelControl, "activeFocus", true)
      compare(fixture.part_settingsPanel.dismissals, 0)

      // Save and use carries the draft and the revision it was based on.
      keyClick(Qt.Key_Tab)
      tryCompare(editor.saveControl, "activeFocus", true)
      keyClick(Qt.Key_Tab)
      tryCompare(editor.saveUseControl, "activeFocus", true)
      keyClick(Qt.Key_Space)
      verify(fixture.part_settingsPanel.aiSaveRequested !== null)
      compare(fixture.part_settingsPanel.aiSaveRequested.create, true)
      compare(fixture.part_settingsPanel.aiSaveRequested.selectAfterSave, true)
      compare(fixture.part_settingsPanel.aiSaveRequested.revision, 3)
      compare(fixture.part_settingsPanel.aiSaveRequested.provider.id, "openai")
      compare(fixture.part_settingsPanel.aiSaveRequested.provider.model, "gpt-5-mini")
      compare(fixture.part_settingsPanel.aiSaveRequested.provider.kind, "openai-chat-completions")
      compare(fixture.part_settingsPanel.aiSaveRequested.provider.credential.kind, "environment")
      compare(fixture.part_settingsPanel.aiSaveRequested.provider.credential.name, "OPENAI_API_KEY")
      compare(fixture.part_settingsPanel.aiSaveRequested.provider.allowRemoteEndpoint, true)
      compare(fixture.part_settingsPanel.aiSaveRequested.provider.enabled, true)

      fixture.part_settingsPanel.aiSettingsBusy = true
      fixture.part_settingsPanel.aiSettingsStatus = "saving"
      verify(!editor.saveControl.enabled)
      fixture.part_settingsPanel.aiSettings = aiSpecSettings(true, 4, [fixture.part_settingsPanel.aiSaveRequested.provider])
      fixture.part_settingsPanel.aiSettingsBusy = false
      fixture.part_settingsPanel.aiSettingsStatus = "saved"
      tryCompare(editor, "opened", false)
      tryCompare(fixture.part_settingsPanel.aiAddProviderControl, "activeFocus", true)
      compare(fixture.part_settingsPanel.renderedAiProviderCount, 1)
      verify(fixture.part_settingsPanel.aiManagementStatusControl.text.indexOf("Saved.") >= 0)
      fixture.part_settingsPanel.visible = false
    }

    function test_settings_ai_provider_removal_defaults_to_cancel_and_restores_focus() {
      fixture.part_settingsPanel.aiSettings = aiSpecSettings(true, 5, [aiSpecProvider("one", true), aiSpecProvider("two", true)])
      fixture.part_settingsPanel.visible = true
      fixture.part_settingsPanel.showPage("ai")
      // Mouse coordinates need a rendered layout, not just a queued JS turn.
      verify(waitForRendering(fixture.part_settingsPanel))
      compare(fixture.part_settingsPanel.renderedAiProviderCount, 2)
      const row = fixture.part_settingsPanel.renderedAiProviderAt(0)
      verify(row !== null)
      mouseClick(row.removeControl)
      tryCompare(fixture.part_settingsPanel.aiRemoveConfirmationControl, "opened", true)
      tryCompare(fixture.part_settingsPanel.aiRemoveConfirmationControl.cancelControl, "activeFocus", true)
      compare(fixture.part_settingsPanel.aiRemoveConfirmationControl.prompt.indexOf("ONE") >= 0, true,
        fixture.part_settingsPanel.aiRemoveConfirmationControl.prompt)
      keyClick(Qt.Key_Return)
      tryCompare(fixture.part_settingsPanel.aiRemoveConfirmationControl, "opened", false)
      compare(fixture.part_settingsPanel.aiDeleteRequestedId, "")
      tryCompare(row.removeControl, "activeFocus", true)

      keyClick(Qt.Key_Space)
      tryCompare(fixture.part_settingsPanel.aiRemoveConfirmationControl, "opened", true)
      keyClick(Qt.Key_Right)
      tryCompare(fixture.part_settingsPanel.aiRemoveConfirmationControl.confirmControl, "activeFocus", true)
      keyClick(Qt.Key_Return)
      compare(fixture.part_settingsPanel.aiDeleteRequestedId, "one")
      compare(fixture.part_settingsPanel.aiDeleteRevision, 5)
      fixture.part_settingsPanel.aiSettingsBusy = true
      fixture.part_settingsPanel.aiSettingsStatus = "saving"
      fixture.part_settingsPanel.aiSettings = aiSpecSettings(true, 6, [aiSpecProvider("two", true)])
      fixture.part_settingsPanel.aiSettingsBusy = false
      fixture.part_settingsPanel.aiSettingsStatus = "saved"
      tryCompare(fixture.part_settingsPanel, "renderedAiProviderCount", 1)
      tryCompare(fixture.part_settingsPanel.aiAddProviderControl, "activeFocus", true)
      fixture.part_settingsPanel.visible = false
    }

    function test_settings_ai_provider_edit_enable_and_recovery_states_use_real_events() {
      // A taller surface keeps every editor control inside the pointer viewport.
      fixture.height = 1000
      fixture.part_settingsPanel.aiSettings = aiSpecSettings(false, 2, [aiSpecProvider("local", true)])
      fixture.part_settingsPanel.visible = true
      fixture.part_settingsPanel.showPage("ai")
      wait(0)
      verify(fixture.part_settingsPanel.aiManagementStatusControl.text.indexOf("Direct AI is off") >= 0)
      compare(fixture.part_settingsPanel.aiEnableControl.checked, false)
      mouseClick(fixture.part_settingsPanel.aiEnableControl)
      compare(fixture.part_settingsPanel.aiEnableRequests, 1)
      compare(fixture.part_settingsPanel.aiEnableRequestedValue, true)
      compare(fixture.part_settingsPanel.aiEnableRevision, 2)

      const row = fixture.part_settingsPanel.renderedAiProviderAt(0)
      mouseClick(row.toggleControl)
      verify(fixture.part_settingsPanel.aiSaveRequested !== null)
      compare(fixture.part_settingsPanel.aiSaveRequested.create, false)
      compare(fixture.part_settingsPanel.aiSaveRequested.provider.enabled, false)
      compare(fixture.part_settingsPanel.aiSaveRequested.provider.id, "local")

      mouseClick(row.editControl)
      const editor = fixture.part_settingsPanel.aiProviderEditorControl
      tryCompare(editor, "opened", true)
      compare(editor.mode, "edit")
      tryCompare(editor.nameControl, "activeFocus", true)
      verify(editor.idControl.readOnly)
      verify(!editor.saveUseControl.visible)
      verify(!editor.testControl.enabled)
      verify(!editor.dirty)
      typeText(" X")
      verify(editor.dirty)
      mouseClick(editor.cancelControl)
      tryCompare(editor.discardConfirmation, "opened", true)
      keyClick(Qt.Key_Escape)
      tryCompare(editor.discardConfirmation, "opened", false)
      compare(editor.opened, true)
      mouseClick(editor.cancelControl)
      mouseClick(editor.discardConfirmation.confirmControl)
      tryCompare(editor, "opened", false)
      tryCompare(row.editControl, "activeFocus", true)

      // A rejected save keeps the draft and shows the daemon's summary.
      mouseClick(row.editControl)
      tryCompare(editor, "opened", true)
      fixture.part_settingsPanel.aiSettingsStatus = "conflict"
      fixture.part_settingsPanel.aiSettingsErrorText = "The AI provider settings changed; reload before saving again."
      compare(editor.errorControl.visible, true)
      compare(editor.opened, true)
      fixture.part_settingsPanel.aiSettingsStatus = "idle"
      fixture.part_settingsPanel.aiSettingsErrorText = ""
      editor.close()

      // Losing the daemon keeps known rows visible and blocks mutations.
      fixture.part_settingsPanel.aiSettingsStatus = "connection-lost"
      compare(fixture.part_settingsPanel.renderedAiProviderCount, 1)
      verify(!fixture.part_settingsPanel.aiAddProviderControl.enabled)
      verify(!row.editControl.enabled)
      verify(fixture.part_settingsPanel.aiManagementStatusControl.text.indexOf("Cannot load provider settings") >= 0)
      compare(fixture.part_settingsPanel.aiReloadControl.text, "Reconnect")
      mouseClick(fixture.part_settingsPanel.aiReloadControl)
      compare(fixture.part_settingsPanel.aiReloads, 1)
      fixture.part_settingsPanel.aiSettingsStatus = "idle"
      fixture.part_settingsPanel.aiSettingsSupported = false
      verify(fixture.part_settingsPanel.aiManagementStatusControl.text.indexOf("cannot manage providers") >= 0)
      verify(!fixture.part_settingsPanel.aiAddProviderControl.enabled)
      fixture.part_settingsPanel.aiSettingsSupported = true
      fixture.part_settingsPanel.visible = false
      fixture.height = 600
    }
  }
}
