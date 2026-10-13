import QtQuick
import QtTest
import "../../qml/common" as CommandDeck
import "../../qml/common/ColorThemeFormat.js" as Format

// Colour-theme rules and how the resolved theme applies them: Omarchy colours,
// CommandDeck's own palette as an override, and a customized palette.
Item {
  id: root
  width: 400
  height: 300

  // Stands in for the Omarchy shell's tokens (qml/omarchy/ThemeTokens.qml).
  QtObject {
    id: hostTokens
    readonly property color scrim: "#80060b1e"
    readonly property color background: "#f2060b1e"
    readonly property color border: "#7d82d9"
    readonly property color text: "#ffcead"
    readonly property color selectedBackground: "#14ffcead"
    readonly property color selectedText: "#7d82d9"
    readonly property color selectedBorder: "#407d82d9"
    readonly property color accent: "#7d82d9"
    readonly property color urgent: "#ff5555"
    readonly property real scale: 1
    readonly property int radius: 10
    readonly property int panelPadding: 12
    readonly property int rowPaddingX: 10
    readonly property int gap: 6
    readonly property string fontFamily: "Host Sans"
    readonly property int fontCaption: 10
    readonly property int fontSmall: 11
    readonly property int fontBody: 12
    readonly property int fontSubtitle: 13
    readonly property int fontTitle: 14
    readonly property int fontHeading: 16
  }

  CommandDeck.Theme { id: theme; loadShellTokens: false; tokens: hostTokens }

  Column {
    CommandDeck.SettingsField { id: field; theme: theme }
    CommandDeck.DeckButton { id: button; theme: theme; text: "Reset colors" }
  }

  TestCase {
    name: "ColorTheme"
    when: windowShown

    function init() {
      theme.overrideHostColors = false
      theme.deckColors = null
    }

    function test_format_normalizes_and_rejects_colors() {
      compare(Format.normalizeColor("#8AB4F8", ""), "#8ab4f8")
      compare(Format.normalizeColor(" 1c1d22 ", ""), "#1c1d22")
      compare(Format.normalizeColor("#12ab3", "fallback"), "fallback")
      compare(Format.normalizeColor("red", "fallback"), "fallback")
      compare(Format.normalizeSource("commanddeck"), "commanddeck")
      compare(Format.normalizeSource("neon"), "omarchy")
    }

    function test_format_parses_partial_corrupt_and_round_trips() {
      compare(Format.parse("").source, "omarchy")
      compare(Format.parse("{not json").colors.accent, "#8ab4f8")
      const partial = Format.parse("{\"source\":\"commanddeck\",\"colors\":{\"accent\":\"#F5C26B\",\"text\":\"nope\"}}")
      compare(partial.source, "commanddeck")
      compare(partial.colors.accent, "#f5c26b")
      compare(partial.colors.text, "#f1f1f1", "an invalid colour keeps the default")
      compare(partial.colors.background, "#0e0e11")
      verify(Format.equal(Format.parse(Format.serialize(partial)), partial))

      const merged = Format.merge(partial, { colors: { background: "#0f1626", accent: "bad" } })
      compare(merged.colors.background, "#0f1626")
      compare(merged.colors.accent, "#f5c26b", "an invalid change keeps the current colour")
      compare(merged.source, "commanddeck")
    }

    function test_format_measures_readability() {
      verify(Format.readable(Format.defaultColors()))
      verify(!Format.readable({ accent: "#8ab4f8", background: "#0e0e11", text: "#1c1d22" }))
      verify(Format.contrastRatio("#ffffff", "#000000") > 20.9)
    }

    function test_format_checks_the_accent_against_the_background() {
      verify(Format.accentVisible(Format.defaultColors()))
      verify(!Format.accentVisible({ accent: "#1c1d22", background: "#0e0e11", text: "#f1f1f1" }),
        "an accent close to the background cannot mark focus")
      verify(Qt.colorEqual(Format.focusColor(Qt.color("#1c1d22"), Qt.color("#f1f1f1"), Qt.color("#0e0e11")), "#f1f1f1"))
      verify(Qt.colorEqual(Format.focusColor(Qt.color("#8ab4f8"), Qt.color("#f1f1f1"), Qt.color("#0e0e11")), "#8ab4f8"))
    }

    // Every focus ring uses the accent. An accent too close to the background
    // would hide keyboard focus, so rings use the text colour instead; the
    // accent still colours highlights.
    function test_focus_rings_stay_visible_with_a_low_contrast_accent() {
      theme.overrideHostColors = true
      theme.deckColors = { accent: "#1c1d22", background: "#0e0e11", text: "#f1f1f1" }
      verify(Qt.colorEqual(theme.accent, "#1c1d22"), "the chosen accent is kept")
      verify(Qt.colorEqual(theme.focusRing, "#f1f1f1"), "focus rings use the text colour")

      field.forceActiveFocus()
      verify(Qt.colorEqual(field.background.border.color, theme.text), "a focused field is outlined in text colour")
      keyClick(Qt.Key_Tab)
      verify(button.activeFocus)
      const ring = button.background.children[0]
      verify(ring.visible && Qt.colorEqual(ring.border.color, theme.text), "a focused button's ring uses the text colour")

      theme.deckColors = { accent: "#8ab4f8", background: "#0e0e11", text: "#f1f1f1" }
      verify(Qt.colorEqual(theme.focusRing, "#8ab4f8"), "a visible accent keeps marking focus")
      verify(Qt.colorEqual(ring.border.color, "#8ab4f8"))

      theme.overrideHostColors = false
      verify(Qt.colorEqual(theme.focusRing, hostTokens.accent), "host themes follow the same rule")
    }

    function test_theme_follows_host_colors_unless_overridden() {
      verify(Qt.colorEqual(theme.text, "#ffcead"))
      verify(Qt.colorEqual(theme.accent, "#7d82d9"))

      theme.overrideHostColors = true
      verify(Qt.colorEqual(theme.text, "#f1f1f1"), "CommandDeck text replaces the host's")
      verify(Qt.colorEqual(theme.accent, "#8ab4f8"))
      verify(Qt.colorEqual(theme.popupBackground, "#0e0e11"))
      compare(theme.fontFamily, "Host Sans", "fonts keep following the host")
      compare(theme.radius, 10, "metrics keep following the host")
    }

    function test_customized_palette_derives_its_shades() {
      theme.overrideHostColors = true
      theme.deckColors = { accent: "#f5c26b", background: "#0f1626", text: "#ede6da" }
      verify(Qt.colorEqual(theme.accent, "#f5c26b"))
      verify(Qt.colorEqual(theme.popupBackground, "#0f1626"))
      verify(Qt.colorEqual(theme.text, "#ede6da"))
      verify(Qt.colorEqual(theme.selectedText, "#ede6da"))
      verify(Qt.colorEqual(theme.selectedBackground, theme.withAlpha(theme.text, 0.10)))
      verify(Qt.colorEqual(theme.border, theme.withAlpha(theme.text, 0.12)))
    }
  }
}
