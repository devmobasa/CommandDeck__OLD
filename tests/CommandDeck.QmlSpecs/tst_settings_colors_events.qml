import QtQuick
import QtTest
import "../../qml/common" as CommandDeck

// Settings → Customization → Colors with real key and mouse events: choosing
// CommandDeck's palette over the Omarchy theme, picking swatches and hex
// colours, the readability warning, and resetting the palette.
EventPathFixture {
  id: scene

  SignalSpy { id: changes; target: scene.part_settingsPanel; signalName: "colorThemeChanged" }
  SignalSpy { id: resets; target: scene.part_settingsPanel; signalName: "colorThemeResetRequested" }
  // Puts text on the clipboard, as copying it from another app would.
  TextInput { id: clipboardSource; visible: false }

  EventPathTestCase {
    fixture: scene
    name: "CommandDeck_settings_colors"

    function openColors(source) {
      const panel = fixture.part_settingsPanel
      panel.colorThemeSource = source
      panel.deckColors = null
      panel.hostThemeAvailable = true
      changes.clear()
      resets.clear()
      panel.visible = true
      waitForValue(panel.searchControl, "activeFocus", true)
      panel.showPage("customization")
      waitForFrame(panel)
      return panel.part_colorsCard
    }

    function lastChange() {
      return changes.signalArguments[changes.count - 1][0]
    }

    // Settings pages scroll; bring a control into view before pointing at it.
    function scrollIntoView(item) {
      let flick = item.parent
      while (flick && flick.contentY === undefined) flick = flick.parent
      verify(flick !== null, "the control sits in a scrolling page")
      const top = item.mapToItem(flick.contentItem, 0, 0).y
      flick.contentY = Math.max(0, Math.min(top - 40, flick.contentHeight - flick.height))
      waitForFrame(fixture.part_settingsPanel)
    }

    function test_choosing_the_commanddeck_theme_by_keyboard() {
      const card = openColors("omarchy")
      compare(card.sourceControl.currentIndex, 0)
      verify(!card.accentControl.visible, "palette rows appear only for the CommandDeck theme")

      card.sourceControl.forceActiveFocus()
      keyClick(Qt.Key_Down)
      compare(changes.count, 1)
      compare(lastChange().source, "commanddeck")

      fixture.part_settingsPanel.colorThemeSource = "commanddeck"
      waitForValue(card.accentControl, "visible", true)
      compare(card.sourceControl.currentIndex, 1)
      verify(card.sourceControl.activeFocus, "focus stays on the choice")
    }

    function test_swatches_are_one_tab_stop_with_arrow_keys() {
      const card = openColors("commanddeck")
      const accent = card.accentControl
      compare(accent.captionText, "Blue")

      card.sourceControl.forceActiveFocus()
      keyClick(Qt.Key_Tab)
      waitForCondition(function() { return accent.swatchAt(0).activeFocus })
      compare(accent.captionText, "Blue · #8ab4f8")

      keyClick(Qt.Key_Right)
      verify(accent.swatchAt(1).activeFocus)
      compare(accent.captionText, "Violet · #b4a1f5")
      keyClick(Qt.Key_Return)
      compare(lastChange().colors.accent, "#b4a1f5")

      keyClick(Qt.Key_Right)
      keyClick(Qt.Key_Space)
      compare(lastChange().colors.accent, "#6fd3c4")

      keyClick(Qt.Key_Tab)
      verify(accent.hexControl.activeFocus, "Tab leaves the swatch group for the hex field")

      scrollIntoView(card.backgroundControl.swatchAt(2))
      mouseClick(card.backgroundControl.swatchAt(2))
      compare(lastChange().colors.background, "#0f1626")
    }

    // Like the other button groups, Up and Down step through the swatches as
    // Left and Right do, and Home and End reach the ends.
    function test_up_down_home_and_end_move_between_swatches() {
      const card = openColors("commanddeck")
      const accent = card.accentControl
      card.sourceControl.forceActiveFocus()
      keyClick(Qt.Key_Tab)
      waitForCondition(function() { return accent.swatchAt(0).activeFocus })

      keyClick(Qt.Key_Down)
      verify(accent.swatchAt(1).activeFocus, "Down steps to the next swatch")
      keyClick(Qt.Key_Up)
      verify(accent.swatchAt(0).activeFocus, "Up steps back")
      keyClick(Qt.Key_Up)
      verify(accent.swatchAt(0).activeFocus, "Up stops at the first swatch")
      keyClick(Qt.Key_End)
      verify(accent.swatchAt(accent.swatchCount - 1).activeFocus, "End reaches the last swatch")
      keyClick(Qt.Key_Down)
      verify(accent.swatchAt(accent.swatchCount - 1).activeFocus, "Down stops at the last swatch")
      keyClick(Qt.Key_Home)
      verify(accent.swatchAt(0).activeFocus, "Home returns to the first swatch")
      compare(changes.count, 0, "moving between swatches applies nothing")

      keyClick(Qt.Key_Down)
      keyClick(Qt.Key_Return)
      compare(lastChange().colors.accent, "#b4a1f5")
    }

    function test_hovering_names_a_swatch_without_moving_the_row() {
      const card = openColors("commanddeck")
      const accent = card.accentControl
      const panel = fixture.part_settingsPanel
      compare(accent.captionText, "Blue")
      const amber = accent.swatchAt(4)
      scrollIntoView(amber)
      const before = amber.mapToItem(panel, 0, 0)

      mouseMove(amber, amber.width / 2, amber.height / 2)
      waitForValue(accent, "captionText", "Amber · #f5c26b")
      waitForFrame(panel)
      const after = amber.mapToItem(panel, 0, 0)
      compare(after.x, before.x, "a longer caption does not shift the swatches under the pointer")
      compare(after.y, before.y)
      verify(amber.hovered, "the pointer stays on the swatch it named")

      accent.focusSwatch(1)
      compare(accent.captionText, "Amber · #f5c26b", "the hovered swatch keeps the caption")
      mouseMove(panel, 2, 2)
      waitForValue(accent, "captionText", "Violet · #b4a1f5")
    }

    function test_hex_field_applies_valid_colors_only() {
      const card = openColors("commanddeck")
      const text = card.textControl
      text.hexControl.forceActiveFocus()
      text.hexControl.selectAll()
      typeText("12ab3")
      keyClick(Qt.Key_Return)
      compare(changes.count, 0)
      verify(text.hexError.length > 0, "an incomplete colour explains the format")
      verify(text.hexControl.activeFocus)

      text.hexControl.selectAll()
      typeText("#EDE6DA")
      keyClick(Qt.Key_Return)
      compare(lastChange().colors.text, "#ede6da")
      compare(text.hexError, "")
    }

    // Escape cancels the edit in the field; only a second Escape closes Settings.
    function test_escape_reverts_a_hex_edit_before_closing_settings() {
      const panel = fixture.part_settingsPanel
      const card = openColors("commanddeck")
      const hex = card.accentControl.hexControl
      panel.dismissals = 0
      hex.forceActiveFocus()
      hex.selectAll()
      typeText("#ff0000")
      keyClick(Qt.Key_Escape)
      compare(changes.count, 0, "Escape does not apply the typed colour")
      compare(panel.dismissals, 0, "the first Escape keeps Settings open")
      compare(hex.text, "#8ab4f8", "the field shows the current colour again")
      verify(hex.activeFocus, "focus stays in the field")

      typeText("12ab3")
      keyClick(Qt.Key_Return)
      verify(card.accentControl.hexError.length > 0)
      keyClick(Qt.Key_Escape)
      compare(card.accentControl.hexError, "", "Escape clears the format error with the edit")
      compare(hex.text, "#8ab4f8")
      compare(panel.dismissals, 0)

      keyClick(Qt.Key_Escape)
      compare(panel.dismissals, 1, "Escape with nothing to revert closes Settings")
      compare(changes.count, 0, "closing Settings applies nothing")
    }

    function pasteIntoHex(hex, value) {
      clipboardSource.text = value
      clipboardSource.selectAll()
      clipboardSource.copy()
      hex.forceActiveFocus()
      hex.selectAll()
      keyClick(Qt.Key_V, Qt.ControlModifier)
    }

    function test_pasted_hex_colors_ignore_surrounding_spaces() {
      const card = openColors("commanddeck")
      const accent = card.accentControl
      pasteIntoHex(accent.hexControl, " #8AB4F9 ")
      compare(accent.hexControl.text, " #8AB4F9 ", "the whole pasted value arrives")
      keyClick(Qt.Key_Return)
      compare(accent.hexError, "")
      compare(lastChange().colors.accent, "#8ab4f9")

      changes.clear()
      pasteIntoHex(accent.hexControl, "#11223380")
      keyClick(Qt.Key_Return)
      compare(changes.count, 0, "an 8-digit colour is not cut down to a different 6-digit one")
      verify(accent.hexError.length > 0, "it is explained instead")
    }

    function test_warns_about_unreadable_colors_and_resets() {
      const card = openColors("commanddeck")
      verify(!card.resetControl.enabled, "the default palette has nothing to reset")
      verify(card.readable)

      fixture.part_settingsPanel.deckColors = { accent: "#8ab4f8", background: "#0e0e11", text: "#1c1d22" }
      verify(!card.readable, "text almost as dark as the background is flagged")
      compare(card.textControl.captionText, "Custom")
      waitForValue(card.resetControl, "enabled", true)
      waitForFrame(fixture.part_settingsPanel)
      card.resetControl.forceActiveFocus()
      keyClick(Qt.Key_Space)
      compare(resets.count, 1)
    }

    function test_warns_when_the_accent_cannot_mark_focus() {
      const card = openColors("commanddeck")
      verify(!card.accentWarningControl.visible)

      const accent = card.accentControl.hexControl
      accent.forceActiveFocus()
      accent.selectAll()
      typeText("#1c1d22")
      keyClick(Qt.Key_Return)
      compare(lastChange().colors.accent, "#1c1d22")
      fixture.part_settingsPanel.deckColors = { accent: "#1c1d22", background: "#0e0e11", text: "#f1f1f1" }
      verify(card.readable, "text is still readable")
      waitForValue(card.accentWarningControl, "visible", true)
      verify(card.accentWarningControl.text.indexOf("focus") >= 0, "the warning says focus rings change")
    }

    function test_without_the_omarchy_theme_the_palette_is_always_editable() {
      const card = openColors("omarchy")
      fixture.part_settingsPanel.hostThemeAvailable = false
      waitForValue(card.accentControl, "visible", true)
      verify(!card.sourceControl.enabled)
      compare(card.sourceControl.currentIndex, 1)
    }
  }
}
