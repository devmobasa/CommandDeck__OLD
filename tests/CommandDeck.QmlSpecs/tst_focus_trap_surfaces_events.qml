import QtQuick
import QtTest
import "../../qml/common" as CommandDeck
import "TabCycle.js" as TabCycle

// The assistant conversation and the AI provider editor keep Tab and
// Shift+Tab on their own usable controls, in one order both ways. Their focus
// restore on close is covered by the launcher journey (OnDemandSurfaceAssertions)
// and the provider editor journeys in tst_settings_providers_events.
EventPathFixture {
  id: scene
  EventPathTestCase {
    id: trapCase
    fixture: scene
    name: "CommandDeck_focus_trap_surfaces"

    function test_assistant_conversation_keeps_tab_inside() {
      const surface = fixture.part_agentEventSurface
      surface.opened = true
      const composer = findChild(surface, "agentComposer")
      waitForCondition(function() { return composer.activeFocus })

      const cycle = TabCycle.verifyCycle(trapCase, surface.Window.window, surface, composer, 40)
      verify(cycle.length > 1, "the conversation has more than one stop")
      surface.opened = false
    }

    function test_provider_editor_keeps_tab_inside() {
      const panel = fixture.part_settingsPanel
      panel.aiSettings = aiSpecSettings(true, 3, [])
      panel.visible = true
      panel.showPage("ai")
      panel.openAiProviderEditor(null, panel.aiAddProviderControl)
      const editor = panel.aiProviderEditorControl
      waitForValue(editor.typeControl, "activeFocus", true)

      const cycle = TabCycle.verifyCycle(trapCase, editor.Window.window, editor, editor.typeControl, 40)
      compare(cycle[1], editor.nameControl, "the display name follows the type")
      editor.close()
      panel.visible = false
    }
  }
}
