import QtQuick
import qs.Commons

// Design tokens read from the running Omarchy shell.
//
// This is the only file in the plugin that imports `qs.Commons`. Theme.qml
// loads it through Qt.createComponent, so a shell API change degrades the
// overlay to its built-in palette instead of aborting the whole plugin.
//
// Omarchy writes per-theme values to theme/shell.toml. The installed Color
// singleton resolves `menu`, `popups`, and `lock` into objects but has no
// `launcher` object yet, so the `[launcher]` keys are read through the generic
// pick/composed helpers. Those helpers read Color.shellValues, which means
// every binding below re-evaluates on a theme switch.
QtObject {
  id: root

  readonly property var omarchyFont: Style.font
  readonly property var omarchySpacing: Style.spacing

  function solid(key, fallback) {
    return Color.flatColor(Color.pick(key, fallback), fallback)
  }

  readonly property color scrim: Color.composed(
    "launcher.scrim", "launcher.scrim-alpha", Color.background, 0.5)
  readonly property color background: Color.composed(
    "launcher.background", "launcher.background-alpha", Color.background, 0.95)
  readonly property color border: Color.composed(
    "launcher.border", "launcher.border-alpha", Color.foreground, 1.0)
  readonly property color selectedBackground: Color.composed(
    "launcher.selected-background",
    "launcher.selected-background-alpha",
    Color.foreground,
    0.08)
  readonly property color selectedBorder: Color.composed(
    "launcher.selected-border",
    "launcher.selected-border-alpha",
    Color.foreground,
    0.25)
  readonly property color text: root.solid("launcher.text", Color.foreground)
  readonly property color selectedText: root.solid(
    "launcher.selected-text", Color.accent)
  readonly property color accent: Color.accent
  readonly property color urgent: Color.urgent

  readonly property real scale: Style.fontScale
  readonly property int radius: Style.cornerRadius

  // Summoned surfaces honor OMARCHY_MENU_FONT; the launcher is one of them.
  readonly property string fontFamily: root.omarchyFont.menuFamily
  readonly property int fontCaption: root.omarchyFont.caption
  readonly property int fontSmall: root.omarchyFont.bodySmall
  readonly property int fontBody: root.omarchyFont.body
  readonly property int fontSubtitle: root.omarchyFont.subtitle
  readonly property int fontTitle: root.omarchyFont.title
  readonly property int fontHeading: root.omarchyFont.heading

  readonly property int panelPadding: root.omarchySpacing.panelPadding
  readonly property int rowPaddingX: root.omarchySpacing.rowPaddingX
  readonly property int gap: root.omarchySpacing.lg
}
