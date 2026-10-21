import QtQuick
import QtTest
import "../../qml/common" as CommandDeck
import "../../qml/common/Protocol.js" as Protocol
import "../../qml/common/UiConstants.js" as Ui
import "../../qml/common/DeckHelpActions.js" as DeckHelpActions
import "../../qml/common/DeckConfirmationActions.js" as DeckConfirmationActions

EventPathFixture {
  id: scene
  EventPathTestCase {
    fixture: scene
    name: "CommandDeck_dialogs"
    function test_confirmation_names_its_target_and_marks_destruction() {
      const owner = {
        resetConfirmation: false, savedSearchDeleteTarget: null, aiTemplateDeleteTarget: null,
        part_invocationController: {
          pendingConfirmationResult: { id: "window:0xabc", title: "omx: CommandDeck" },
          pendingConfirmationAction: { id: "close", title: "Close Window", isDestructive: true }
        }
      }
      const close = DeckConfirmationActions.confirmationContent(owner)
      compare(close.prompt, "Close Window “omx: CommandDeck”?")
      compare(close.confirmLabel, "Close Window")
      verify(close.destructive)

      owner.resetConfirmation = true
      verify(DeckConfirmationActions.confirmationContent(owner).destructive, "resetting is destructive")

      const confirmation = fixture.part_confirmation
      confirmation.destructive = true
      confirmation.opened = true
      waitForCondition(function() { return confirmation.cancelControl.activeFocus })
      compare(confirmation.confirmControl.kind, Ui.ButtonVariant.Danger)
      compare(confirmation.confirmControl.Accessible.description, "Destructive action. Requires confirmation")
      confirmation.destructive = false
      keyClick(Qt.Key_Tab)
      compare(confirmation.confirmControl.kind, Ui.ButtonVariant.Primary)
      confirmation.opened = false
    }

    function test_ranking_explanation_separates_sections_once() {
      const owner = { hasRankingExplanation: result => DeckHelpActions.hasRankingExplanation(null, result) }
      const text = DeckHelpActions.rankingExplanationText(owner, {
        title: "Firefox", sourceId: "applications",
        ranking: {
          finalScore: 1.08,
          placement: { tier: "grouped", reason: "Requires two accepted uses" },
          contributions: [{ label: "Catalog lexical match", value: 0.76 }],
          matches: [{ field: "title", value: "Firefox" }]
        }
      })
      compare(text.split("\n").slice(0, 7), ["Firefox", "Source: applications", "Final score: 1.0800",
        "Placement: grouped — Requires two accepted uses", "", "Score contributions:",
        "- Catalog lexical match: +0.7600"])
      verify(text.indexOf("\n\n\n") < 0, "sections are separated by a single blank line")
    }

    function test_settings_keeps_the_rounded_palette_frame() {
      const panel = fixture.part_settingsPanel
      panel.visible = true
      verify(waitForFrame(panel))
      const image = grabImage(panel)
      const corner = image.pixel(1, 1)
      const inside = image.pixel(panel.width / 2, 2)
      verify(!Qt.colorEqual(corner, inside), "the corner falls outside the rounded frame")
      verify(Qt.colorEqual(image.pixel(panel.width / 2, 0), fixture.part_theme.border),
        "the frame draws the palette border along the top edge")
      panel.visible = false
    }

    function test_confirmation_traps_focus_and_handles_real_keys() {
      fixture.part_confirmation.opened = true
      waitForValue(fixture.part_confirmation.cancelControl, "activeFocus", true)
      mouseClick(fixture, 5, 5)
      compare(fixture.part_backgroundTarget.clicks, 0)
      compare(fixture.part_confirmation.cancelControl.kind, "primary")
      compare(fixture.part_confirmation.confirmControl.kind, "secondary")
      keyClick(Qt.Key_Right)
      compare(fixture.part_confirmation.confirmControl.activeFocus, true)
      compare(fixture.part_confirmation.confirmControl.kind, "primary")
      compare(fixture.part_confirmation.cancelControl.kind, "secondary")
      keyClick(Qt.Key_Left)
      compare(fixture.part_confirmation.cancelControl.activeFocus, true)
      keyClick(Qt.Key_Return)
      compare(fixture.part_confirmation.cancels, 1)
      compare(fixture.part_confirmation.accepts, 0)
      keyClick(Qt.Key_Tab)
      compare(fixture.part_confirmation.confirmControl.activeFocus, true)
      keyClick(Qt.Key_Return)
      compare(fixture.part_confirmation.accepts, 1)
      keyClick(Qt.Key_Backtab)
      compare(fixture.part_confirmation.cancelControl.activeFocus, true)
      keyClick(Qt.Key_Space)
      compare(fixture.part_confirmation.cancels, 2)
      keyClick(Qt.Key_Escape)
      compare(fixture.part_confirmation.cancels, 3)
    }

    function test_detail_dialog_is_pointer_modal_and_traps_focus() {
      fixture.part_detailDialog.opened = true
      waitForValue(fixture.part_detailDialog.closeControl, "activeFocus", true)
      compare(fixture.part_detailDialog.Accessible.role, Accessible.Dialog)
      compare(fixture.part_detailDialog.Accessible.name, "AI response")
      mouseClick(fixture, 5, 5)
      compare(fixture.part_backgroundTarget.clicks, 0)
      compare(fixture.part_detailDialog.dismissals, 1)

      fixture.part_detailDialog.opened = true
      waitForValue(fixture.part_detailDialog.closeControl, "activeFocus", true)
      keyClick(Qt.Key_Tab)
      compare(fixture.part_detailDialog.closeControl.activeFocus, true)
      keyClick(Qt.Key_Backtab)
      compare(fixture.part_detailDialog.closeControl.activeFocus, true)
      keyClick(Qt.Key_Escape)
      compare(fixture.part_detailDialog.dismissals, 2)
    }

    function test_query_history_uses_real_confirmed_modal_events() {
      fixture.part_queryHistory.opened = true
      waitForValue(fixture.part_queryHistory.closeControl, "activeFocus", true)
      compare(fixture.part_queryHistory.Accessible.role, Accessible.Dialog)
      mouseClick(fixture, 5, 5)
      compare(fixture.part_backgroundTarget.clicks, 0)

      mouseClick(fixture.part_queryHistory.entryItemAt(0).deleteControl)
      compare(fixture.part_queryHistory.pendingDeleteId, 7)
      waitForValue(fixture.part_queryHistory.confirmationControl.cancelControl, "activeFocus", true)
      keyClick(Qt.Key_Tab)
      keyClick(Qt.Key_Return)
      compare(fixture.part_queryHistory.deletedId, 7)

      mouseClick(fixture.part_queryHistory.resetControl)
      verify(fixture.part_queryHistory.pendingReset)
      waitForValue(fixture.part_queryHistory.confirmationControl.cancelControl, "activeFocus", true)
      keyClick(Qt.Key_Escape)
      compare(fixture.part_queryHistory.resets, 0)

      fixture.part_queryHistory.closeControl.forceActiveFocus()
      keyClick(Qt.Key_Escape)
      compare(fixture.part_queryHistory.dismissals, 1)
    }

    function test_ai_modals_disclose_authority_and_trap_focus() {
      fixture.part_aiPreflightSurface.opened = true
      nextTurn()
      verify(fixture.part_aiPreflightSurface.disclosureText.indexOf("Provider: cloud") >= 0)
      verify(fixture.part_aiPreflightSurface.disclosureText.indexOf("Model: model-safe") >= 0)
      verify(fixture.part_aiPreflightSurface.disclosureText.indexOf(
        "Destination: https://api.example.test/v1/chat/completions") >= 0)
      verify(fixture.part_aiPreflightSurface.disclosureText.indexOf("Credential source: environment-reference") >= 0)
      verify(fixture.part_aiPreflightSurface.disclosureText.indexOf("Payload digest (SHA-256)") >= 0)
      verify(fixture.part_aiPreflightSurface.disclosureText.indexOf("notes.txt — 17 UTF-8 bytes") >= 0)
      verify(fixture.part_aiPreflightSurface.disclosureText.indexOf('"stream_options"') >= 0)
      verify(fixture.part_aiPreflightSurface.disclosureText.indexOf('"tool_choice":"none"') >= 0)
      const cancel = findChild(fixture.part_aiPreflightSurface, "aiPreflightCancel")
      const send = findChild(fixture.part_aiPreflightSurface, "aiPreflightSend")
      verify(cancel.activeFocus)
      keyClick(Qt.Key_Tab)
      verify(send.activeFocus)
      keyClick(Qt.Key_Tab, Qt.ShiftModifier)
      verify(cancel.activeFocus)
      keyClick(Qt.Key_Space)
      compare(fixture.part_aiPreflightSurface.cancelCount, 1)
      compare(fixture.part_aiSurfaceController.state, "idle")
      fixture.part_aiPreflightSurface.opened = false

      fixture.part_aiStreamingSurface.opened = true
      nextTurn()
      verify(fixture.part_aiStreamingSurface.activeFocus || findChild(fixture.part_aiStreamingSurface, "aiOutputSave") !== null)
      keyClick(Qt.Key_Tab)
      verify(fixture.part_aiStreamingSurface.focusTargets().some(item => item.activeFocus))
      keyClick(Qt.Key_Tab, Qt.ShiftModifier)
      verify(fixture.part_aiStreamingSurface.focusTargets().some(item => item.activeFocus))
      fixture.part_aiStreamingSurface.opened = false
    }
  }
}
