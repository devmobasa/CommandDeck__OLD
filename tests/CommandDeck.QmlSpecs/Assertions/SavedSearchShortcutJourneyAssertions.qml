import QtQuick
import "Protocol.js" as Protocol

QtObject {
  id: journey
  required property var host
  readonly property var deck: host.part_integratedDeck
  property var previousSearches: null
  property string launcherDraft: ""

  function composer() {
    return deck.testAgentSurface
      ? host.part_hostEvents.findChild(deck.testAgentSurface, "agentComposer") : null
  }

  function formEditor() {
    const form = deck.testExtensionView ? deck.testExtensionView.formControl : null
    const field = form ? form.fieldItemAt(0) : null
    return field ? field.editor : null
  }

  function key(modifiers) {
    host.part_hostEvents.keyClick(modifiers === Qt.MetaModifier ? Qt.Key_J : Qt.Key_U, modifiers)
  }

  function unchanged(message) {
    host.expect(deck.queryText === launcherDraft && deck.activeFilters.length === 0, message)
  }

  function restore() {
    if (previousSearches !== null) deck.savedSearches = previousSearches
    previousSearches = null
  }

  function steps() {
    return [
      {
        ready: () => true,
        message: "saved-search shortcut checks leave the previous conversation",
        action: () => {
          deck.closeAgentSurface()
          deck.part_agentController.resetConversation()
          previousSearches = deck.savedSearches
          const expression = {includeFilterIds: ["files"], excludeFilterIds: [],
            clauses: [{field: null, text: "Shortcut target", excluded: false}]}
          deck.savedSearches = [
            {id: "saved-search:meta", name: "Meta search", shortcut: "SUPER+J", expression: expression},
            {id: "saved-search:control", name: "Control search", shortcut: "CTRL+ALT+U", expression: expression}
          ]
        }
      },
      {
        ready: () => deck.testSearchInput.activeFocus,
        message: "saved searches begin on the focused launcher",
        action: () => {
          key(Qt.ControlModifier | Qt.AltModifier)
          host.expect(deck.queryText === '"Shortcut target"' && deck.activeFilters[0].id === "files",
            "the launcher activates a saved-search chord")
          deck.activeFilters = []
          deck.queryText = "Launcher draft"
          deck.queryInputRequested(deck.queryText)
          launcherDraft = deck.queryText
          deck.openSettings()
        }
      },
      {
        ready: () => deck.testSettingsPanel !== null && deck.testSettingsPanel.searchControl.activeFocus,
        message: "Settings owns its search input before a saved-search chord",
        action: () => {
          const input = deck.testSettingsPanel.searchControl
          key(Qt.ControlModifier | Qt.AltModifier)
          unchanged("a saved-search chord cannot change the hidden launcher under Settings")
          const text = input.text
          host.part_hostEvents.keyClick(Qt.Key_N)
          host.expect(input.text === text + "n" && input.activeFocus,
            "Settings keeps keyboard input after the ignored launcher chord")
          deck.closeSettings()
          deck.openAgentSurface()
        }
      },
      {
        ready: () => composer() !== null && composer().activeFocus,
        message: "the assistant composer owns input before a saved-search chord",
        action: () => {
          key(Qt.ControlModifier | Qt.AltModifier)
          unchanged("a saved-search chord cannot change the hidden launcher under the composer")
          const text = composer().text
          host.part_hostEvents.keyClick(Qt.Key_A)
          host.expect(composer().text === text + "a" && composer().activeFocus,
            "the assistant keeps typing after the ignored launcher chord")
          deck.closeAgentSurface()
          const extension = deck.part_extensionController
          extension.open({id: "extension:demo:form", title: "Demo form"})
          extension.sessionId = "demo-session"
          extension.busy = false
          extension.snapshot = {revision: 1, content: {kind: Protocol.ExtensionViewKinds.Form,
            id: "demo-form", title: "Demo form", fields: [
              {id: "note", kind: Protocol.ExtensionFormFieldKinds.Text, label: "Note"}
            ], submit: {title: "Save", handle: "save"}}}
        }
      },
      {
        ready: () => formEditor() !== null && formEditor().activeFocus,
        message: "the extension form owns input before a saved-search chord",
        action: () => {
          key(Qt.ControlModifier | Qt.AltModifier)
          unchanged("a saved-search chord cannot change the hidden launcher under an extension form")
          const text = formEditor().text
          host.part_hostEvents.keyClick(Qt.Key_F)
          host.expect(formEditor().text === text + "f" && formEditor().activeFocus,
            "the extension form keeps typing after the ignored launcher chord")
          deck.part_extensionController.close()
        }
      },
      {
        ready: () => deck.testSearchInput.activeFocus,
        message: "closing the extension restores the launcher before the persisted Super chord",
        action: () => {
          key(Qt.MetaModifier)
          host.expect(deck.queryText === '"Shortcut target"' && deck.activeFilters[0].id === "files",
            "a persisted SUPER+J saved search responds to the real Meta+J key event")
          host.expect(deck.testSearchInput.activeFocus, "saved-search activation keeps search focused")
          deck.close()
          deck.open("{}")
        }
      },
      {
        ready: () => deck.testSearchInput.activeFocus,
        message: "reopening the launcher restores search for saved shortcuts",
        action: () => {
          key(Qt.MetaModifier)
          host.expect(deck.queryText === '"Shortcut target"' && deck.activeFilters[0].id === "files",
            "the persisted Super shortcut still activates after reopening")
        }
      }
    ]
  }
}
