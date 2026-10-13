import QtQuick
import "UiConstants.js" as Ui
import "ColorThemeFormat.js" as Format

// Resolved design tokens for the overlay.
//
// Prefers the running Omarchy shell so CommandDeck matches the active theme,
// and falls back to CommandDeck's own palette when host tokens cannot be
// loaded or the user chooses that palette instead.
QtObject {
  id: root

  property bool loadShellTokens: true
  property url tokenSource
  property var tokens: null
  property bool reduceMotion: false
  readonly property bool shellThemed: root.tokens !== null

  // CommandDeck's palette as { accent, background, text } (Ui.DeckColorRole),
  // or null for the defaults. overrideHostColors replaces only the host
  // theme's colours; its fonts and metrics still apply.
  property bool overrideHostColors: false
  property var deckColors: null
  readonly property var colorTokens: root.overrideHostColors ? null : root.tokens
  readonly property color deckAccent: root.deckColors
    ? root.deckColors[Ui.DeckColorRole.Accent] : Ui.DeckColors.Accent
  readonly property color deckBackground: root.deckColors
    ? root.deckColors[Ui.DeckColorRole.Background] : Ui.DeckColors.Background
  readonly property color deckText: root.deckColors
    ? root.deckColors[Ui.DeckColorRole.Text] : Ui.DeckColors.Text

  function withAlpha(value, amount) {
    return Qt.rgba(value.r, value.g, value.b, amount)
  }

  readonly property color scrim: root.colorTokens
    ? root.colorTokens.scrim
    : Qt.rgba(0, 0, 0, 0.42)

  // C8 focus/scrim. The theme's own scrim alpha is often quite transparent
  // (the shell default composes at ~0.5, the built-in fallback at 0.42), so
  // background windows bleed through and read as part of the launcher. This is
  // a tunable floor on the scrim opacity instead of a magic literal in the
  // surface: `scrimColor` keeps the theme's scrim hue but never renders more
  // transparent than `scrimStrength`, whose default is deliberately opaque so
  // the overlay is unmistakably modal. Raise it for an even darker backdrop.
  property real scrimStrength: 0.72
  readonly property color scrimColor: Qt.rgba(
    root.scrim.r, root.scrim.g, root.scrim.b,
    Math.max(root.scrim.a, root.scrimStrength))

  // User-facing panel transparency (Settings → Customization). Off forces a
  // solid panel: Omarchy themes ship `launcher.background-alpha` at 0.95 (and
  // the built-in fallback is 0.985), which is enough for windows behind the
  // launcher to bleed through. On applies `panelTransparency` (0–0.8) to the
  // palette AND scales the overlay scrim so the wallpaper can actually show
  // through; fading only the panel would reveal the opaque scrim.
  property bool panelTransparencyEnabled: false
  property real panelTransparency: 0
  readonly property real panelTransparencyAmount: Math.max(
    0, Math.min(0.8, root.panelTransparency))
  readonly property color panelBackground: {
    if (!root.panelTransparencyEnabled)
      return root.withAlpha(root.background, 1)
    return root.withAlpha(
      root.background, Math.max(0.2, 1 - root.panelTransparencyAmount))
  }
  readonly property color overlayScrimColor: {
    const base = root.scrimColor
    if (!root.panelTransparencyEnabled) return base
    return Qt.rgba(
      base.r, base.g, base.b,
      Math.max(0.12, base.a * (1 - root.panelTransparencyAmount)))
  }

  readonly property color background: root.colorTokens
    ? root.colorTokens.background
    : root.withAlpha(root.deckBackground, 0.985)
  // Dialogs, popovers, pickers, and toasts float over other launcher content.
  // Themes ship `background` slightly translucent, which lets the rows and
  // detail text beneath read through, so floating surfaces are always opaque.
  readonly property color popupBackground: root.withAlpha(root.background, 1)
  readonly property color border: root.colorTokens
    ? root.colorTokens.border
    : root.withAlpha(root.deckText, 0.12)
  readonly property color text: root.colorTokens ? root.colorTokens.text : root.deckText
  readonly property color selectedBackground: root.colorTokens
    ? root.colorTokens.selectedBackground
    : root.withAlpha(root.deckText, 0.10)
  readonly property color selectedText: root.colorTokens
    ? root.colorTokens.selectedText
    : root.deckText
  readonly property color selectedBorder: root.colorTokens
    ? root.colorTokens.selectedBorder
    : root.withAlpha(root.deckText, 0.14)
  readonly property color accent: root.colorTokens ? root.colorTokens.accent : root.deckAccent
  // Keyboard focus rings and outlines: the accent, unless it is too close to
  // the background to see, then the text colour.
  readonly property color focusRing: Format.focusColor(root.accent, root.text, root.background)
  readonly property color urgent: root.colorTokens ? root.colorTokens.urgent : "#ff8d8d"

  // Derived from `text` so both palettes stay internally consistent.
  readonly property color textMuted: root.withAlpha(root.text, 0.62)
  readonly property color textFaint: root.withAlpha(root.text, 0.42)
  readonly property color surface: root.withAlpha(root.text, 0.05)
  readonly property color surfaceStrong: root.withAlpha(root.text, 0.09)
  readonly property color separator: root.withAlpha(root.text, 0.10)

  // Raw font scale from the running shell (the OMARCHY font scale). Layout
  // metrics have always multiplied by this; fonts have not.
  readonly property real fontScale: root.tokens ? root.tokens.scale : 1

  // Display scale grows BOTH layout metrics AND font sizes so the overlay is
  // not rendered too small on a high-resolution monitor. CommandDeck.qml
  // computes it from the active screen and binds it here; it stays 1 on a 1080p
  // display and in the built-in fallback path the QML tests exercise.
  property real displayScale: 1
  // Surface density scales fonts and layout equally, independently of the
  // gentler font growth used for high-resolution displays.
  property real densityScale: 1

  // The scale every layout metric multiplies by. Folding the display scale into
  // the shell font scale grows the whole overlay on a 4K panel.
  readonly property real scale: root.fontScale * root.displayScale * root.densityScale

  // Fonts grow more gently than the layout: a 4K overlay should be roomier, but
  // scaling glyphs by the full display factor makes them oversized. Half the
  // extra growth keeps text proportionate (displayScale 1.5 -> ~1.25x fonts).
  readonly property real fontDisplayScale: (1 + (root.displayScale - 1) * 0.5) * root.densityScale
  readonly property int radius: Math.round(
    (root.tokens ? root.tokens.radius : 14) * root.displayScale * root.densityScale)
  readonly property int radiusSmall: Math.max(0, Math.round(root.radius * 0.55))
  readonly property int panelPadding: root.tokens
    ? Math.round(root.tokens.panelPadding * root.displayScale * root.densityScale)
    : Math.round(14 * root.scale)
  readonly property int rowPaddingX: root.tokens
    ? Math.round(root.tokens.rowPaddingX * root.displayScale * root.densityScale)
    : Math.round(12 * root.scale)
  readonly property int gap: root.tokens
    ? Math.round(root.tokens.gap * root.displayScale * root.densityScale)
    : Math.round(8 * root.scale)

  // Layout metrics follow the theme's font scale, so raising [font] base-size
  // in shell.toml grows the palette instead of clipping it.
  readonly property int rowHeight: Math.round(46 * root.scale)
  readonly property int compactRowHeight: Math.round(38 * root.scale)
  readonly property int compactIconSize: Math.round(26 * root.scale)
  readonly property int rowGap: Math.max(1, Math.round(2 * root.scale))
  readonly property int iconSize: Math.round(30 * root.scale)
  readonly property int iconGap: Math.round(11 * root.scale)
  readonly property int inputHeight: Math.round(46 * root.scale)
  readonly property int actionRowHeight: Math.round(36 * root.scale)
  readonly property int paletteWidth: Math.round(900 * root.scale)
  readonly property int paletteHeight: Math.round(600 * root.scale)
  readonly property int durationFast: root.reduceMotion ? 0 : 90
  readonly property int durationNormal: root.reduceMotion ? 0 : 140
  readonly property int highlightMoveDuration: root.reduceMotion ? 0 : 80

  readonly property string fontFamily: root.tokens
    ? root.tokens.fontFamily
    : "monospace"
  // Fonts scale with the gentler fontDisplayScale (not the shell font scale) so
  // a 4K panel enlarges text alongside the boxes without oversizing it.
  // At displayScale 1 and densityScale 1 these retain their base sizes.
  readonly property int fontCaption: Math.round(
    (root.tokens ? root.tokens.fontCaption : 10) * root.fontDisplayScale)
  readonly property int fontSmall: Math.round(
    (root.tokens ? root.tokens.fontSmall : 11) * root.fontDisplayScale)
  readonly property int fontBody: Math.round(
    (root.tokens ? root.tokens.fontBody : 12) * root.fontDisplayScale)
  readonly property int fontSubtitle: Math.round(
    (root.tokens ? root.tokens.fontSubtitle : 13) * root.fontDisplayScale)
  readonly property int fontTitle: Math.round(
    (root.tokens ? root.tokens.fontTitle : 14) * root.fontDisplayScale)
  readonly property int fontHeading: Math.round(
    (root.tokens ? root.tokens.fontHeading : 16) * root.fontDisplayScale)

  Component.onCompleted: {
    if (!root.loadShellTokens || String(root.tokenSource).length === 0) return
    const component = Qt.createComponent(
      root.tokenSource, Component.PreferSynchronous)

    if (component.status === Component.Ready) {
      root.tokens = component.createObject(root)
      return
    }

    console.warn("CommandDeck: Omarchy shell tokens unavailable, keeping the "
      + "built-in palette. " + component.errorString())
  }
}
