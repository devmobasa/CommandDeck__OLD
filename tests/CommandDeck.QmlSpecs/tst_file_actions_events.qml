import QtQuick
import QtTest
import "../../qml/common" as CommandDeck
import "../../qml/common/Protocol.js" as Protocol
import "../../qml/common/UiConstants.js" as Ui

EventPathFixture {
  id: scene
  EventPathTestCase {
    fixture: scene
    name: "CommandDeck_file_actions"
    function test_picker_filters_selects_original_index_and_cancels() {
      const picker = createTemporaryObject(fixture.part_pickerComponent, fixture, {
        width: 620, height: 460, theme: fixture.part_theme,
        session: { prompt: "Choose", choices: [{ value: "a", label: "Alpha" }, { value: "b", label: "Beta" }, { value: "c", label: "Beta two" }] }
      })
      verify(picker !== null)
      tryCompare(picker.inputControl, "activeFocus", true)
      typeText("beta")
      compare(picker.filtered.length, 2)
      keyClick(Qt.Key_Down)
      keyClick(Qt.Key_Return)
      compare(picker.chosenIndex, 2)
      picker.inputControl.text = "no match"
      picker.chosenIndex = -1
      keyClick(Qt.Key_Return)
      compare(picker.chosenIndex, -1)
      keyClick(Qt.Key_Escape)
      verify(picker.wasCanceled)
    }

    function test_file_selection_buttons_have_keyboard_actions() {
      const bar = createTemporaryObject(fixture.part_fileSelectionComponent, fixture, {
        width: 800, theme: fixture.part_theme, entries: [{ id: "file:a", title: "A", path: "/tmp/a" }]
      })
      verify(bar !== null)
      bar.firstControl.forceActiveFocus()
      keyClick(Qt.Key_Return)
      verify(bar.copied)
      keyClick(Qt.Key_Tab)
      keyClick(Qt.Key_Tab)
      keyClick(Qt.Key_Tab)
      keyClick(Qt.Key_Return)
      compare(bar.removed.id, "file:a")
    }

    function test_action_inputs_require_values_and_preserve_literal_text() {
      const dialog = createTemporaryObject(fixture.part_actionInputComponent, fixture, {
        width: 900, height: 600, theme: fixture.part_theme,
        action: { title: "Copy snippet", inputs: [{ name: "topic" }] }, opened: true
      })
      verify(dialog !== null)
      wait(30)
      dialog.submit()
      verify(dialog.errorText.indexOf("topic") >= 0)
      typeText("literal {clipboard}")
      keyClick(Qt.Key_Return)
      compare(dialog.submittedValues.topic, "literal {clipboard}")
    }

    function test_alias_validation_and_save_use_real_enter_events() {
      fixture.part_aliasDialog.openFor({ title: "Browser", aliases: [] })
      tryCompare(fixture.part_aliasDialog.inputControl, "activeFocus", true)
      mouseClick(fixture, 5, 5)
      compare(fixture.part_backgroundTarget.clicks, 0)
      typeText("file")
      keyClick(Qt.Key_Return)
      verify(fixture.part_aliasDialog.localError.indexOf("reserved") >= 0)

      fixture.part_aliasDialog.inputControl.text = "browseralias"
      keyClick(Qt.Key_Return)
      compare(fixture.part_aliasDialog.saved, "browseralias")
    }

    function test_action_navigation_and_source_toggle_use_real_events() {
      fixture.part_actionPanel.openFor({
        title: "Browser",
        canCustomize: true,
        isFavorite: false,
        aliases: [],
        actions: [{ id: "launch", title: "Launch", kind: "launch-desktop" }]
      })
      tryCompare(fixture.part_actionPanel.filterControl, "activeFocus", true)
      keyClick(Qt.Key_Down)
      compare(fixture.part_actionPanel.selectedIndex, 1)
      keyClick(Qt.Key_1, Qt.AltModifier)
      compare(fixture.part_actionPanel.chosen, "")
      fixture.part_actionPanel.filterControl.text = ""
      keyClick(Qt.Key_1, Qt.ControlModifier | Qt.ShiftModifier)
      compare(fixture.part_actionPanel.chosen, "")
      keyClick(Qt.Key_1, Qt.ControlModifier)
      compare(fixture.part_actionPanel.chosen, "launch")

      fixture.part_actionPanel.filterControl.forceActiveFocus()
      keyClick(Qt.Key_Left)
      compare(fixture.part_actionPanel.dismissals, 1)

      fixture.part_actionPanel.selectedIndex = 0
      fixture.part_actionPanel.restoreActionFocus()
      tryCompare(fixture.part_actionPanel.actionItemAt(0), "activeFocus", true)
      fixture.part_actionPanel.chosen = ""
      keyClick(Qt.Key_1, Qt.ControlModifier | Qt.ShiftModifier)
      compare(fixture.part_actionPanel.chosen, "")
      keyClick(Qt.Key_1, Qt.ControlModifier)
      compare(fixture.part_actionPanel.chosen, "launch")
      keyClick(Qt.Key_Left)
      compare(fixture.part_actionPanel.dismissals, 2)

      mouseClick(fixture.part_sourceRow.searchToggleControl)
      compare(fixture.part_sourceRow.searchChanges, 1)
      compare(fixture.part_sourceRow.lastSearchValue, false)
    }

    function test_alias_focus_returns_to_originating_action_row() {
        fixture.part_actionPanel.openFor({
          title: "Browser",
          canCustomize: true,
          isFavorite: false,
          aliases: [],
          actions: [{ id: "launch", title: "Launch", kind: "launch-desktop" }]
        })
        tryCompare(fixture.part_actionPanel.filterControl, "activeFocus", true)
        keyClick(Qt.Key_Down)
        keyClick(Qt.Key_Down)
        keyClick(Qt.Key_Return)
        tryCompare(fixture.part_aliasDialog.inputControl, "activeFocus", true)
        keyClick(Qt.Key_Escape)
        tryCompare(fixture.part_actionPanel.actionItemAt(2), "activeFocus", true)
      }

    function test_fallback_action_is_rendered_without_personalization_actions() {
      fixture.part_actionPanel.openFor({
        title: "Search the web",
        providerId: Protocol.ProviderIds.Fallback,
        canCustomize: false,
        actions: [{ id: "open", title: "Search the Web", kind: Protocol.ActionKinds.OpenUrl }]
      })
      compare(fixture.part_actionPanel.visibleActionCount, 1)
      compare(fixture.part_actionPanel.actionItemAt(0).modelData.id, "open")
    }

    function test_ranking_explanation_action_uses_real_events() {
      fixture.part_actionPanel.openFor({
        title: "Browser",
        canCustomize: false,
        ranking: {
          finalScore: 0.91,
          contributions: [
            { kind: "lexical", label: "Catalog lexical match", value: 0.81 },
            { kind: "source-weight", label: "Source weight ×1.1", value: 0.10 }
          ],
          matches: [
            { field: "title", value: "Browser", ranges: [{ start: 0, length: 3 }] }
          ]
        },
        actions: [{ id: "launch", title: "Launch", kind: "launch-desktop" }]
      })
      compare(fixture.part_actionPanel.visibleActionCount, 2)
      compare(fixture.part_actionPanel.actionItemAt(1).modelData.kind, "ranking-explanation")
      fixture.part_actionPanel.filterControl.forceActiveFocus()
      keyClick(Qt.Key_Down)
      keyClick(Qt.Key_Return)
      compare(fixture.part_actionPanel.chosen, "why-result")
    }
  }
}
