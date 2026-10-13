import QtQuick
import QtQuick.Controls
import QtTest
import "../../qml/common" as Deck
import "../../qml/common/Protocol.js" as Protocol
import "../../qml/common/DeckMessageHandling.js" as Messages

Item {
  id: scene
  width: 1000
  height: 740
  property int sequence: 0
  property bool settingsShown: false
  property bool delayValidation: false
  property var preferences: ({clipboardHistoryShortcut: "CTRL+SHIFT+V", hideAfterAction: false})
  property var savedSearches: []
  Deck.Theme { id: theme; loadShellTokens: false }
  TextField { id: search; width: 250; text: "main query" }
  Deck.PersonalizationController {
    id: personalization
    nextRequestId: function() { return ++scene.sequence }
    onPreferencesDraftChanged: draft => scene.preferences = draft
    onRollbackRequested: preferences => scene.preferences = preferences
  }
  Deck.ClipboardHistoryController {
    id: controller
    nextRequestId: function() { return ++scene.sequence }
    frontendInstance: "123456781234423482341234567890ab"
    openGeneration: 1
  }
  Deck.ClipboardHistorySurface {
    id: browser
    anchors.fill: parent
    theme: theme
    controller: controller
    returnFocus: search
  }
  Deck.ClipboardOpenShortcut {
    id: shortcut
    preferences: scene.preferences
    savedSearches: scene.savedSearches
    active: !scene.settingsShown && !controller.opened
    stateReady: true
    preferenceUpdateInFlight: personalization.preferenceInFlight
    onRequested: controller.open("")
  }
  Deck.ClipboardShortcutSettings {
    id: editor
    y: 50
    width: 500
    visible: scene.settingsShown
    theme: theme
    preferences: scene.preferences
    onSaved: values => {
      if (scene.delayValidation) personalization.submitPreferences(values)
      else scene.preferences = values
    }
  }
  Deck.HelpSheet {
    id: help
    anchors.fill: parent
    theme: theme
    clipboardShortcut: shortcut.availableChord
    returnFocusTarget: search
  }
  TestCase {
    name: "ClipboardShortcut"
    when: windowShown
    function init() {
      personalization.abort("")
      scene.delayValidation = false
      shortcut.stateReady = true
      scene.preferences = {clipboardHistoryShortcut: "CTRL+SHIFT+V", hideAfterAction: false}
      personalization.resetPreferences(scene.preferences)
      scene.savedSearches = []
      scene.settingsShown = false
      search.forceActiveFocus()
    }
    function cleanup() { help.opened = false; controller.close(false); wait(0) }
    function test_default_opens_empty_search_and_restores_focus() {
      keyClick(Qt.Key_V, Qt.ControlModifier)
      verify(!controller.opened, "normal Paste does not open history")
      keyClick(Qt.Key_V, Qt.ControlModifier | Qt.ShiftModifier)
      tryCompare(controller, "opened", true)
      compare(controller.query, "")
      tryCompare(browser.searchControl, "activeFocus", true)
      keyClick(Qt.Key_A)
      compare(controller.query, "a")
      keyClick(Qt.Key_Escape)
      keyClick(Qt.Key_Escape)
      tryCompare(controller, "opened", false)
      tryCompare(search, "activeFocus", true)
      keyClick(Qt.Key_V, Qt.ControlModifier | Qt.ShiftModifier)
      tryCompare(controller, "opened", true)
      tryCompare(browser.searchControl, "activeFocus", true)
    }
    function test_settings_apply_rebind_disable_and_block_modal() {
      scene.settingsShown = true
      mouseClick(editor.field)
      editor.field.selectAll()
      for (const character of "Ctrl+Alt+V") keyClick(character)
      keyClick(Qt.Key_V, Qt.ControlModifier | Qt.ShiftModifier)
      verify(!controller.opened, "shortcut never interrupts Settings")
      // Restore the draft after testing the editing chord; no clipboard content is used.
      editor.field.text = "Ctrl+Alt+V"
      mouseClick(editor.applyControl)
      compare(scene.preferences.clipboardHistoryShortcut, "Ctrl+Alt+V")
      compare(scene.preferences.hideAfterAction, false)
      scene.settingsShown = false
      search.forceActiveFocus()
      keyClick(Qt.Key_V, Qt.ControlModifier | Qt.ShiftModifier)
      verify(!controller.opened, "old binding is removed")
      keyClick(Qt.Key_V, Qt.ControlModifier | Qt.AltModifier)
      tryCompare(controller, "opened", true)
      controller.close(false)
      scene.settingsShown = true
      mouseClick(editor.field)
      keyClick(Qt.Key_A, Qt.ControlModifier)
      keyClick(Qt.Key_Backspace)
      keyClick(Qt.Key_Return)
      compare(scene.preferences.clipboardHistoryShortcut, "")
      scene.settingsShown = false
      search.forceActiveFocus()
      keyClick(Qt.Key_V, Qt.ControlModifier | Qt.AltModifier)
      verify(!controller.opened)
    }
    function test_existing_saved_search_keeps_its_binding() {
      scene.savedSearches = [{shortcut: "CTRL+SHIFT+V"}]
      keyClick(Qt.Key_V, Qt.ControlModifier | Qt.ShiftModifier)
      verify(!controller.opened)
      verify(shortcut.conflict)
      scene.savedSearches = []
      keyClick(Qt.Key_V, Qt.ControlModifier | Qt.ShiftModifier)
      tryCompare(controller, "opened", true)
    }
    function test_pending_validation_after_settings_closes_data() {
      return [{tag: "accepted", accepted: true}, {tag: "rejected", accepted: false}]
    }
    function test_pending_validation_after_settings_closes(data) {
      scene.delayValidation = true
      scene.settingsShown = true
      mouseClick(editor.field)
      editor.field.selectAll()
      for (const character of "Ctrl+Alt+V") keyClick(character)
      mouseClick(editor.applyControl)
      compare(scene.preferences.clipboardHistoryShortcut, "Ctrl+Alt+V")
      verify(personalization.preferenceInFlight)

      scene.settingsShown = false
      search.forceActiveFocus()
      keyClick(Qt.Key_V, Qt.ControlModifier | Qt.AltModifier)
      verify(!controller.opened, "uncommitted chord stays disabled after Settings closes")

      help.opened = true
      verify(!help.shortcutListed("CTRL+ALT+V"), "Help hides the pending draft")

      if (data.accepted) personalization.acceptPreferences(scene.preferences)
      else personalization.rejectPreferences("Shortcut conflicts with an effective Hyprland binding.")
      verify(!personalization.preferenceInFlight)
      verify(help.shortcutListed(data.accepted ? "CTRL+ALT+V" : "CTRL+SHIFT+V"))
      help.opened = false
      search.forceActiveFocus()
      if (!data.accepted) {
        keyClick(Qt.Key_V, Qt.ControlModifier | Qt.AltModifier)
        verify(!controller.opened, "rejected chord remains disabled")
      }
      keyClick(Qt.Key_V, Qt.ControlModifier | (data.accepted ? Qt.AltModifier : Qt.ShiftModifier))
      tryCompare(controller, "opened", true)
      tryCompare(browser.searchControl, "activeFocus", true)
    }
    function test_help_writes_the_chord_like_its_other_keys() {
      help.opened = true
      const general = help.shortcutSections.find(section => section.title === "General")
      const clipboard = general.items.find(item => item.text === "Clipboard History")
      compare(clipboard.keys.length, 1)
      compare(clipboard.keys[0], "Ctrl+Shift+V")
      compare(help.chordLabel("CTRL+ALT+V"), "Ctrl+Alt+V")
    }

    function test_help_hides_unloaded_disconnected_and_conflicting_chords() {
      help.opened = true
      verify(help.shortcutListed("CTRL+SHIFT+V"))
      shortcut.stateReady = false
      verify(!help.shortcutListed("CTRL+SHIFT+V"), "stale preferences are hidden until bootstrap completes")
      shortcut.stateReady = true
      verify(help.shortcutListed("CTRL+SHIFT+V"))
      scene.savedSearches = [{shortcut: "CTRL+SHIFT+V"}]
      verify(!help.shortcutListed("CTRL+SHIFT+V"), "conflicting chord is hidden")
    }
    function test_bootstrap_waits_for_authoritative_ownership() {
      shortcut.stateReady = false
      scene.preferences = null
      keyClick(Qt.Key_V, Qt.ControlModifier | Qt.ShiftModifier)
      verify(!controller.opened)
      scene.preferences = {clipboardHistoryShortcut: "CTRL+SHIFT+V"}
      keyClick(Qt.Key_V, Qt.ControlModifier | Qt.ShiftModifier)
      verify(!controller.opened, "preferences alone do not establish ownership")
      scene.savedSearches = [{shortcut: "CTRL+SHIFT+V"}]
      shortcut.stateReady = true
      keyClick(Qt.Key_V, Qt.ControlModifier | Qt.ShiftModifier)
      verify(!controller.opened, "legacy owner wins after bootstrap")
      scene.savedSearches = []
      scene.preferences = {clipboardHistoryShortcut: ""}
      keyClick(Qt.Key_V, Qt.ControlModifier | Qt.ShiftModifier)
      verify(!controller.opened, "persisted disable wins after bootstrap")
      scene.preferences = {clipboardHistoryShortcut: "CTRL+ALT+V"}
      keyClick(Qt.Key_V, Qt.ControlModifier | Qt.ShiftModifier)
      verify(!controller.opened)
      keyClick(Qt.Key_V, Qt.ControlModifier | Qt.AltModifier)
      tryCompare(controller, "opened", true)
    }
    function test_bootstrap_handler_requires_both_payloads() {
      const ignored = {handleMessage: function() { return false }}
      const host = {
        activeBootstrapRequestId: 42,
        part_searchSession: ignored, part_clipboardController: ignored, part_queryHistoryController: ignored,
        part_hiddenItemsController: ignored,
        part_extensionManagementController: ignored, part_extensionController: ignored,
        part_aiController: ignored, part_agentController: ignored, part_aiSettingsController: ignored,
        part_personalization: {resetPreferences: function(value) { scene.preferences = value }}
      }
      const payloads = [
        {},
        {preferences: {clipboardHistoryShortcut: "CTRL+SHIFT+V"}},
        {savedSearches: []},
        {preferences: {clipboardHistoryShortcut: "CTRL+SHIFT+V"}, savedSearches: []}
      ]
      for (let index = 0; index < payloads.length; index++) {
        host.activeBootstrapRequestId = 42
        Messages.handleMessage(host, {type: Protocol.Messages.BootstrapResult,
          requestId: 42, bootstrap: payloads[index]})
        shortcut.stateReady = host.clipboardShortcutStateReady
        scene.savedSearches = host.savedSearches
        compare(shortcut.stateReady, index === 3)
        keyClick(Qt.Key_V, Qt.ControlModifier | Qt.ShiftModifier)
        compare(controller.opened, index === 3)
      }
    }
    function test_invalid_optimistic_draft_never_steals_paste() {
      scene.preferences = {clipboardHistoryShortcut: "CTRL+V"}
      keyClick(Qt.Key_V, Qt.ControlModifier)
      verify(!controller.opened)
      compare(shortcut.chord, "")
      scene.preferences = {clipboardHistoryShortcut: "CTRL+SHIFT+C"}
      keyClick(Qt.Key_C, Qt.ControlModifier | Qt.ShiftModifier)
      verify(!controller.opened)
    }
  }
}
